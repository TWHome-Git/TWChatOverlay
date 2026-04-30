using System;
using System.Net;
using System.Text.RegularExpressions;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 실시간 던전 입장 횟수 로그를 감지해 작은 표시 창으로 알려줍니다.
    /// </summary>
    public sealed class DungeonCountDisplayService
    {
        private const int AbaddonMaxCount = 10;
        private const int CravingPleasureMaxCount = 20;
        private const int CravingPleasureDailyEnergy = 21;

        private static readonly Regex HtmlTagRegex = new(
            "<[^>]+>",
            RegexOptions.Compiled);

        private static readonly Regex WhiteSpaceRegex = new(
            @"\s+",
            RegexOptions.Compiled);

        private static readonly Regex AbaddonRoadRegex = new(
            @"이번\s*주\s*어밴던\s*로드\s*(?<region>.+?)\s*지역의\s*도전\s*횟수는\s*(?<count>\d+)\s*번",
            RegexOptions.Compiled);

        private static readonly Regex CravingPleasureRegex = new(
            @"남은\s*에너지는\s*\[\s*(?<remain>\d+)\s*\]",
            RegexOptions.Compiled);

        private readonly ChatSettings _settings;

        public DungeonCountDisplayService(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public void Process(LogAnalysisResult analysis)
        {
            if (!analysis.IsSuccess || !analysis.IsRealTime)
                return;

            string text = Normalize(analysis.Parsed.FormattedText);
            ProcessNormalized(text);
        }

        public void ProcessRaw(string html, bool isRealTime)
        {
            if (!isRealTime)
                return;

            string text = Normalize(html);
            ProcessNormalized(text);
        }

        private void ProcessNormalized(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            if (TryShowAbaddonRoad(text))
                return;

            TryShowCravingPleasure(text);
        }

        private bool TryShowAbaddonRoad(string text)
        {
            if (!_settings.EnableAbaddonRoadCountAlert)
                return false;

            Match match = AbaddonRoadRegex.Match(text);
            if (!match.Success)
                return false;

            string region = match.Groups["region"].Value.Trim();
            if (!int.TryParse(match.Groups["count"].Value, out int count))
                return false;

            count = Math.Clamp(count, 1, AbaddonMaxCount);
            DungeonCountDisplayWindowService.Show(
                $"어밴던로드 - {region}",
                count,
                AbaddonMaxCount,
                _settings.AbaddonRoadCountAlertDurationSeconds,
                _settings);
            return true;
        }

        private bool TryShowCravingPleasure(string text)
        {
            if (!_settings.EnableCravingPleasureCountAlert)
                return false;

            Match match = CravingPleasureRegex.Match(text);
            if (!match.Success || !int.TryParse(match.Groups["remain"].Value, out int remain))
                return false;

            int count = Math.Clamp(CravingPleasureDailyEnergy - remain, 1, CravingPleasureMaxCount);
            DungeonCountDisplayWindowService.Show(
                "갈망하는 즐거움",
                count,
                CravingPleasureMaxCount,
                _settings.AbaddonRoadCountAlertDurationSeconds,
                _settings);
            return true;
        }

        private static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string decoded = WebUtility.HtmlDecode(text);
            decoded = HtmlTagRegex.Replace(decoded, " ");
            return WhiteSpaceRegex.Replace(decoded, " ").Trim();
        }
    }
}
