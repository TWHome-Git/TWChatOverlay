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
        private const int AbandonMaxCount = 10;
        private const int CravingPleasureMaxCount = 20;
        private const int CravingPleasureDailyEnergy = 21;

        private static readonly Regex HtmlTagRegex = new(
            "<[^>]+>",
            RegexOptions.Compiled);

        private static readonly Regex WhiteSpaceRegex = new(
            @"\s+",
            RegexOptions.Compiled);

        private static readonly Regex AbandonRoadRegex = new(
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

            if (TryShowAbandonRoad(text))
                return;

            if (TryShowCravingPleasure(text))
                return;

            TryShowTreasuryGoldPouch(text);
        }

        // ── 심연의 보물창고: 입장 후 금화 주머니 획득 카운트 ──
        // 금화 주머니는 다른 컨텐츠에서도 나올 수 있어, 입장 로그 이후 세션 시간 안에서만 센다.
        private static readonly TimeSpan TreasurySessionDuration = TimeSpan.FromMinutes(10);
        private int _treasuryGoldPouchCount;
        private DateTime _treasurySessionStartedUtc = DateTime.MinValue;

        private bool TryShowTreasuryGoldPouch(string text)
        {
            if (!_settings.EnableTreasuryGoldCountAlert)
                return false;

            // 입장 로그 → 세션 시작 (카운트만 리셋, 창은 첫 획득 때 표시)
            if (text.Contains("심연의 보물창고 입장 횟수:", StringComparison.Ordinal))
            {
                _treasuryGoldPouchCount = 0;
                _treasurySessionStartedUtc = DateTime.UtcNow;
                return true;
            }

            // "이름 : 금화 주머니를 획득 했습니다." (본인/파티원 공통, 띄어쓰기 변형 허용)
            if (text.Contains("금화 주머니를 획득", StringComparison.Ordinal))
            {
                if (DateTime.UtcNow - _treasurySessionStartedUtc > TreasurySessionDuration)
                    return false; // 보물창고 밖(세션 종료 후) 획득은 무시

                _treasuryGoldPouchCount++;
                ShowTreasuryCount();
                return true;
            }

            return false;
        }

        private const string SeedPouchIconUri = "pack://application:,,,/Data/images/Item/시드.png";

        private void ShowTreasuryCount()
        {
            DungeonCountDisplayWindowService.ShowMessage(
                "심연의 보물창고",
                $"금화 주머니 {_treasuryGoldPouchCount}개 획득",
                _settings.AbandonRoadCountAlertDurationSeconds,
                _settings,
                _settings.DungeonCountDisplayFontSize,
                SeedPouchIconUri);
        }

        private bool TryShowAbandonRoad(string text)
        {
            if (!_settings.EnableAbandonRoadCountAlert)
                return false;

            Match match = AbandonRoadRegex.Match(text);
            if (!match.Success)
                return false;

            string region = match.Groups["region"].Value.Trim();
            if (!int.TryParse(match.Groups["count"].Value, out int count))
                return false;

            count = Math.Clamp(count, 1, AbandonMaxCount);
            DungeonCountDisplayWindowService.Show(
                $"어밴던로드 - {region}",
                count,
                AbandonMaxCount,
                _settings.AbandonRoadCountAlertDurationSeconds,
                _settings,
                _settings.DungeonCountDisplayFontSize);
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
                _settings.CravingPleasureCountAlertDurationSeconds,
                _settings,
                _settings.CravingPleasureCountFontSize);
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
