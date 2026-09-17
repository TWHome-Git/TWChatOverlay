using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace TWChatOverlay.Services
{
    public sealed class BossTimerService
    {
        private const string BossTimerUrl = RemoteEndpoints.BossTimer;
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        private static readonly RemoteJsonCacheClient CacheClient = new(
            "BossTimerService",
            BossTimerUrl,
            CacheTtl,
            HttpClient,
            refreshAnchorHourLocal: 11,
            forceRemoteCheckOnFirstCall: true);
        private static readonly SemaphoreSlim LoadLock = new(1, 1);
        private static readonly string[] OrderedBossIds =
        {
            "Arkan",
            "Scherzendo",
            "Origin of Doom",
            "Confused Land",
            "event"
        };

        private static IReadOnlyList<BossTimerDefinition> _bosses = CreateFallbackBosses();

        // 이벤트 기간 한정 추가 등장(기본 시간표는 건드리지 않는다). 기간이 지나면 자연히 무시된다.
        private static IReadOnlyList<BossTimerEvent> _events = Array.Empty<BossTimerEvent>();

        public static event Action? BossesUpdated;

        public static IReadOnlyList<BossTimerDefinition> GetBosses()
            => _bosses;

        public static IReadOnlyList<BossTimerEvent> GetEvents()
            => _events;

        public static async Task EnsureLoadedAsync(bool forceRefresh = false)
        {
            await LoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                bool manifestChanged = await RemoteResourceManifestService.ShouldForceRefreshAsync("BossTimer.json").ConfigureAwait(false);
                string? json = await CacheClient.GetJsonAsync(forceRefresh || manifestChanged).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(json))
                    return;

                var payload = JsonSerializer.Deserialize<BossTimerPayload>(json);
                if (payload?.Bosses == null || payload.Bosses.Count == 0)
                    return;

                var ordered = payload.Bosses
                    .Where(static boss => !string.IsNullOrWhiteSpace(boss.Id))
                    .OrderBy(static boss =>
                    {
                        int index = Array.IndexOf(OrderedBossIds, boss.Id);
                        return index < 0 ? int.MaxValue : index;
                    })
                    .ThenBy(static boss => boss.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (ordered.Count == 0)
                    return;

                _bosses = ordered;
                _events = (payload.Events ?? new List<BossTimerEvent>())
                    .Where(static ev => ev.Additions != null && ev.Additions.Count > 0 && ev.TryGetPeriod(out _, out _))
                    .ToList();
                await RemoteResourceManifestService.MarkResourceVersionAppliedAsync("BossTimer.json").ConfigureAwait(false);
                BossesUpdated?.Invoke();
                AppLogger.Info($"Boss timer data loaded. Count={ordered.Count}, Events={_events.Count}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Boss timer data load failed.", ex);
            }
            finally
            {
                LoadLock.Release();
            }
        }

        public static string BuildScheduleText(BossTimerDefinition boss)
        {
            if (boss.Schedule == null)
                return "-";

            if (string.Equals(boss.Schedule.Type, "hourly", StringComparison.OrdinalIgnoreCase))
            {
                return boss.Schedule.Minute == 0
                    ? "매시 정각"
                    : $"매시 {boss.Schedule.Minute:00}분";
            }

            if (boss.Schedule.Times == null || boss.Schedule.Times.Count == 0)
                return "-";

            string text = string.Join(" / ", boss.Schedule.Times);

            // 오늘 진행 중인 이벤트의 추가 등장만 덧붙인다 (끝난 이벤트는 표시하지 않는다)
            DateTime today = DateTime.Today;
            foreach (BossTimerEvent ev in _events)
            {
                if (!ev.IsActiveOn(today))
                    continue;

                string? summary = ev.BuildSummary(boss.Id);
                if (!string.IsNullOrEmpty(summary))
                    text += "\n" + summary;
            }

            return text;
        }

        public static bool HasDisplayableSchedule(BossTimerDefinition boss)
        {
            if (boss.Schedule == null)
                return false;

            if (string.Equals(boss.Schedule.Type, "hourly", StringComparison.OrdinalIgnoreCase))
            {
                return boss.Schedule.Minute >= 0 && boss.Schedule.Minute <= 59;
            }

            return boss.Schedule.Times != null &&
                   boss.Schedule.Times.Any(static value => !string.IsNullOrWhiteSpace(value));
        }

        public static IEnumerable<DateTime> GetOccurrences(BossTimerDefinition boss, DateTime date)
        {
            if (boss.Schedule == null)
                yield break;

            if (string.Equals(boss.Schedule.Type, "hourly", StringComparison.OrdinalIgnoreCase))
            {
                int minute = Math.Clamp(boss.Schedule.Minute, 0, 59);
                for (int hour = 0; hour < 24; hour++)
                {
                    yield return new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, date.Kind);
                }

                yield break;
            }

            if (boss.Schedule.Times == null)
                yield break;

            var occurrences = new SortedSet<DateTime>();
            foreach (string value in boss.Schedule.Times)
            {
                if (!TimeSpan.TryParse(value, out TimeSpan time))
                    continue;

                occurrences.Add(date.Date.Add(time));
            }

            // 이벤트 기간 안의 추가 등장 시각을 합친다 (기본 시각과 겹치면 한 번만)
            foreach (BossTimerEvent ev in _events)
            {
                foreach (DateTime occurrence in ev.GetAdditionalOccurrences(boss.Id, date))
                    occurrences.Add(occurrence);
            }

            foreach (DateTime occurrence in occurrences)
                yield return occurrence;
        }

        private static IReadOnlyList<BossTimerDefinition> CreateFallbackBosses()
        {
            return new List<BossTimerDefinition>
            {
                new()
                {
                    Id = "Arkan",
                    Name = "아칸",
                    Category = "field",
                    Schedule = new BossTimerSchedule { Type = "fixed", Times = new List<string> { "14:30", "21:30" } }
                },
                new()
                {
                    Id = "Scherzendo",
                    Name = "스페르첸드",
                    Category = "field",
                    Schedule = new BossTimerSchedule { Type = "fixed", Times = new List<string> { "01:00", "04:00", "08:00", "16:00", "19:00", "23:00" } }
                },
                new()
                {
                    Id = "Origin of Doom",
                    Name = "파멸의 기원",
                    Category = "field",
                    Schedule = new BossTimerSchedule { Type = "fixed", Times = new List<string> { "00:30", "11:00", "20:00" } }
                },
                new()
                {
                    Id = "Confused Land",
                    Name = "혼란한 대지",
                    Category = "field",
                    Schedule = new BossTimerSchedule { Type = "fixed", Times = new List<string> { "00:00", "07:00", "13:00", "18:00", "21:00" } }
                },
                new()
                {
                    Id = "event",
                    Name = "이벤트",
                    Category = "event",
                    Schedule = new BossTimerSchedule { Type = "hourly", Minute = 0 }
                }
            };
        }

        public sealed class BossTimerDefinition
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("category")]
            public string Category { get; set; } = string.Empty;

            [JsonPropertyName("enabledByDefault")]
            public bool EnabledByDefault { get; set; }

            [JsonPropertyName("schedule")]
            public BossTimerSchedule? Schedule { get; set; }
        }

        public sealed class BossTimerSchedule
        {
            [JsonPropertyName("type")]
            public string Type { get; set; } = "fixed";

            [JsonPropertyName("times")]
            public List<string> Times { get; set; } = new();

            [JsonPropertyName("minute")]
            public int Minute { get; set; }
        }

        private sealed class BossTimerPayload
        {
            [JsonPropertyName("bosses")]
            public List<BossTimerDefinition> Bosses { get; set; } = new();

            [JsonPropertyName("events")]
            public List<BossTimerEvent>? Events { get; set; }
        }
    }
}
