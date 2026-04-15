using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 드롭 아이템 JSON을 로드하고 획득 로그를 매칭합니다.
    /// JSON 구조:
    /// {
    ///   "items": [
    ///     { "name": "아이템명", "grade": "Rare|Special|Normal" }
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

        private static Dictionary<string, ItemDropGrade> _trackedItems = new(StringComparer.OrdinalIgnoreCase);
        private static List<(string Name, ItemDropGrade Grade)> _trackedItemList = new();

        public static void InitializeAsync()
        {
            if (Interlocked.Exchange(ref _isInitialized, 1) != 0)
                return;

            AppLogger.Info("Initializing tracked item resolver.");
            _ = EnsureLoadedAsync();
        }

        public static bool TryExtractTrackedItem(string message, out string itemName)
        {
            int ignoredCount;
            return TryExtractTrackedItem(message, out itemName, out _, out ignoredCount);
        }

        public static bool TryExtractTrackedItem(string message, out string itemName, out int count)
            => TryExtractTrackedItem(message, out itemName, out _, out count);

        public static IReadOnlyList<(string Name, ItemDropGrade Grade)> GetTrackedItemsSnapshot()
            => _trackedItemList;

        public static bool TryExtractTrackedItem(string message, out string itemName, out ItemDropGrade grade)
            => TryExtractTrackedItem(message, out itemName, out grade, out _);

        public static bool TryExtractTrackedItem(string message, out string itemName, out ItemDropGrade grade, out int count)
        {
            itemName = string.Empty;
            grade = ItemDropGrade.Normal;
            count = 1;

            if (string.IsNullOrWhiteSpace(message))
                return false;

            bool looksLikeItemAcquire = message.Contains("획득", StringComparison.Ordinal);
            if (!looksLikeItemAcquire)
                return false;

            if (_trackedItems.Count == 0)
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

            if (_trackedItems.TryGetValue(itemName, out var mappedGrade))
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
                var payload = JsonSerializer.Deserialize<DropItemPayload>(json);
                if (payload == null)
                    return false;

                var next = new Dictionary<string, ItemDropGrade>(StringComparer.OrdinalIgnoreCase);
                var ordered = new List<(string Name, ItemDropGrade Grade)>();
                foreach (var item in payload.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Name))
                        continue;

                    string name = item.Name.Trim();
                    var grade = ParseGrade(item.Grade);
                    next[name] = grade;
                    ordered.Add((name, grade));
                }

                _trackedItems = next;
                _trackedItemList = ordered;
                AppLogger.Info($"Tracked item list applied. Count={_trackedItems.Count}");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("DropItem JSON apply failed.", ex);
                return false;
            }
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
        }
    }
}
