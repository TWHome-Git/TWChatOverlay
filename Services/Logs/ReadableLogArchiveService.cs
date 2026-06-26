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
    public sealed class ReadableLogArchiveService
    {
        private sealed record SourceLogFile(string Path, DateTime Date);

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
        private static readonly Regex DateFromFileNameRegex = new(
            @"TWChatLog_(?<y>\d{4})_(?<m>\d{2})_(?<d>\d{2})\.html$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
        private static readonly TimeSpan SourceLogFileReadTimeout = TimeSpan.FromMinutes(1);
        private const string AbandonDaySummarySuffix = ".summary.day.json";
        private readonly Dictionary<DateTime, AbandonMonthlySummarySnapshotEntry> _abandonDayBuffer = new();

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
        }

        public async Task<LogArchiveInitializationResult> EnsureInitializedFromRawLogsAsync(
            string chatLogFolderPath,
            LogAnalysisService logAnalysisService,
            Func<string, bool> isContentRelevant,
            Action<string, int, int>? onProgressText = null,
            Func<DateTime, bool>? sourceDateFilter = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(chatLogFolderPath) || !Directory.Exists(chatLogFolderPath))
                return LogArchiveInitializationResult.Empty;

            if (logAnalysisService == null || isContentRelevant == null)
                return LogArchiveInitializationResult.Empty;

            await _buildGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var allSourceFiles = DiscoverSourceLogFiles(chatLogFolderPath, sourceDateFilter);
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
                foreach (SourceLogFile source in allSourceFiles)
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

                    string weekKey = GetIsoWeekKey(day);
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
                            .Where(source => missingContentWeeks.Contains(GetIsoWeekKey(source.Date)))
                            .Select(source => source.Date.Date)
                            .DefaultIfEmpty(DateTime.MaxValue)
                            .Min();

                        if (contentMissingDate != DateTime.MaxValue)
                            earliestMissingDate = !earliestMissingDate.HasValue ? contentMissingDate : MinDate(earliestMissingDate.Value, contentMissingDate);
                    }

                    sourceFiles = earliestMissingDate.HasValue
                        ? allSourceFiles.Where(source => source.Date.Date >= earliestMissingDate.Value.Date).ToList()
                        : new List<SourceLogFile>();
                }

                EnsureArchiveDirectoriesExist();

                return await BuildFromRawLogsAsync(
                    sourceFiles,
                    logAnalysisService,
                    isContentRelevant,
                    onProgressText,
                    cancellationToken,
                    missingShoutDates,
                    missingExpDates,
                    missingItemDays,
                    missingContentWeeks,
                    missingAbandonDays,
                    missingAbandonSummaryDays).ConfigureAwait(false);
            }
            finally
            {
                _buildGate.Release();
            }
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

                SaveMigrationVersion(ContentArchiveMigrationKey, CurrentContentArchiveMigrationVersion);
                DeleteLegacyStateFiles();
            }
        }

        public void ResetRawLogRebuildCheckpoint()
        {
            lock (_syncRoot)
            {
                try
                {
                    Directory.CreateDirectory(_stateDirectory);
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
                    AppLogger.Warn("Failed to reset raw log rebuild checkpoint.", ex);
                }
            }
        }

        public void ClearArchiveLogsAndResetCheckpoint()
        {
            lock (_syncRoot)
            {
                try
                {
                    EnsureArchiveDirectoriesExist();
                    _abandonDayBuffer.Clear();

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

            string text = parsed.FormattedText;
            lock (_syncRoot)
            {
                AppendFromAnalysisSelective(
                    logDate,
                    analysis,
                    isContentRelevant,
                    writeShout: true,
                    writeItem: true,
                    writeExp: true,
                    writeContent: true,
                    writeAbandon: true);
            }
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

        private async Task<LogArchiveInitializationResult> BuildFromRawLogsAsync(
            IReadOnlyList<SourceLogFile> sourceFiles,
            LogAnalysisService logAnalysisService,
            Func<string, bool> isContentRelevant,
            Action<string, int, int>? onProgressText,
            CancellationToken cancellationToken,
            HashSet<DateTime> missingShoutDates,
            HashSet<DateTime> missingExpDates,
            HashSet<DateTime> missingItemDays,
            HashSet<string> missingContentWeeks,
            HashSet<DateTime> missingAbandonDays,
            HashSet<DateTime> missingAbandonSummaryDays)
        {
            DropItemResolver.DropItemFilterSnapshot filterSnapshot = await DropItemResolver.LoadDefaultFilterSnapshotAsync().ConfigureAwait(false);
            _abandonDayBuffer.Clear();
            using var pendingArchiveWrites = new ArchiveWriteBatch();
            var timedOutFiles = new List<string>();
            int total = sourceFiles.Count;
            DateTime? lastProcessedDate = null;
            for (int i = 0; i < sourceFiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SourceLogFile source = sourceFiles[i];
                DateTime logDate = source.Date.Date;
                onProgressText?.Invoke($"{logDate:yyyy-MM-dd} 처리 중...", i + 1, total);
                string dayKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                bool writeShout = missingShoutDates.Contains(logDate);
                bool writeExp = missingExpDates.Contains(logDate);
                bool writeItem = missingItemDays.Contains(logDate);
                string weekKey = GetIsoWeekKey(logDate);
                bool writeContent = missingContentWeeks.Contains(weekKey);
                bool writeAbandon = missingAbandonDays.Contains(logDate);
                bool rebuildAbandonSummary = missingAbandonSummaryDays.Contains(logDate);

                bool hasArchiveWriteTarget = writeShout || writeExp || writeItem || writeContent || writeAbandon;

                SourceFileReadOutcome outcome = await ProcessSourceLinesMergedAsync(source.Path, cancellationToken, line =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var analysis = logAnalysisService.Analyze(line, isRealTime: false, filterSnapshot);
                    if (!analysis.IsSuccess)
                        return;

                    string formattedText = analysis.Parsed.FormattedText;

                    if (rebuildAbandonSummary && !writeAbandon && TryBuildAbandonDelta(formattedText, out AbandonMonthlySummarySnapshotEntry summaryDelta))
                    {
                        UpdateAbandonDailySummary(logDate, summaryDelta);
                    }

                    bool contentRelevant = isContentRelevant(formattedText);
                    if (hasArchiveWriteTarget)
                    {
                        lock (_syncRoot)
                        {
                            AppendFromAnalysisSelective(
                                logDate,
                                analysis,
                                contentRelevant,
                                writeShout,
                                writeItem,
                                writeExp,
                                writeContent,
                                writeAbandon,
                                pendingArchiveWrites);
                        }
                    }
                }).ConfigureAwait(false);

                if (outcome == SourceFileReadOutcome.TimedOut)
                {
                    timedOutFiles.Add(source.Path);
                    onProgressText?.Invoke($"{logDate:yyyy-MM-dd} 처리 시간이 1분을 넘겨 다음 파일로 넘어갑니다...", i + 1, total);
                }

                lastProcessedDate = logDate;
            }
            pendingArchiveWrites.FlushAll();
            FlushAbandonDayBuffer();
            if (lastProcessedDate.HasValue)
                SaveRawLogRebuildCheckpoint(lastProcessedDate.Value);

            return new LogArchiveInitializationResult(timedOutFiles);
        }

        private static List<SourceLogFile> DiscoverSourceLogFiles(string chatLogFolderPath, Func<DateTime, bool>? sourceDateFilter)
        {
            return Directory.EnumerateFiles(chatLogFolderPath, "TWChatLog_*.html")
                .Select(path => new { Path = path, DateFound = TryExtractDateFromPath(path, out DateTime date), Date = date })
                .Where(x => x.DateFound)
                .Where(x => sourceDateFilter == null || sourceDateFilter(x.Date.Date))
                .Select(x => new SourceLogFile(x.Path, x.Date.Date))
                .OrderBy(x => x.Date)
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
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
            ArchiveWriteBatch? pendingArchiveWrites = null)
        {
            var parsed = analysis.Parsed;
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.FormattedText))
                return;

            string text = parsed.FormattedText;
            bool isJoySorrowRewardLine =
                text.Contains("레이티아 퇴치 보상으로 레이티아 보상 상자", StringComparison.Ordinal) ||
                text.Contains("설계자 퇴치 보상으로 설계자 보상 상자", StringComparison.Ordinal) ||
                text.Contains("[환희의 레이티아 보상 상자] 아이템을 1개 획득하였습니다.", StringComparison.Ordinal);

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
                        bool flushSummaryNow = pendingArchiveWrites == null;
                        UpdateAbandonDailySummary(logDate, gainDelta, flushImmediately: flushSummaryNow);
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
                AppendAbandonLog(logDate, text, pendingArchiveWrites);
            }
        }

        private void AppendShoutLog(DateTime logDate, string text, ArchiveWriteBatch? pendingArchiveWrites = null)
        {
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_shoutDirectory, $"{dateKey}.html");
            string entry = $"<div class=\"log shout\" data-date=\"{dateKey}\">{WebUtility.HtmlEncode(text)}</div>";
            AppendHtmlLine(path, $"Shout {dateKey}", entry, pendingArchiveWrites);
        }

        private void AppendItemLog(DateTime logDate, string text, string itemName, string itemGrade, int itemCount, ArchiveWriteBatch? pendingArchiveWrites = null)
        {
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_itemDirectory, $"{dateKey}.html");
            string entry = $"<div class=\"log item\" data-date=\"{dateKey}\" data-item-name=\"{WebUtility.HtmlEncode(itemName)}\" data-item-grade=\"{WebUtility.HtmlEncode(itemGrade)}\" data-item-count=\"{Math.Max(1, itemCount)}\">{WebUtility.HtmlEncode(text)}</div>";
            AppendHtmlLine(path, $"Item {dateKey}", entry, pendingArchiveWrites);
        }

        private void AppendContentLog(DateTime logDate, string text, ArchiveWriteBatch? pendingArchiveWrites = null)
        {
            string weekKey = GetIsoWeekKey(logDate);
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_contentDirectory, $"{weekKey}.html");
            string entry = $"<div class=\"log content\" data-date=\"{dateKey}\">{WebUtility.HtmlEncode(text)}</div>";
            AppendHtmlLine(path, $"Content {weekKey}", entry, pendingArchiveWrites);
        }

        private void AppendExpLog(DateTime logDate, string text, ArchiveWriteBatch? pendingArchiveWrites = null)
        {
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_expDirectory, $"{dateKey}.html");
            string encodedText = WebUtility.HtmlEncode(text);
            string entry = $"<div class=\"log exp\" data-date=\"{dateKey}\">{encodedText}</div>";
            AppendHtmlLine(path, $"Exp {dateKey}", entry, pendingArchiveWrites);
        }

        private void AppendAbandonLog(DateTime logDate, string text, ArchiveWriteBatch? pendingArchiveWrites = null)
        {
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_AbandonDirectory, $"{dateKey}.html");
            string entry = $"<div class=\"log Abandon\" data-date=\"{dateKey}\">{WebUtility.HtmlEncode(text)}</div>";
            AppendHtmlLine(path, $"Abandon {dateKey}", entry, pendingArchiveWrites);

            if (TryBuildAbandonDelta(text, out AbandonMonthlySummarySnapshotEntry delta))
            {
                bool flushSummaryNow = pendingArchiveWrites == null;
                UpdateAbandonDailySummary(logDate, delta, flushImmediately: flushSummaryNow);
            }
        }

        private static bool IsExperienceDecreaseLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (ExpDecreaseHundredBillionRegex.IsMatch(text))
                return true;

            return text.Contains(ExactExpEssenceExchangeText, StringComparison.Ordinal);
        }

        private static bool IsAbandonRelevantLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
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
        {
            DateTime day = date.Date;
            int diff = (7 + (int)day.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return day.AddDays(-diff);
        }

        private static DateTime MinDate(DateTime left, DateTime right)
            => left <= right ? left : right;

        private static string GetIsoWeekKey(DateTime date)
        {
            int isoYear = ISOWeek.GetYear(date);
            int isoWeek = ISOWeek.GetWeekOfYear(date);
            return $"{isoYear}-W{isoWeek:00}";
        }

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


        private enum SourceFileReadOutcome
        {
            Completed,
            TimedOut,
            Failed
        }

        private static async Task<SourceFileReadOutcome> ProcessSourceLinesMergedAsync(string path, CancellationToken cancellationToken, Action<string> onLine)
        {
            Encoding encoding = Encoding.GetEncoding(949);
            const int maxAttempts = 5;
            const int streamBufferSize = 1 << 20; // 1MB

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    DateTime deadlineUtc = DateTime.UtcNow.Add(SourceLogFileReadTimeout);
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite,
                        bufferSize: streamBufferSize,
                        FileOptions.SequentialScan | FileOptions.Asynchronous);
                    using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true, bufferSize: streamBufferSize);

                    string? pending = null;
                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string? raw = await ReadLineWithTimeoutAsync(reader, cancellationToken, deadlineUtc).ConfigureAwait(false);
                        if (raw == null)
                            break;

                        if (string.IsNullOrWhiteSpace(raw))
                            continue;

                        string current = raw.Trim();
                        if (pending == null)
                        {
                            pending = current;
                            continue;
                        }

                        var mergedPair = ShoutLineMergeHelper.MergeWrappedShoutLines(new[] { pending, current });
                        if (mergedPair.Count == 1)
                        {
                            pending = mergedPair[0];
                            continue;
                        }

                        onLine(pending);
                        if (DateTime.UtcNow >= deadlineUtc)
                            throw new TimeoutException();
                        pending = current;
                    }

                    if (!string.IsNullOrWhiteSpace(pending))
                    {
                        onLine(pending);
                        if (DateTime.UtcNow >= deadlineUtc)
                            throw new TimeoutException();
                    }
                    return SourceFileReadOutcome.Completed;
                }
                catch (TimeoutException)
                {
                    AppLogger.Warn($"Failed to stream source chat log '{path}' because reading it took more than {SourceLogFileReadTimeout.TotalMinutes:0} minute(s). Skipping the rest of the file.");
                    return SourceFileReadOutcome.TimedOut;
                }
                catch (IOException ex)
                {
                    if (attempt == maxAttempts)
                    {
                        AppLogger.Warn($"Failed to stream source chat log '{path}' after {maxAttempts} attempts.", ex);
                        return SourceFileReadOutcome.Failed;
                    }

                    await Task.Delay(120 * attempt, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to stream source chat log '{path}'.", ex);
                    return SourceFileReadOutcome.Failed;
                }
            }

            return SourceFileReadOutcome.Failed;
        }

        private static async Task<string?> ReadLineWithTimeoutAsync(
            StreamReader reader,
            CancellationToken cancellationToken,
            DateTime deadlineUtc)
        {
            TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException();

            Task<string?> readTask = reader.ReadLineAsync();
            _ = readTask.ContinueWith(
                t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return await readTask.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
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

        private static bool TryExtractDateFromPath(string path, out DateTime date)
        {
            date = DateTime.MinValue;
            string fileName = Path.GetFileName(path);
            Match match = DateFromFileNameRegex.Match(fileName);
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups["y"].Value, out int y) ||
                !int.TryParse(match.Groups["m"].Value, out int m) ||
                !int.TryParse(match.Groups["d"].Value, out int d))
                return false;

            try
            {
                date = new DateTime(y, m, d);
                return true;
            }
            catch
            {
                return false;
            }
        }


        private static void AppendHtmlLine(string path, string title, string htmlLine, ArchiveWriteBatch? pendingArchiveWrites = null)
        {
            if (pendingArchiveWrites != null)
            {
                pendingArchiveWrites.Enqueue(path, title, htmlLine);
                return;
            }

            EnsureInitialized(path, title);
            using var writer = new StreamWriter(path, append: true, Utf8NoBomEncoding);
            writer.WriteLine(htmlLine);
        }

        private sealed class ArchiveWriteBatch : IDisposable
        {
            private const int FlushThresholdChars = 256 * 1024;
            private readonly Dictionary<string, StringBuilder> _bufferByPath = new(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, StreamWriter> _writerByPath = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> TouchedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

            public void Enqueue(string path, string title, string htmlLine)
            {
                if (!_bufferByPath.TryGetValue(path, out StringBuilder? buffer))
                {
                    buffer = new StringBuilder(4096);
                    _bufferByPath[path] = buffer;
                }

                buffer.AppendLine(htmlLine);
                TouchedPaths.Add(path);
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
