using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// MainWindow 코드비하인드에 흩어져 있던 순수 로그 텍스트 분류/정규화 로직을 모은 서비스.
    /// 창 상태에 의존하지 않는 순수 함수만 담아 단위 테스트가 가능하도록 분리했다.
    /// </summary>
    public static class LogTextClassifier
    {
        /// <summary>혼란한 대지/색을 잃은 땅 미션 성공 + ELSO 획득 보상 라인인지.</summary>
        public static bool IsConfusedOrColorlessElsoReward(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool isTargetContent =
                text.Contains("혼란한 대지 미션에 성공하여", StringComparison.Ordinal) ||
                text.Contains("색을 잃은 땅 미션에 성공하여", StringComparison.Ordinal);

            if (!isTargetContent)
                return false;

            return text.Contains("ELSO를 획득했습니다", StringComparison.Ordinal);
        }

        /// <summary>경험치 100억 차감 → 경험의 정수 교환 라인인지.</summary>
        public static bool IsExperienceEssenceExchange(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains(
                "경험치 100억이 차감되고, 경험의 정수 1개를 획득 하였습니다.",
                StringComparison.Ordinal);
        }

        /// <summary>ISO 8601 주차 키("2026-W03" 형식)를 반환.</summary>
        public static string GetIsoWeekKey(DateTime date)
        {
            int isoYear = ISOWeek.GetYear(date);
            int isoWeek = ISOWeek.GetWeekOfYear(date);
            return $"{isoYear}-W{isoWeek:00}";
        }

        /// <summary>메인 채팅 탭 태그를 알려진 값으로 정규화한다(미지값은 "Basic").</summary>
        public static string NormalizeMainTabTag(string? tabTag)
        {
            if (string.Equals(tabTag, "Basic", StringComparison.OrdinalIgnoreCase))
                return "Basic";
            if (string.Equals(tabTag, "General", StringComparison.OrdinalIgnoreCase))
                return "General";
            if (string.Equals(tabTag, "Team", StringComparison.OrdinalIgnoreCase))
                return "Team";
            if (string.Equals(tabTag, "Club", StringComparison.OrdinalIgnoreCase))
                return "Club";
            if (string.Equals(tabTag, "Shout", StringComparison.OrdinalIgnoreCase))
                return "Shout";
            if (string.Equals(tabTag, "System", StringComparison.OrdinalIgnoreCase))
                return "System";
            return "Basic";
        }

        private static readonly Regex TimestampTextRegex = new(@"^\d{1,2}:\d{2}(?::\d{2})?$", RegexOptions.Compiled);

        /// <summary>"12:34" / "12:34:56" 형태의 시각 텍스트인지.</summary>
        public static bool IsTimestampText(string value)
            => TimestampTextRegex.IsMatch(value);

        /// <summary>1~9999 범위의 에타 레벨 텍스트인지.</summary>
        public static bool IsEtaLevelText(string value)
            => int.TryParse(value, out int level) && level is >= 1 and <= 9999;
    }
}
