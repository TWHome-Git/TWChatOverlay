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
        private static readonly Regex AbandonEntryFeeRegex = new(
            @"입장료\s*(?<value>[\d,]+)\s*만\s*Seed",
            RegexOptions.Compiled);

        private static readonly Regex MagicStoneGainRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개",
            RegexOptions.Compiled);

        private static readonly Regex MagicStoneLossRegex = new(
            @"(?<grade>하급)\s*마정석\s*(?<count>[\d,]+)\s*개를\s*빼앗겼습니다",
            RegexOptions.Compiled);

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

            string body = Regex.Replace(formattedText, @"^\[[^\]]+\]\s*", string.Empty);
            if (body.Contains("주문을 통해", StringComparison.Ordinal))
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
