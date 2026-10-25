using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    public sealed class ReadableLogArchiveService : IDisposable
    {
        public sealed record LogArchiveInitializationResult(IReadOnlyList<string> TimedOutFiles)
        {
            public static LogArchiveInitializationResult Empty { get; } =
                new(Array.Empty<string>());

            public bool HasTimedOutFiles => TimedOutFiles.Count > 0;
        }

        private static readonly UTF8Encoding Utf8BomEncoding = new(encoderShouldEmitUTF8Identifier: true);
        private static readonly UTF8Encoding Utf8NoBomEncoding = new(encoderShouldEmitUTF8Identifier: false);
        private static readonly Regex ExpDecreaseHundredBillionRegex = new(
            @"경험치가\s*(?:10[,，]?)?0{9}\s*(?:감소|차감)",
            RegexOptions.Compiled);
        private const string ExactExpEssenceExchangeText = "경험치 100억이 차감되고, 경험의 정수 1개를 획득 하였습니다.";
        private static readonly Regex MagicStoneGainRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석(?:\s*(?:\[(?<countBracket>[\d,]+)\]|(?<countPlain>[\d,]+))\s*개)?(?:\s*(?:을|를))?\s*획득",
            RegexOptions.Compiled);
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);

        private readonly object _syncRoot = new();
        private readonly SemaphoreSlim _buildGate = new(1, 1);
        private readonly string _rootDirectory;
        private readonly string _shoutDirectory;
        private readonly string _itemDirectory;
        private readonly string _expDirectory;
        private readonly string _contentDirectory;
        private readonly string _AbandonDirectory;
        private readonly string _stateDirectory;
        private const string MigrationStateFileName = "migration.json";
        private const string ContentArchiveMigrationKey = "content-archive-experience-removal";
        private const int CurrentContentArchiveMigrationVersion = 1;
        private const string RawLogRebuildCheckpointKey = "raw-log-rebuild";
        private const string LogsFolderResetMigrationKey = "logs-folder-reset-5x";
        // 컨텐츠 아카이브 판정 기준이 바뀐 뒤(어비스 입장·보상 줄 등 정규식 판정 포함) 예전 빌드가 쓴 주간 파일을 1회 재구축
        private const string ContentArchiveRebuildMigrationKey = "content-archive-rebuild";
        private const int CurrentContentArchiveRebuildVersion = 3;
        private const int CurrentLogsFolderResetVersion = 1;
        private const string AbandonDaySummarySuffix = ".summary.day.json";
        private readonly Dictionary<DateTime, AbandonMonthlySummarySnapshotEntry> _abandonDayBuffer = new();

        // 실시간 아카이브 쓰기는 줄마다 파일을 열지 않고 모아 두었다가 1초 뒤(또는 FlushAll)에 한 번 쓴다.
        // 파일 핸들은 플러시마다 열고 닫아 아카이브를 지우거나 읽는 다른 코드와 충돌하지 않게 한다.
        private static readonly TimeSpan LiveFlushDelay = TimeSpan.FromSeconds(1);
        private readonly ArchiveWriteBatch _liveWrites = new(keepWritersOpen: false);
        private readonly Timer _liveFlushTimer;
        private bool _liveFlushScheduled;
        private DateTime _liveWriteDate = DateTime.MinValue;

        public ReadableLogArchiveService(string? rootDirectory = null)
        {
            _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
                ? LogStoragePaths.RootDirectory
                : rootDirectory;
            _shoutDirectory = Path.Combine(_rootDirectory, "Shout");
            _itemDirectory = Path.Combine(_rootDirectory, "Item");
            _expDirectory = Path.Combine(_rootDirectory, "Exp");
            _contentDirectory = Path.Combine(_rootDirectory, "Content");
            _AbandonDirectory = Path.Combine(_rootDirectory, "Abandon");
            _stateDirectory = Path.Combine(_rootDirectory, "_state");
            _liveFlushTimer = new Timer(_ => FlushLiveWrites(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// 원본 채팅 로그에서 아카이브(외치기·아이템·경험치·컨텐츠·어밴던)가 없는 날짜를 채운다.
        /// 파일 한 번 읽기에 <paramref name="extraConsumers"/>(사냥 기록·던전 타이머·시드·보물창고)도 함께 올라타므로
        /// 같은 파일을 서비스마다 따로 열지 않는다. 아카이브 쪽이 이미 다 있는 날짜라도 다른 소비자가 원하면 파일은 읽힌다.
        /// </summary>
        public async Task<LogArchiveInitializationResult> EnsureInitializedFromRawLogsAsync(
            string chatLogFolderPath,
            LogAnalysisService logAnalysisService,
            Func<string, bool> isContentRelevant,
            Action<string, int, int>? onProgressText = null,
            Func<DateTime, bool>? sourceDateFilter = null,
            CancellationToken cancellationToken = default,
            bool updateCheckpoint = true,
            IReadOnlyList<IRawLogScanConsumer>? extraConsumers = null)
        {
            if (string.IsNullOrWhiteSpace(chatLogFolderPath) || !Directory.Exists(chatLogFolderPath))
                return LogArchiveInitializationResult.Empty;

            if (logAnalysisService == null || isContentRelevant == null)
                return LogArchiveInitializationResult.Empty;

            await _buildGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                RebuildContentArchiveIfNeeded();
                List<RawLogFile> allSourceFiles = RawChatLogReader.EnumerateFiles(chatLogFolderPath);
                if (sourceDateFilter != null)
                    allSourceFiles.RemoveAll(f => !sourceDateFilter(f.Date));
                if (allSourceFiles.Count == 0)
                    return LogArchiveInitializationResult.Empty;

                DateTime? lastCompletedDate = LoadRawLogRebuildCheckpoint();
                var sourceFiles = allSourceFiles;
                if (lastCompletedDate.HasValue)
                {
                    sourceFiles = sourceFiles
                        .Where(source => source.Date.Date > lastCompletedDate.Value.Date)
                        .ToList();
                }

                var missingShoutDates = new HashSet<DateTime>();
                var missingExpDates = new HashSet<DateTime>();
                var missingItemDays = new HashSet<DateTime>();
                var missingContentWeeks = new HashSet<string>(StringComparer.Ordinal);
                var missingAbandonDays = new HashSet<DateTime>();
                var missingAbandonSummaryDays = new HashSet<DateTime>();

                bool hasCheckpoint = lastCompletedDate.HasValue;
                foreach (RawLogFile source in allSourceFiles)
                {
                    DateTime day = source.Date.Date;
                    string dayKey = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    string shoutPath = Path.Combine(_shoutDirectory, $"{dayKey}.html");
                    if ((!hasCheckpoint && !HasUsableArchiveContent(shoutPath)) ||
                        (hasCheckpoint && File.Exists(shoutPath) && !HasUsableArchiveContent(shoutPath)))
                        missingShoutDates.Add(day);

                    string expPath = Path.Combine(_expDirectory, $"{dayKey}.html");
                    if ((!hasCheckpoint && !HasUsableArchiveContent(expPath)) ||
                        (hasCheckpoint && File.Exists(expPath) && !HasUsableArchiveContent(expPath)))
                        missingExpDates.Add(day);

                    string itemPath = Path.Combine(_itemDirectory, $"{dayKey}.html");
                    if ((!hasCheckpoint && !HasUsableArchiveContent(itemPath)) ||
                        (hasCheckpoint && File.Exists(itemPath) && !HasUsableArchiveContent(itemPath)))
                        missingItemDays.Add(day);

                    string weekKey = LogTextClassifier.GetIsoWeekKey(day);
                    string contentPath = Path.Combine(_contentDirectory, $"{weekKey}.html");
                    if ((!hasCheckpoint && !HasUsableArchiveContent(contentPath)) ||
                        (hasCheckpoint && File.Exists(contentPath) && !HasUsableArchiveContent(contentPath)))
                        missingContentWeeks.Add(weekKey);

                    string AbandonPath = Path.Combine(_AbandonDirectory, $"{dayKey}.html");
                    if ((!hasCheckpoint && !HasUsableArchiveContent(AbandonPath)) ||
                        (hasCheckpoint && File.Exists(AbandonPath) && !HasUsableArchiveContent(AbandonPath)))
                        missingAbandonDays.Add(day);
                    string abandonSummaryPath = Path.Combine(_AbandonDirectory, $"{dayKey}{AbandonDaySummarySuffix}");
                    if (!File.Exists(abandonSummaryPath))
                        missingAbandonSummaryDays.Add(day);
                }

                bool hasAnyMissingArchive =
                    missingShoutDates.Count > 0 ||
                    missingExpDates.Count > 0 ||
                    missingItemDays.Count > 0 ||
                    missingContentWeeks.Count > 0 ||
                    missingAbandonDays.Count > 0 ||
                    missingAbandonSummaryDays.Count > 0;

                if (sourceFiles.Count == 0 && hasAnyMissingArchive)
                {
                    // Checkpoint says "done", but some program logs are missing.
                    // Rebuild only from the earliest missing date, not from the beginning.
                    DateTime? earliestMissingDate = null;
                    if (missingShoutDates.Count > 0)
                        earliestMissingDate = missingShoutDates.Min();
                    if (missingExpDates.Count > 0)
                        earliestMissingDate = !earliestMissingDate.HasValue ? missingExpDates.Min() : MinDate(earliestMissingDate.Value, missingExpDates.Min());
                    if (missingItemDays.Count > 0)
                        earliestMissingDate = !earliestMissingDate.HasValue ? missingItemDays.Min() : MinDate(earliestMissingDate.Value, missingItemDays.Min());
                    if (missingAbandonDays.Count > 0)
                        earliestMissingDate = !earliestMissingDate.HasValue ? missingAbandonDays.Min() : MinDate(earliestMissingDate.Value, missingAbandonDays.Min());

                    if (missingContentWeeks.Count > 0)
                    {
                        DateTime contentMissingDate = allSourceFiles
                            .Where(source => missingContentWeeks.Contains(LogTextClassifier.GetIsoWeekKey(source.Date)))
                            .Select(source => source.Date.Date)
                            .DefaultIfEmpty(DateTime.MaxValue)
                            .Min();

                        if (contentMissingDate != DateTime.MaxValue)
                            earliestMissingDate = !earliestMissingDate.HasValue ? contentMissingDate : MinDate(earliestMissingDate.Value, contentMissingDate);
                    }

                    sourceFiles = earliestMissingDate.HasValue
                        ? allSourceFiles.Where(source => source.Date.Date >= earliestMissingDate.Value.Date).ToList()
                        : new List<RawLogFile>();
                }

                EnsureArchiveDirectoriesExist();

                DropItemResolver.DropItemFilterSnapshot filterSnapshot = await AppServices.Get<DropItemResolver>().LoadDefaultFilterSnapshotAsync().ConfigureAwait(false);
                var archiveConsumer = new ArchiveScanConsumer(
                    this,
                    logAnalysisService,
                    isContentRelevant,
                    filterSnapshot,
                    new HashSet<DateTime>(sourceFiles.Select(f => f.Date)),
                    missingShoutDates,
                    missingExpDates,
                    missingItemDays,
                    missingContentWeeks,
                    missingAbandonDays,
                    missingAbandonSummaryDays,
                    updateCheckpoint);

                var consumers = new List<IRawLogScanConsumer> { archiveConsumer };
                if (extraConsumers != null)
                    consumers.AddRange(extraConsumers);

                RawLogScanResult scan = await RawLogScanService.RunAsync(
                    chatLogFolderPath,
                    consumers,
                    sourceDateFilter,
                    onProgressText,
                    cancellationToken).ConfigureAwait(false);

                return new LogArchiveInitializationResult(scan.TimedOutFiles);
            }
            finally
            {
                _buildGate.Release();
            }
        }

        /// <summary>
        /// 아카이브 재구축 소비자: 패스가 넘기는 줄을 분석해 빠진 아카이브 파일에만 쓴다.
        /// 어디까지 끝냈는지(raw-log-rebuild 체크포인트)는 패스가 끝날 때 올린다.
        /// </summary>
        private sealed class ArchiveScanConsumer : IRawLogScanConsumer
        {
            private readonly ReadableLogArchiveService _owner;
            private readonly LogAnalysisService _logAnalysisService;
            private readonly Func<string, bool> _isContentRelevant;
            private readonly DropItemResolver.DropItemFilterSnapshot _filterSnapshot;
            private readonly HashSet<DateTime> _sourceDates;
            private readonly HashSet<DateTime> _missingShoutDates;
            private readonly HashSet<DateTime> _missingExpDates;
            private readonly HashSet<DateTime> _missingItemDays;
            private readonly HashSet<string> _missingContentWeeks;
            private readonly HashSet<DateTime> _missingAbandonDays;
            private readonly HashSet<DateTime> _missingAbandonSummaryDays;
            private readonly bool _updateCheckpoint;
            private readonly ArchiveWriteBatch _pendingWrites = new(keepWritersOpen: true);
            private DateTime? _lastProcessedDate;

            // 지금 읽는 파일에 무엇을 써야 하는지
            private DateTime _logDate;
            private bool _writeShout, _writeExp, _writeItem, _writeContent, _writeAbandon, _rebuildAbandonSummary, _hasArchiveWriteTarget;

            public ArchiveScanConsumer(
                ReadableLogArchiveService owner,
                LogAnalysisService logAnalysisService,
                Func<string, bool> isContentRelevant,
                DropItemResolver.DropItemFilterSnapshot filterSnapshot,
                HashSet<DateTime> sourceDates,
                HashSet<DateTime> missingShoutDates,
                HashSet<DateTime> missingExpDates,
                HashSet<DateTime> missingItemDays,
                HashSet<string> missingContentWeeks,
                HashSet<DateTime> missingAbandonDays,
                HashSet<DateTime> missingAbandonSummaryDays,
                bool updateCheckpoint)
            {
                _owner = owner;
                _logAnalysisService = logAnalysisService;
                _isContentRelevant = isContentRelevant;
                _filterSnapshot = filterSnapshot;
                _sourceDates = sourceDates;
                _missingShoutDates = missingShoutDates;
                _missingExpDates = missingExpDates;
                _missingItemDays = missingItemDays;
                _missingContentWeeks = missingContentWeeks;
                _missingAbandonDays = missingAbandonDays;
                _missingAbandonSummaryDays = missingAbandonSummaryDays;
                _updateCheckpoint = updateCheckpoint;
            }

            public void BeginScan(IReadOnlyList<RawLogFile> files)
            {
                lock (_owner._syncRoot)
                    _owner._abandonDayBuffer.Clear();
            }

            public bool BeginFile(RawLogFile file, long fileLength)
            {
                DateTime logDate = file.Date.Date;
                if (!_sourceDates.Contains(logDate))
                    return false;

                _logDate = logDate;
                string weekKey = LogTextClassifier.GetIsoWeekKey(logDate);
                _writeShout = _missingShoutDates.Contains(logDate);
                _writeExp = _missingExpDates.Contains(logDate);
                _writeItem = _missingItemDays.Contains(logDate);
                _writeContent = _missingContentWeeks.Contains(weekKey);
                _writeAbandon = _missingAbandonDays.Contains(logDate);
                _rebuildAbandonSummary = _missingAbandonSummaryDays.Contains(logDate);
                _hasArchiveWriteTarget = _writeShout || _writeExp || _writeItem || _writeContent || _writeAbandon;

                // 이미 모든 아카이브가 있는 날짜는 파싱 자체를 건너뛴다 (재시작·백필 시 최근 구간 무료 통과)
                if (!_hasArchiveWriteTarget && !_rebuildAbandonSummary)
                {
                    _lastProcessedDate = logDate;
                    return false;
                }

                return true;
            }

            public void OnLine(RawLogLine line)
            {
                var analysis = _logAnalysisService.Analyze(line.Html, isRealTime: false, _filterSnapshot);
                if (!analysis.IsSuccess)
                    return;

                string formattedText = analysis.Parsed.FormattedText;
                lock (_owner._syncRoot)
                {
                    if (_rebuildAbandonSummary && !_writeAbandon && TryBuildAbandonDelta(formattedText, out AbandonMonthlySummarySnapshotEntry summaryDelta))
                        _owner.UpdateAbandonDailySummary(_logDate, summaryDelta);

                    if (_hasArchiveWriteTarget)
                    {
                        _owner.AppendFromAnalysisSelective(
                            _logDate,
                            analysis,
                            _isContentRelevant(formattedText),
                            _writeShout,
                            _writeItem,
                            _writeExp,
                            _writeContent,
                            _writeAbandon,
                            _pendingWrites,
                            flushSummaryImmediately: false);
                    }
                }
            }

            public void EndFile(RawLogFile file, RawLogReadOutcome outcome)
            {
                _lastProcessedDate = file.Date.Date;
            }

            public void EndScan(bool completed)
            {
                lock (_owner._syncRoot)
                {
                    _pendingWrites.Dispose();
                    _owner.FlushAbandonDayBuffer();
                }
                // 부분 범위(최근 로그만) 초기화 시에는 체크포인트를 올리지 않아
                // 과거 로그 백필이 건너뛰어지지 않게 한다
                if (completed && _updateCheckpoint && _lastProcessedDate.HasValue)
                    _owner.SaveRawLogRebuildCheckpoint(_lastProcessedDate.Value);
            }
        }

        #region 레거시 마이그레이션 (4.x → 5.x, 시작 시 각 1회 호출 — 구버전 사용자가 남아 있는 동안 유지)

        /// <summary>
        /// [레거시] 4.x → 5.x 업그레이드 1회 마이그레이션: 구버전 형식의 Logs 폴더를 통째로 삭제해
        /// 원본 채팅 로그에서 새로 재구축되게 한다. 적용 여부는 Logs\_state\migration.json에 기록되어
        /// 이후 실행(신규 설치 포함)에서는 아무것도 하지 않는다.
        /// </summary>
        public void ResetLogsFolderForV5IfNeeded()
        {
            lock (_syncRoot)
            {
                try
                {
                    if (LoadMigrationVersion(LogsFolderResetMigrationKey) >= CurrentLogsFolderResetVersion)
                        return;

                    _abandonDayBuffer.Clear();

                    if (Directory.Exists(_rootDirectory))
                    {
                        try
                        {
                            Directory.Delete(_rootDirectory, recursive: true);
                            AppLogger.Info("Legacy Logs folder was deleted for the 5.x archive rebuild.");
                        }
                        catch (Exception ex)
                        {
                            // 폴더 삭제가 막히면(파일 잠금 등) 내용물 삭제로 폴백 — 남는 게 있어도 재구축이 덮어쓴다
                            AppLogger.Warn("Failed to delete legacy Logs folder; falling back to per-file cleanup.", ex);
                            EnsureArchiveDirectoriesExist();
                            DeleteFilesByPattern(_shoutDirectory, "*");
                            DeleteFilesByPattern(_itemDirectory, "*");
                            DeleteFilesByPattern(_expDirectory, "*");
                            DeleteFilesByPattern(_contentDirectory, "*");
                            DeleteFilesByPattern(_AbandonDirectory, "*");
                            DeleteFilesByPattern(_stateDirectory, "*");
                            DeleteFilesByPattern(_rootDirectory, "*");
                        }
                    }

                    EnsureArchiveDirectoriesExist();
                    SaveMigrationVersion(LogsFolderResetMigrationKey, CurrentLogsFolderResetVersion);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to reset legacy Logs folder for 5.x.", ex);
                }
            }
        }

        /// <summary>
        /// [레거시] 컨텐츠 아카이브에 섞여 있던 경험치 줄 제거(버전 1). 시작 시 전경 패스 뒤에 한 번 부른다.
        /// 적용 뒤에는 옛 상태 파일 정리만 한다.
        /// </summary>
        /// <summary>
        /// 컨텐츠 아카이브를 예전 빌드가 썼다면(어비스 입장·보상 줄이 빠진 채) 주간 파일을 모두 지워
        /// 다음 패스가 원본 로그에서 다시 만들게 한다. 체크포인트도 함께 지워 지운 주가 '누락'으로 잡히게 한다. 1회만.
        /// </summary>
        private void RebuildContentArchiveIfNeeded()
        {
            lock (_syncRoot)
            {
                try
                {
                    EnsureArchiveDirectoriesExist();
                    if (LoadMigrationVersion(ContentArchiveRebuildMigrationKey) >= CurrentContentArchiveRebuildVersion)
                        return;

                    _liveWrites.FlushAll();
                    string[] files = Directory.GetFiles(_contentDirectory, "*.html", SearchOption.TopDirectoryOnly);
                    var failed = new List<string>();
                    foreach (string path in files)
                    {
                        if (!TryDeleteOrTruncate(path, out string outcome))
                            failed.Add($"{Path.GetFileName(path)}: {outcome}");
                    }

                    if (failed.Count > 0)
                    {
                        // 비우지 못한 파일이 있으면 완료로 기록하지 않고 다음 시작에 다시 시도한다
                        AppLogger.Warn($"[진단] 컨텐츠 아카이브 재구축 준비 실패 ({failed.Count}/{files.Length}): {string.Join(" | ", failed)}");
                        return;
                    }

                    var state = LoadMigrationState();
                    if (state.Checkpoints.Remove(RawLogRebuildCheckpointKey))
                    {
                        string migrationPath = Path.Combine(_stateDirectory, MigrationStateFileName);
                        File.WriteAllText(migrationPath, JsonSerializer.Serialize(state, JsonOptions), Utf8BomEncoding);
                    }

                    SaveMigrationVersion(ContentArchiveRebuildMigrationKey, CurrentContentArchiveRebuildVersion);
                    AppLogger.Warn($"[진단] 컨텐츠 아카이브 {files.Length}개 비움 → 원본 로그에서 재구축 (dir={_contentDirectory})");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to reset content archive for rebuild.", ex);
                }
            }
        }

        /// <summary>파일을 지우고, 다른 창이 읽고 있어 못 지우면 내용을 비운다. 끝난 뒤 실제로 비었는지 확인한다.</summary>
        private static bool TryDeleteOrTruncate(string path, out string outcome)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    File.Delete(path);
                    if (!File.Exists(path)) { outcome = "deleted"; return true; }
                }
                catch (Exception ex) { outcome = "delete: " + ex.GetType().Name; }

                try
                {
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                        fs.SetLength(0);
                    if (!File.Exists(path) || new FileInfo(path).Length == 0) { outcome = "truncated"; return true; }
                }
                catch (Exception ex) { outcome = "truncate: " + ex.GetType().Name + " " + ex.Message; }

                Thread.Sleep(200);
            }

            outcome = "still has content";
            try { outcome = "still " + new FileInfo(path).Length + " bytes"; } catch { }
            return false;
        }

        public void MigrateContentArchiveIfNeeded()
        {
            lock (_syncRoot)
            {
                EnsureArchiveDirectoriesExist();
                int appliedVersion = LoadMigrationVersion(ContentArchiveMigrationKey);
                if (appliedVersion >= CurrentContentArchiveMigrationVersion)
                {
                    DeleteLegacyStateFiles();
                    return;
                }

                RemoveExperienceLinesFromContentArchive();

                // SaveMigrationVersion이 옛 상태 파일 정리까지 한다
                SaveMigrationVersion(ContentArchiveMigrationKey, CurrentContentArchiveMigrationVersion);
            }
        }

        #endregion

        public void ClearArchiveLogsAndResetCheckpoint()
        {
            lock (_syncRoot)
            {
                try
                {
                    EnsureArchiveDirectoriesExist();
                    _abandonDayBuffer.Clear();
                    _liveWrites.DiscardAll(); // 아직 안 쓴 실시간 줄이 지운 파일을 되살리지 않게

                    DeleteFilesByPattern(_shoutDirectory, "*.html");
                    DeleteFilesByPattern(_itemDirectory, "*.html");
                    DeleteFilesByPattern(_expDirectory, "*.html");
                    DeleteFilesByPattern(_contentDirectory, "*.html");
                    DeleteFilesByPattern(_AbandonDirectory, "*.html");
                    DeleteFilesByPattern(_AbandonDirectory, $"*{AbandonDaySummarySuffix}");

                    var state = LoadMigrationState();
                    if (state.Checkpoints.Remove(RawLogRebuildCheckpointKey))
                    {
                        string migrationPath = Path.Combine(_stateDirectory, MigrationStateFileName);
                        string json = JsonSerializer.Serialize(state, JsonOptions);
                        File.WriteAllText(migrationPath, json, Utf8BomEncoding);
                    }

                    DeleteLegacyStateFiles();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to clear archive logs and reset checkpoint.", ex);
                }
            }
        }

        public void AppendFromAnalysis(DateTime logDate, LogAnalysisResult analysis, bool isContentRelevant)
        {
            if (analysis == null || !analysis.IsSuccess)
                return;

            var parsed = analysis.Parsed;
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.FormattedText))
                return;

            lock (_syncRoot)
            {
                // 날짜가 바뀌면 전날 몫을 먼저 내려 쓴다
                if (_liveWriteDate != logDate.Date)
                {
                    _liveWrites.FlushAll();
                    _liveWriteDate = logDate.Date;
                }

                AppendFromAnalysisSelective(
                    logDate,
                    analysis,
                    isContentRelevant,
                    writeShout: true,
                    writeItem: true,
                    writeExp: true,
                    writeContent: true,
                    writeAbandon: true,
                    _liveWrites,
                    flushSummaryImmediately: true);

                if (!_liveFlushScheduled)
                {
                    _liveFlushScheduled = true;
                    _liveFlushTimer.Change(LiveFlushDelay, Timeout.InfiniteTimeSpan);
                }
            }
        }

        private void FlushLiveWrites()
        {
            lock (_syncRoot)
            {
                _liveFlushScheduled = false;
                try { _liveWrites.FlushAll(); }
                catch (Exception ex) { AppLogger.Warn("Failed to flush pending realtime archive writes.", ex); }
            }
        }

        /// <summary>모아 둔 실시간 아카이브 줄을 지금 파일에 쓴다. 아카이브 파일을 읽는 쪽은 읽기 전에 이걸 불러 마지막 1초 줄을 놓치지 않게 한다.</summary>
        public void FlushAll() => FlushLiveWrites();

        /// <summary>아카이브 파일을 읽는 공용 통로 — 대기 중인 실시간 줄을 먼저 내려 쓴 뒤 줄을 돌려준다. 파일이 없으면 빈 열거.</summary>
        public IEnumerable<string> ReadArchiveLines(string path)
        {
            FlushAll();
            return File.Exists(path) ? File.ReadLines(path, Encoding.UTF8) : Array.Empty<string>();
        }

        public void Dispose()
        {
            try { _liveFlushTimer.Dispose(); } catch { }
            FlushLiveWrites();
        }

        public AbandonSummaryValue LoadAbandonWeeklySummary(DateTime date)
        {
            lock (_syncRoot)
            {
                DateTime weekStart = GetIsoWeekStart(date);
                DateTime weekEnd = weekStart.AddDays(6);
                var weekly = new AbandonMonthlySummarySnapshotEntry { MonthStart = weekStart };
                foreach (DateTime day in EachDay(weekStart, weekEnd))
                {
                    string dayPath = Path.Combine(_AbandonDirectory, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + AbandonDaySummarySuffix);
                    if (!TryLoadAbandonSummary(dayPath, out AbandonMonthlySummarySnapshotEntry daySummary))
                        continue;

                    AccumulateSummary(weekly, daySummary);
                }

                return AbandonSummaryCalculator.FromMonthly(weekly);
            }
        }

        private void EnsureArchiveDirectoriesExist()
        {
            Directory.CreateDirectory(_shoutDirectory);
            Directory.CreateDirectory(_itemDirectory);
            Directory.CreateDirectory(_expDirectory);
            Directory.CreateDirectory(_contentDirectory);
            Directory.CreateDirectory(_AbandonDirectory);
            Directory.CreateDirectory(_stateDirectory);
        }

        private DateTime? LoadRawLogRebuildCheckpoint()
        {
            var state = LoadMigrationState();
            return state.Checkpoints.TryGetValue(RawLogRebuildCheckpointKey, out var checkpoint) &&
                   DateTime.TryParse(checkpoint.Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed)
                ? parsed.Date
                : null;
        }

        private void SaveRawLogRebuildCheckpoint(DateTime completedDate)
        {
            try
            {
                Directory.CreateDirectory(_stateDirectory);
                var state = LoadMigrationState();
                state.Checkpoints[RawLogRebuildCheckpointKey] = new MigrationCheckpoint
                {
                    Value = completedDate.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    AppliedAtUtc = DateTime.UtcNow
                };

                string migrationPath = Path.Combine(_stateDirectory, MigrationStateFileName);
                string json = JsonSerializer.Serialize(state, JsonOptions);
                File.WriteAllText(migrationPath, json, Utf8BomEncoding);
                DeleteLegacyStateFiles();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to save raw log rebuild checkpoint for {completedDate:yyyy-MM-dd}.", ex);
            }
        }

        private sealed class MigrationState
        {
            public Dictionary<string, MigrationRecord> Migrations { get; set; } = new(StringComparer.Ordinal);
            public Dictionary<string, MigrationCheckpoint> Checkpoints { get; set; } = new(StringComparer.Ordinal);
        }

        private sealed class MigrationRecord
        {
            public int Version { get; set; }
            public DateTime AppliedAtUtc { get; set; }
        }

        private sealed class MigrationCheckpoint
        {
            public string Value { get; set; } = string.Empty;
            public DateTime AppliedAtUtc { get; set; }
        }

        private MigrationState LoadMigrationState()
        {
            string migrationPath = Path.Combine(_stateDirectory, MigrationStateFileName);
            if (!File.Exists(migrationPath))
                return new MigrationState();

            try
            {
                string json = File.ReadAllText(migrationPath, Encoding.UTF8);
                var state = JsonSerializer.Deserialize<MigrationState>(json, JsonOptions);
                return state ?? new MigrationState();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load migration state '{migrationPath}'.", ex);
                return new MigrationState();
            }
        }

        private int LoadMigrationVersion(string key)
        {
            var state = LoadMigrationState();
            return state.Migrations.TryGetValue(key, out var record) ? record.Version : 0;
        }

        private void SaveMigrationVersion(string key, int version)
        {
            try
            {
                Directory.CreateDirectory(_stateDirectory);
                var state = LoadMigrationState();
                state.Migrations[key] = new MigrationRecord
                {
                    Version = version,
                    AppliedAtUtc = DateTime.UtcNow
                };

                string migrationPath = Path.Combine(_stateDirectory, MigrationStateFileName);
                string json = JsonSerializer.Serialize(state, JsonOptions);
                File.WriteAllText(migrationPath, json, Utf8BomEncoding);
                DeleteLegacyStateFiles();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to save migration state for '{key}' version {version}.", ex);
            }
        }

        private void DeleteLegacyStateFiles()
        {
            foreach (string legacyName in new[] { "raw-log-rebuild.checkpoint", "content-migration.version" })
            {
                try
                {
                    string legacyPath = Path.Combine(_stateDirectory, legacyName);
                    if (File.Exists(legacyPath))
                        File.Delete(legacyPath);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to delete legacy state file '{legacyName}'.", ex);
                }
            }
        }

        private void RemoveExperienceLinesFromContentArchive()
        {
            if (!Directory.Exists(_contentDirectory))
                return;

            foreach (string path in Directory.EnumerateFiles(_contentDirectory, "*.html"))
            {
                try
                {
                    string[] lines = File.ReadAllLines(path, Utf8NoBomEncoding);
                    var filtered = lines
                        .Where(line => !ShouldRemoveContentArchiveLine(line))
                        .ToArray();

                    if (filtered.Length == lines.Length)
                        continue;

                    File.WriteAllLines(path, filtered, Utf8BomEncoding);
                    AppLogger.Info($"Migrated content archive file: {Path.GetFileName(path)}");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to migrate content archive file '{path}'.", ex);
                }
            }
        }

        private static bool ShouldRemoveContentArchiveLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            if (!line.Contains("<div", StringComparison.OrdinalIgnoreCase) ||
                !line.Contains("class=\"log content\"", StringComparison.OrdinalIgnoreCase))
                return false;

            string decoded = WebUtility.HtmlDecode(line);
            return decoded.Contains("경험치 100억이 차감되고, 경험의 정수 1개를 획득 하였습니다.", StringComparison.Ordinal) ||
                   (decoded.Contains("머큐리얼 케이브", StringComparison.Ordinal) &&
                    decoded.Contains("경험치가", StringComparison.Ordinal) &&
                    decoded.Contains("올랐습니다", StringComparison.Ordinal));
        }


        private void AppendFromAnalysisSelective(
            DateTime logDate,
            LogAnalysisResult analysis,
            bool isContentRelevant,
            bool writeShout,
            bool writeItem,
            bool writeExp,
            bool writeContent,
            bool writeAbandon,
            ArchiveWriteBatch pendingArchiveWrites,
            bool flushSummaryImmediately)
        {
            var parsed = analysis.Parsed;
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.FormattedText))
                return;

            string text = parsed.FormattedText;
            // 세 문구 모두 "보상 상자"를 품으므로 그 한 번으로 먼저 거른다
            bool isJoySorrowRewardLine =
                text.Contains("보상 상자", StringComparison.Ordinal) &&
                (text.Contains("레이티아 퇴치 보상으로 레이티아 보상 상자", StringComparison.Ordinal) ||
                 text.Contains("설계자 퇴치 보상으로 설계자 보상 상자", StringComparison.Ordinal) ||
                 text.Contains("[환희의 레이티아 보상 상자] 아이템을 1개 획득하였습니다.", StringComparison.Ordinal));

            if (writeShout && parsed.Category == ChatCategory.Shout)
            {
                AppendShoutLog(logDate, text, pendingArchiveWrites);
            }

            if (writeItem && !isJoySorrowRewardLine)
            {
                if (analysis.HasTrackedItemDrop && !string.IsNullOrWhiteSpace(parsed.TrackedItemName))
                {
                    AppendItemLog(logDate, text, parsed.TrackedItemName!, parsed.TrackedItemGrade.ToString(), Math.Max(1, parsed.TrackedItemCount), pendingArchiveWrites);
                }
                else if (TryExtractMagicStoneGain(text, out string stoneName, out int stoneCount))
                {
                    if (stoneName == "하급 마정석" ||
                        stoneName == "중급 마정석" ||
                        stoneName == "상급 마정석" ||
                        stoneName == "최상급 마정석")
                    {
                        var gainDelta = new AbandonMonthlySummarySnapshotEntry();
                        switch (stoneName)
                        {
                            case "하급 마정석":
                                gainDelta.LowGain += stoneCount;
                                break;
                            case "중급 마정석":
                                gainDelta.MidGain += stoneCount;
                                break;
                            case "상급 마정석":
                                gainDelta.HighGain += stoneCount;
                                break;
                            case "최상급 마정석":
                                gainDelta.TopGain += stoneCount;
                                break;
                        }
                        NormalizeStoneConsistency(gainDelta);
                        UpdateAbandonDailySummary(logDate, gainDelta, flushImmediately: flushSummaryImmediately);
                        return;
                    }
                }
            }

            if (writeExp && IsExperienceDecreaseLog(text))
            {
                AppendExpLog(logDate, text, pendingArchiveWrites);
            }

            if (writeContent && isContentRelevant)
            {
                AppendContentLog(logDate, text, pendingArchiveWrites);
            }

            if (writeAbandon && IsAbandonRelevantLog(text))
            {
                AppendAbandonLog(logDate, text, pendingArchiveWrites, flushSummaryImmediately);
            }
        }

        private void AppendShoutLog(DateTime logDate, string text, ArchiveWriteBatch pendingArchiveWrites)
        {
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_shoutDirectory, $"{dateKey}.html");
            string entry = $"<div class=\"log shout\" data-date=\"{dateKey}\">{WebUtility.HtmlEncode(text)}</div>";
            AppendHtmlLine(path, $"Shout {dateKey}", entry, pendingArchiveWrites);
        }

        private void AppendItemLog(DateTime logDate, string text, string itemName, string itemGrade, int itemCount, ArchiveWriteBatch pendingArchiveWrites)
        {
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_itemDirectory, $"{dateKey}.html");
            string entry = $"<div class=\"log item\" data-date=\"{dateKey}\" data-item-name=\"{WebUtility.HtmlEncode(itemName)}\" data-item-grade=\"{WebUtility.HtmlEncode(itemGrade)}\" data-item-count=\"{Math.Max(1, itemCount)}\">{WebUtility.HtmlEncode(text)}</div>";
            AppendHtmlLine(path, $"Item {dateKey}", entry, pendingArchiveWrites);
        }

        private void AppendContentLog(DateTime logDate, string text, ArchiveWriteBatch pendingArchiveWrites)
        {
            string weekKey = LogTextClassifier.GetIsoWeekKey(logDate);
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_contentDirectory, $"{weekKey}.html");
            string entry = $"<div class=\"log content\" data-date=\"{dateKey}\">{WebUtility.HtmlEncode(text)}</div>";
            AppendHtmlLine(path, $"Content {weekKey}", entry, pendingArchiveWrites);
        }

        private void AppendExpLog(DateTime logDate, string text, ArchiveWriteBatch pendingArchiveWrites)
        {
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_expDirectory, $"{dateKey}.html");
            string encodedText = WebUtility.HtmlEncode(text);
            string entry = $"<div class=\"log exp\" data-date=\"{dateKey}\">{encodedText}</div>";
            AppendHtmlLine(path, $"Exp {dateKey}", entry, pendingArchiveWrites);
        }

        private void AppendAbandonLog(DateTime logDate, string text, ArchiveWriteBatch pendingArchiveWrites, bool flushSummaryImmediately)
        {
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_AbandonDirectory, $"{dateKey}.html");
            string entry = $"<div class=\"log Abandon\" data-date=\"{dateKey}\">{WebUtility.HtmlEncode(text)}</div>";
            AppendHtmlLine(path, $"Abandon {dateKey}", entry, pendingArchiveWrites);

            if (TryBuildAbandonDelta(text, out AbandonMonthlySummarySnapshotEntry delta))
                UpdateAbandonDailySummary(logDate, delta, flushImmediately: flushSummaryImmediately);
        }

        private static bool IsExperienceDecreaseLog(string text)
        {
            // 정규식("경험치가 …")과 정수 교환 문구("경험치 100억이 …") 모두 "경험치"를 품는다
            if (string.IsNullOrWhiteSpace(text) || !text.Contains("경험치", StringComparison.Ordinal))
                return false;

            if (ExpDecreaseHundredBillionRegex.IsMatch(text))
                return true;

            return text.Contains(ExactExpEssenceExchangeText, StringComparison.Ordinal);
        }

        private static bool IsAbandonRelevantLog(string text)
        {
            // 어밴던 판정 정규식은 "입장료"(입장료 줄) 또는 "마정석"(획득·빼앗김 줄)을 반드시 품는다
            if (string.IsNullOrWhiteSpace(text) ||
                (!text.Contains("입장료", StringComparison.Ordinal) && !text.Contains("마정석", StringComparison.Ordinal)))
                return false;

            var summary = new AbandonSummaryValue();
            return AbandonSummaryCalculator.TryAccumulate(text, ref summary);
        }

        private static bool TryBuildAbandonDelta(string text, out AbandonMonthlySummarySnapshotEntry delta)
        {
            delta = new AbandonMonthlySummarySnapshotEntry();
            return AbandonSummaryCalculator.TryAccumulate(text, delta);
        }

        private void UpdateAbandonDailySummary(DateTime logDate, AbandonMonthlySummarySnapshotEntry delta, bool flushImmediately = false)
        {
            string dayKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string summaryPath = Path.Combine(_AbandonDirectory, $"{dayKey}{AbandonDaySummarySuffix}");
            if (!_abandonDayBuffer.TryGetValue(logDate.Date, out AbandonMonthlySummarySnapshotEntry? summary))
            {
                summary = LoadAbandonSummary(summaryPath) ?? new AbandonMonthlySummarySnapshotEntry
                {
                    MonthStart = logDate.Date
                };
            }
            NormalizeStoneConsistency(summary);

            summary.TotalEntryFeeMan += delta.TotalEntryFeeMan;
            summary.Low += delta.Low;
            summary.LowGain += delta.LowGain;
            summary.LowLoss += delta.LowLoss;
            summary.Mid += delta.Mid;
            summary.MidGain += delta.MidGain;
            summary.MidLoss += delta.MidLoss;
            summary.High += delta.High;
            summary.HighGain += delta.HighGain;
            summary.HighLoss += delta.HighLoss;
            summary.Top += delta.Top;
            summary.TopGain += delta.TopGain;
            summary.TopLoss += delta.TopLoss;
            NormalizeStoneConsistency(summary);
            _abandonDayBuffer[logDate.Date] = summary;

            if (flushImmediately)
            {
                string json = JsonSerializer.Serialize(summary, JsonOptions);
                File.WriteAllText(summaryPath, json, Utf8BomEncoding);
                _abandonDayBuffer.Remove(logDate.Date);
            }
        }
        private void FlushAbandonDayBuffer()
        {
            foreach (var kv in _abandonDayBuffer)
            {
                DateTime day = kv.Key;
                string dayKey = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string summaryPath = Path.Combine(_AbandonDirectory, $"{dayKey}{AbandonDaySummarySuffix}");
                string json = JsonSerializer.Serialize(kv.Value, JsonOptions);
                File.WriteAllText(summaryPath, json, Utf8BomEncoding);
            }
            _abandonDayBuffer.Clear();
        }
        private static void NormalizeStoneConsistency(AbandonMonthlySummarySnapshotEntry summary)
        {
            static (long net, long gain, long loss) NormalizeOne(long net, long gain, long loss)
            {
                if (gain == 0 && loss == 0 && net != 0)
                {
                    if (net > 0) gain = net;
                    else loss = -net;
                }

                if (gain < 0) gain = 0;
                if (loss < 0) loss = 0;
                net = gain - loss;
                return (net, gain, loss);
            }

            (summary.Low, summary.LowGain, summary.LowLoss) = NormalizeOne(summary.Low, summary.LowGain, summary.LowLoss);
            (summary.Mid, summary.MidGain, summary.MidLoss) = NormalizeOne(summary.Mid, summary.MidGain, summary.MidLoss);
            (summary.High, summary.HighGain, summary.HighLoss) = NormalizeOne(summary.High, summary.HighGain, summary.HighLoss);
            (summary.Top, summary.TopGain, summary.TopLoss) = NormalizeOne(summary.Top, summary.TopGain, summary.TopLoss);
        }


        private static AbandonMonthlySummarySnapshotEntry? LoadAbandonSummary(string summaryPath)
        {
            if (!File.Exists(summaryPath))
                return null;

            try
            {
                string json = File.ReadAllText(summaryPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<AbandonMonthlySummarySnapshotEntry>(json, JsonOptions);
            }
            catch
            {
                return null;
            }
        }
        private static bool TryLoadAbandonSummary(string path, out AbandonMonthlySummarySnapshotEntry summary)
        {
            summary = new AbandonMonthlySummarySnapshotEntry();
            var loaded = LoadAbandonSummary(path);
            if (loaded == null)
                return false;
            summary = loaded;
            return true;
        }
        private static IEnumerable<DateTime> EachDay(DateTime start, DateTime end)
        {
            for (DateTime day = start.Date; day <= end.Date; day = day.AddDays(1))
                yield return day;
        }
        private static void AccumulateSummary(AbandonMonthlySummarySnapshotEntry target, AbandonMonthlySummarySnapshotEntry delta)
        {
            target.TotalEntryFeeMan += delta.TotalEntryFeeMan;
            target.Low += delta.Low;
            target.LowGain += delta.LowGain;
            target.LowLoss += delta.LowLoss;
            target.Mid += delta.Mid;
            target.MidGain += delta.MidGain;
            target.MidLoss += delta.MidLoss;
            target.High += delta.High;
            target.HighGain += delta.HighGain;
            target.HighLoss += delta.HighLoss;
            target.Top += delta.Top;
            target.TopGain += delta.TopGain;
            target.TopLoss += delta.TopLoss;
        }

        private static DateTime GetIsoWeekStart(DateTime date)
            => LogTextClassifier.GetWeekStart(date.Date);

        private static DateTime MinDate(DateTime left, DateTime right)
            => left <= right ? left : right;

        private static bool TryExtractMagicStoneGain(string text, out string itemName, out int count)
        {
            itemName = string.Empty;
            count = 0;

            if (string.IsNullOrWhiteSpace(text) || !text.Contains("획득", StringComparison.Ordinal))
                return false;

            // Ignore announcement lines that should not count as player's abandoned-road gains.
            if (text.Contains("누군가 어밴던로드에서 주문을 통해 하급 마정석 1000개를 획득 하였습니다.", StringComparison.Ordinal))
                return false;

            Match match = MagicStoneGainRegex.Match(text);
            if (!match.Success)
                return false;

            string grade = match.Groups["grade"].Value.Trim();
            string countRaw = match.Groups["countBracket"].Success
                ? match.Groups["countBracket"].Value
                : match.Groups["countPlain"].Value;

            int parsedCount = 1;
            if (!string.IsNullOrWhiteSpace(countRaw))
            {
                countRaw = countRaw.Replace(",", string.Empty).Trim();
                if (!int.TryParse(countRaw, out parsedCount) || parsedCount <= 0)
                    return false;
            }

            itemName = $"{grade} 마정석";
            count = parsedCount;
            return true;
        }


        private static bool HasUsableArchiveContent(string path)
        {
            if (!File.Exists(path))
                return false;

            try
            {
                foreach (string line in File.ReadLines(path, Encoding.UTF8))
                {
                    if (line.Contains("<div class=\"log ", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static void DeleteFilesByPattern(string directoryPath, string pattern)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return;

            foreach (string path in Directory.EnumerateFiles(directoryPath, pattern, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    File.Delete(path);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to delete archive log file '{path}'.", ex);
                }
            }
        }

        private static void AppendHtmlLine(string path, string title, string htmlLine, ArchiveWriteBatch pendingArchiveWrites)
            => pendingArchiveWrites.Enqueue(path, title, htmlLine);

        /// <summary>
        /// 경로별로 줄을 모아 한 번에 쓴다. 재구축(keepWritersOpen=true)은 파일을 열어 둔 채 큰 덩어리로 쓰고,
        /// 실시간(keepWritersOpen=false)은 플러시마다 열고 닫아 다른 코드가 파일을 지우거나 읽는 데 걸리지 않게 한다.
        /// </summary>
        private sealed class ArchiveWriteBatch : IDisposable
        {
            private const int FlushThresholdChars = 256 * 1024;
            private readonly bool _keepWritersOpen;
            private readonly Dictionary<string, StringBuilder> _bufferByPath = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, StreamWriter> _writerByPath = new(StringComparer.OrdinalIgnoreCase);

            public ArchiveWriteBatch(bool keepWritersOpen)
            {
                _keepWritersOpen = keepWritersOpen;
            }

            /// <summary>아직 안 쓴 줄을 버린다 (아카이브를 비울 때).</summary>
            public void DiscardAll() => _bufferByPath.Clear();

            public void Enqueue(string path, string title, string htmlLine)
            {
                if (!_bufferByPath.TryGetValue(path, out StringBuilder? buffer))
                {
                    buffer = new StringBuilder(4096);
                    _bufferByPath[path] = buffer;
                }

                buffer.AppendLine(htmlLine);
                if (buffer.Length >= FlushThresholdChars)
                    FlushPath(path, title);
            }

            public void FlushAll()
            {
                foreach (string path in _bufferByPath.Keys.ToList())
                {
                    string title = Path.GetFileNameWithoutExtension(path) ?? "Log";
                    FlushPath(path, title);
                }

                foreach (var writer in _writerByPath.Values)
                    writer.Flush();
            }

            private void FlushPath(string path, string title)
            {
                if (!_bufferByPath.TryGetValue(path, out StringBuilder? buffer) || buffer.Length == 0)
                    return;

                if (!_keepWritersOpen)
                {
                    EnsureInitialized(path, title);
                    using var oneShot = new StreamWriter(path, append: true, Utf8NoBomEncoding);
                    oneShot.Write(buffer.ToString());
                    buffer.Clear();
                    return;
                }

                if (!_writerByPath.TryGetValue(path, out StreamWriter? writer))
                {
                    EnsureInitialized(path, title);
                    writer = new StreamWriter(path, append: true, Utf8NoBomEncoding);
                    _writerByPath[path] = writer;
                }

                writer.Write(buffer.ToString());
                buffer.Clear();
            }

            public void Dispose()
            {
                FlushAll();
                foreach (var writer in _writerByPath.Values)
                    writer.Dispose();
                _writerByPath.Clear();
            }
        }

        private static void EnsureInitialized(string path, string title)
        {
            if (File.Exists(path))
                return;

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            byte[] bom = Utf8BomEncoding.GetPreamble();
            if (bom.Length > 0)
                stream.Write(bom, 0, bom.Length);

            using var writer = new StreamWriter(stream, Utf8NoBomEncoding);
            writer.WriteLine("<!doctype html>");
            writer.WriteLine("<html lang=\"ko\">");
            writer.WriteLine("<head>");
            writer.WriteLine("  <meta charset=\"utf-8\" />");
            writer.WriteLine($"  <title>{WebUtility.HtmlEncode(title)}</title>");
            writer.WriteLine("  <style>");
            writer.WriteLine("    body{background:#111;color:#eee;font-family:'Malgun Gothic',sans-serif;font-size:13px;line-height:1.45;padding:12px;}");
            writer.WriteLine("    .log{margin:2px 0;padding:2px 0;border-bottom:1px solid rgba(255,255,255,.06);}");
            writer.WriteLine("    .summary{margin:2px 0;padding:4px 0;color:#9ad3ff;font-weight:600;border-bottom:1px dashed rgba(154,211,255,.35);}");
            writer.WriteLine("  </style>");
            writer.WriteLine("</head>");
            writer.WriteLine("<body>");
        }

    }
}
