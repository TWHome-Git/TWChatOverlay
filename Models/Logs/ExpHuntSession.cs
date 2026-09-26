using System;

namespace TWChatOverlay.Models
{
    /// <summary>
    /// 사냥 한 판의 경험치 기록. 경험치가 계속 들어오는 동안을 한 판으로 보고,
    /// 일정 시간 끊기면 판이 끝난 것으로 친다. 파일에는 판 하나가 한 줄로 쌓인다.
    /// </summary>
    public sealed class ExpHuntSession
    {
        public DateTime StartedAt { get; init; }
        public DateTime EndedAt { get; init; }
        /// <summary>이 판에서 얻은 경험치 합계 (차감·감소는 빼고 센다).</summary>
        public long TotalExp { get; init; }
        /// <summary>경험치가 들어온 횟수 (대략 잡은 마리 수).</summary>
        public int GainCount { get; init; }

        public TimeSpan Duration => EndedAt - StartedAt;

        /// <summary>시간당 획득 경험치. 판이 너무 짧으면 0.</summary>
        public long ExpPerHour
        {
            get
            {
                double hours = Duration.TotalHours;
                return hours > 0 ? (long)(TotalExp / hours) : 0;
            }
        }

        /// <summary>파일 한 줄: 시작,종료,총경험치,획득횟수 (시간은 초 단위까지, 로캘과 무관하게).</summary>
        public string ToLine()
            => string.Join(',',
                StartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                EndedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                TotalExp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                GainCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

        public static ExpHuntSession? FromLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            string[] parts = line.Split(',');
            if (parts.Length < 4)
                return null;

            const System.Globalization.DateTimeStyles styles = System.Globalization.DateTimeStyles.None;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (!DateTime.TryParseExact(parts[0].Trim(), "yyyy-MM-dd HH:mm:ss", culture, styles, out DateTime started) ||
                !DateTime.TryParseExact(parts[1].Trim(), "yyyy-MM-dd HH:mm:ss", culture, styles, out DateTime ended) ||
                !long.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Integer, culture, out long total) ||
                !int.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Integer, culture, out int count))
            {
                return null;
            }

            return new ExpHuntSession { StartedAt = started, EndedAt = ended, TotalExp = total, GainCount = count };
        }
    }
}
