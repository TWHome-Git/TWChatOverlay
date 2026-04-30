using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    public sealed class MonthlyLogSourceSnapshot
    {
        public string FileName { get; set; } = string.Empty;

        public long Length { get; set; }

        public DateTime LastWriteTimeUtc { get; set; }
    }

    public sealed class MonthlyReadableLogData
    {
        public MonthlyReadableLogData(
            DateTime monthStart,
            IReadOnlyList<ItemLogSnapshotEntry> itemSnapshots,
            AbaddonMonthlySummarySnapshotEntry abaddonSummary,
            string sourceSignature,
            IReadOnlyList<MonthlyLogSourceSnapshot> sourceFiles)
        {
            MonthStart = monthStart;
            ItemSnapshots = itemSnapshots.ToList();
            AbaddonSummary = abaddonSummary;
            SourceSignature = sourceSignature;
            SourceFiles = sourceFiles.ToList();
        }

        public DateTime MonthStart { get; }

        public List<ItemLogSnapshotEntry> ItemSnapshots { get; set; }

        public AbaddonMonthlySummarySnapshotEntry AbaddonSummary { get; set; }

        public string SourceSignature { get; set; }

        public List<MonthlyLogSourceSnapshot> SourceFiles { get; set; }
    }

    public static class MonthlyReadableLogExportService
    {
        private static readonly string ItemLogDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Itemlog");
        private static readonly object SyncRoot = new();
        private static readonly Dictionary<DateTime, MonthlyReadableLogData> MonthlyDataCache = new();

        private static readonly Regex AbaddonEntryFeeRegex = new(
            @"입장료\s*(?<value>[\d,]+)\s*만\s*Seed",
            RegexOptions.Compiled);
        private static readonly Regex MagicStoneGainRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개",
            RegexOptions.Compiled);
        private static readonly Regex MagicStoneLossRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개를\s*빼앗겼습니다",
            RegexOptions.Compiled);

        public static MonthlyReadableLogData LoadOrBuildMonth(
            DateTime monthStart,
            string? chatLogFolderPath,
            LogAnalysisService logAnalysisService,
            IProgress<MonthlyBuildProgress>? progress = null)
        {
            ArgumentNullException.ThrowIfNull(logAnalysisService);

            lock (SyncRoot)
            {
                Directory.CreateDirectory(ItemLogDirectoryPath);
                CleanupLegacyMonthlySyncFiles();

                var currentSources = EnumerateMonthChatLogInfos(monthStart, chatLogFolderPath).ToList();
                string csvPath = GetMonthlyCsvPath(monthStart);
                string syncPath = GetMonthlySyncStatePath();
                var syncIndex = LoadMonthlySyncIndex(syncPath);
                var syncState = syncIndex.EmptyMonths.FirstOrDefault(state => state.MonthStart.Date == monthStart.Date);
                if (TryGetCachedMonthlyData(monthStart, out var cached))
                {
                    if (currentSources.Count == 0 || SourcesMatch(cached.SourceFiles, currentSources))
                    {
                        DeleteLegacyArtifacts(monthStart);
                        return cached;
                    }
                }

                if (File.Exists(csvPath))
                {
                    var loaded = LoadFromCsv(csvPath);
                    if (loaded != null &&
                        (currentSources.Count == 0 || SourcesMatch(loaded.SourceFiles, currentSources)))
                    {
                        CacheMonthlyData(loaded);
                        DeleteLegacyArtifacts(monthStart);
                        return loaded;
                    }
                }

                if (syncState != null &&
                    (currentSources.Count == 0 || SourcesMatch(syncState.SourceFiles, currentSources)))
                {
                    var emptyMonth = CreateEmptyMonth(monthStart, currentSources, syncState.SourceSignature);
                    CacheMonthlyData(emptyMonth);
                    DeleteLegacyArtifacts(monthStart);
                    return emptyMonth;
                }

                var rebuilt = BuildFromChatLogs(monthStart, currentSources, logAnalysisService, progress);
                WriteCsv(csvPath, rebuilt);
                CacheMonthlyData(rebuilt);
                SaveMonthlySyncState(syncPath, rebuilt);
                DeleteLegacyArtifacts(monthStart);
                return rebuilt;
            }
        }

        public static MonthlyReadableLogData RebuildMonth(
            DateTime monthStart,
            string? chatLogFolderPath,
            LogAnalysisService logAnalysisService,
            IProgress<MonthlyBuildProgress>? progress = null)
        {
            ArgumentNullException.ThrowIfNull(logAnalysisService);

            lock (SyncRoot)
            {
                Directory.CreateDirectory(ItemLogDirectoryPath);
                CleanupLegacyMonthlySyncFiles();

                var currentSources = EnumerateMonthChatLogInfos(monthStart, chatLogFolderPath).ToList();
                var rebuilt = BuildFromChatLogs(monthStart, currentSources, logAnalysisService, progress);
                WriteCsv(GetMonthlyCsvPath(monthStart), rebuilt);
                CacheMonthlyData(rebuilt);
                SaveMonthlySyncState(GetMonthlySyncStatePath(), rebuilt);
                DeleteLegacyArtifacts(monthStart);
                return rebuilt;
            }
        }

        public static MonthlyReadableLogData RefreshCurrentMonthFromTodayOnly(
            DateTime monthStart,
            string? chatLogFolderPath,
            LogAnalysisService logAnalysisService,
            IProgress<MonthlyBuildProgress>? progress = null)
        {
            ArgumentNullException.ThrowIfNull(logAnalysisService);

            lock (SyncRoot)
            {
                Directory.CreateDirectory(ItemLogDirectoryPath);
                CleanupLegacyMonthlySyncFiles();

                DateTime today = DateTime.Today;
                if (monthStart.Year != today.Year || monthStart.Month != today.Month)
                {
                    return LoadOrBuildMonth(monthStart, chatLogFolderPath, logAnalysisService, progress);
                }

                string csvPath = GetMonthlyCsvPath(monthStart);
                var existing = TryGetCachedMonthlyData(monthStart, out var cachedExisting) ? cachedExisting : LoadFromCsv(csvPath);
                if (existing == null)
                {
                    var rebuilt = BuildFromChatLogs(monthStart, EnumerateMonthChatLogInfos(monthStart, chatLogFolderPath).ToList(), logAnalysisService, progress);
                    WriteCsv(csvPath, rebuilt);
                    CacheMonthlyData(rebuilt);
                    SaveMonthlySyncState(GetMonthlySyncStatePath(), rebuilt);
                    DeleteLegacyArtifacts(monthStart);
                    return rebuilt;
                }

                var currentSources = EnumerateMonthChatLogInfos(monthStart, chatLogFolderPath).ToList();
                progress?.Report(new MonthlyBuildProgress(0, 2, "오늘 로그를 다시 분석하는 중..."));
                var preservedSnapshots = existing.ItemSnapshots
                    .Where(snapshot => snapshot.Date.Date < today)
                    .ToList();
                var todaySources = currentSources
                    .Where(source => source.Date.Date == today)
                    .ToList();
                var rebuiltTodaySnapshots = BuildItemSnapshotsFromSources(todaySources, logAnalysisService, progress);

                preservedSnapshots.AddRange(rebuiltTodaySnapshots);
                progress?.Report(new MonthlyBuildProgress(1, 2, "어밴던 합계를 다시 계산하는 중..."));
                var todayAbaddonSummary = BuildAbaddonSummaryFromSources(todaySources, logAnalysisService);
                string abaddonStatePath = GetMonthlyAbaddonTodayStatePath(monthStart);
                var abaddonState = LoadMonthlyAbaddonTodayState(abaddonStatePath);
                if (abaddonState == null)
                {
                    existing.AbaddonSummary = BuildAbaddonSummaryFromSources(currentSources, logAnalysisService);
                }
                else
                {
                    ApplyAbaddonSummaryDelta(existing.AbaddonSummary, abaddonState, -1);
                    ApplyAbaddonSummaryDelta(existing.AbaddonSummary, todayAbaddonSummary, 1);
                }

                existing.ItemSnapshots = preservedSnapshots
                    .OrderBy(snapshot => snapshot.Date)
                    .ThenBy(snapshot => snapshot.DisplayName ?? snapshot.ItemName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                existing.SourceFiles = currentSources
                    .Select(source => new MonthlyLogSourceSnapshot
                    {
                        FileName = source.FileName,
                        Length = source.Length,
                        LastWriteTimeUtc = source.LastWriteTimeUtc
                    })
                    .ToList();
                existing.SourceSignature = ComputeSourceSignature(currentSources);

                WriteCsv(csvPath, existing);
                CacheMonthlyData(existing);
                SaveMonthlyAbaddonTodayState(abaddonStatePath, today, todayAbaddonSummary);
                SaveMonthlySyncState(GetMonthlySyncStatePath(), existing);
                progress?.Report(new MonthlyBuildProgress(2, 2, "달력 데이터를 정리하는 중..."));
                DeleteLegacyArtifacts(monthStart);
                return existing;
            }
        }

        public static MonthlyReadableLogData RefreshCurrentMonthIncremental(
            DateTime monthStart,
            string? chatLogFolderPath,
            LogAnalysisService logAnalysisService)
        {
            ArgumentNullException.ThrowIfNull(logAnalysisService);

            lock (SyncRoot)
            {
                Directory.CreateDirectory(ItemLogDirectoryPath);
                CleanupLegacyMonthlySyncFiles();

                DateTime today = DateTime.Today;
                if (monthStart.Year != today.Year || monthStart.Month != today.Month)
                {
                    return LoadOrBuildMonth(monthStart, chatLogFolderPath, logAnalysisService);
                }

                string csvPath = GetMonthlyCsvPath(monthStart);
                var currentSources = EnumerateMonthChatLogInfos(monthStart, chatLogFolderPath).ToList();
                var existing = TryGetCachedMonthlyData(monthStart, out var cachedExisting) ? cachedExisting : LoadFromCsv(csvPath);
                if (existing == null)
                {
                    var rebuilt = BuildFromChatLogs(monthStart, currentSources, logAnalysisService);
                    WriteCsv(csvPath, rebuilt);
                    CacheMonthlyData(rebuilt);
                    SaveMonthlySyncState(GetMonthlySyncStatePath(), rebuilt);
                    DeleteLegacyArtifacts(monthStart);
                    return rebuilt;
                }

                var existingSourceMap = existing.SourceFiles.ToDictionary(source => source.FileName, StringComparer.OrdinalIgnoreCase);
                var refreshedSnapshots = existing.ItemSnapshots
                    .Where(snapshot => snapshot.Date.Year == monthStart.Year && snapshot.Date.Month == monthStart.Month)
                    .ToList();

                foreach (var source in currentSources)
                {
                    bool isTodayLog = source.Date.Date == today;
                    bool needsRefresh = isTodayLog;

                    if (!needsRefresh)
                    {
                        if (!existingSourceMap.TryGetValue(source.FileName, out var savedSource) ||
                            savedSource.Length != source.Length ||
                            savedSource.LastWriteTimeUtc != source.LastWriteTimeUtc)
                        {
                            needsRefresh = true;
                        }
                    }

                    if (!needsRefresh)
                        continue;

                    refreshedSnapshots.RemoveAll(snapshot => snapshot.Date.Date == source.Date.Date);
                    refreshedSnapshots.AddRange(BuildItemSnapshotsFromSources(new[] { source }, logAnalysisService));
                }

                existing.ItemSnapshots = refreshedSnapshots
                    .OrderBy(snapshot => snapshot.Date)
                    .ThenBy(snapshot => snapshot.DisplayName ?? snapshot.ItemName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                existing.SourceFiles = currentSources
                    .Select(source => new MonthlyLogSourceSnapshot
                    {
                        FileName = source.FileName,
                        Length = source.Length,
                        LastWriteTimeUtc = source.LastWriteTimeUtc
                    })
                    .ToList();
                existing.SourceSignature = ComputeSourceSignature(currentSources);

                WriteCsv(csvPath, existing);
                CacheMonthlyData(existing);
                SaveMonthlySyncState(GetMonthlySyncStatePath(), existing);
                DeleteLegacyArtifacts(monthStart);
                return existing;
            }
        }

        public static MonthlyReadableLogData? LoadFromCsv(DateTime monthStart)
        {
            lock (SyncRoot)
            {
                if (TryGetCachedMonthlyData(monthStart, out var cached))
                    return cached;

                return LoadFromCsv(GetMonthlyCsvPath(monthStart));
            }
        }

        public static void AppendItemSnapshot(DateTime date, LogParser.ParseResult itemLog)
        {
            if (itemLog == null)
                throw new ArgumentNullException(nameof(itemLog));

            if (string.IsNullOrWhiteSpace(itemLog.FormattedText))
                return;

            lock (SyncRoot)
            {
                try
                {
                    DateTime monthStart = new(date.Year, date.Month, 1);
                    var data = TryGetCachedMonthlyData(monthStart, out var cachedData) ? cachedData : LoadFromCsv(monthStart) ?? new MonthlyReadableLogData(
                        monthStart,
                        new List<ItemLogSnapshotEntry>(),
                        new AbaddonMonthlySummarySnapshotEntry { MonthStart = monthStart.Date },
                        string.Empty,
                        new List<MonthlyLogSourceSnapshot>());

                    data.ItemSnapshots.Add(CreateSnapshotFromItemLog(itemLog, date.Date));
                    WriteCsv(GetMonthlyCsvPath(monthStart), data);
                    CacheMonthlyData(data);
                    SaveMonthlySyncState(GetMonthlySyncStatePath(), data);
                    DeleteLegacyArtifacts(monthStart);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to append monthly item snapshot.", ex);
                }
            }
        }

        public static void AppendItemSnapshots(DateTime monthStart, IReadOnlyList<ItemLogSnapshotEntry> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
                return;

            lock (SyncRoot)
            {
                try
                {
                    Directory.CreateDirectory(ItemLogDirectoryPath);
                    var data = TryGetCachedMonthlyData(monthStart, out var cachedData) ? cachedData : LoadFromCsv(monthStart) ?? new MonthlyReadableLogData(
                        monthStart,
                        new List<ItemLogSnapshotEntry>(),
                        new AbaddonMonthlySummarySnapshotEntry { MonthStart = monthStart.Date },
                        string.Empty,
                        new List<MonthlyLogSourceSnapshot>());

                    data.ItemSnapshots.AddRange(snapshots);
                    data.ItemSnapshots = data.ItemSnapshots
                        .OrderBy(snapshot => snapshot.Date)
                        .ThenBy(snapshot => snapshot.DisplayName ?? snapshot.ItemName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    WriteCsv(GetMonthlyCsvPath(monthStart), data);
                    CacheMonthlyData(data);
                    SaveMonthlySyncState(GetMonthlySyncStatePath(), data);
                    DeleteLegacyArtifacts(monthStart);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to append monthly item snapshots.", ex);
                }
            }
        }

        public static void SetAbaddonSummary(DateTime monthStart, AbaddonMonthlySummarySnapshotEntry summary)
        {
            lock (SyncRoot)
            {
                try
                {
                    Directory.CreateDirectory(ItemLogDirectoryPath);
                    var data = TryGetCachedMonthlyData(monthStart, out var cachedData) ? cachedData : LoadFromCsv(monthStart) ?? new MonthlyReadableLogData(
                        monthStart,
                        new List<ItemLogSnapshotEntry>(),
                        new AbaddonMonthlySummarySnapshotEntry { MonthStart = monthStart.Date },
                        string.Empty,
                        new List<MonthlyLogSourceSnapshot>());

                    data.AbaddonSummary = summary;
                    WriteCsv(GetMonthlyCsvPath(monthStart), data);
                    CacheMonthlyData(data);
                    SaveMonthlySyncState(GetMonthlySyncStatePath(), data);
                    DeleteLegacyArtifacts(monthStart);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to update monthly Abaddon summary.", ex);
                }
            }
        }

        public static void CleanupLegacyArtifacts()
        {
            lock (SyncRoot)
            {
                try
                {
                    if (!Directory.Exists(ItemLogDirectoryPath))
                        return;

                    foreach (string path in Directory.EnumerateFiles(ItemLogDirectoryPath))
                    {
                        if (IsLegacyArtifact(path))
                            TryDelete(path);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to clean up legacy monthly artifacts.", ex);
                }
            }
        }

        private static MonthlyReadableLogData BuildFromChatLogs(
            DateTime monthStart,
            IReadOnlyList<MonthlyChatLogSourceInfo> sourceFiles,
            LogAnalysisService logAnalysisService,
            IProgress<MonthlyBuildProgress>? progress = null)
        {
            var itemSnapshots = new List<ItemLogSnapshotEntry>();
            var abaddonSummary = new AbaddonMonthlySummarySnapshotEntry { MonthStart = monthStart.Date };
            int processed = 0;
            int total = sourceFiles.Count;

            foreach (var sourceFile in sourceFiles)
            {
                try
                {
                    using var stream = new FileStream(sourceFile.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream, Encoding.GetEncoding(949));
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var analysis = logAnalysisService.Analyze(line, isRealTime: false);
                        if (!analysis.IsSuccess)
                            continue;

                        TryAccumulateAbaddonSummary(analysis.Parsed.FormattedText, abaddonSummary);

                        if (analysis.HasTrackedItemDrop)
                            itemSnapshots.Add(CreateSnapshotFromItemLog(analysis.Parsed, sourceFile.Date));
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to build monthly logs from '{sourceFile.FullPath}'.", ex);
                }
                finally
                {
                    processed++;
                    progress?.Report(new MonthlyBuildProgress(processed, total, sourceFile.FileName));
                }
            }

            return new MonthlyReadableLogData(
                monthStart.Date,
                itemSnapshots,
                abaddonSummary,
                ComputeSourceSignature(sourceFiles),
                sourceFiles.Select(sourceFile => new MonthlyLogSourceSnapshot
                {
                    FileName = sourceFile.FileName,
                    Length = sourceFile.Length,
                    LastWriteTimeUtc = sourceFile.LastWriteTimeUtc
                }).ToList());
        }

        private static List<ItemLogSnapshotEntry> BuildItemSnapshotsFromSources(
            IReadOnlyList<MonthlyChatLogSourceInfo> sourceFiles,
            LogAnalysisService logAnalysisService,
            IProgress<MonthlyBuildProgress>? progress = null)
        {
            var itemSnapshots = new List<ItemLogSnapshotEntry>();
            int processed = 0;
            int total = sourceFiles.Count;

            foreach (var sourceFile in sourceFiles)
            {
                try
                {
                    using var stream = new FileStream(sourceFile.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream, Encoding.GetEncoding(949));
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var analysis = logAnalysisService.Analyze(line, isRealTime: false);
                        if (!analysis.IsSuccess || !analysis.HasTrackedItemDrop)
                            continue;

                        itemSnapshots.Add(CreateSnapshotFromItemLog(analysis.Parsed, sourceFile.Date));
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to build item snapshots from '{sourceFile.FullPath}'.", ex);
                }
                finally
                {
                    processed++;
                    progress?.Report(new MonthlyBuildProgress(processed, total, sourceFile.FileName));
                }
            }

            return itemSnapshots;
        }

        private static AbaddonMonthlySummarySnapshotEntry BuildAbaddonSummaryFromSources(
            IReadOnlyList<MonthlyChatLogSourceInfo> sourceFiles,
            LogAnalysisService logAnalysisService,
            IProgress<MonthlyBuildProgress>? progress = null)
        {
            var summary = new AbaddonMonthlySummarySnapshotEntry();
            int processed = 0;
            int total = sourceFiles.Count;

            foreach (var sourceFile in sourceFiles)
            {
                try
                {
                    using var stream = new FileStream(sourceFile.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream, Encoding.GetEncoding(949));
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var analysis = logAnalysisService.Analyze(line, isRealTime: false);
                        if (!analysis.IsSuccess)
                            continue;

                        TryAccumulateAbaddonSummary(analysis.Parsed.FormattedText, summary);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to build monthly Abaddon summary from '{sourceFile.FullPath}'.", ex);
                }
                finally
                {
                    processed++;
                    progress?.Report(new MonthlyBuildProgress(processed, total, sourceFile.FileName));
                }
            }

            return summary;
        }

        private static MonthlyReadableLogData? LoadFromCsv(string csvPath)
        {
            if (!File.Exists(csvPath))
                return null;

            try
            {
                var lines = File.ReadAllLines(csvPath, Encoding.UTF8);
                if (lines.Length == 0)
                    return null;

                DateTime monthStart = ParseMonthFromCsvPath(csvPath);
                string sourceSignature = string.Empty;
                var sourceFiles = new List<MonthlyLogSourceSnapshot>();
                var itemSnapshots = new List<ItemLogSnapshotEntry>();
                var abaddonSummary = new AbaddonMonthlySummarySnapshotEntry { MonthStart = monthStart.Date };
                bool sawHeader = false;

                foreach (string rawLine in lines)
                {
                    if (string.IsNullOrWhiteSpace(rawLine))
                        continue;

                    if (rawLine.StartsWith("#", StringComparison.Ordinal))
                    {
                        ParseMetadataLine(rawLine, ref monthStart, ref sourceSignature, sourceFiles);
                        continue;
                    }

                    if (!sawHeader)
                    {
                        sawHeader = rawLine.StartsWith("Kind,Date,Name,DisplayName,Grade,Count,EntryFeeMan,Low,Mid,High,Top,StoneRevenueMan,NetProfitMan", StringComparison.OrdinalIgnoreCase);
                        continue;
                    }

                    var columns = SplitCsvLine(rawLine);
                    if (columns.Count < 13)
                        continue;

                    if (columns[0].Equals("item", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!DateTime.TryParseExact(columns[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
                            continue;

                        string? itemName = string.IsNullOrWhiteSpace(columns[2]) ? null : columns[2];
                        itemSnapshots.Add(new ItemLogSnapshotEntry
                        {
                            Date = date.Date,
                            ItemName = itemName,
                            DisplayName = ResolveDisplayName(
                                itemName,
                                string.IsNullOrWhiteSpace(columns[3]) ? null : columns[3]),
                            Grade = Enum.TryParse(columns[4], out ItemDropGrade grade) ? grade : ItemDropGrade.Normal,
                            Count = int.TryParse(columns[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) ? count : 1
                        });
                    }
                    else if (columns[0].Equals("abaddon", StringComparison.OrdinalIgnoreCase))
                    {
                        if (DateTime.TryParseExact(columns[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime abaddonMonth))
                            abaddonSummary.MonthStart = abaddonMonth.Date;

                        abaddonSummary.TotalEntryFeeMan = TryParseLong(columns[6], out long totalEntryFeeMan) ? totalEntryFeeMan : 0;
                        abaddonSummary.Low = TryParseLong(columns[7], out long low) ? low : 0;
                        abaddonSummary.Mid = TryParseLong(columns[8], out long mid) ? mid : 0;
                        abaddonSummary.High = TryParseLong(columns[9], out long high) ? high : 0;
                        abaddonSummary.Top = TryParseLong(columns[10], out long top) ? top : 0;
                    }
                }

                return new MonthlyReadableLogData(monthStart.Date, itemSnapshots, abaddonSummary, sourceSignature, sourceFiles);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load monthly CSV '{csvPath}'.", ex);
                return null;
            }
        }

        private static void WriteCsv(string csvPath, MonthlyReadableLogData data)
        {
            try
            {
                if (!HasAnyData(data))
                {
                    TryDelete(csvPath);
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine($"# month={data.MonthStart:yyyy-MM}");
                sb.AppendLine($"# sourceSignature={data.SourceSignature}");
                foreach (var sourceFile in data.SourceFiles)
                {
                    sb.AppendLine($"# sourceFile={sourceFile.FileName}|{sourceFile.Length}|{sourceFile.LastWriteTimeUtc.Ticks}");
                }

                sb.AppendLine("Kind,Date,Name,DisplayName,Grade,Count,EntryFeeMan,Low,Mid,High,Top,StoneRevenueMan,NetProfitMan");

                foreach (var snapshot in data.ItemSnapshots
                             .OrderBy(snapshot => snapshot.Date)
                             .ThenBy(snapshot => snapshot.DisplayName ?? snapshot.ItemName ?? string.Empty))
                {
                    sb.Append("item,");
                    sb.Append(EscapeCsv(snapshot.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                    sb.Append(',');
                    sb.Append(EscapeCsv(snapshot.ItemName ?? string.Empty));
                    sb.Append(',');
                    sb.Append(EscapeCsv(snapshot.DisplayName ?? string.Empty));
                    sb.Append(',');
                    sb.Append(EscapeCsv(snapshot.Grade.ToString()));
                    sb.Append(',');
                    sb.Append(snapshot.Count);
                    sb.AppendLine(",,,,,,,");
                }

                if (data.AbaddonSummary != null)
                {
                    sb.Append("abaddon,");
                    sb.Append(EscapeCsv(data.MonthStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
                    sb.Append(',');
                    sb.Append(EscapeCsv("어밴던로드"));
                    sb.Append(',');
                    sb.Append(EscapeCsv("어밴던로드"));
                    sb.Append(",,,");
                    sb.Append(data.AbaddonSummary.TotalEntryFeeMan);
                    sb.Append(',');
                    sb.Append(data.AbaddonSummary.Low);
                    sb.Append(',');
                    sb.Append(data.AbaddonSummary.Mid);
                    sb.Append(',');
                    sb.Append(data.AbaddonSummary.High);
                    sb.Append(',');
                    sb.Append(data.AbaddonSummary.Top);
                    sb.Append(',');
                    sb.Append(data.AbaddonSummary.StoneRevenueMan);
                    sb.Append(',');
                    sb.AppendLine(data.AbaddonSummary.NetProfitMan.ToString(CultureInfo.InvariantCulture));
                }

                File.WriteAllText(csvPath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to write monthly CSV '{csvPath}'.", ex);
            }
        }

        private static bool HasAnyData(MonthlyReadableLogData data)
        {
            if (data.ItemSnapshots.Count > 0)
                return true;

            if (data.AbaddonSummary == null)
                return false;

            return data.AbaddonSummary.TotalEntryFeeMan != 0 ||
                   data.AbaddonSummary.Low != 0 ||
                   data.AbaddonSummary.Mid != 0 ||
                   data.AbaddonSummary.High != 0 ||
                   data.AbaddonSummary.Top != 0 ||
                   data.AbaddonSummary.StoneRevenueMan != 0 ||
                   data.AbaddonSummary.NetProfitMan != 0;
        }

        private static MonthlyReadableLogData CreateEmptyMonth(
            DateTime monthStart,
            IReadOnlyList<MonthlyChatLogSourceInfo> sourceFiles,
            string sourceSignature)
        {
            return new MonthlyReadableLogData(
                monthStart.Date,
                new List<ItemLogSnapshotEntry>(),
                new AbaddonMonthlySummarySnapshotEntry { MonthStart = monthStart.Date },
                sourceSignature,
                sourceFiles.Select(sourceFile => new MonthlyLogSourceSnapshot
                {
                    FileName = sourceFile.FileName,
                    Length = sourceFile.Length,
                    LastWriteTimeUtc = sourceFile.LastWriteTimeUtc
                }).ToList());
        }

        private static bool TryGetCachedMonthlyData(DateTime monthStart, out MonthlyReadableLogData data)
        {
            return MonthlyDataCache.TryGetValue(monthStart.Date, out data!);
        }

        private static void CacheMonthlyData(MonthlyReadableLogData data)
        {
            MonthlyDataCache[data.MonthStart.Date] = data;
        }

        private static MonthlySyncIndex LoadMonthlySyncIndex(string syncPath)
        {
            if (!File.Exists(syncPath))
                return new MonthlySyncIndex();

            try
            {
                string json = File.ReadAllText(syncPath, Encoding.UTF8);
                using var document = JsonDocument.Parse(json);

                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    (document.RootElement.TryGetProperty(nameof(MonthlySyncIndex.EmptyMonths), out _) ||
                     document.RootElement.TryGetProperty("emptyMonths", out _)))
                {
                    return NormalizeMonthlySyncIndex(JsonSerializer.Deserialize<MonthlySyncIndex>(json) ?? new MonthlySyncIndex());
                }

                var legacyState = JsonSerializer.Deserialize<MonthlySyncState>(json);
                if (legacyState == null)
                    return new MonthlySyncIndex();

                var index = new MonthlySyncIndex();
                if (!legacyState.HasAnyData)
                    index.EmptyMonths.Add(legacyState);

                return NormalizeMonthlySyncIndex(index);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load monthly sync state '{syncPath}'.", ex);
                return new MonthlySyncIndex();
            }
        }

        private static MonthlyAbaddonTodayState? LoadMonthlyAbaddonTodayState(string statePath)
        {
            if (!File.Exists(statePath))
                return null;

            try
            {
                return JsonSerializer.Deserialize<MonthlyAbaddonTodayState>(File.ReadAllText(statePath, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load monthly Abaddon daily state '{statePath}'.", ex);
                return null;
            }
        }

        private static void SaveMonthlySyncState(string syncPath, MonthlyReadableLogData data)
        {
            try
            {
                Directory.CreateDirectory(ItemLogDirectoryPath);
                var index = LoadMonthlySyncIndex(syncPath);
                DateTime monthStart = data.MonthStart.Date;
                index.EmptyMonths.RemoveAll(state => state.MonthStart.Date == monthStart);

                if (!HasAnyData(data))
                {
                    index.EmptyMonths.Add(new MonthlySyncState
                    {
                        MonthStart = monthStart,
                        SourceSignature = data.SourceSignature,
                        SourceFiles = data.SourceFiles.ToList()
                    });
                }

                index = NormalizeMonthlySyncIndex(index);
                if (index.EmptyMonths.Count == 0)
                {
                    TryDelete(syncPath);
                    return;
                }

                File.WriteAllText(syncPath, JsonSerializer.Serialize(index), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to save monthly sync state '{syncPath}'.", ex);
            }
        }

        private static void SaveMonthlyAbaddonTodayState(
            string statePath,
            DateTime contributionDate,
            AbaddonMonthlySummarySnapshotEntry summary)
        {
            try
            {
                Directory.CreateDirectory(ItemLogDirectoryPath);
                var state = new MonthlyAbaddonTodayState
                {
                    MonthStart = contributionDate.Date,
                    ContributionDate = contributionDate.Date,
                    TotalEntryFeeMan = summary.TotalEntryFeeMan,
                    Low = summary.Low,
                    Mid = summary.Mid,
                    High = summary.High,
                    Top = summary.Top
                };

                File.WriteAllText(statePath, JsonSerializer.Serialize(state), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to save monthly Abaddon daily state '{statePath}'.", ex);
            }
        }

        private static void ApplyAbaddonSummaryDelta(
            AbaddonMonthlySummarySnapshotEntry target,
            MonthlyAbaddonTodayState delta,
            int sign)
        {
            target.TotalEntryFeeMan += delta.TotalEntryFeeMan * sign;
            target.Low += delta.Low * sign;
            target.Mid += delta.Mid * sign;
            target.High += delta.High * sign;
            target.Top += delta.Top * sign;
        }

        private static void ApplyAbaddonSummaryDelta(
            AbaddonMonthlySummarySnapshotEntry target,
            AbaddonMonthlySummarySnapshotEntry delta,
            int sign)
        {
            target.TotalEntryFeeMan += delta.TotalEntryFeeMan * sign;
            target.Low += delta.Low * sign;
            target.Mid += delta.Mid * sign;
            target.High += delta.High * sign;
            target.Top += delta.Top * sign;
        }

        private static void ParseMetadataLine(
            string line,
            ref DateTime monthStart,
            ref string sourceSignature,
            List<MonthlyLogSourceSnapshot> sourceFiles)
        {
            string payload = line.Substring(1).Trim();
            int separatorIndex = payload.IndexOf('=');
            if (separatorIndex <= 0)
                return;

            string key = payload[..separatorIndex].Trim();
            string value = payload[(separatorIndex + 1)..].Trim();

            if (key.Equals("month", StringComparison.OrdinalIgnoreCase) &&
                DateTime.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime parsedMonth))
            {
                monthStart = parsedMonth;
                return;
            }

            if (key.Equals("sourceSignature", StringComparison.OrdinalIgnoreCase))
            {
                sourceSignature = value;
                return;
            }

            if (key.Equals("sourceFile", StringComparison.OrdinalIgnoreCase))
            {
                var parts = value.Split('|');
                if (parts.Length != 3)
                    return;

                if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long length))
                    return;

                if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks))
                    return;

                sourceFiles.Add(new MonthlyLogSourceSnapshot
                {
                    FileName = parts[0],
                    Length = length,
                    LastWriteTimeUtc = new DateTime(ticks, DateTimeKind.Utc)
                });
            }
        }

        private static List<string> SplitCsvLine(string line)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int index = 0; index < line.Length; index++)
            {
                char c = line[index];
                if (inQuotes)
                {
                    if (c == '"' && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                        continue;
                    }

                    if (c == '"')
                    {
                        inQuotes = false;
                        continue;
                    }

                    current.Append(c);
                    continue;
                }

                if (c == ',')
                {
                    values.Add(current.ToString());
                    current.Clear();
                    continue;
                }

                if (c == '"')
                {
                    inQuotes = true;
                    continue;
                }

                current.Append(c);
            }

            values.Add(current.ToString());
            return values;
        }

        private static void TryAccumulateAbaddonSummary(string formattedText, AbaddonMonthlySummarySnapshotEntry summary)
            => AbaddonSummaryCalculator.TryAccumulate(formattedText, summary);

        private static ItemLogSnapshotEntry CreateSnapshotFromItemLog(LogParser.ParseResult itemLog, DateTime date)
        {
            return new ItemLogSnapshotEntry
            {
                Date = date.Date,
                ItemName = itemLog.TrackedItemName,
                DisplayName = ResolveDisplayName(itemLog.TrackedItemName, "Item"),
                Grade = itemLog.TrackedItemGrade,
                Count = Math.Max(1, itemLog.TrackedItemCount),
                FormattedText = itemLog.FormattedText
            };
        }

        private static string ResolveDisplayName(string? itemName, string? fallback)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return fallback ?? string.Empty;

            string resolved = DropItemResolver.GetTrackedItemDisplayName(itemName);
            return string.IsNullOrWhiteSpace(resolved) ? (fallback ?? itemName) : resolved;
        }

        private static void ApplyMagicStoneDelta(AbaddonMonthlySummarySnapshotEntry summary, string grade, long delta)
        {
            switch (grade)
            {
                case "하급":
                    summary.Low += delta;
                    break;
                case "중급":
                    summary.Mid += delta;
                    break;
                case "상급":
                    summary.High += delta;
                    break;
                case "최상급":
                    summary.Top += delta;
                    break;
            }
        }

        private static bool TryParseLong(string raw, out long value)
            => long.TryParse(raw.Replace(",", string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static string EscapeCsv(string value)
        {
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
                return $"\"{value.Replace("\"", "\"\"")}\"";

            return value;
        }

        private static string ComputeSourceSignature(IEnumerable<MonthlyChatLogSourceInfo> sourceFiles)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var sourceFile in sourceFiles)
            {
                string fingerprint = $"{sourceFile.FileName}|{sourceFile.Length}|{sourceFile.LastWriteTimeUtc.Ticks}";
                hash.AppendData(Encoding.UTF8.GetBytes(fingerprint));
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }

        private static bool SourcesMatch(IReadOnlyList<MonthlyLogSourceSnapshot> savedFiles, IReadOnlyList<MonthlyChatLogSourceInfo> currentFiles)
        {
            if (savedFiles.Count != currentFiles.Count)
                return false;

            for (int index = 0; index < savedFiles.Count; index++)
            {
                var saved = savedFiles[index];
                var current = currentFiles[index];
                if (!string.Equals(saved.FileName, current.FileName, StringComparison.OrdinalIgnoreCase) ||
                    saved.Length != current.Length ||
                    saved.LastWriteTimeUtc != current.LastWriteTimeUtc)
                {
                    return false;
                }
            }

            return true;
        }

        private static IEnumerable<MonthlyChatLogSourceInfo> EnumerateMonthChatLogInfos(DateTime monthStart, string? chatLogFolderPath)
        {
            if (string.IsNullOrWhiteSpace(chatLogFolderPath) || !Directory.Exists(chatLogFolderPath))
                yield break;

            foreach (string path in Directory.EnumerateFiles(chatLogFolderPath, "TWChatLog_*.html")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                DateTime logDate = ExtractDateFromChatLogPath(path);
                if (logDate == DateTime.MinValue ||
                    logDate.Year != monthStart.Year ||
                    logDate.Month != monthStart.Month)
                {
                    continue;
                }

                FileInfo info = new(path);
                yield return new MonthlyChatLogSourceInfo
                {
                    FullPath = path,
                    FileName = info.Name,
                    Length = info.Length,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    Date = logDate
                };
            }
        }

        private static DateTime ExtractDateFromChatLogPath(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = Regex.Match(fileName, @"(?<y>\d{4})_(?<m>\d{2})_(?<d>\d{2})$");
            if (!match.Success)
                return DateTime.MinValue;

            if (!int.TryParse(match.Groups["y"].Value, out int year) ||
                !int.TryParse(match.Groups["m"].Value, out int month) ||
                !int.TryParse(match.Groups["d"].Value, out int day))
            {
                return DateTime.MinValue;
            }

            return new DateTime(year, month, day);
        }

        private static DateTime ParseMonthFromCsvPath(string csvPath)
        {
            string fileName = Path.GetFileNameWithoutExtension(csvPath);
            var match = Regex.Match(fileName, @"ItemLog-(?<y>\d{2})-(?<m>\d{2})$");
            if (!match.Success)
                return DateTime.Today;

            if (!int.TryParse(match.Groups["y"].Value, out int year) ||
                !int.TryParse(match.Groups["m"].Value, out int month))
            {
                return DateTime.Today;
            }

            return new DateTime(2000 + year, month, 1);
        }

        private static void DeleteLegacyArtifacts(DateTime monthStart)
        {
            TryDelete(Path.Combine(ItemLogDirectoryPath, $"ItemLog_{monthStart:yyyy_MM}.jsonl"));
            TryDelete(Path.Combine(ItemLogDirectoryPath, $"ItemLogSummary_{monthStart:yyyy_MM}.csv"));
            TryDelete(Path.Combine(ItemLogDirectoryPath, $"ItemLogSummary_{monthStart:yyyy_MM}.txt"));
            TryDelete(Path.Combine(ItemLogDirectoryPath, $"AbaddonSummary_{monthStart:yyyy_MM}.json"));
            TryDelete(Path.Combine(ItemLogDirectoryPath, $"AbaddonSummary_{monthStart:yyyy_MM}.sync.json"));
            TryDelete(Path.Combine(ItemLogDirectoryPath, $"AbaddonSummary_{monthStart:yyyy_MM}.csv"));
            TryDelete(Path.Combine(ItemLogDirectoryPath, $"AbaddonSummaryView_{monthStart:yyyy_MM}.txt"));
            TryDelete(Path.Combine(ItemLogDirectoryPath, $"ItemLog-{monthStart:yy-MM}.txt"));
        }

        private static bool IsLegacyArtifact(string path)
        {
            string fileName = Path.GetFileName(path);
            return
                (fileName.StartsWith("ItemLog_", StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)) ||
                fileName.StartsWith("ItemLogSummary_", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("AbaddonSummary_", StringComparison.OrdinalIgnoreCase) ||
                fileName.StartsWith("AbaddonSummaryView_", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to delete legacy artifact '{path}'.", ex);
            }
        }

        private static string GetMonthlyCsvPath(DateTime date)
            => Path.Combine(ItemLogDirectoryPath, $"ItemLog-{date:yy-MM}.csv");

        private static string GetMonthlySyncStatePath()
            => Path.Combine(ItemLogDirectoryPath, "ItemLog.sync.json");

        private static string GetMonthlyAbaddonTodayStatePath(DateTime date)
            => Path.Combine(ItemLogDirectoryPath, $"MonthlyAbaddonSummary_{date:yyyy_MM}.today.json");

        private static void CleanupLegacyMonthlySyncFiles()
        {
            try
            {
                if (!Directory.Exists(ItemLogDirectoryPath))
                    return;

                foreach (string path in Directory.EnumerateFiles(ItemLogDirectoryPath, "ItemLog_*.sync.json"))
                    TryDelete(path);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to clean legacy monthly sync files.", ex);
            }
        }

        private static MonthlySyncIndex NormalizeMonthlySyncIndex(MonthlySyncIndex index)
        {
            var normalized = new MonthlySyncIndex();

            foreach (var state in index.EmptyMonths
                         .Where(state => state != null)
                         .GroupBy(state => state.MonthStart.Date)
                         .Select(group => group.Last())
                         .OrderBy(state => state.MonthStart))
            {
                normalized.EmptyMonths.Add(new MonthlySyncState
                {
                    MonthStart = state.MonthStart.Date,
                    SourceSignature = state.SourceSignature ?? string.Empty,
                    SourceFiles = state.SourceFiles
                        .Select(sourceFile => new MonthlyLogSourceSnapshot
                        {
                            FileName = sourceFile.FileName,
                            Length = sourceFile.Length,
                            LastWriteTimeUtc = sourceFile.LastWriteTimeUtc
                        })
                        .ToList()
                });
            }

            return normalized;
        }

        private sealed class MonthlySyncIndex
        {
            public List<MonthlySyncState> EmptyMonths { get; set; } = new();
        }

        private sealed class MonthlySyncState
        {
            public DateTime MonthStart { get; set; }

            public string SourceSignature { get; set; } = string.Empty;

            public bool HasAnyData { get; set; }

            public List<MonthlyLogSourceSnapshot> SourceFiles { get; set; } = new();
        }

        private sealed class MonthlyAbaddonTodayState
        {
            public DateTime MonthStart { get; set; }

            public DateTime ContributionDate { get; set; }

            public long TotalEntryFeeMan { get; set; }

            public long Low { get; set; }

            public long Mid { get; set; }

            public long High { get; set; }

            public long Top { get; set; }
        }

        private sealed class MonthlyChatLogSourceInfo
        {
            public string FullPath { get; set; } = string.Empty;

            public string FileName { get; set; } = string.Empty;

            public long Length { get; set; }

            public DateTime LastWriteTimeUtc { get; set; }

            public DateTime Date { get; set; }
        }

        public sealed record MonthlyBuildProgress(int Processed, int Total, string CurrentFileName)
        {
            public int Percent => Total <= 0 ? 100 : (int)Math.Round((double)Processed * 100.0 / Total);
        }
    }
}
