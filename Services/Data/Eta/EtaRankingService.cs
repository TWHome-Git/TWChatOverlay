using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// Loads ETA ranking data, refreshes the remote cache, and builds profile indexes.
    /// </summary>
    public sealed class EtaRankingService
    {
        private const string EtaRankingUrl = RemoteEndpoints.EtaRanking;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
        private const int RefreshAnchorHourLocal = 10;

        private readonly RemoteJsonCacheClient CacheClient = new(
            "EtaRankingService",
            EtaRankingUrl,
            CacheTtl,
            RemoteEndpoints.SharedHttpClient,
            refreshAnchorHourLocal: RefreshAnchorHourLocal,
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

        private Dictionary<string, EtaProfileResolver.EtaProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<EtaProfileResolver.EtaRankingEntry> _rankings = Array.Empty<EtaProfileResolver.EtaRankingEntry>();
        private int _isInitialized;
        private readonly SemaphoreSlim LoadLock = new(1, 1);

        public void InitializeAsync()
        {
            if (Interlocked.Exchange(ref _isInitialized, 1) != 0)
                return;

            _ = EnsureLoadedAsync();
        }

        public async Task EnsureLoadedAsync()
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

        public bool TryGetProfile(string userId, out EtaProfileResolver.EtaProfile profile)
        {
            profile = default;
            if (string.IsNullOrEmpty(userId))
                return false;

            return _profiles.TryGetValue(userId, out profile);
        }

        public IReadOnlyList<EtaProfileResolver.EtaRankingEntry> GetRankings(string? characterName = null)
        {
            if (string.IsNullOrWhiteSpace(characterName) || characterName == "\uC804\uCCB4")
                return _rankings;

            return _rankings.Where(x => x.CharacterName == characterName).ToList();
        }

        public void DeleteCache()
        {
            try
            {
                CacheClient.DeleteCache();
                AppLogger.Info("ETA cache deleted.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ETA cache delete failed.", ex);
            }
        }

        private async Task LoadRankingsAsync()
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
                AppLogger.Warn("ETA ranking load failed.", ex);
            }
        }

        private bool TryApplyRankingJson(string json)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<EtaRankingPayload>(json);
                if (payload == null)
                    return false;

                // 신 형식: Servers = { "서버명": [rows] }. 아이디는 서버와 무관하게 공유되므로 전 서버를 병합한다.
                // 구 형식(Rankings 평면 배열)도 계속 지원.
                List<EtaRankingRow> rows = payload.Servers is { Count: > 0 }
                    ? payload.Servers.Values.Where(list => list != null).SelectMany(list => list!).ToList()
                    : payload.Rankings ?? new List<EtaRankingRow>();

                if (rows.Count == 0)
                    return false;

                var next = new Dictionary<string, EtaProfileResolver.EtaProfile>(StringComparer.OrdinalIgnoreCase);
                var rankingRows = new List<EtaProfileResolver.EtaRankingEntry>(rows.Count);
                int order = 0;

                foreach (var row in rows)
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
                AppLogger.Warn("ETA JSON apply failed.", ex);
                return false;
            }
        }

        private sealed class EtaRankingPayload
        {
            [JsonPropertyName("Rankings")]
            public List<EtaRankingRow>? Rankings { get; set; }

            [JsonPropertyName("Servers")]
            public Dictionary<string, List<EtaRankingRow>>? Servers { get; set; }
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
