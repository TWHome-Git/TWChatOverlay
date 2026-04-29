using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 드롭 아이템 JSON을 로드하고 획득 로그를 매칭합니다.
    /// JSON 구조:
    /// {
    ///   "items": [
    ///     { "name": "아이템명", "grade": "Rare|Special|Normal", "abbr": "줄임말" }
    ///   ]
    /// }
    /// </summary>
    public static class DropItemResolver
    {
        private const string DropItemUrl = "https://raw.githubusercontent.com/TWHome-Git/TWHomeDB/main/DropItem.Json";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private static readonly RemoteJsonCacheClient CacheClient = new(
            "DropItemResolver",
            DropItemUrl,
            CacheTtl,
            HttpClient,
            refreshAnchorHourLocal: 11,
            forceRemoteCheckOnFirstCall: true);

        // 지원 형식:
        // [아이템명] 아이템을 획득 하였습니다.
        // [아이템명]을(를) [1]개 획득하였습니다.
        // [아이템명] 10개를 획득하였습니다.
        private static Regex _acquireRegex = new(
            @"^\[(?<item>.+?)\](?:\s*\uC544\uC774\uD15C\uC744\s*\uD68D\uB4DD\s*\uD558\uC600\uC2B5\uB2C8\uB2E4\.?|\s*(?:\uC744\(\uB97C\)\s*)?(?:\[(?<countBracket>[\d,]+)\]|(?<countPlain>[\d,]+))\uAC1C(?:\uB97C)?\s*\uD68D\uB4DD\uD558\uC600\uC2B5\uB2C8\uB2E4\.?)$",
            RegexOptions.Compiled);

        private static readonly SemaphoreSlim LoadLock = new(1, 1);
        private static int _isInitialized;
        private static ChatSettings? _settings;

        private static Dictionary<string, ItemDropGrade> _trackedItems = new(StringComparer.OrdinalIgnoreCase);
        private static Dictionary<string, string> _trackedItemDisplayNames = new(StringComparer.OrdinalIgnoreCase);
        private static List<(string Name, ItemDropGrade Grade, string? Abbreviation)> _trackedItemList = new();

        public sealed class DropItemFilterSnapshot
        {
            public DropItemFilterSnapshot(
                IReadOnlyDictionary<string, ItemDropGrade> items,
                IReadOnlyDictionary<string, string> displayNames)
            {
                Items = items ?? throw new ArgumentNullException(nameof(items));
                DisplayNames = displayNames ?? throw new ArgumentNullException(nameof(displayNames));
            }

            public static DropItemFilterSnapshot Empty { get; } = new(
                new Dictionary<string, ItemDropGrade>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            public IReadOnlyDictionary<string, ItemDropGrade> Items { get; }

            public IReadOnlyDictionary<string, string> DisplayNames { get; }

            public bool TryGetGrade(string itemName, out ItemDropGrade grade)
                => Items.TryGetValue(itemName, out grade);

            public string ResolveDisplayName(string itemName)
            {
                if (string.IsNullOrWhiteSpace(itemName))
                    return string.Empty;

                return DisplayNames.TryGetValue(itemName, out string? displayName) &&
                       !string.IsNullOrWhiteSpace(displayName)
                    ? displayName
                    : itemName;
            }
        }

        public static void InitializeAsync(ChatSettings? settings = null)
        {
            if (settings != null)
                _settings = settings;

            if (Interlocked.Exchange(ref _isInitialized, 1) != 0)
                return;

            AppLogger.Info("Initializing tracked item resolver.");
            _ = EnsureLoadedAsync();
        }

        public static async Task ReloadAsync(ChatSettings? settings = null)
        {
            if (settings != null)
                _settings = settings;

            await LoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                _trackedItems = new Dictionary<string, ItemDropGrade>(StringComparer.OrdinalIgnoreCase);
                _trackedItemDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _trackedItemList = new List<(string Name, ItemDropGrade Grade, string? Abbreviation)>();
            }
            finally
            {
                LoadLock.Release();
            }

            await EnsureLoadedAsync().ConfigureAwait(false);
        }

        public static bool TryApplyJsonForSession(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            return TryApplyJson(json);
        }

        public static bool TryCreateFilterSnapshot(string json, out DropItemFilterSnapshot snapshot)
        {
            snapshot = DropItemFilterSnapshot.Empty;

            if (string.IsNullOrWhiteSpace(json))
                return false;

            if (!TryParseJson(json, out var rows, out _))
                return false;

            snapshot = BuildSnapshot(rows.Select(item => (item.Name, item.Grade, item.Abbreviation)));
            return true;
        }

        public static bool TryExtractTrackedItem(string message, out string itemName)
        {
            int ignoredCount;
            return TryExtractTrackedItem(message, out itemName, out _, out ignoredCount);
        }

        public static bool TryExtractTrackedItem(string message, out string itemName, out int count)
            => TryExtractTrackedItem(message, out itemName, out _, out count);

        public static IReadOnlyList<(string Name, ItemDropGrade Grade, string? Abbreviation)> GetTrackedItemsSnapshot()
            => _trackedItemList;

        public static DropItemFilterSnapshot GetSessionFilterSnapshot()
            => new(_trackedItems, _trackedItemDisplayNames);

        public static async Task<DropItemFilterSnapshot> LoadDefaultFilterSnapshotAsync()
        {
            var items = await LoadDefaultItemsAsync().ConfigureAwait(false);
            return BuildSnapshot(items);
        }

        public static string GetTrackedItemDisplayName(string itemName)
            => GetTrackedItemDisplayName(itemName, null);

        public static string GetTrackedItemDisplayName(string itemName, DropItemFilterSnapshot? filterSnapshot)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return string.Empty;

            if (filterSnapshot != null)
                return filterSnapshot.ResolveDisplayName(itemName);

            return _trackedItemDisplayNames.TryGetValue(itemName, out string? displayName) &&
                   !string.IsNullOrWhiteSpace(displayName)
                ? displayName
                : itemName;
        }

        public static bool TryExtractTrackedItem(string message, out string itemName, out ItemDropGrade grade)
            => TryExtractTrackedItem(message, out itemName, out grade, out _);

        public static bool TryExtractTrackedItem(string message, out string itemName, out ItemDropGrade grade, out int count)
            => TryExtractTrackedItem(message, null, out itemName, out grade, out count);

        public static bool TryExtractTrackedItem(
            string message,
            DropItemFilterSnapshot? filterSnapshot,
            out string itemName,
            out ItemDropGrade grade,
            out int count)
        {
            itemName = string.Empty;
            grade = ItemDropGrade.Normal;
            count = 1;

            if (string.IsNullOrWhiteSpace(message))
                return false;

            bool looksLikeItemAcquire = message.Contains("획득", StringComparison.Ordinal);
            if (!looksLikeItemAcquire)
                return false;

            var trackedItems = filterSnapshot?.Items ?? _trackedItems;
            if (trackedItems.Count == 0)
            {
                return false;
            }

            var match = _acquireRegex.Match(message);
            if (!match.Success)
            {
                return false;
            }

            itemName = match.Groups["item"].Value.Trim();
            if (string.IsNullOrWhiteSpace(itemName))
            {
                return false;
            }

            string countRaw = match.Groups["countBracket"].Success
                ? match.Groups["countBracket"].Value
                : match.Groups["countPlain"].Value;

            if (!string.IsNullOrWhiteSpace(countRaw) &&
                int.TryParse(countRaw.Replace(",", string.Empty), out int parsedCount) &&
                parsedCount > 0)
            {
                count = parsedCount;
            }

            if (trackedItems.TryGetValue(itemName, out var mappedGrade))
            {
                grade = mappedGrade;
                AppLogger.Info($"Tracked item drop detected. Item='{itemName}', Count='{count}', Grade='{grade}', Message='{message}'");
                return true;
            }

            return false;
        }

        private static async Task EnsureLoadedAsync()
        {
            await LoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_trackedItems.Count > 0)
                {
                    AppLogger.Debug($"Tracked item list already loaded. Count={_trackedItems.Count}");
                    return;
                }

                string? json = await CacheClient.GetJsonAsync().ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    bool applied = TryApplyJson(json);
                    AppLogger.Info($"Tracked item list load {(applied ? "succeeded" : "failed")}.");
                }
                else
                {
                    AppLogger.Warn("Tracked item list JSON was empty. Item-drop detection will stay unavailable until data is loaded.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Tracked item list loading failed.", ex);
            }
            finally
            {
                LoadLock.Release();
            }
        }

        private static bool TryApplyJson(string json)
        {
            try
            {
                if (!TryParseJson(json, out var rows, out _))
                    return false;

                var snapshot = BuildSnapshot(rows.Select(item => (item.Name, item.Grade, item.Abbreviation)));

                _trackedItems = new Dictionary<string, ItemDropGrade>(snapshot.Items, StringComparer.OrdinalIgnoreCase);
                _trackedItemDisplayNames = new Dictionary<string, string>(snapshot.DisplayNames, StringComparer.OrdinalIgnoreCase);
                _trackedItemList = rows.Select(item => (item.Name, item.Grade, item.Abbreviation)).ToList();
                AppLogger.Info($"Tracked item list applied. Count={_trackedItems.Count}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DropItem JSON apply failed.", ex);
                return false;
            }
        }

        public static async Task<IReadOnlyList<(string Name, ItemDropGrade Grade, string? Abbreviation)>> LoadDefaultItemsAsync()
        {
            string? json = await CacheClient.GetJsonAsync().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(json) &&
                TryParseJson(json, out var rows, out _))
            {
                return rows.ConvertAll(row => (row.Name, row.Grade, row.Abbreviation));
            }

            return Array.Empty<(string Name, ItemDropGrade Grade, string? Abbreviation)>();
        }

        private static DropItemFilterSnapshot BuildSnapshot(IEnumerable<(string Name, ItemDropGrade Grade, string? Abbreviation)> items)
        {
            var trackedItems = new Dictionary<string, ItemDropGrade>(StringComparer.OrdinalIgnoreCase);
            var displayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var item in items)
            {
                trackedItems[item.Name] = item.Grade;
                if (!string.IsNullOrWhiteSpace(item.Abbreviation))
                    displayNames[item.Name] = item.Abbreviation.Trim();
            }

            return new DropItemFilterSnapshot(trackedItems, displayNames);
        }

        public static bool TryValidateJson(string json, out string message)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                message = "사용자 정의 필터가 비어 있습니다.";
                return true;
            }

            try
            {
                if (!TryParseJson(json, out var rows, out string parseMessage))
                {
                    message = parseMessage;
                    return false;
                }

                if (rows.Count == 0)
                {
                    message = "items 배열에 name이 있는 항목이 없습니다.";
                    return false;
                }

                message = $"{rows.Count:N0}개 항목을 사용할 수 있습니다.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"JSON 형식이 올바르지 않습니다: {ex.Message}";
                return false;
            }
        }

        private static bool TryParseJson(string json, out List<DropItemDefinition> rows, out string message)
        {
            rows = new List<DropItemDefinition>();
            message = string.Empty;

            var payload = JsonSerializer.Deserialize<DropItemPayload>(json);
            if (payload == null)
            {
                message = "JSON을 읽을 수 없습니다.";
                return false;
            }

            foreach (var item in payload.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Name))
                    continue;

                rows.Add(new DropItemDefinition(item.Name.Trim(), ParseGrade(item.Grade), NormalizeAbbreviation(item.Abbreviation)));
            }

            return true;
        }

        private static string? NormalizeAbbreviation(string? abbreviation)
        {
            if (string.IsNullOrWhiteSpace(abbreviation))
                return null;

            return abbreviation.Trim();
        }

        private static ItemDropGrade ParseGrade(string? grade)
        {
            if (string.IsNullOrWhiteSpace(grade))
                return ItemDropGrade.Normal;

            if (grade.Equals("SPECIAL", StringComparison.OrdinalIgnoreCase))
                return ItemDropGrade.Special;
            if (grade.Equals("RARE", StringComparison.OrdinalIgnoreCase))
                return ItemDropGrade.Rare;
            return ItemDropGrade.Normal;
        }

        private sealed class DropItemPayload
        {
            [JsonPropertyName("items")]
            public List<DropItemRow> Items { get; set; } = new();
        }

        private sealed class DropItemRow
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("grade")]
            public string Grade { get; set; } = "Normal";

            [JsonPropertyName("abbr")]
            public string? Abbreviation { get; set; }
        }

        private sealed record DropItemDefinition(string Name, ItemDropGrade Grade, string? Abbreviation);
    }
}
