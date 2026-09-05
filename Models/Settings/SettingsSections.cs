using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TWChatOverlay.Models
{
    // ============================================================================
    // settings.json v2의 실제 저장 구조.
    // 상태는 여기(섹션)에 저장되고, ChatSettings의 기존 평면 프로퍼티는
    // 호환용 위임(facade)으로 남아 호출부와 바인딩을 그대로 유지한다.
    // 값 검증(클램프)은 facade에서 수행하므로 섹션은 순수 데이터 홀더다.
    // ============================================================================

    /// <summary>창 위치/크기. null인 값은 저장하지 않는다.</summary>
    public class WindowRect
    {
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Left { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Top { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Width { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Height { get; set; }
        /// <summary>오른쪽 가장자리 고정용(경험치 추적창).</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? Right { get; set; }
    }

    // ----------------------------- Chat -----------------------------

    public class ChatFilterSettings
    {
        public bool ShowNormal { get; set; } = true;
        public bool ShowTeam { get; set; } = true;
        public bool ShowClub { get; set; } = true;
        public bool ShowShout { get; set; } = true;
        public bool ShowSystem { get; set; } = true;
        public bool ShowClubBoss { get; set; } = true;
        /// <summary>각 줄 앞에 [일반]/[팀]/[클럽]/[시스템] 종류 말머리를 붙인다.</summary>
        public bool ShowCategoryPrefix { get; set; } = false;
        public string NormalColor { get; set; } = "#FFFFFF";
        public string TeamColor { get; set; } = "#00BFFF";
        public string ClubColor { get; set; } = "#00FF00";
        public string SystemColor { get; set; } = "#FFFF00";
        public string ShoutColor { get; set; } = "#FF8000";
    }

    public class IdDisplaySettings
    {
        public bool ShowEtaLevel { get; set; } = true;
        public bool ShowEtaCharacter { get; set; } = true;
        public bool ShowIdTag { get; set; } = true;
        public bool ShowTimestamp { get; set; } = true;
        // 항목별 색상 동기화: 켜면 줄 색을 따르고, 끄면 아래 개별 색을 쓴다
        public bool SenderIdColorSync { get; set; } = true;
        public bool EtaLevelColorSync { get; set; } = true;
        public bool EtaCharacterColorSync { get; set; } = true;
        public bool TimestampColorSync { get; set; } = true;
        public bool IdTagColorSync { get; set; } = true;
        public string SenderIdColor { get; set; } = "#E8EAE9";
        // 에타 레벨 구간별 색상: 켜면 동기화/개별 색 대신 레벨 구간에 따라 칠한다 (레벨 0은 에타 정보 없음 → 표기 자체를 생략)
        public string EtaLevelRange1Color { get; set; } = "#C8CDD2"; // 1~20
        public string EtaLevelRange2Color { get; set; } = "#7EE081"; // 21~40
        public string EtaLevelRange3Color { get; set; } = "#5AC8E8"; // 41~60
        public string EtaLevelRange4Color { get; set; } = "#C08BFF"; // 61~80
        public string EtaLevelRange5Color { get; set; } = "#FFD84A"; // 81~
        public string EtaCharacterColor { get; set; } = "#5AC8E8";
        public string TimestampColor { get; set; } = "#9AA0A6";
        public string IdTagColor { get; set; } = "#B4BBC2";
        // 클럽 보스 공지 줄 색: 동기화를 끄면 클럽 색 대신 이 색을 쓴다
        public bool ClubBossColorSync { get; set; } = true;
        public string ClubBossColor { get; set; } = "#00FF00";
    }

    public class ChatFontSettings
    {
        public string Family { get; set; } = "사용자 설정";
        public double Size { get; set; } = 17.0;
        public double LineMargin { get; set; } = 0.0;
        public double LineMarginLeft { get; set; } = 0.0;
    }

    public class ChatCloneSettings
    {
        public bool IsOpen { get; set; } = false;
        public bool FollowMainFont { get; set; } = true;
        public string FontFamily { get; set; } = string.Empty;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? FontSize { get; set; }
        public string TabTag { get; set; } = "General";
    }

    public class ChatSection
    {
        public ChatFilterSettings Filters { get; set; } = new();
        public IdDisplaySettings IdDisplay { get; set; } = new();
        public ChatFontSettings Font { get; set; } = new();
        public string MainTabTag { get; set; } = "Basic";
        public ChatCloneSettings Clone1 { get; set; } = new();
        public ChatCloneSettings Clone2 { get; set; } = new();
    }

    // ----------------------------- Shout -----------------------------

    public class ShoutSection
    {
        public bool ToastPopup { get; set; } = true;
        public bool AutoCopyNickname { get; set; } = false;
        public int ToastDurationSeconds { get; set; } = 5;
        public double ToastFontSize { get; set; } = 15.0;
        public double ReplayFontSize { get; set; } = 14.0;
    }

    // ----------------------------- Alerts -----------------------------

    public class KeywordAlertSettings
    {
        public bool Enabled { get; set; } = false;
        public bool UseColor { get; set; } = false;
        public bool UseSound { get; set; } = false;
        public double Volume { get; set; } = 1.0;
        public string Keywords { get; set; } = string.Empty;
    }

    public class ExperienceAlertSettings
    {
        public bool ShowTracker { get; set; } = false;
        public bool LowEfficiencyAlarm { get; set; } = false;
        public long Threshold { get; set; } = 10000;
        public double Volume { get; set; } = 1.0;
        public double AlertFontSize { get; set; } = 18.0;
        public bool LimitAlertEnabled { get; set; } = false;
        public bool ShowLimitAlertWindow { get; set; } = false;
        public long LimitTotalExp { get; set; } = 0;
        public bool LimitStateInitialized { get; set; } = false;
        public string LimitLastRefreshWeekKey { get; set; } = string.Empty;
        public string LimitWeeklyPromptShownWeekKey { get; set; } = string.Empty;
    }

    public class DungeonAlertSettings
    {
        public bool MagicCircleAlert { get; set; } = false;
        public double MagicCircleVolume { get; set; } = 1.0;
        public bool EtosDirectionAlert { get; set; } = false;
        public bool ShowEtosHelperWindow { get; set; } = false;
        public bool ReflectionPatternAlert { get; set; } = false;
        public double ReflectionPatternVolume { get; set; } = 1.0;
        public bool AbandonRoadCountAlert { get; set; } = false;
        /// <summary>심연의 보물창고: 입장 후 금화 주머니 획득 카운트 표시.</summary>
        public bool TreasuryGoldCountAlert { get; set; } = true;
        /// <summary>심연의 보물창고 주간 상태: 주 키(ISO)와 회차별 금화 주머니 획득 수.</summary>
        public string TreasuryWeekKey { get; set; } = string.Empty;
        public List<int> TreasuryRunCounts { get; set; } = new();
        public bool ShowAbandonRoadSummaryWindow { get; set; } = false;
        public bool CravingPleasureCountAlert { get; set; } = false;
        public bool ShowCountDisplayWindow { get; set; } = false;
        public int CountAlertDurationSeconds { get; set; } = 30;
        public double CountDisplayFontSize { get; set; } = 18.0;
        /// <summary>갈망하는 즐거움 알림 창 지속 시간(초). 어밴던로드와 별도.</summary>
        public int CravingDurationSeconds { get; set; } = 30;
        /// <summary>갈망하는 즐거움 알림 창 폰트 크기. 어밴던로드와 별도.</summary>
        public double CravingFontSize { get; set; } = 18.0;
        /// <summary>보급품 탈환 진입 시 미니 지도 창 표시.</summary>
        public bool ShowRecaptureSupplyMap { get; set; } = true;
        public Dictionary<string, DungeonItemConfig> ItemConfigs { get; set; } = new();
    }

    public class ItemDropAlertSettings
    {
        public bool Enabled { get; set; } = false;
        public double Volume { get; set; } = 0.1;
        public double ToastFontSize { get; set; } = 18.0;
        public bool ShowHelperWindow { get; set; } = false;
        public bool UseCustomFilter { get; set; } = false;
        public string CustomFilterJson { get; set; } = string.Empty;
    }

    public class BuffTrackerAlertSettings
    {
        public bool Enabled { get; set; } = false;
        public bool EndSound { get; set; } = false;
        public double EndSoundVolume { get; set; } = 1.0;
    }

    public class BossAlertSettings
    {
        public double Volume { get; set; } = 1.0;
        public double ToastFontSize { get; set; } = 18.0;
        /// <summary>혼란한 대지: 등장 후 입장 가능 시간(4분)을 팝업으로 카운트다운.</summary>
        public bool ConfusedLandEntryCountdown { get; set; } = true;
        /// <summary>파멸의 기원: 등장 후 문 닫힘까지(6분)를 팝업으로 카운트다운.</summary>
        public bool OriginOfDoomEntryCountdown { get; set; } = true;
        public Dictionary<string, BossAlertConfig> Configs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public class AlertsSection
    {
        public KeywordAlertSettings Keyword { get; set; } = new();
        public ExperienceAlertSettings Experience { get; set; } = new();
        public DungeonAlertSettings Dungeon { get; set; } = new();
        public ItemDropAlertSettings ItemDrop { get; set; } = new();
        public BuffTrackerAlertSettings Buff { get; set; } = new();
        public BossAlertSettings Boss { get; set; } = new();
    }

    // ----------------------------- Windows -----------------------------

    public class WindowsSection
    {
        public WindowRect Main { get; set; } = new() { Width = 650.0, Height = 250.0 };
        public WindowRect Menu { get; set; } = new() { Left = 0.0, Top = 0.0 };
        public WindowRect SubMenu { get; set; } = new() { Left = 0.0, Top = 0.0 };
        public WindowRect DailyWeekly { get; set; } = new() { Left = 0.0, Top = 0.0, Width = 280.0, Height = 540.0 };
        public WindowRect SubAddon { get; set; } = new() { Left = 0.0, Top = 0.0 };
        public WindowRect ItemDropHelper { get; set; } = new() { Left = 0.0, Top = 0.0 };
        public WindowRect BuffTracker { get; set; } = new() { Left = 0.0, Top = 0.0 };
        public WindowRect ItemCalendar { get; set; } = new() { Left = 0.0, Top = 0.0 };
        public WindowRect AbandonRoadSummary { get; set; } = new() { Left = 0.0, Top = 0.0 };
        /// <summary>심연의 보물창고 주간 통계 창 위치 (미설정 시 어밴던로드 통계창 위치를 따른다).</summary>
        public WindowRect TreasurySummary { get; set; } = new();
        public WindowRect ShoutToast { get; set; } = new();
        public WindowRect MessengerToast { get; set; } = new();
        public WindowRect RecaptureSupply { get; set; } = new();
        public WindowRect ExperienceLimitAlert { get; set; } = new();
        public WindowRect BossAlertToast { get; set; } = new();
        /// <summary>통합 알림 스택(외치기·던전·경험치·아이템·보스) 기준 위치.</summary>
        public WindowRect ToastStack { get; set; } = new();
        /// <summary>알림 표시 위치 통합 여부. false면 알림 종류별로 각자의 저장 위치에 표시한다.</summary>
        public bool UnifiedToastStack { get; set; } = true;
        public WindowRect DungeonCountDisplay { get; set; } = new();
        public WindowRect ExpTracker { get; set; } = new();
        public WindowRect Clone1 { get; set; } = new();
        public WindowRect Clone2 { get; set; } = new();
        public WindowRect ShoutReplay { get; set; } = new();
        public WindowRect Memo { get; set; } = new();
        /// <summary>잠금 해제 인스펙터에서 지정한 창별 투명도(10~100%). 키 = 창 타입명.</summary>
        public Dictionary<string, double> OpacityPercents { get; set; } = new();
    }

    // ----------------------------- Ui -----------------------------

    public class MenuBarSettings
    {
        public bool Pinned { get; set; } = false;
        public bool Horizontal { get; set; } = false;
    }

    public class DailyWeeklyUiSettings
    {
        public bool Show { get; set; } = false;
        public bool AutoCollapseEnabled { get; set; } = false;
        public int AutoCollapseSeconds { get; set; } = 10;
        public double FontSize { get; set; } = 12.0;
    }

    public class CalendarUiSettings
    {
        public bool UseIcons { get; set; } = false;
        public double FontSize { get; set; } = 11.0;
    }

    public class MemoUiSettings
    {
        public string Text { get; set; } = string.Empty;
        public bool TextOnlyMode { get; set; } = false;
        public double FontSize { get; set; } = 20.0;
        public bool Bold { get; set; } = false;
        public bool Italic { get; set; } = false;
        public string ColorKey { get; set; } = "White";
    }

    public class UiSection
    {
        /// <summary>메인·서브 채팅창과 자동으로 뜨는 창의 통합 배경 불투명도(%). 20~100.</summary>
        public double OverlayOpacityPercent { get; set; } = 96.0;
        /// <summary>오버레이 항상 위. 끄면 처음 실행 때만 위에 두고, 이후에는 일반 창처럼 다른 앱 뒤로 내려간다.</summary>
        public bool OverlaysAlwaysOnTop { get; set; } = true;
        /// <summary>따로 여는 창(달력·컨텐츠·어밴던)의 배경 불투명도(%). 키는 OverlayOpacityService의 그룹 키.</summary>
        public Dictionary<string, double> OverlayOpacityByGroup { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public bool WindowSnapEnabled { get; set; } = false;
        public MenuBarSettings MenuBar { get; set; } = new();
        public DailyWeeklyUiSettings DailyWeekly { get; set; } = new();
        public CalendarUiSettings Calendar { get; set; } = new();
        public MemoUiSettings Memo { get; set; } = new();
        public double MessengerEtaFontSize { get; set; } = 18.0;
    }

    // ----------------------------- Hotkeys / System / Presets -----------------------------

    public class HotkeysSection
    {
        public string Exit { get; set; } = string.Empty;
        public string ToggleOverlay { get; set; } = string.Empty;
        public string ToggleDailyWeekly { get; set; } = string.Empty;
        public string ToggleSettings { get; set; } = string.Empty;
        public string ToggleTrayAll { get; set; } = string.Empty;
        public string ToggleUnlock { get; set; } = string.Empty;
    }

    public class SystemSection
    {
        public string ChatLogFolderPath { get; set; } = @"C:\Nexon\TalesWeaver\ChatLog";
        public bool WizardCompleted { get; set; } = false;
        public bool StartupLogReadCanceled { get; set; } = false;
        public bool StartupTodayOnlyBootstrapCompleted { get; set; } = false;
    }

}
