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

        /// <summary>
        /// 오를리 방어전에서 한 대 때릴 때마다 찍히는 줄. 채팅을 가득 채우므로 따로 빼서 보여준다.
        /// 시각 다음에 바로 와야 잡는다 — 외치기나 일반 채팅에 같은 말이 섞인 줄("남은 공격 횟수 : 5 팝니다")은 건드리지 않는다.
        /// </summary>
        private static readonly Regex OrlyRemainingAttackRegex = new(
            @"^(?:\[[^\]]*\]\s*)?남은\s*공격\s*횟수\s*:\s*(?<remain>\d+)",
            RegexOptions.Compiled);


        /// <summary>표시 창이 떠 있는 시간. 다음 공격이 곧 들어와 값을 새로 쓰므로 길게 둘 필요가 없다.</summary>
        private const int OrlyRemainingAttackDurationSeconds = 15;

        /// <summary>베스티지에서 보스가 나왔을 때. 처치하면 별사탕이 떨어지고, 그 줄로 알림을 내린다.</summary>
        private const string VestigeBossAppearKeyword = "성난 빅테디가 출현하였습니다";
        private const string VestigeBossClearedKeyword = "[성난 빅테디의 별사탕] 아이템을 획득하였습니다";

        /// <summary>심연의 보물창고가 끝나 곧 밖으로 나가게 될 때.</summary>
        private static readonly Regex TreasuryExitRegex = new(
            @"\d+\s*분\s*후\s*심연의\s*보물창고\s*밖으로\s*자동\s*이동합니다",
            RegexOptions.Compiled);

        /// <summary>퇴장 예고는 한 번만 오므로 잠깐 보여 주고 지운다.</summary>
        private const int TreasuryExitDurationSeconds = 15;

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

            if (TryShowOrlyRemainingAttack(text))
                return;

            if (TryShowVestigeBoss(text))
                return;

            if (TryShowTreasuryExit(text))
                return;

            TryShowTreasuryGoldPouch(text);
        }

        // ── 심연의 보물창고: 주간(1~7회차) 금화 주머니 획득 통계 ──
        // 금화 주머니는 다른 컨텐츠에서도 나올 수 있어, 입장 로그 이후 세션 시간(약 2분) 안에서만 센다.
        // 회차별 획득 수는 설정(Alerts.Dungeon.TreasuryRunCounts)에 주 단위로 저장된다.
        private static readonly TimeSpan TreasurySessionDuration = TimeSpan.FromSeconds(150); // 2분 30초

        private static readonly Regex TreasuryEntryRegex = new(
            @"심연의\s*보물창고\s*입장\s*횟수:\s*\[?\s*(?<run>\d+)\s*회",
            RegexOptions.Compiled);

        private int _treasuryCurrentRun;
        private DateTime _treasurySessionStartedUtc = DateTime.MinValue;

        private bool TryShowTreasuryGoldPouch(string text)
        {
            if (!_settings.EnableTreasuryGoldCountAlert)
                return false;

            var dungeon = _settings.Alerts.Dungeon;

            // 입장 로그 → 회차 확정 + 세션 시작 (주가 바뀌었으면 통계 리셋)
            Match entry = TreasuryEntryRegex.Match(text);
            if (entry.Success && int.TryParse(entry.Groups["run"].Value, out int run))
            {
                string weekKey = LogTextClassifier.GetIsoWeekKey(DateTime.Today);
                if (!string.Equals(dungeon.TreasuryWeekKey, weekKey, StringComparison.Ordinal))
                {
                    dungeon.TreasuryWeekKey = weekKey;
                    dungeon.TreasuryRunCounts.Clear();
                }

                run = Math.Clamp(run, 1, 7);
                while (dungeon.TreasuryRunCounts.Count < run)
                    dungeon.TreasuryRunCounts.Add(0);
                dungeon.TreasuryRunCounts[run - 1] = 0; // 이번 회차 새로 시작

                _treasuryCurrentRun = run;
                _treasurySessionStartedUtc = DateTime.UtcNow;
                ConfigService.SaveDeferred(_settings);
                Views.TreasurySummaryWindow.ShowOrUpdate(_settings, dungeon.TreasuryRunCounts.ToArray(), run);
                // 새 회차에 들어왔으니 지난 회차의 퇴장 예고는 지운다
                Views.DungeonAlertWindow.HideAlert(Views.DungeonAlertSource.TreasuryExit);
                return true;
            }

            // "금화 주머니를 획득 했습니다." (띄어쓰기 변형 허용) — 세션 안에서만 집계
            if (text.Contains("금화 주머니를 획득", StringComparison.Ordinal))
            {
                if (_treasuryCurrentRun <= 0 ||
                    DateTime.UtcNow - _treasurySessionStartedUtc > TreasurySessionDuration)
                    return false; // 보물창고 밖(세션 종료 후) 획득은 무시

                dungeon.TreasuryRunCounts[_treasuryCurrentRun - 1]++;
                ConfigService.SaveDeferred(_settings);
                Views.TreasurySummaryWindow.ShowOrUpdate(_settings, dungeon.TreasuryRunCounts.ToArray(), _treasuryCurrentRun);
                return true;
            }

            return false;
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
            AppServices.Get<DungeonCountDisplayWindowService>().Show(
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
            AppServices.Get<DungeonCountDisplayWindowService>().Show(
                "갈망하는 즐거움",
                count,
                CravingPleasureMaxCount,
                _settings.CravingPleasureCountAlertDurationSeconds,
                _settings,
                _settings.CravingPleasureCountFontSize);
            return true;
        }

        /// <summary>오를리 방어전 "남은 공격 횟수 : N" → 작은 창에 남은 횟수만 띄운다 (채팅에서는 감춘다). 끄고 켜는 설정 없이 늘 동작한다.</summary>
        private bool TryShowOrlyRemainingAttack(string text)
        {
            Match match = OrlyRemainingAttackRegex.Match(text);
            if (!match.Success || !int.TryParse(match.Groups["remain"].Value, out int remain))
                return false;

            // 감전 패턴 알림과 같은 모양의 창 — 통합 스택에 얹지 않고 자기 자리를 따로 갖는다
            Views.DungeonAlertWindow.Flash(
                Views.DungeonAlertSource.OrlyAttack,
                _settings,
                "오를리 방어전",
                $"남은 공격 {remain}회",
                TimeSpan.FromSeconds(OrlyRemainingAttackDurationSeconds));
            return true;
        }

        /// <summary>
        /// 베스티지: 성난 빅테디가 나오면 알림 창을 띄우고, 처치해서 별사탕이 떨어지면 내린다.
        /// 처치 줄을 놓쳐도 창이 영영 남지 않도록 상한 시간(3분)이 지나면 저절로 닫힌다.
        /// </summary>
        private bool TryShowVestigeBoss(string text)
        {
            if (text.Contains(VestigeBossAppearKeyword, StringComparison.Ordinal))
            {
                Views.DungeonAlertWindow.Show(
                    Views.DungeonAlertSource.VestigeBoss, _settings, "베스티지", "성난 빅테디 출현");
                return true;
            }

            if (text.Contains(VestigeBossClearedKeyword, StringComparison.Ordinal))
            {
                Views.DungeonAlertWindow.HideAlert(Views.DungeonAlertSource.VestigeBoss);
                return true;
            }

            return false;
        }

        /// <summary>심연의 보물창고 종료 — 알림 창에 잠깐 띄운다. 다음 회차에 들어가면 그때 지운다.</summary>
        private bool TryShowTreasuryExit(string text)
        {
            if (!TreasuryExitRegex.IsMatch(text))
                return false;

            Views.DungeonAlertWindow.Flash(
                Views.DungeonAlertSource.TreasuryExit,
                _settings,
                "심연의 보물창고",
                "종료",
                TimeSpan.FromSeconds(TreasuryExitDurationSeconds));
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
