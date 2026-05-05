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

        private static readonly UTF8Encoding Utf8BomEncoding = new(encoderShouldEmitUTF8Identifier: true);
        private static readonly UTF8Encoding Utf8NoBomEncoding = new(encoderShouldEmitUTF8Identifier: false);
        private static readonly Regex LineSplitRegex = new(@"</?br\s*>|\r?\n", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ShoutLineRegex = new(
            @"^\s*<font[^>]*color=[""']?#?(?:white|ffffff)[""']?[^>]*>\s*(?<time>\[[^<]+?\])\s*</font>\s*<font[^>]*color=[""']?#?c896c8[""']?[^>]*>(?<content>.*?)</font>\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
        private const string RawLogCheckpointFileName = "raw-log-rebuild.checkpoint";
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

        public async Task EnsureInitializedFromRawLogsAsync(
            string chatLogFolderPath,
            LogAnalysisService logAnalysisService,
            Func<string, bool> isContentRelevant,
            Action<string, int, int>? onProgressText = null)
        {
            if (string.IsNullOrWhiteSpace(chatLogFolderPath) || !Directory.Exists(chatLogFolderPath))
                return;

            if (logAnalysisService == null || isContentRelevant == null)
                return;

            await _buildGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var allSourceFiles = DiscoverSourceLogFiles(chatLogFolderPath);
                if (allSourceFiles.Count == 0)
                    return;

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

                foreach (SourceLogFile source in allSourceFiles)
                {
                    DateTime day = source.Date.Date;
                    string dayKey = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    string shoutPath = Path.Combine(_shoutDirectory, $"{dayKey}.html");
                    if (!File.Exists(shoutPath))
                        missingShoutDates.Add(day);

                    string expPath = Path.Combine(_expDirectory, $"{dayKey}.html");
                    if (!File.Exists(expPath))
                        missingExpDates.Add(day);

                    string itemPath = Path.Combine(_itemDirectory, $"{dayKey}.html");
                    if (!File.Exists(itemPath))
                        missingItemDays.Add(day);

                    string weekKey = GetIsoWeekKey(day);
                    string contentPath = Path.Combine(_contentDirectory, $"{weekKey}.html");
                    if (!File.Exists(contentPath))
                        missingContentWeeks.Add(weekKey);

                    string AbandonPath = Path.Combine(_AbandonDirectory, $"{dayKey}.html");
                    if (!File.Exists(AbandonPath))
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
                EnsureMissingArchiveFiles(
                    missingShoutDates,
                    missingExpDates,
                    missingItemDays,
                    missingContentWeeks,
                    missingAbandonDays);

                await BuildFromRawLogsAsync(
                    sourceFiles,
                    logAnalysisService,
                    isContentRelevant,
                    onProgressText,
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

        private async Task BuildFromRawLogsAsync(
            IReadOnlyList<SourceLogFile> sourceFiles,
            LogAnalysisService logAnalysisService,
            Func<string, bool> isContentRelevant,
            Action<string, int, int>? onProgressText,
            HashSet<DateTime> missingShoutDates,
            HashSet<DateTime> missingExpDates,
            HashSet<DateTime> missingItemDays,
            HashSet<string> missingContentWeeks,
            HashSet<DateTime> missingAbandonDays,
            HashSet<DateTime> missingAbandonSummaryDays)
        {
            DropItemResolver.DropItemFilterSnapshot filterSnapshot = await DropItemResolver.LoadDefaultFilterSnapshotAsync().ConfigureAwait(false);
            _abandonDayBuffer.Clear();
            var pendingArchiveWrites = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            int total = sourceFiles.Count;
            for (int i = 0; i < sourceFiles.Count; i++)
            {
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

                if (!TryReadSourceLogContent(source.Path, out string rawContent))
                    continue;

                var lines = SplitLogLines(rawContent);
                foreach (string line in lines)
                {
                    var analysis = logAnalysisService.Analyze(line, isRealTime: false, filterSnapshot);
                    if (!analysis.IsSuccess)
                        continue;

                    string formattedText = analysis.Parsed.FormattedText;
                    if (TryExtractMagicStoneGain(formattedText, out string stoneName, out int stoneCount))
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
                        UpdateAbandonDailySummary(logDate, gainDelta);
                        continue;
                    }

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
                }

                FlushPendingArchiveWrites(pendingArchiveWrites);
                SaveRawLogRebuildCheckpoint(logDate);
            }
            FlushPendingArchiveWrites(pendingArchiveWrites);
            FlushAbandonDayBuffer();
        }

        private static List<SourceLogFile> DiscoverSourceLogFiles(string chatLogFolderPath)
        {
            return Directory.EnumerateFiles(chatLogFolderPath, "TWChatLog_*.html")
                .Select(path => new { Path = path, DateFound = TryExtractDateFromPath(path, out DateTime date), Date = date })
                .Where(x => x.DateFound)
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
            string checkpointPath = Path.Combine(_stateDirectory, RawLogCheckpointFileName);
            if (!File.Exists(checkpointPath))
                return null;

            try
            {
                string text = File.ReadAllText(checkpointPath, Encoding.UTF8).Trim();
                if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out DateTime parsed))
                    return parsed.Date;
            }
            catch
            {
            }

            return null;
        }

        private void SaveRawLogRebuildCheckpoint(DateTime completedDate)
        {
            try
            {
                Directory.CreateDirectory(_stateDirectory);
                string checkpointPath = Path.Combine(_stateDirectory, RawLogCheckpointFileName);
                File.WriteAllText(checkpointPath, completedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to save raw log rebuild checkpoint for {completedDate:yyyy-MM-dd}.", ex);
            }
        }

        private void EnsureMissingArchiveFiles(
            IEnumerable<DateTime> missingShoutDates,
            IEnumerable<DateTime> missingExpDates,
            IEnumerable<DateTime> missingItemDays,
            IEnumerable<string> missingContentWeeks,
            IEnumerable<DateTime> missingAbandonDays)
        {
            foreach (DateTime date in missingShoutDates.Distinct().OrderBy(d => d))
            {
                string dateKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string path = Path.Combine(_shoutDirectory, $"{dateKey}.html");
                EnsureInitialized(path, $"Shout {dateKey}");
            }

            foreach (DateTime date in missingExpDates.Distinct().OrderBy(d => d))
            {
                string dateKey = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string path = Path.Combine(_expDirectory, $"{dateKey}.html");
                EnsureInitialized(path, $"Exp {dateKey}");
            }

            foreach (DateTime day in missingItemDays.Distinct().OrderBy(d => d))
            {
                string dayKey = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string path = Path.Combine(_itemDirectory, $"{dayKey}.html");
                EnsureInitialized(path, $"Item {dayKey}");
            }

            foreach (string weekKey in missingContentWeeks.Distinct(StringComparer.Ordinal).OrderBy(w => w, StringComparer.Ordinal))
            {
                string path = Path.Combine(_contentDirectory, $"{weekKey}.html");
                EnsureInitialized(path, $"Content {weekKey}");
            }

            foreach (DateTime day in missingAbandonDays.Distinct().OrderBy(d => d))
            {
                string dayKey = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                string path = Path.Combine(_AbandonDirectory, $"{dayKey}.html");
                EnsureInitialized(path, $"Abandon {dayKey}");
            }
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
            Dictionary<string, List<string>>? pendingArchiveWrites = null)
        {
            var parsed = analysis.Parsed;
            if (parsed == null || string.IsNullOrWhiteSpace(parsed.FormattedText))
                return;

            string text = parsed.FormattedText;

            if (writeShout && parsed.Category == ChatCategory.Shout)
            {
                AppendShoutLog(logDate, text, pendingArchiveWrites);
            }

            if (writeItem)
            {
                if (analysis.HasTrackedItemDrop && !string.IsNullOrWhiteSpace(parsed.TrackedItemName))
                {
                    AppendItemLog(logDate, text, parsed.TrackedItemName!, parsed.TrackedItemGrade.ToString(), Math.Max(1, parsed.TrackedItemCount));
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
                        UpdateAbandonDailySummary(logDate, gainDelta, flushImmediately: true);
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

        private void AppendShoutLog(DateTime logDate, string text, Dictionary<string, List<string>>? pendingArchiveWrites = null)
        {
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_shoutDirectory, $"{dateKey}.html");
            string entry = $"<div class=\"log shout\" data-date=\"{dateKey}\">{WebUtility.HtmlEncode(text)}</div>";
            AppendHtmlLine(path, $"Shout {dateKey}", entry, pendingArchiveWrites);
        }

        private void AppendItemLog(DateTime logDate, string text, string itemName, string itemGrade, int itemCount, Dictionary<string, List<string>>? pendingArchiveWrites = null)
        {
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_itemDirectory, $"{dateKey}.html");
            string entry = $"<div class=\"log item\" data-date=\"{dateKey}\" data-item-name=\"{WebUtility.HtmlEncode(itemName)}\" data-item-grade=\"{WebUtility.HtmlEncode(itemGrade)}\" data-item-count=\"{Math.Max(1, itemCount)}\">{WebUtility.HtmlEncode(text)}</div>";
            AppendHtmlLine(path, $"Item {dateKey}", entry, pendingArchiveWrites);
        }

        private void AppendContentLog(DateTime logDate, string text, Dictionary<string, List<string>>? pendingArchiveWrites = null)
        {
            string weekKey = GetIsoWeekKey(logDate);
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_contentDirectory, $"{weekKey}.html");
            string entry = $"<div class=\"log content\" data-date=\"{dateKey}\">{WebUtility.HtmlEncode(text)}</div>";
            AppendHtmlLine(path, $"Content {weekKey}", entry, pendingArchiveWrites);
        }

        private void AppendExpLog(DateTime logDate, string text, Dictionary<string, List<string>>? pendingArchiveWrites = null)
        {
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_expDirectory, $"{dateKey}.html");
            string encodedText = WebUtility.HtmlEncode(text);
            string entry = $"<div class=\"log exp\" data-date=\"{dateKey}\">{encodedText}</div>";
            AppendHtmlLine(path, $"Exp {dateKey}", entry, pendingArchiveWrites);
        }

        private void AppendAbandonLog(DateTime logDate, string text, Dictionary<string, List<string>>? pendingArchiveWrites = null)
        {
            string dateKey = logDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string path = Path.Combine(_AbandonDirectory, $"{dateKey}.html");
            string entry = $"<div class=\"log Abandon\" data-date=\"{dateKey}\">{WebUtility.HtmlEncode(text)}</div>";
            AppendHtmlLine(path, $"Abandon {dateKey}", entry, pendingArchiveWrites);

            if (TryBuildAbandonDelta(text, out AbandonMonthlySummarySnapshotEntry delta))
                UpdateAbandonDailySummary(logDate, delta, flushImmediately: true);
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

        private static bool TryReadSourceLogContent(string path, out string rawContent)
        {
            rawContent = string.Empty;
            Encoding encoding = Encoding.GetEncoding(949);

            const int maxAttempts = 5;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
                    rawContent = reader.ReadToEnd();
                    return true;
                }
                catch (IOException ex)
                {
                    if (attempt == maxAttempts)
                    {
                        AppLogger.Warn($"Failed to read source chat log '{path}' after {maxAttempts} attempts.", ex);
                        return false;
                    }

                    Thread.Sleep(120 * attempt);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to read source chat log '{path}'.", ex);
                    return false;
                }
            }

            return false;
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

        private static List<string> SplitLogLines(string rawContent)
        {
            if (string.IsNullOrWhiteSpace(rawContent))
                return new List<string>();

            var lines = LineSplitRegex.Split(rawContent)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => line.Trim())
                .ToList();
            return MergeWrappedShoutLines(lines);
        }

        private static List<string> MergeWrappedShoutLines(IReadOnlyList<string> lines)
        {
            if (lines.Count <= 1)
                return lines.ToList();

            var merged = new List<string>(lines.Count);
            int index = 0;
            while (index < lines.Count)
            {
                string current = lines[index].Trim();
                if (index + 1 < lines.Count &&
                    TryMergeWrappedShout(current, lines[index + 1], out string joined))
                {
                    merged.Add(joined);
                    index += 2;
                    continue;
                }

                merged.Add(current);
                index++;
            }

            return merged;
        }

        private static bool TryMergeWrappedShout(string firstLine, string secondLine, out string mergedLine)
        {
            mergedLine = firstLine;

            var first = ShoutLineRegex.Match(firstLine);
            var second = ShoutLineRegex.Match(secondLine);
            if (!first.Success || !second.Success)
                return false;

            string time1 = NormalizeWhitespace(WebUtility.HtmlDecode(first.Groups["time"].Value));
            string time2 = NormalizeWhitespace(WebUtility.HtmlDecode(second.Groups["time"].Value));
            if (!string.Equals(time1, time2, StringComparison.Ordinal))
                return false;

            string content1 = first.Groups["content"].Value;
            string content2 = second.Groups["content"].Value;
            string plain1 = NormalizeWhitespace(WebUtility.HtmlDecode(content1));
            string plain2 = NormalizeWhitespace(WebUtility.HtmlDecode(content2));

            bool firstLooksLikeShout = plain1.Contains("외치기", StringComparison.OrdinalIgnoreCase);
            bool secondLooksLikeContinuation = !plain2.Contains("외치기", StringComparison.OrdinalIgnoreCase);
            if (!firstLooksLikeShout || !secondLooksLikeContinuation)
                return false;

            mergedLine = $@"<font color=""white"">{first.Groups["time"].Value}</font><font color=""#c896c8"">{content1}{content2}</font>";
            return true;
        }

        private static string NormalizeWhitespace(string text)
            => Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();

        private static void AppendHtmlLine(string path, string title, string htmlLine, Dictionary<string, List<string>>? pendingArchiveWrites = null)
        {
            if (pendingArchiveWrites != null)
            {
                if (!pendingArchiveWrites.TryGetValue(path, out List<string>? lines))
                {
                    lines = new List<string>();
                    pendingArchiveWrites[path] = lines;
                }

                lines.Add(htmlLine);
                return;
            }

            EnsureInitialized(path, title);
            using var writer = new StreamWriter(path, append: true, Utf8NoBomEncoding);
            writer.WriteLine(htmlLine);
        }

        private static void FlushPendingArchiveWrites(Dictionary<string, List<string>> pendingArchiveWrites)
        {
            if (pendingArchiveWrites.Count == 0)
                return;

            foreach (var kv in pendingArchiveWrites)
            {
                string path = kv.Key;
                List<string> lines = kv.Value;
                if (lines.Count == 0)
                    continue;

                string title = Path.GetFileNameWithoutExtension(path) ?? "Log";
                EnsureInitialized(path, title);
                using var writer = new StreamWriter(path, append: true, Utf8NoBomEncoding);
                foreach (string line in lines)
                    writer.WriteLine(line);
            }

            pendingArchiveWrites.Clear();
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
