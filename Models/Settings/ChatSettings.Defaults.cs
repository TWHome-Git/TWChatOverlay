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

        private void ApplyDefaultValues()
        {
            _normalColor = "#FFFFFF";
            _teamColor = "#00BFFF";
            _clubColor = "#00FF00";
            _systemColor = "#FFFF00";
            _shoutColor = "#FF8000";
            _showEtaLevel = true;
            _showEtaCharacter = true;
            _showShoutToastPopup = true;
            _autoCopyShoutNickname = false;
            _shoutToastDurationSeconds = 5;
            _shoutToastFontSize = 15.0;
            _chatLogFolderPath = @"C:\Nexon\TalesWeaver\ChatLog";
            _keywordInput = "";
            _fontFamily = "사용자 설정";
            _useAlertColor = false;
            _useAlertSound = false;
            _useMagicCircleAlert = false;
            _showExpTracker = false;
            _enableExperienceLimitAlert = false;
            _showExperienceLimitAlertWindow = false;
            _showDailyWeeklyContentOverlay = false;
            _showEtosDirectionAlert = false;
            _enableAbaddonRoadCountAlert = false;
            _showAbaddonRoadSummaryWindow = false;
            _enableCravingPleasureCountAlert = false;
            _showDungeonCountDisplayWindow = false;
            _showItemDropAlert = false;
            _showEtosHelperWindow = false;
            _showItemDropHelperWindow = false;
            _useCustomDropItemFilter = false;
            _customDropItemJson = string.Empty;
            _enableBuffTrackerAlert = false;
            _enableBuffTrackerEndSound = false;
            _showBuffTrackerWindow = false;
            _enableCharacterProfiles = false;
            _profile1DisplayName = "프로필1";
            _profile2DisplayName = "프로필2";
            _profile1SwitchLog = "[이클립스 코어] 진화 4단계-3세트 효과가 발동되었습니다.";
            _profile2SwitchLog = "[이클립스 코어] 진화 2단계-6세트 효과가 발동되었습니다.";
            _experienceLimitTotalExp = 0;
            _experienceLimitProfile1Exp = 0;
            _experienceLimitProfile2Exp = 0;
            _experienceLimitStateInitialized = false;
            _buffTrackerEndSoundVolume = 1.0;
            _itemDropAlertVolume = 0.1;
            _highlightAlertVolume = 1.0;
            _magicCircleAlertVolume = 1.0;
            _expBuffAlertVolume = 1.0;
            _bossAlertVolume = 1.0;
            _alwaysVisible = false;
            _enableDebugLogging = false;
            _exitHotKey = "";
            _toggleOverlayHotKey = "";
            _toggleAddonHotKey = "";
            _toggleAlwaysVisibleHotKey = "";
            _toggleDailyWeeklyContentHotKey = "";
            _toggleEtaRankingHotKey = "";
            _toggleCoefficientHotKey = "";
            _toggleEquipmentDbHotKey = "";
            _toggleEncryptHotKey = "";
            _toggleSettingsHotKey = "";
            _dailyWeeklyContentOverlayLeft = 0.0;
            _dailyWeeklyContentOverlayTop = 0.0;
            _subAddonWindowLeft = 0.0;
            _subAddonWindowTop = 0.0;
            _itemDropWindowLeft = 0.0;
            _itemDropWindowTop = 0.0;
            _buffTrackerWindowLeft = 0.0;
            _buffTrackerWindowTop = 0.0;
            _itemCalendarWindowLeft = 0.0;
            _itemCalendarWindowTop = 0.0;
            _abaddonRoadSummaryWindowLeft = 0.0;
            _abaddonRoadSummaryWindowTop = 0.0;
            _shoutToastWindowLeft = null;
            _shoutToastWindowTop = null;
            _recaptureSupplyWindowLeft = null;
            _recaptureSupplyWindowTop = null;
            _experienceLimitAlertWindowLeft = null;
            _experienceLimitAlertWindowTop = null;
            _dungeonCountDisplayWindowLeft = null;
            _dungeonCountDisplayWindowTop = null;
            _chatCloneWindow1Left = null;
            _chatCloneWindow1Top = null;
            _chatCloneWindow2Left = null;
            _chatCloneWindow2Top = null;
            _chatCloneWindow1FollowMainFont = true;
            _chatCloneWindow1FontFamily = string.Empty;
            _chatCloneWindow1FontSize = null;
            _chatCloneWindow2FollowMainFont = true;
            _chatCloneWindow2FontFamily = string.Empty;
            _chatCloneWindow2FontSize = null;
            _fontSize = 17.0;
            _lineMargin = 0.0;
            _lineMarginLeft = 0.0;
            _expAlarmThreshold = 10000;
            _abaddonRoadCountAlertDurationSeconds = 30;
            _lastSelectedPresetNumber = 1;
            ShowNormal = true;
            ShowShout = true;
            ShowTeam = true;
            ShowWhisper = true;
            ShowSystem = true;
            ShowClub = true;
            UseKeywordAlert = false;
            IsExpAlarmEnabled = false;
            EnableExperienceLimitAlert = false;
            ShowExperienceLimitAlertWindow = false;
            WindowWidth = 650.0;
            WindowHeight = 250.0;
            DailyWeeklyContentOverlayLeft = 0.0;
            DailyWeeklyContentOverlayTop = 0.0;
            SubAddonWindowLeft = 0.0;
            SubAddonWindowTop = 0.0;
            SubMenuWindowLeft = 0.0;
            SubMenuWindowTop = 0.0;
            MenuWindowLeft = 0.0;
            MenuWindowTop = 0.0;
            Preset1 = new WindowPositionPreset("프리셋 1", 0, 0);
            Preset2 = new WindowPositionPreset("프리셋 2", 0, 0);
            Preset3 = new WindowPositionPreset("프리셋 3", 0, 0);
            DungeonItemConfigs = CreateDefaultDungeonItemConfigs();
            BossAlertConfigs = CreateDefaultBossAlertConfigs();
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
                ["갈망하는 즐거움"] = new() { IsEnabled = true },
                ["청소 아르바이트"] = new() { IsEnabled = true },
                ["프라바 방어전"] = new() { IsEnabled = true },
                ["카타콤 지옥"] = new() { IsEnabled = true },
                ["어비스 - 심층Ⅰ"] = new() { IsEnabled = true },
                ["어비스 - 심층Ⅱ"] = new() { IsEnabled = true },
                ["어비스 - 심층Ⅲ"] = new() { IsEnabled = true },
                ["신조의 둥지 어려움"] = new() { IsEnabled = true },
                ["아페티리아 일반"] = new() { IsEnabled = false },
                ["아페티리아 어려움"] = new() { IsEnabled = true },
                ["시오칸 하임 보스 토벌전"] = new() { IsEnabled = true },
                ["시오칸 하임 오딘 전면전"] = new() { IsEnabled = true },
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
