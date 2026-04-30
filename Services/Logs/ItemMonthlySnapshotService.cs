using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    public static class ItemMonthlySnapshotService
    {
        private static readonly string ItemLogDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Itemlog");
        private static readonly object SyncRoot = new();

        public static void CleanupLegacyMonthly화TextLogs()
        {
            MonthlyReadableLogExportService.CleanupLegacyArtifacts();
        }

        public static IReadOnlyList<ItemLogSnapshotEntry> LoadOrBuildMonthlySnapshots(
            DateTime monthStart,
            string? chatLogFolderPath,
            LogAnalysisService logAnalysisService)
        {
            ArgumentNullException.ThrowIfNull(logAnalysisService);

            lock (SyncRoot)
            {
                var data = MonthlyReadableLogExportService.LoadOrBuildMonth(monthStart, chatLogFolderPath, logAnalysisService);
                return data.ItemSnapshots;
            }
        }

        public static IReadOnlyList<ItemLogSnapshotEntry> RebuildMonthlySnapshots(
            DateTime monthStart,
            string? chatLogFolderPath,
            LogAnalysisService logAnalysisService)
        {
            ArgumentNullException.ThrowIfNull(logAnalysisService);

            lock (SyncRoot)
            {
                var data = MonthlyReadableLogExportService.RebuildMonth(monthStart, chatLogFolderPath, logAnalysisService);
                return data.ItemSnapshots;
            }
        }

        public static IReadOnlyList<ItemLogSnapshotEntry> RefreshCurrentMonthFromTodayOnly(
            DateTime monthStart,
            string? chatLogFolderPath,
            LogAnalysisService logAnalysisService)
        {
            ArgumentNullException.ThrowIfNull(logAnalysisService);

            lock (SyncRoot)
            {
                var data = MonthlyReadableLogExportService.RefreshCurrentMonthFromTodayOnly(monthStart, chatLogFolderPath, logAnalysisService);
                return data.ItemSnapshots;
            }
        }

        public static IReadOnlyList<ItemLogSnapshotEntry> RefreshCurrentMonthIncremental(
            DateTime monthStart,
            string? chatLogFolderPath,
            LogAnalysisService logAnalysisService)
        {
            ArgumentNullException.ThrowIfNull(logAnalysisService);

            lock (SyncRoot)
            {
                var data = MonthlyReadableLogExportService.RefreshCurrentMonthIncremental(monthStart, chatLogFolderPath, logAnalysisService);
                return data.ItemSnapshots;
            }
        }

        public static void AppendMonthlySnapshot(DateTime date, LogParser.ParseResult itemLog, int profileSlot = 0)
        {
            if (itemLog == null)
                throw new ArgumentNullException(nameof(itemLog));

            if (string.IsNullOrWhiteSpace(itemLog.FormattedText))
                return;

            lock (SyncRoot)
            {
                MonthlyReadableLogExportService.AppendItemSnapshot(date, itemLog, profileSlot);
            }
        }

        public static void AppendMonthlySnapshots(DateTime monthStart, IReadOnlyList<ItemLogSnapshotEntry> snapshots)
        {
            if (snapshots == null || snapshots.Count == 0)
                return;

            lock (SyncRoot)
            {
                MonthlyReadableLogExportService.AppendItemSnapshots(monthStart, snapshots);
            }
        }

        public static IReadOnlyList<ItemLogSnapshotEntry> LoadMonthlySnapshots(DateTime monthStart)
        {
            return MonthlyReadableLogExportService.LoadFromCsv(monthStart)?.ItemSnapshots ?? new List<ItemLogSnapshotEntry>();
        }

        private static List<ItemLogSnapshotEntry> BuildMonthlySnapshotsFromChatLogs(
            DateTime monthStart,
            string chatLogFolderPath,
            LogAnalysisService logAnalysisService)
        {
            var snapshots = new List<ItemLogSnapshotEntry>();

            foreach (MonthlyChatLogSourceInfo sourceInfo in EnumerateMonthChatLogInfos(monthStart, chatLogFolderPath))
            {
                string chatLogPath = sourceInfo.FullPath;
                DateTime logDate = ExtractDateFromChatLogPath(chatLogPath);
                if (logDate == DateTime.MinValue)
                    continue;

                try
                {
                    using var stream = new FileStream(chatLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream, Encoding.GetEncoding(949));
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var analysis = logAnalysisService.Analyze(line, isRealTime: false);
                        if (!analysis.IsSuccess || !analysis.HasTrackedItemDrop)
                            continue;

                        snapshots.Add(CreateSnapshotFromItemLog(analysis.Parsed, logDate));
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to warm up monthly item snapshots from '{chatLogPath}'.", ex);
                }
            }

            return snapshots;
        }

        private static void SaveMonthlySnapshots(DateTime monthStart, IReadOnlyList<ItemLogSnapshotEntry> snapshots)
        {
            try
            {
                Directory.CreateDirectory(ItemLogDirectoryPath);
                string path = GetMonthlyItemLogPath(monthStart);
                using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
                foreach (var snapshot in snapshots)
                {
                    writer.WriteLine(JsonSerializer.Serialize(snapshot));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save monthly item snapshots.", ex);
            }
        }

        private static void SaveMonthlySyncState(DateTime monthStart, string sourceSignature, int itemCount, IReadOnlyList<MonthlyChatLogSourceInfo> sourceFiles)
        {
            try
            {
                Directory.CreateDirectory(ItemLogDirectoryPath);
                string path = GetMonthlySyncStatePath(monthStart);
                var state = new ItemMonthlySyncState
                {
                    MonthStart = monthStart.Date,
                    SourceSignature = sourceSignature,
                    ItemCount = itemCount,
                    SyncedUtc = DateTime.UtcNow,
                    SourceFiles = sourceFiles.Select(sourceFile => new MonthlyChatLogSourceSnapshot
                    {
                        FileName = sourceFile.FileName,
                        Length = sourceFile.Length,
                        LastWriteTimeUtc = sourceFile.LastWriteTimeUtc
                    }).ToList()
                };

                File.WriteAllText(path, JsonSerializer.Serialize(state), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save monthly item sync state.", ex);
            }
        }

        private static void SaveReadableExports(DateTime monthStart, IReadOnlyList<ItemLogSnapshotEntry> snapshots)
        {
            _ = monthStart;
            _ = snapshots;
        }

        private static void EnsureReadableExports(DateTime monthStart, IReadOnlyList<ItemLogSnapshotEntry> snapshots)
            => SaveReadableExports(monthStart, snapshots);

        private static ItemLogSnapshotEntry CreateSnapshotFromItemLog(LogParser.ParseResult itemLog, DateTime date)
        {
            return new ItemLogSnapshotEntry
            {
                Date = date.Date,
                ItemName = itemLog.TrackedItemName,
                DisplayName = string.IsNullOrWhiteSpace(itemLog.TrackedItemName)
                    ? "Item"
                    : DropItemResolver.GetTrackedItemDisplayName(itemLog.TrackedItemName),
                Grade = itemLog.TrackedItemGrade,
                Count = Math.Max(1, itemLog.TrackedItemCount),
                FormattedText = itemLog.FormattedText
            };
        }

        private static string ComputeSourceSignature(IEnumerable<MonthlyChatLogSourceInfo> sourceFiles)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var sourceFile in sourceFiles)
            {
                try
                {
                    string fingerprint = $"{sourceFile.FileName}|{sourceFile.Length}|{sourceFile.LastWriteTimeUtc.Ticks}";
                    byte[] bytes = Encoding.UTF8.GetBytes(fingerprint);
                    hash.AppendData(bytes);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to fingerprint monthly item log source '{sourceFile.FullPath}'.", ex);
                }
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }

        private static ItemMonthlySyncState? LoadMonthlySyncState(DateTime monthStart)
        {
            string path = GetMonthlySyncStatePath(monthStart);
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonSerializer.Deserialize<ItemMonthlySyncState>(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load monthly item sync state from '{path}'.", ex);
                return null;
            }
        }

        private static bool SourceFilesMatch(IReadOnlyList<MonthlyChatLogSourceSnapshot> savedFiles, IReadOnlyList<MonthlyChatLogSourceInfo> currentFiles)
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

        private static IEnumerable<MonthlyChatLogSourceInfo> EnumerateMonthChatLogInfos(DateTime monthStart, string chatLogFolderPath)
        {
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
                    LastWriteTimeUtc = info.LastWriteTimeUtc
                };
            }
        }

        private static DateTime ExtractDateFromChatLogPath(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = System.Text.RegularExpressions.Regex.Match(fileName, @"(?<y>\d{4})_(?<m>\d{2})_(?<d>\d{2})$");
            if (!match.Success)
                return DateTime.MinValue;

            if (!int.TryParse(match.Groups["y"].Value, out int year) ||
                !int.TryParse(match.Groups["m"].Value, out int month) ||
                !int.TryParse(match.Groups["d"].Value, out int day))
                return DateTime.MinValue;

            return new DateTime(year, month, day);
        }

        private static string GetMonthlyItemLogPath(DateTime date)
            => Path.Combine(ItemLogDirectoryPath, $"ItemLog_{date:yyyy_MM}.jsonl");

        private static string GetMonthlySyncStatePath(DateTime date)
            => Path.Combine(ItemLogDirectoryPath, $"ItemLog_{date:yyyy_MM}.sync.json");

        private sealed class ItemMonthlySyncState
        {
            public DateTime MonthStart { get; set; }

            public string SourceSignature { get; set; } = string.Empty;

            public int ItemCount { get; set; }

            public DateTime SyncedUtc { get; set; }

            public List<MonthlyChatLogSourceSnapshot> SourceFiles { get; set; } = new();
        }

        private sealed class MonthlyChatLogSourceInfo
        {
            public string FullPath { get; set; } = string.Empty;

            public string FileName { get; set; } = string.Empty;

            public long Length { get; set; }

            public DateTime LastWriteTimeUtc { get; set; }
        }

        private sealed class MonthlyChatLogSourceSnapshot
        {
            public string FileName { get; set; } = string.Empty;

            public long Length { get; set; }

            public DateTime LastWriteTimeUtc { get; set; }
        }
    }
}
