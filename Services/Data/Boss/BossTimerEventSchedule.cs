using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 이벤트 기간에만 추가되는 보스 등장 시각(BossTimer.json의 "events" 항목).
    /// 기본 시간표("schedule.times")는 그대로 두고, 기간 안의 등장 시각만 조건(매일/평일/주말·공휴일 또는 날짜 지정)에 맞게 더한다.
    /// 기간이 끝나면 자동으로 무시되므로 데이터에서 지우지 않아도 된다.
    /// </summary>
    public sealed class BossTimerEvent
    {
        public const string DaysAll = "all";
        public const string DaysWeekday = "weekday";
        public const string DaysWeekend = "weekend";

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>시작: "yyyy-MM-dd"(그날 0시부터) 또는 "yyyy-MM-dd HH:mm"</summary>
        [JsonPropertyName("from")]
        public string From { get; set; } = string.Empty;

        /// <summary>종료: "yyyy-MM-dd"(그날 끝까지 포함) 또는 "yyyy-MM-dd HH:mm"(그 시각 미만까지)</summary>
        [JsonPropertyName("to")]
        public string To { get; set; } = string.Empty;

        /// <summary>주말과 같은 규칙을 적용할 공휴일 목록, yyyy-MM-dd</summary>
        [JsonPropertyName("holidays")]
        public List<string> Holidays { get; set; } = new();

        [JsonPropertyName("additions")]
        public List<BossTimerEventAddition> Additions { get; set; } = new();

        /// <summary>기간을 [start, endExclusive)로 해석한다.</summary>
        public bool TryGetPeriod(out DateTime start, out DateTime endExclusive)
        {
            start = default;
            endExclusive = default;
            if (!TryParseBoundary(From, out start, out _))
                return false;
            if (!TryParseBoundary(To, out DateTime to, out bool toIsDateOnly))
                return false;

            endExclusive = toIsDateOnly ? to.AddDays(1) : to;
            return start < endExclusive;
        }

        /// <summary>해당 날짜에 기간이 조금이라도 걸치는지.</summary>
        public bool IsActiveOn(DateTime date)
            => TryGetPeriod(out DateTime start, out DateTime end) && date.Date < end && date.Date.AddDays(1) > start;

        public bool IsWeekendOrHoliday(DateTime date)
        {
            if (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                return true;

            foreach (string value in Holidays)
            {
                if (TryParseDate(value, out DateTime holiday) && holiday == date.Date)
                    return true;
            }

            return false;
        }

        /// <summary>해당 날짜에 이 이벤트가 보스에 더하는 등장 시각(기간 경계 안의 것만).</summary>
        public IEnumerable<DateTime> GetAdditionalOccurrences(string bossId, DateTime date)
        {
            if (!TryGetPeriod(out DateTime start, out DateTime end))
                yield break;

            DateTime day = date.Date;
            bool weekendLike = IsWeekendOrHoliday(day);
            foreach (BossTimerEventAddition addition in Additions)
            {
                if (!string.Equals(addition.BossId, bossId, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!addition.AppliesTo(day, weekendLike))
                    continue;

                foreach (string value in addition.Times)
                {
                    if (!TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out TimeSpan time))
                        continue;

                    DateTime occurrence = day.Add(time);
                    if (occurrence >= start && occurrence < end)
                        yield return occurrence;
                }
            }
        }

        /// <summary>설정 카드용 요약: "축제(9/17~10/22 08:00) 추가: 매일 19:30 / 20:30 · 주말·공휴일 11:30"</summary>
        public string? BuildSummary(string bossId)
        {
            if (!TryParseBoundary(From, out DateTime from, out bool fromDateOnly) ||
                !TryParseBoundary(To, out DateTime to, out bool toDateOnly))
                return null;

            var parts = new List<string>();
            foreach (var group in Additions
                         .Where(a => string.Equals(a.BossId, bossId, StringComparison.OrdinalIgnoreCase))
                         .GroupBy(a => a.RuleLabel, StringComparer.Ordinal))
            {
                var times = group
                    .SelectMany(a => a.Times)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct()
                    .OrderBy(t => t, StringComparer.Ordinal)
                    .ToList();

                if (times.Count > 0)
                    parts.Add($"{group.Key} {string.Join(" / ", times)}");
            }

            if (parts.Count == 0)
                return null;

            string name = string.IsNullOrWhiteSpace(Name) ? "이벤트" : Name;
            string fromText = fromDateOnly ? from.ToString("M/d", CultureInfo.InvariantCulture) : from.ToString("M/d HH:mm", CultureInfo.InvariantCulture);
            string toText = toDateOnly ? to.ToString("M/d", CultureInfo.InvariantCulture) : to.ToString("M/d HH:mm", CultureInfo.InvariantCulture);
            return $"{name}({fromText}~{toText}) 추가: {string.Join(" · ", parts)}";
        }

        private static bool TryParseBoundary(string? value, out DateTime result, out bool dateOnly)
        {
            string text = (value ?? string.Empty).Trim();
            if (DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            {
                dateOnly = false;
                return true;
            }

            dateOnly = true;
            return TryParseDate(text, out result);
        }

        internal static bool TryParseDate(string? value, out DateTime date)
            => DateTime.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    public sealed class BossTimerEventAddition
    {
        [JsonPropertyName("bossId")]
        public string BossId { get; set; } = string.Empty;

        /// <summary>"all"(매일, 기본) | "weekday"(월~금, 공휴일 제외) | "weekend"(토·일 + 공휴일). "dates"가 있으면 무시된다.</summary>
        [JsonPropertyName("days")]
        public string Days { get; set; } = BossTimerEvent.DaysAll;

        /// <summary>요일 규칙 대신 적용할 날짜를 직접 나열(yyyy-MM-dd). 비어 있으면 "days" 규칙을 쓴다.</summary>
        [JsonPropertyName("dates")]
        public List<string> Dates { get; set; } = new();

        [JsonPropertyName("times")]
        public List<string> Times { get; set; } = new();

        public bool UsesExplicitDates => Dates.Count > 0;

        public string NormalizedDays
        {
            get
            {
                string value = (Days ?? string.Empty).Trim().ToLowerInvariant();
                return value is BossTimerEvent.DaysWeekday or BossTimerEvent.DaysWeekend ? value : BossTimerEvent.DaysAll;
            }
        }

        /// <summary>카드 요약에 쓰는 조건 이름.</summary>
        public string RuleLabel => UsesExplicitDates
            ? "지정일"
            : NormalizedDays switch
            {
                BossTimerEvent.DaysWeekday => "평일",
                BossTimerEvent.DaysWeekend => "주말·공휴일",
                _ => "매일"
            };

        public bool AppliesTo(DateTime day, bool weekendOrHoliday)
        {
            if (UsesExplicitDates)
                return Dates.Any(d => BossTimerEvent.TryParseDate(d, out DateTime date) && date == day.Date);

            return NormalizedDays switch
            {
                BossTimerEvent.DaysWeekday => !weekendOrHoliday,
                BossTimerEvent.DaysWeekend => weekendOrHoliday,
                _ => true
            };
        }
    }
}
