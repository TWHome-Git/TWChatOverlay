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
        // 공휴일 날짜 (JSON의 holidays). 규칙의 days에 "holiday"가 있으면 이 날짜에 적용된다.
        private static HashSet<DateTime> _holidays = new();

        public static event Action? BossesUpdated;

        public static IReadOnlyList<BossTimerDefinition> GetBosses()
            => _bosses;

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

                var holidays = new HashSet<DateTime>();
                foreach (string value in payload.Holidays ?? new List<string>())
                {
                    if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None, out DateTime day))
                        holidays.Add(day.Date);
                }

                _holidays = holidays;
                _bosses = ordered;
                await RemoteResourceManifestService.MarkResourceVersionAppliedAsync("BossTimer.json").ConfigureAwait(false);
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

            if (boss.Schedule.Rules is { Count: > 0 })
            {
                // 규칙이 있으면 오늘 실제로 뜨는 시각을 보여주고, 진행 중인 기간 규칙(이벤트)이 있으면 끝나는 시각을 붙인다
                DateTime now = DateTime.Now;
                var today = GetOccurrences(boss, now.Date).Select(static t => t.ToString("HH:mm")).ToList();
                string text = today.Count > 0 ? "오늘 " + string.Join(" / ", today) : "오늘 없음";
                var activeEvents = boss.Schedule.Rules
                    .Where(rule => rule.UntilTime.HasValue && IsRuleInPeriod(rule, now))
                    .ToList();
                foreach (var rule in activeEvents.GroupBy(static r => (r.Label, r.UntilTime)).Select(static g => g.First()))
                {
                    string label = string.IsNullOrWhiteSpace(rule.Label) ? "이벤트" : rule.Label;
                    text += $"\n{label} ~ {rule.UntilTime!.Value:MM.dd HH:mm}";
                }
                return text;
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

            if (boss.Schedule.Rules is { Count: > 0 })
                return boss.Schedule.Rules.Any(static rule => rule.Times.Any(static value => !string.IsNullOrWhiteSpace(value)));

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

            // 규칙이 있으면 규칙만 쓴다 (times는 규칙을 모르는 옛 버전용으로 남겨 둔 값)
            if (boss.Schedule.Rules is { Count: > 0 })
            {
                var seen = new SortedSet<DateTime>();
                foreach (BossTimerRule rule in boss.Schedule.Rules)
                {
                    if (!IsRuleOnDay(rule, date.Date))
                        continue;
                    foreach (string value in rule.Times)
                    {
                        if (!TimeSpan.TryParse(value, out TimeSpan time))
                            continue;
                        DateTime occurrence = date.Date.Add(time);
                        if (IsRuleInPeriod(rule, occurrence))
                            seen.Add(occurrence);
                    }
                }

                foreach (DateTime occurrence in seen)
                    yield return occurrence;
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

        /// <summary>출현 시각이 규칙의 기간(from 포함 ~ until 미포함) 안인지. 기간을 안 적었으면 항상.</summary>
        private static bool IsRuleInPeriod(BossTimerRule rule, DateTime at)
            => (!rule.FromTime.HasValue || at >= rule.FromTime.Value) &&
               (!rule.UntilTime.HasValue || at < rule.UntilTime.Value);

        /// <summary>
        /// 규칙의 days가 이 날짜에 맞는지. 비어 있으면 매일.
        /// weekday = 월~금 중 공휴일이 아닌 날, weekend = 토·일, holiday = JSON holidays 날짜, mon~sun = 해당 요일.
        /// </summary>
        private static bool IsRuleOnDay(BossTimerRule rule, DateTime day)
        {
            if (rule.Days == null || rule.Days.Count == 0)
                return true;

            bool isHoliday = _holidays.Contains(day);
            bool isWeekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            foreach (string raw in rule.Days)
            {
                string value = raw?.Trim().ToLowerInvariant() ?? string.Empty;
                bool match = value switch
                {
                    "weekday" => !isWeekend && !isHoliday,
                    "weekend" => isWeekend,
                    "holiday" => isHoliday,
                    "mon" => day.DayOfWeek == DayOfWeek.Monday,
                    "tue" => day.DayOfWeek == DayOfWeek.Tuesday,
                    "wed" => day.DayOfWeek == DayOfWeek.Wednesday,
                    "thu" => day.DayOfWeek == DayOfWeek.Thursday,
                    "fri" => day.DayOfWeek == DayOfWeek.Friday,
                    "sat" => day.DayOfWeek == DayOfWeek.Saturday,
                    "sun" => day.DayOfWeek == DayOfWeek.Sunday,
                    _ => false,
                };
                if (match)
                    return true;
            }
            return false;
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

            /// <summary>
            /// 날짜 규칙 목록 (fixed 전용). 있으면 times 대신 규칙들의 합으로 출현 시각을 만든다.
            /// times는 규칙을 모르는 옛 버전 앱을 위해 그대로 둔다.
            /// </summary>
            [JsonPropertyName("rules")]
            public List<BossTimerRule>? Rules { get; set; }
        }

        /// <summary>
        /// 출현 시각 규칙 하나: 기간(from ~ until)과 요일 조건(days)에 맞는 날에 times를 추가한다.
        /// 예) { "label": "이벤트", "times": ["15:00"], "days": ["weekend", "holiday"], "from": "2026-09-17", "until": "2026-10-22T08:00" }
        /// </summary>
        public sealed class BossTimerRule
        {
            [JsonPropertyName("label")]
            public string? Label { get; set; }

            [JsonPropertyName("times")]
            public List<string> Times { get; set; } = new();

            /// <summary>weekday / weekend / holiday / mon~sun. 비우면 매일.</summary>
            [JsonPropertyName("days")]
            public List<string>? Days { get; set; }

            /// <summary>시작 (포함). "2026-09-17" 또는 "2026-09-17T08:00". 비우면 제한 없음.</summary>
            [JsonPropertyName("from")]
            public string? From { get; set; }

            /// <summary>끝 (미포함). 이 시각부터는 출현하지 않는다. 비우면 제한 없음.</summary>
            [JsonPropertyName("until")]
            public string? Until { get; set; }

            [JsonIgnore]
            public DateTime? FromTime => ParseDateTime(From);

            [JsonIgnore]
            public DateTime? UntilTime => ParseDateTime(Until);

            private static DateTime? ParseDateTime(string? value)
                => !string.IsNullOrWhiteSpace(value) &&
                   DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                       System.Globalization.DateTimeStyles.None, out DateTime parsed)
                    ? parsed
                    : null;
        }

        private sealed class BossTimerPayload
        {
            [JsonPropertyName("bosses")]
            public List<BossTimerDefinition> Bosses { get; set; } = new();

            /// <summary>공휴일 날짜 목록 ("2026-09-24"). 규칙의 days에 holiday가 있으면 이 날짜에 적용.</summary>
            [JsonPropertyName("holidays")]
            public List<string>? Holidays { get; set; }
        }
    }
}
