using System;
using System.Net;
using System.Text.RegularExpressions;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// DailyWeeklyContentWindow 코드비하인드에 있던 순수 로그 파싱 로직을 추출한 서비스.
    /// 창 상태에 의존하지 않는 파싱/정규화만 담아 단위 테스트가 가능하도록 분리했다.
    /// </summary>
    public static class DailyWeeklyLogParser
    {
        private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhiteSpaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex AbandonRoadCountRegex = new(
            @"이번\s*주\s*어밴던\s*로드\s*(?<region>필멸의\s*땅|카디프|오를란느)\s*지역의\s*도전\s*횟수는\s*(?<count>\d+)\s*번",
            RegexOptions.Compiled);
        private static readonly Regex CravingPleasureEnergyRegex = new(
            @"남은\s*에너지는\s*\[\s*(?<remain>\d+)\s*\]",
            RegexOptions.Compiled);

        // 어밴던로드 지역명(원본 창의 상수 값과 동일하게 유지)
        private const string ImmortalLandItemName = "필멸의 땅";
        private const string CardiffItemName = "카디프";
        private const string OrlanneItemName = "오를란느";

        /// <summary>HTML 태그/엔티티/공백을 정리한 순수 텍스트를 반환.</summary>
        public static string NormalizeLogText(string rawLog)
        {
            if (string.IsNullOrWhiteSpace(rawLog))
                return string.Empty;

            string decoded = WebUtility.HtmlDecode(rawLog).Replace("&nbsp", " ");
            decoded = HtmlTagRegex.Replace(decoded, " ");
            return WhiteSpaceRegex.Replace(decoded, " ").Trim();
        }

        /// <summary>어밴던로드 도전 횟수 로그에서 지역명과 횟수를 추출.</summary>
        public static bool TryExtractAbandonRoadCount(string text, out string itemName, out int count)
        {
            itemName = string.Empty;
            count = 0;

            Match match = AbandonRoadCountRegex.Match(text);
            if (!match.Success || !int.TryParse(match.Groups["count"].Value, out count))
                return false;

            string region = WhiteSpaceRegex.Replace(match.Groups["region"].Value, string.Empty);
            itemName = region switch
            {
                "필멸의땅" => ImmortalLandItemName,
                "카디프" => CardiffItemName,
                "오를란느" => OrlanneItemName,
                _ => string.Empty
            };

            return !string.IsNullOrEmpty(itemName);
        }

        /// <summary>갈망하는 즐거움 "남은 에너지" 로그에서 소진 횟수(21 - 남은값)를 추출.</summary>
        public static bool TryExtractCravingPleasureCount(string text, out int count)
        {
            count = 0;

            Match match = CravingPleasureEnergyRegex.Match(text);
            if (!match.Success || !int.TryParse(match.Groups["remain"].Value, out int remain))
                return false;

            count = 21 - remain;
            return true;
        }
    }
}
