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
        private const string BossTimerUrl = "https://raw.githubusercontent.com/TWHome-Git/TWHomeDB/main/BossTimer.json";
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

        public static event Action? BossesUpdated;

        public static IReadOnlyList<BossTimerDefinition> GetBosses()
            => _bosses;

        public static async Task EnsureLoadedAsync(bool forceRefresh = false)
        {
            await LoadLock.WaitAsync().ConfigureAwait(false);
            try
            {
                string? json = await CacheClient.GetJsonAsync(forceRefresh).ConfigureAwait(false);
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
                BossesUpdated?.Invoke();
                AppLogger.Info($"Boss timer data loaded. Count={ordered.Count}");
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

            return string.Join(" / ", boss.Schedule.Times);
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

            foreach (string value in boss.Schedule.Times)
            {
                if (!TimeSpan.TryParse(value, out TimeSpan time))
                    continue;

                yield return date.Date.Add(time);
            }
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
        }
    }
}
