using System;
using System.Collections.Generic;

namespace TWChatOverlay.Models
{
    public partial class ChatSettings
    {
        /// <summary>
        /// 모든 설정을 기본값으로 되돌립니다.
        /// </summary>
        public void ResetToDefault()
        {
            ApplyDefaultValues();
        }

        /// <summary>
        /// 다른 설정 인스턴스의 내용을 통째로 가져온다 (프로필 불러오기).
        /// 섹션 교체 방식이라 초기화(ResetToDefault)와 같은 전체 갱신 경로를 쓰면 된다.
        /// </summary>
        public void ApplyFrom(ChatSettings source)
        {
            if (source == null)
                return;

            Chat = source.Chat ?? new ChatSection();
            Shout = source.Shout ?? new ShoutSection();
            Alerts = source.Alerts ?? new AlertsSection();
            Windows = source.Windows ?? new WindowsSection();
            Ui = source.Ui ?? new UiSection();
            Hotkeys = source.Hotkeys ?? new HotkeysSection();
            SystemConfig = source.SystemConfig ?? new SystemSection();
            Presets = source.Presets ?? new PresetsSection();
            _enableDebugLogging = source.EnableDebugLogging;
        }

        /// <summary>
        /// 섹션을 새로 만들어 모든 값을 기본값으로 채운다. (섹션 클래스의 초기값 = 앱 기본값)
        /// </summary>
        private void ApplyDefaultValues()
        {
            Chat = new ChatSection();
            Shout = new ShoutSection();
            Alerts = new AlertsSection
            {
                Dungeon = { ItemConfigs = CreateDefaultDungeonItemConfigs() },
                Boss = { Configs = CreateDefaultBossAlertConfigs() }
            };
            Windows = new WindowsSection();
            Ui = new UiSection();
            Hotkeys = new HotkeysSection();
            SystemConfig = new SystemSection();
            Presets = new PresetsSection();

            _enableDebugLogging = false;
        }

        private static Dictionary<string, DungeonItemConfig> CreateDefaultDungeonItemConfigs()
        {
            return new Dictionary<string, DungeonItemConfig>
            {
                ["로카고스"] = new() { IsEnabled = true },
                ["에토스"] = new() { IsEnabled = true },
                ["체리아"] = new() { IsEnabled = true },
                ["마티아"] = new() { IsEnabled = true },
                ["티로로스"] = new() { IsEnabled = true },
                ["라이코스"] = new() { IsEnabled = true },
                ["이클립스 토벌전"] = new() { IsEnabled = true },
                ["보급품 탈환"] = new() { IsEnabled = true },
                ["훈련소"] = new() { IsEnabled = true },
                ["별동대"] = new() { IsEnabled = true },
                ["혼란한 대지"] = new() { IsEnabled = true },
                ["색을 잃은 땅"] = new() { IsEnabled = true },
                ["코어던전"] = new() { IsEnabled = true },
                ["발굴지"] = new() { IsEnabled = true },
                ["렐릭"] = new() { IsEnabled = true },
                ["채굴장"] = new() { IsEnabled = true },
                ["차원의 틈"] = new() { IsEnabled = true },
                ["심연의 보물창고"] = new() { IsEnabled = true },
                ["이터널 플로어"] = new() { IsEnabled = true },
                ["베스티지"] = new() { IsEnabled = true },
                ["오를리 방어전 지옥"] = new() { IsEnabled = true },
                ["갈망하는 즐거움"] = new() { IsEnabled = true },
                ["청소 아르바이트"] = new() { IsEnabled = true },
                ["프라바 방어전"] = new() { IsEnabled = true },
                ["카타콤 지옥"] = new() { IsEnabled = true },
                ["어비스 - 심층Ⅰ"] = new() { IsEnabled = true },
                ["어비스 - 심층Ⅱ"] = new() { IsEnabled = true },
                ["어비스 - 심층Ⅲ"] = new() { IsEnabled = true },
                ["신조의 둥지 어려움"] = new() { IsEnabled = true },
                ["아페티리아"] = new() { IsEnabled = true },
                ["시오칸 하임 보스 토벌전"] = new() { IsEnabled = true },
                ["시오칸 하임 오딘 전면전"] = new() { IsEnabled = true },
                ["아페티리아 EX"] = new() { IsEnabled = true },
                ["어비스 보스전(EX)"] = new() { IsEnabled = true },
                ["이클립스 코어 마스터"] = new() { IsEnabled = true },
                ["로카고스 코어 마스터"] = new() { IsEnabled = true },
                ["에토스 코어 마스터"] = new() { IsEnabled = true },
                ["체리아 코어 마스터"] = new() { IsEnabled = true },
                ["마티아 코어 마스터"] = new() { IsEnabled = true },
                ["라이코스 코어 마스터"] = new() { IsEnabled = true },
                ["티로로스 코어 마스터"] = new() { IsEnabled = true },
                ["어비스 코어 마스터"] = new() { IsEnabled = true },
                ["심층Ⅰ 코어 마스터"] = new() { IsEnabled = true },
                ["심층Ⅱ 코어 마스터"] = new() { IsEnabled = true },
                ["심층Ⅲ 코어 마스터"] = new() { IsEnabled = true },
                ["머큐리얼 코어 마스터"] = new() { IsEnabled = true },
                ["머큐리얼 주간"] = new() { IsEnabled = true },
                ["샐리온 코어 마스터"] = new() { IsEnabled = true },
                ["샐레아나 코어 마스터"] = new() { IsEnabled = true },
                ["실라이론 코어 마스터"] = new() { IsEnabled = true },
                ["실반 코어 마스터"] = new() { IsEnabled = true },
                ["루미너스 코어 마스터"] = new() { IsEnabled = true },
                ["최후의 결전"] = new() { IsEnabled = true },
                ["추종하는 환희(일반)"] = new() { IsEnabled = true },
                ["추종하는 환희(어려움)"] = new() { IsEnabled = true },
                ["응시하는 슬픔(일반)"] = new() { IsEnabled = true },
                ["응시하는 슬픔(어려움)"] = new() { IsEnabled = true },
                ["환희의 잔상"] = new() { IsEnabled = true },
                ["에타 일일 도전 과제"] = new() { IsEnabled = true },
                ["에타의 의지 퀘스트"] = new() { IsEnabled = true },
                ["필멸의 땅"] = new() { IsEnabled = true },
                ["카디프"] = new() { IsEnabled = true },
                ["오를란느"] = new() { IsEnabled = true },
                ["일일 컨텐츠"] = new() { IsEnabled = true },
                ["이클립스 보스"] = new() { IsEnabled = true },
                ["주간 컨텐츠"] = new() { IsEnabled = true },
                ["어비스 지옥"] = new() { IsEnabled = true },
                ["어밴던로드"] = new() { IsEnabled = true }
            };
        }

        private static Dictionary<string, BossAlertConfig> CreateDefaultBossAlertConfigs()
        {
            return new Dictionary<string, BossAlertConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["Arkan"] = new(),
                ["Scherzendo"] = new(),
                ["Origin of Doom"] = new(),
                ["Confused Land"] = new(),
                ["event"] = new()
            };
        }

        private static double ClampVolume(double value)
        {
            return Math.Max(0.0, Math.Min(1.0, value));
        }
    }
}
