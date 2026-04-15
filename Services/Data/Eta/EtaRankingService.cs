using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// Loads ETA ranking data, refreshes the remote cache, and builds profile indexes.
    /// </summary>
    public static class EtaRankingService
    {
        private const string EtaRankingUrl = "https://raw.githubusercontent.com/TWHome-Git/TWHomeDB/main/eta_ranking.json";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        private static readonly RemoteJsonCacheClient CacheClient = new(
            "EtaRankingService",
            EtaRankingUrl,
            CacheTtl,
            HttpClient,
            refreshAnchorHourLocal: 11,
            forceRemoteCheckOnFirstCall: true);

        private static readonly Dictionary<int, string> CharacterNameByCode = new()
        {
            [0] = "\uB8E8\uC2DC\uC548",
            [1] = "\uBCF4\uB9AC\uC2A4",
            [2] = "\uB9C9\uC2DC\uBBFC",
            [3] = "\uC2DC\uBCA8\uB9B0",
            [4] = "\uC870\uC288\uC544",
            [5] = "\uB780\uC9C0\uC5D0",
            [6] = "\uC774\uC790\uD06C",
            [7] = "\uBC00\uB77C",
            [8] = "\uD2F0\uCE58\uC5D8",
            [9] = "\uC774\uC2A4\uD540",
            [10] = "\uB098\uC57C\uD2B8\uB808\uC774",
            [11] = "\uC544\uB098\uC774\uC2A4",
            [12] = "\uD074\uB85C\uC5D0",
            [13] = "\uBCA4\uC57C",
            [14] = "\uC774\uC194\uB81B",
            [15] = "\uB85C\uC544\uBBF8\uB2C8",
            [16] = "\uB179\uD134",
            [17] = "\uB9AC\uCCB4",
            [18] = "\uC608\uD504\uB128"
        };

        private static Dictionary<string, EtaProfileResolver.EtaProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
        private static IReadOnlyList<EtaProfileResolver.EtaRankingEntry> _rankings = Array.Empty<EtaProfileResolver.EtaRankingEntry>();
        private static int _isInitialized;
        private static readonly SemaphoreSlim LoadLock = new(1, 1);

        public static void InitializeAsync()
        {
            if (Interlocked.Exchange(ref _isInitialized, 1) != 0)
                return;

            _ = EnsureLoadedAsync();
        }

        public static async Task EnsureLoadedAsync()
        {
            if (_rankings.Count > 0)
                return;

            await LoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_rankings.Count > 0)
                    return;

                await LoadRankingsAsync().ConfigureAwait(false);
            }
            finally
            {
                LoadLock.Release();
            }
        }

        public static async Task<bool> ForceRefreshAsync()
        {
            await LoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string? json = await CacheClient.GetJsonAsync(forceRefresh: true).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                return TryApplyRankingJson(json);
            }
            finally
            {
                LoadLock.Release();
            }
        }

        public static bool TryGetProfile(string userId, out EtaProfileResolver.EtaProfile profile)
        {
            profile = default;
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            return _profiles.TryGetValue(userId, out profile);
        }

        public static IReadOnlyList<EtaProfileResolver.EtaRankingEntry> GetRankings(string? characterName = null)
        {
            if (string.IsNullOrWhiteSpace(characterName) || characterName == "\uC804\uCCB4")
                return _rankings;

            return _rankings.Where(x => x.CharacterName == characterName).ToList();
        }

        public static void DeleteCache()
        {
            try
            {
                CacheClient.DeleteCache();
                Debug.WriteLine("ETA cache deleted.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ETA cache delete failed: {ex.Message}");
            }
        }

        private static async Task LoadRankingsAsync()
        {
            try
            {
                string? json = await CacheClient.GetJsonAsync(forceRefresh: false).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                TryApplyRankingJson(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ETA ranking load failed: {ex.Message}");
            }
        }

        private static bool TryApplyRankingJson(string json)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<EtaRankingPayload>(json);
                if (payload?.Rankings == null || payload.Rankings.Count == 0)
                    return false;

                var next = new Dictionary<string, EtaProfileResolver.EtaProfile>(StringComparer.OrdinalIgnoreCase);
                var rankingRows = new List<EtaProfileResolver.EtaRankingEntry>(payload.Rankings.Count);
                int order = 0;

                foreach (var row in payload.Rankings)
                {
                    if (string.IsNullOrWhiteSpace(row.UserId))
                        continue;

                    string characterName = CharacterNameByCode.TryGetValue(row.CharacterCode, out var name)
                        ? name
                        : $"\uCF54\uB4DC{row.CharacterCode}";

                    rankingRows.Add(new EtaProfileResolver.EtaRankingEntry(row.CharacterCode, characterName, row.UserId, row.Level, row.Essence, order++));

                    var candidate = new EtaProfileResolver.EtaProfile(row.Level, characterName);
                    if (next.TryGetValue(row.UserId, out var existing))
                    {
                        if (candidate.Level > existing.Level)
                        {
                            next[row.UserId] = candidate;
                        }
                    }
                    else
                    {
                        next[row.UserId] = candidate;
                    }
                }

                _profiles = next;
                _rankings = new ReadOnlyCollection<EtaProfileResolver.EtaRankingEntry>(rankingRows);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ETA JSON apply failed: {ex.Message}");
                return false;
            }
        }

        private sealed class EtaRankingPayload
        {
            [JsonPropertyName("Rankings")]
            public List<EtaRankingRow> Rankings { get; set; } = new();
        }

        private sealed class EtaRankingRow
        {
            [JsonPropertyName("CharacterCode")]
            public int CharacterCode { get; set; }

            [JsonPropertyName("UserId")]
            public string UserId { get; set; } = string.Empty;

            [JsonPropertyName("Level")]
            public int Level { get; set; }

            [JsonPropertyName("Essence")]
            public int Essence { get; set; }
        }
    }
}
