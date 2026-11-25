using System;
using System.Globalization;
using System.Text.RegularExpressions;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    public struct AbandonSummaryValue
    {
        private const long LowMagicStoneValueMan = 50;
        private const long MidMagicStoneValueMan = 500;
        private const long HighMagicStoneValueMan = 5000;
        private const long TopMagicStoneValueMan = 50000;

        public long TotalEntryFeeMan;
        public long Low;
        public long LowGain;
        public long LowLoss;
        public long Mid;
        public long MidGain;
        public long MidLoss;
        public long High;
        public long HighGain;
        public long HighLoss;
        public long Top;
        public long TopGain;
        public long TopLoss;

        public readonly long StoneRevenueMan =>
            (Low * LowMagicStoneValueMan) +
            (Mid * MidMagicStoneValueMan) +
            (High * HighMagicStoneValueMan) +
            (Top * TopMagicStoneValueMan);

        public readonly long NetProfitMan => StoneRevenueMan - TotalEntryFeeMan;
    }

    public static class AbandonSummaryCalculator
    {
        private static readonly Regex LeadingBracketRegex = new(@"^\[[^\]]+\]\s*", RegexOptions.Compiled);

        private static readonly Regex AbandonEntryFeeRegex = new(
            @"입장료\s*(?<value>[\d,]+)\s*만\s*Seed",
            RegexOptions.Compiled);

        private static readonly Regex MagicStoneGainRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개",
            RegexOptions.Compiled);

        private static readonly Regex MagicStoneLossRegex = new(
            @"(?<grade>하급)\s*마정석\s*(?<count>[\d,]+)\s*개를\s*빼앗겼습니다",
            RegexOptions.Compiled);

        // 어밴던로드 스켈레톤이 바닥에 떨어뜨린 마정석을 주우면 "[하급 마정석] 1개를 입수했습니다." 꼴로 찍힌다
        // (대괄호가 있고 "획득"이 없어 위 획득 정규식에 안 걸려 집계에서 빠졌다 — 2026-11-25 사용자: "그걸 먹는건 집계가 안되고 있어").
        // 원본 로그 2025-12-20 ~ 2026-11-24에 하급 · 중급 · 상급 425줄, 모두 입장료 줄 30분 안이고, 같은 줍기가 획득 문구로 또 찍힌 줄은 없다(±1초 0건).
        // 본문 맨 앞만 본다 — 남의 채팅은 "이름 : "으로 시작해 걸리지 않는다. ReadableLogArchiveService.TryExtractMagicStoneGain도 이 판정을 쓴다.
        private static readonly Regex MagicStoneFloorPickupRegex = new(
            @"^\[(?<grade>하급|중급|상급|최상급)\s*마정석\]\s*(?<count>[\d,]+)\s*개를\s*입수했습니다",
            RegexOptions.Compiled);

        /// <summary>바닥에서 주운 마정석 줄("[하급 마정석] 1개를 입수했습니다.")이면 등급과 개수를 준다. <paramref name="formattedText"/>는 시각이 붙은 줄.</summary>
        public static bool TryParseFloorPickup(string? formattedText, out string grade, out long count)
        {
            grade = string.Empty;
            count = 0;
            if (string.IsNullOrEmpty(formattedText) || !formattedText.Contains("입수했습니다", StringComparison.Ordinal))
                return false;

            return TryParseFloorPickupBody(LeadingBracketRegex.Replace(formattedText, string.Empty), out grade, out count);
        }

        private static bool TryParseFloorPickupBody(string body, out string grade, out long count)
        {
            grade = string.Empty;
            count = 0;
            var match = MagicStoneFloorPickupRegex.Match(body);
            if (!match.Success || !TryParseLong(match.Groups["count"].Value, out long parsed) || parsed <= 0)
                return false;

            grade = match.Groups["grade"].Value;
            count = parsed;
            return true;
        }

        /// <summary>최상급 마정석을 얻은 줄(획득 문구 · 바닥 줍기)인지. 요약에 더하는 판정과 같은 것을 써서, 요약에 최상급이 더해지는 줄과 늘 같다.</summary>
        public static bool IsTopMagicStoneGain(string? formattedText)
            => !string.IsNullOrEmpty(formattedText) &&
               formattedText.Contains("최상급", StringComparison.Ordinal) &&
               TryParseDelta(formattedText, out string kind, out string grade, out long value) &&
               kind == "stone" && grade == "최상급" && value > 0;

        public static bool TryAccumulate(string formattedText, ref AbandonSummaryValue summary)
        {
            if (!TryParseDelta(formattedText, out string kind, out string grade, out long value))
                return false;

            if (kind == "fee")
                summary.TotalEntryFeeMan += value;
            else
                ApplyMagicStoneDelta(ref summary, grade, value);

            return true;
        }

        public static bool TryAccumulate(string formattedText, AbandonMonthlySummarySnapshotEntry summary)
        {
            if (summary == null)
                throw new ArgumentNullException(nameof(summary));

            if (!TryParseDelta(formattedText, out string kind, out string grade, out long value))
                return false;

            if (kind == "fee")
                summary.TotalEntryFeeMan += value;
            else
                ApplyMagicStoneDelta(summary, grade, value);

            return true;
        }

        public static AbandonSummaryValue FromMonthly(AbandonMonthlySummarySnapshotEntry summary)
        {
            if (summary == null)
                return new AbandonSummaryValue();

            return new AbandonSummaryValue
            {
                TotalEntryFeeMan = summary.TotalEntryFeeMan,
                Low = summary.Low,
                LowGain = summary.LowGain,
                LowLoss = summary.LowLoss,
                Mid = summary.Mid,
                MidGain = summary.MidGain,
                MidLoss = summary.MidLoss,
                High = summary.High,
                HighGain = summary.HighGain,
                HighLoss = summary.HighLoss,
                Top = summary.Top,
                TopGain = summary.TopGain,
                TopLoss = summary.TopLoss
            };
        }

        public static string FormatSignedCount(long count)
            => (count >= 0 ? "+" : string.Empty) + $"{count:N0}";

        public static string FormatManAmount(long totalMan)
        {
            string sign = totalMan < 0 ? "-" : string.Empty;
            long abs = Math.Abs(totalMan);
            long eok = abs / 10000;
            long man = abs % 10000;
            return $"{sign}{eok:N0}억 {man:N0}만";
        }

        private static bool TryParseDelta(string formattedText, out string kind, out string grade, out long value)
        {
            kind = string.Empty;
            grade = string.Empty;
            value = 0;

            if (string.IsNullOrWhiteSpace(formattedText))
                return false;

            string body = LeadingBracketRegex.Replace(formattedText, string.Empty);
            if (body.Contains("주문을 통해", StringComparison.Ordinal))
                return false;
            if (body.Contains("대량으로 획득", StringComparison.Ordinal))
                return false;

            var feeMatch = AbandonEntryFeeRegex.Match(body);
            if (feeMatch.Success && TryParseLong(feeMatch.Groups["value"].Value, out long feeMan))
            {
                kind = "fee";
                value = feeMan;
                return true;
            }

            var lossMatch = MagicStoneLossRegex.Match(body);
            if (lossMatch.Success && TryParseLong(lossMatch.Groups["count"].Value, out long lossCount))
            {
                kind = "stone";
                grade = lossMatch.Groups["grade"].Value;
                value = -lossCount;
                return true;
            }

            var gainMatch = MagicStoneGainRegex.Match(body);
            if (gainMatch.Success &&
                body.Contains("획득", StringComparison.Ordinal) &&
                TryParseLong(gainMatch.Groups["count"].Value, out long gainCount))
            {
                kind = "stone";
                grade = gainMatch.Groups["grade"].Value;
                value = gainCount;
                return true;
            }

            if (TryParseFloorPickupBody(body, out string pickupGrade, out long pickupCount))
            {
                kind = "stone";
                grade = pickupGrade;
                value = pickupCount;
                return true;
            }

            return false;
        }

        private static bool TryParseLong(string raw, out long value)
            => long.TryParse(raw.Replace(",", string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static void ApplyMagicStoneDelta(ref AbandonSummaryValue summary, string grade, long delta)
        {
            switch (grade)
            {
                case "하급":
                    summary.Low += delta;
                    if (delta >= 0) summary.LowGain += delta;
                    else summary.LowLoss += -delta;
                    break;
                case "중급":
                    summary.Mid += delta;
                    if (delta >= 0) summary.MidGain += delta;
                    else summary.MidLoss += -delta;
                    break;
                case "상급":
                    summary.High += delta;
                    if (delta >= 0) summary.HighGain += delta;
                    else summary.HighLoss += -delta;
                    break;
                case "최상급":
                    summary.Top += delta;
                    if (delta >= 0) summary.TopGain += delta;
                    else summary.TopLoss += -delta;
                    break;
            }
        }

        private static void ApplyMagicStoneDelta(AbandonMonthlySummarySnapshotEntry summary, string grade, long delta)
        {
            switch (grade)
            {
                case "하급":
                    summary.Low += delta;
                    if (delta >= 0) summary.LowGain += delta;
                    else summary.LowLoss += -delta;
                    break;
                case "중급":
                    summary.Mid += delta;
                    if (delta >= 0) summary.MidGain += delta;
                    else summary.MidLoss += -delta;
                    break;
                case "상급":
                    summary.High += delta;
                    if (delta >= 0) summary.HighGain += delta;
                    else summary.HighLoss += -delta;
                    break;
                case "최상급":
                    summary.Top += delta;
                    if (delta >= 0) summary.TopGain += delta;
                    else summary.TopLoss += -delta;
                    break;
            }
        }
    }
}
