using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TWChatOverlay.Models
{
    /// <summary>
    /// 애플리케이션의 설정 클래스.
    /// 저장 구조(settings.json v2)는 섹션 프로퍼티(Chat/Shout/Alerts/Windows/Ui/Hotkeys/System)이며,
    /// 그 아래의 평면 프로퍼티들은 기존 호출부·바인딩 호환용 위임(facade)이다. ([JsonIgnore], 값 검증 담당)
    /// </summary>
    public partial class ChatSettings : INotifyPropertyChanged
    {
        public ChatSettings()
        {
            ApplyDefaultValues();
        }

        #region 저장 구조 (settings.json v2)

        /// <summary>설정 파일 스키마 버전. 구버전(평면 구조) 파일에는 이 키가 없다.</summary>
        [JsonPropertyOrder(0)]
        public int Version { get; set; } = 2;

        [JsonPropertyOrder(1)]
        public ChatSection Chat { get; set; } = new();

        [JsonPropertyOrder(2)]
        public ShoutSection Shout { get; set; } = new();

        [JsonPropertyOrder(3)]
        public AlertsSection Alerts { get; set; } = new();

        [JsonPropertyOrder(4)]
        public WindowsSection Windows { get; set; } = new();

        [JsonPropertyOrder(5)]
        public UiSection Ui { get; set; } = new();

        [JsonPropertyOrder(6)]
        public HotkeysSection Hotkeys { get; set; } = new();

        [JsonPropertyOrder(7)]
        [JsonPropertyName("System")]
        public SystemSection SystemConfig { get; set; } = new();

        /// <summary>역직렬화 후 누락된 컬렉션을 기본값으로 보정한다.</summary>
        public void EnsureLoadedDefaults()
        {
            Chat ??= new ChatSection();
            Shout ??= new ShoutSection();
            Alerts ??= new AlertsSection();
            Windows ??= new WindowsSection();
            Ui ??= new UiSection();
            Hotkeys ??= new HotkeysSection();
            SystemConfig ??= new SystemSection();

            if (Alerts.Dungeon.ItemConfigs == null || Alerts.Dungeon.ItemConfigs.Count == 0)
                Alerts.Dungeon.ItemConfigs = CreateDefaultDungeonItemConfigs();
            if (Alerts.Boss.Configs == null || Alerts.Boss.Configs.Count == 0)
                Alerts.Boss.Configs = CreateDefaultBossAlertConfigs();
            Windows.OpacityPercents ??= new Dictionary<string, double>();
            Ui.OverlayOpacityByGroup ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region 비저장 상태

        private bool _enableDebugLogging = false;

        #endregion

        #region 채팅 필터 (facade)

        [JsonIgnore]
        public bool ShowNormal { get => Chat.Filters.ShowNormal; set { Chat.Filters.ShowNormal = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool ShowTeam { get => Chat.Filters.ShowTeam; set { Chat.Filters.ShowTeam = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool ShowClub { get => Chat.Filters.ShowClub; set { Chat.Filters.ShowClub = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool ShowShout { get => Chat.Filters.ShowShout; set { Chat.Filters.ShowShout = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool ShowSystem { get => Chat.Filters.ShowSystem; set { Chat.Filters.ShowSystem = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool ShowClubBoss { get => Chat.Filters.ShowClubBoss; set { Chat.Filters.ShowClubBoss = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool ShowCategoryPrefix { get => Chat.Filters.ShowCategoryPrefix; set { Chat.Filters.ShowCategoryPrefix = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string NormalColor { get => Chat.Filters.NormalColor; set { Chat.Filters.NormalColor = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string TeamColor { get => Chat.Filters.TeamColor; set { Chat.Filters.TeamColor = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string ClubColor { get => Chat.Filters.ClubColor; set { Chat.Filters.ClubColor = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string SystemColor { get => Chat.Filters.SystemColor; set { Chat.Filters.SystemColor = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string ShoutColor { get => Chat.Filters.ShoutColor; set { Chat.Filters.ShoutColor = value; OnPropertyChanged(); } }

        #endregion

        #region 아이디 표시 (facade)

        [JsonIgnore]
        public bool ShowEtaLevel { get => Chat.IdDisplay.ShowEtaLevel; set { Chat.IdDisplay.ShowEtaLevel = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool ShowEtaCharacter { get => Chat.IdDisplay.ShowEtaCharacter; set { Chat.IdDisplay.ShowEtaCharacter = value; OnPropertyChanged(); } }
        /// <summary>idtag.txt에 등록된 아이디 태그를 채팅에 [태그]로 표시.</summary>
        [JsonIgnore]
        public bool ShowIdTag { get => Chat.IdDisplay.ShowIdTag; set { Chat.IdDisplay.ShowIdTag = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool ShowTimestamp { get => Chat.IdDisplay.ShowTimestamp; set { Chat.IdDisplay.ShowTimestamp = value; OnPropertyChanged(); } }
        // 항목별 색상 동기화: 켜면 줄 색을 따르고, 끄면 개별 색을 쓴다
        [JsonIgnore]
        public bool SenderIdColorSync { get => Chat.IdDisplay.SenderIdColorSync; set { Chat.IdDisplay.SenderIdColorSync = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string SenderIdColor { get => Chat.IdDisplay.SenderIdColor; set { Chat.IdDisplay.SenderIdColor = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool EtaLevelColorSync { get => Chat.IdDisplay.EtaLevelColorSync; set { Chat.IdDisplay.EtaLevelColorSync = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool EtaCharacterColorSync { get => Chat.IdDisplay.EtaCharacterColorSync; set { Chat.IdDisplay.EtaCharacterColorSync = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool TimestampColorSync { get => Chat.IdDisplay.TimestampColorSync; set { Chat.IdDisplay.TimestampColorSync = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool IdTagColorSync { get => Chat.IdDisplay.IdTagColorSync; set { Chat.IdDisplay.IdTagColorSync = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string IdTagColor { get => Chat.IdDisplay.IdTagColor; set { Chat.IdDisplay.IdTagColor = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string EtaCharacterColor { get => Chat.IdDisplay.EtaCharacterColor; set { Chat.IdDisplay.EtaCharacterColor = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string TimestampColor { get => Chat.IdDisplay.TimestampColor; set { Chat.IdDisplay.TimestampColor = value; OnPropertyChanged(); } }
        // 에타 레벨 구간별 색상 (동기화 해제 시 적용)
        [JsonIgnore]
        public string EtaLevelRange1Color { get => Chat.IdDisplay.EtaLevelRange1Color; set { Chat.IdDisplay.EtaLevelRange1Color = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string EtaLevelRange2Color { get => Chat.IdDisplay.EtaLevelRange2Color; set { Chat.IdDisplay.EtaLevelRange2Color = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string EtaLevelRange3Color { get => Chat.IdDisplay.EtaLevelRange3Color; set { Chat.IdDisplay.EtaLevelRange3Color = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string EtaLevelRange4Color { get => Chat.IdDisplay.EtaLevelRange4Color; set { Chat.IdDisplay.EtaLevelRange4Color = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string EtaLevelRange5Color { get => Chat.IdDisplay.EtaLevelRange5Color; set { Chat.IdDisplay.EtaLevelRange5Color = value; OnPropertyChanged(); } }

        #endregion

        #region 폰트/채팅창 기본 (facade)

        [JsonIgnore]
        public string FontFamily
        {
            get => Chat.Font.Family;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "사용자 설정" : value;
                if (Chat.Font.Family == normalized) return;
                Chat.Font.Family = normalized;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public double FontSize { get => Chat.Font.Size; set { Chat.Font.Size = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double LineMargin { get => Chat.Font.LineMargin; set { Chat.Font.LineMargin = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double LineMarginLeft { get => Chat.Font.LineMarginLeft; set { Chat.Font.LineMarginLeft = value; OnPropertyChanged(); } }

        [JsonIgnore]
        public string MainWindowChatTabTag
        {
            get => Chat.MainTabTag;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "Basic" : value;
                if (Chat.MainTabTag == normalized) return;
                Chat.MainTabTag = normalized;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public double WindowWidth { get => Windows.Main.Width ?? 650.0; set { Windows.Main.Width = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double WindowHeight { get => Windows.Main.Height ?? 250.0; set { Windows.Main.Height = value; OnPropertyChanged(); } }

        [JsonIgnore]
        public List<string> AvailableFonts { get; } = new() { "나눔고딕", "굴림", "사용자 설정" };

        #endregion

        #region 서브 채팅창 (facade)

        [JsonIgnore]
        public bool ChatCloneWindow1IsOpen
        {
            get => Chat.Clone1.IsOpen;
            set { if (Chat.Clone1.IsOpen == value) return; Chat.Clone1.IsOpen = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public bool ChatCloneWindow1FollowMainFont
        {
            get => Chat.Clone1.FollowMainFont;
            set { if (Chat.Clone1.FollowMainFont == value) return; Chat.Clone1.FollowMainFont = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public string ChatCloneWindow1FontFamily
        {
            get => Chat.Clone1.FontFamily;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "사용자 설정" : value;
                if (Chat.Clone1.FontFamily == normalized) return;
                Chat.Clone1.FontFamily = normalized;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public double? ChatCloneWindow1FontSize
        {
            get => Chat.Clone1.FontSize;
            set { if (Chat.Clone1.FontSize == value) return; Chat.Clone1.FontSize = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public string ChatCloneWindow1TabTag
        {
            get => Chat.Clone1.TabTag;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "General" : value;
                if (Chat.Clone1.TabTag == normalized) return;
                Chat.Clone1.TabTag = normalized;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public bool ChatCloneWindow2IsOpen
        {
            get => Chat.Clone2.IsOpen;
            set { if (Chat.Clone2.IsOpen == value) return; Chat.Clone2.IsOpen = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public bool ChatCloneWindow2FollowMainFont
        {
            get => Chat.Clone2.FollowMainFont;
            set { if (Chat.Clone2.FollowMainFont == value) return; Chat.Clone2.FollowMainFont = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public string ChatCloneWindow2FontFamily
        {
            get => Chat.Clone2.FontFamily;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "사용자 설정" : value;
                if (Chat.Clone2.FontFamily == normalized) return;
                Chat.Clone2.FontFamily = normalized;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public double? ChatCloneWindow2FontSize
        {
            get => Chat.Clone2.FontSize;
            set { if (Chat.Clone2.FontSize == value) return; Chat.Clone2.FontSize = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public string ChatCloneWindow2TabTag
        {
            get => Chat.Clone2.TabTag;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "General" : value;
                if (Chat.Clone2.TabTag == normalized) return;
                Chat.Clone2.TabTag = normalized;
                OnPropertyChanged();
            }
        }

        #endregion

        #region 외치기 (facade)

        [JsonIgnore]
        public bool ShowShoutToastPopup { get => Shout.ToastPopup; set { Shout.ToastPopup = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool AutoCopyShoutNickname { get => Shout.AutoCopyNickname; set { Shout.AutoCopyNickname = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public int ShoutToastDurationSeconds
        {
            get => Shout.ToastDurationSeconds;
            set
            {
                int clamped = Math.Max(1, Math.Min(300, value));
                if (Shout.ToastDurationSeconds == clamped) return;
                Shout.ToastDurationSeconds = clamped;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public double ShoutToastFontSize
        {
            get => Shout.ToastFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(40.0, value));
                if (Math.Abs(Shout.ToastFontSize - clamped) < 0.0001) return;
                Shout.ToastFontSize = clamped;
                OnPropertyChanged();
            }
        }
        /// <summary>외치기 로그 창 본문 폰트 크기.</summary>
        [JsonIgnore]
        public double ShoutReplayFontSize
        {
            get => Shout.ReplayFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(28.0, value));
                if (Math.Abs(Shout.ReplayFontSize - clamped) < 0.0001) return;
                Shout.ReplayFontSize = clamped;
                OnPropertyChanged();
            }
        }

        #endregion

        #region 키워드 알림 (facade)

        [JsonIgnore]
        public bool UseKeywordAlert { get => Alerts.Keyword.Enabled; set { Alerts.Keyword.Enabled = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool UseAlertColor { get => Alerts.Keyword.UseColor; set { Alerts.Keyword.UseColor = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool UseAlertSound { get => Alerts.Keyword.UseSound; set { Alerts.Keyword.UseSound = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string KeywordInput { get => Alerts.Keyword.Keywords; set { Alerts.Keyword.Keywords = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double HighlightAlertVolume
        {
            get => Alerts.Keyword.Volume;
            set
            {
                double clamped = ClampVolume(value);
                if (Math.Abs(Alerts.Keyword.Volume - clamped) < 0.0001) return;
                Alerts.Keyword.Volume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HighlightAlertVolumePercent));
            }
        }
        [JsonIgnore]
        public double HighlightAlertVolumePercent
        {
            get => Math.Round(Alerts.Keyword.Volume * 100.0, 0);
            set => HighlightAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }

        #endregion

        #region 경험치 (facade)

        [JsonIgnore]
        public bool ShowExpTracker
        {
            get => Alerts.Experience.ShowTracker;
            set
            {
                if (Alerts.Experience.ShowTracker == value) return;
                Alerts.Experience.ShowTracker = value;

                if (!value)
                {
                    IsExpAlarmEnabled = false;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsExpAlarmEnabled));
            }
        }
        [JsonIgnore]
        public bool IsExpAlarmEnabled { get => Alerts.Experience.LowEfficiencyAlarm; set { Alerts.Experience.LowEfficiencyAlarm = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public long ExpAlarmThreshold { get => Alerts.Experience.Threshold; set { Alerts.Experience.Threshold = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double ExpAlarmThresholdMan
        {
            get => Alerts.Experience.Threshold / 10000.0;
            set
            {
                double safeValue = value <= 0 ? 1.0 : value;
                ExpAlarmThreshold = (long)(safeValue * 10000);
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public double ExpBuffAlertVolume
        {
            get => Alerts.Experience.Volume;
            set
            {
                double clamped = ClampVolume(value);
                if (Math.Abs(Alerts.Experience.Volume - clamped) < 0.0001) return;
                Alerts.Experience.Volume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpBuffAlertVolumePercent));
            }
        }
        [JsonIgnore]
        public double ExpBuffAlertVolumePercent
        {
            get => Math.Round(Alerts.Experience.Volume * 100.0, 0);
            set => ExpBuffAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        /// <summary>경험치 누적 알림창 본문 폰트 크기.</summary>
        [JsonIgnore]
        public double ExperienceAlertFontSize
        {
            get => Alerts.Experience.AlertFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(40.0, value));
                if (Math.Abs(Alerts.Experience.AlertFontSize - clamped) < 0.0001) return;
                Alerts.Experience.AlertFontSize = clamped;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public bool EnableExperienceLimitAlert
        {
            get => Alerts.Experience.LimitAlertEnabled;
            set { if (Alerts.Experience.LimitAlertEnabled == value) return; Alerts.Experience.LimitAlertEnabled = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public bool ShowExperienceLimitAlertWindow
        {
            get => Alerts.Experience.ShowLimitAlertWindow;
            set { if (Alerts.Experience.ShowLimitAlertWindow == value) return; Alerts.Experience.ShowLimitAlertWindow = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public long ExperienceLimitTotalExp
        {
            get => Alerts.Experience.LimitTotalExp;
            set
            {
                long clamped = Math.Max(0, value);
                if (Alerts.Experience.LimitTotalExp == clamped) return;
                Alerts.Experience.LimitTotalExp = clamped;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public bool ExperienceLimitStateInitialized
        {
            get => Alerts.Experience.LimitStateInitialized;
            set { if (Alerts.Experience.LimitStateInitialized == value) return; Alerts.Experience.LimitStateInitialized = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public string ExperienceLimitLastRefreshWeekKey
        {
            get => Alerts.Experience.LimitLastRefreshWeekKey;
            set
            {
                value ??= string.Empty;
                if (Alerts.Experience.LimitLastRefreshWeekKey == value) return;
                Alerts.Experience.LimitLastRefreshWeekKey = value;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public string ExperienceLimitWeeklyPromptShownWeekKey
        {
            get => Alerts.Experience.LimitWeeklyPromptShownWeekKey;
            set
            {
                value ??= string.Empty;
                if (Alerts.Experience.LimitWeeklyPromptShownWeekKey == value) return;
                Alerts.Experience.LimitWeeklyPromptShownWeekKey = value;
                OnPropertyChanged();
            }
        }

        #endregion

        #region 던전 도우미 (facade)

        [JsonIgnore]
        public bool UseMagicCircleAlert { get => Alerts.Dungeon.MagicCircleAlert; set { Alerts.Dungeon.MagicCircleAlert = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double MagicCircleAlertVolume
        {
            get => Alerts.Dungeon.MagicCircleVolume;
            set
            {
                double clamped = ClampVolume(value);
                if (Math.Abs(Alerts.Dungeon.MagicCircleVolume - clamped) < 0.0001) return;
                Alerts.Dungeon.MagicCircleVolume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MagicCircleAlertVolumePercent));
            }
        }
        [JsonIgnore]
        public double MagicCircleAlertVolumePercent
        {
            get => Math.Round(Alerts.Dungeon.MagicCircleVolume * 100.0, 0);
            set => MagicCircleAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonIgnore]
        public bool ShowEtosDirectionAlert { get => Alerts.Dungeon.EtosDirectionAlert; set { Alerts.Dungeon.EtosDirectionAlert = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool ShowEtosHelperWindow { get => Alerts.Dungeon.ShowEtosHelperWindow; set { Alerts.Dungeon.ShowEtosHelperWindow = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool EnableReflectionPatternAlert { get => Alerts.Dungeon.ReflectionPatternAlert; set { Alerts.Dungeon.ReflectionPatternAlert = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double ReflectionPatternAlertVolume
        {
            get => Alerts.Dungeon.ReflectionPatternVolume;
            set
            {
                double clamped = ClampVolume(value);
                if (Math.Abs(Alerts.Dungeon.ReflectionPatternVolume - clamped) < 0.0001) return;
                Alerts.Dungeon.ReflectionPatternVolume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ReflectionPatternAlertVolumePercent));
            }
        }
        [JsonIgnore]
        public double ReflectionPatternAlertVolumePercent
        {
            get => Math.Round(Alerts.Dungeon.ReflectionPatternVolume * 100.0, 0);
            set => ReflectionPatternAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonIgnore]
        public bool EnableAbandonRoadCountAlert { get => Alerts.Dungeon.AbandonRoadCountAlert; set { Alerts.Dungeon.AbandonRoadCountAlert = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool ShowAbandonRoadSummaryWindow { get => Alerts.Dungeon.ShowAbandonRoadSummaryWindow; set { Alerts.Dungeon.ShowAbandonRoadSummaryWindow = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool EnableCravingPleasureCountAlert { get => Alerts.Dungeon.CravingPleasureCountAlert; set { Alerts.Dungeon.CravingPleasureCountAlert = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool ShowDungeonCountDisplayWindow { get => Alerts.Dungeon.ShowCountDisplayWindow; set { Alerts.Dungeon.ShowCountDisplayWindow = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public int AbandonRoadCountAlertDurationSeconds
        {
            get => Alerts.Dungeon.CountAlertDurationSeconds;
            set
            {
                int clamped = Math.Max(1, Math.Min(300, value));
                if (Alerts.Dungeon.CountAlertDurationSeconds == clamped) return;
                Alerts.Dungeon.CountAlertDurationSeconds = clamped;
                OnPropertyChanged();
            }
        }
        /// <summary>던전 카운트 알림창 본문 폰트 크기.</summary>
        [JsonIgnore]
        public double DungeonCountDisplayFontSize
        {
            get => Alerts.Dungeon.CountDisplayFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(40.0, value));
                if (Math.Abs(Alerts.Dungeon.CountDisplayFontSize - clamped) < 0.0001) return;
                Alerts.Dungeon.CountDisplayFontSize = clamped;
                OnPropertyChanged();
            }
        }
        /// <summary>갈망하는 즐거움 알림 창 지속 시간(초).</summary>
        [JsonIgnore]
        public int CravingPleasureCountAlertDurationSeconds
        {
            get => Alerts.Dungeon.CravingDurationSeconds;
            set
            {
                int clamped = Math.Max(1, Math.Min(300, value));
                if (Alerts.Dungeon.CravingDurationSeconds == clamped) return;
                Alerts.Dungeon.CravingDurationSeconds = clamped;
                OnPropertyChanged();
            }
        }
        /// <summary>갈망하는 즐거움 알림 창 폰트 크기.</summary>
        [JsonIgnore]
        public double CravingPleasureCountFontSize
        {
            get => Alerts.Dungeon.CravingFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(40.0, value));
                if (Math.Abs(Alerts.Dungeon.CravingFontSize - clamped) < 0.0001) return;
                Alerts.Dungeon.CravingFontSize = clamped;
                OnPropertyChanged();
            }
        }
        /// <summary>보급품 탈환 진입 시 미니 지도 창 표시.</summary>
        [JsonIgnore]
        public bool ShowRecaptureSupplyMap { get => Alerts.Dungeon.ShowRecaptureSupplyMap; set { Alerts.Dungeon.ShowRecaptureSupplyMap = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public Dictionary<string, DungeonItemConfig> DungeonItemConfigs
        {
            get => Alerts.Dungeon.ItemConfigs;
            set => Alerts.Dungeon.ItemConfigs = value ?? new Dictionary<string, DungeonItemConfig>();
        }

        #endregion

        #region 아이템 드롭 (facade)

        [JsonIgnore]
        public bool ShowItemDropAlert { get => Alerts.ItemDrop.Enabled; set { Alerts.ItemDrop.Enabled = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double ItemDropAlertVolume
        {
            get => Alerts.ItemDrop.Volume;
            set
            {
                double clamped = Math.Max(0.0, Math.Min(0.1, value));
                if (Math.Abs(Alerts.ItemDrop.Volume - clamped) < 0.0001) return;
                Alerts.ItemDrop.Volume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ItemDropAlertVolumePercent));
            }
        }
        [JsonIgnore]
        public double ItemDropAlertVolumePercent
        {
            get => Math.Round(Alerts.ItemDrop.Volume * 1000.0, 0);
            set => ItemDropAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 1000.0;
        }
        /// <summary>아이템 드롭 알림 토스트 폰트 크기.</summary>
        [JsonIgnore]
        public double ItemDropToastFontSize
        {
            get => Alerts.ItemDrop.ToastFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(40.0, value));
                if (Math.Abs(Alerts.ItemDrop.ToastFontSize - clamped) < 0.0001) return;
                Alerts.ItemDrop.ToastFontSize = clamped;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public bool ShowItemDropHelperWindow { get => Alerts.ItemDrop.ShowHelperWindow; set { Alerts.ItemDrop.ShowHelperWindow = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool UseCustomDropItemFilter { get => Alerts.ItemDrop.UseCustomFilter; set { Alerts.ItemDrop.UseCustomFilter = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public string CustomDropItemJson { get => Alerts.ItemDrop.CustomFilterJson; set { Alerts.ItemDrop.CustomFilterJson = value ?? string.Empty; OnPropertyChanged(); } }

        #endregion

        #region 버프 추적 (facade)

        [JsonIgnore]
        public bool EnableBuffTrackerAlert { get => Alerts.Buff.Enabled; set { Alerts.Buff.Enabled = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool EnableBuffTrackerEndSound { get => Alerts.Buff.EndSound; set { Alerts.Buff.EndSound = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double BuffTrackerEndSoundVolume
        {
            get => Alerts.Buff.EndSoundVolume;
            set
            {
                double clamped = ClampVolume(value);
                if (Math.Abs(Alerts.Buff.EndSoundVolume - clamped) < 0.0001) return;
                Alerts.Buff.EndSoundVolume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BuffTrackerEndSoundVolumePercent));
            }
        }
        [JsonIgnore]
        public double BuffTrackerEndSoundVolumePercent
        {
            get => Math.Round(Alerts.Buff.EndSoundVolume * 100.0, 0);
            set => BuffTrackerEndSoundVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonIgnore]
        public bool ShowBuffTrackerWindow { get => Alerts.Buff.ShowMaxSizeWindow; set { Alerts.Buff.ShowMaxSizeWindow = value; OnPropertyChanged(); } }

        #endregion

        #region 필드 보스 (facade)

        [JsonIgnore]
        public double BossAlertVolume
        {
            get => Alerts.Boss.Volume;
            set
            {
                double clamped = ClampVolume(value);
                if (Math.Abs(Alerts.Boss.Volume - clamped) < 0.0001) return;
                Alerts.Boss.Volume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BossAlertVolumePercent));
            }
        }
        [JsonIgnore]
        public double BossAlertVolumePercent
        {
            get => Math.Round(Alerts.Boss.Volume * 100.0, 0);
            set => BossAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonIgnore]
        public double BossAlertToastFontSize
        {
            get => Alerts.Boss.ToastFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(40.0, value));
                if (Math.Abs(Alerts.Boss.ToastFontSize - clamped) < 0.0001) return;
                Alerts.Boss.ToastFontSize = clamped;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public Dictionary<string, BossAlertConfig> BossAlertConfigs
        {
            get => Alerts.Boss.Configs;
            set => Alerts.Boss.Configs = value ?? new Dictionary<string, BossAlertConfig>(StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region 창 위치/크기 (facade)

        [JsonIgnore]
        public double? MenuWindowLeft { get => Windows.Menu.Left; set { Windows.Menu.Left = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double? MenuWindowTop { get => Windows.Menu.Top; set { Windows.Menu.Top = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double? SubMenuWindowLeft { get => Windows.SubMenu.Left; set { Windows.SubMenu.Left = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double? SubMenuWindowTop { get => Windows.SubMenu.Top; set { Windows.SubMenu.Top = value; OnPropertyChanged(); } }

        [JsonIgnore]
        public double? DailyWeeklyContentOverlayLeft
        {
            get => Windows.DailyWeekly.Left;
            set { if (Windows.DailyWeekly.Left == value) return; Windows.DailyWeekly.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? DailyWeeklyContentOverlayTop
        {
            get => Windows.DailyWeekly.Top;
            set { if (Windows.DailyWeekly.Top == value) return; Windows.DailyWeekly.Top = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? DailyWeeklyContentOverlayWidth
        {
            get => Windows.DailyWeekly.Width;
            set { if (Windows.DailyWeekly.Width == value) return; Windows.DailyWeekly.Width = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? DailyWeeklyContentOverlayHeight
        {
            get => Windows.DailyWeekly.Height;
            set { if (Windows.DailyWeekly.Height == value) return; Windows.DailyWeekly.Height = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? SubAddonWindowLeft
        {
            get => Windows.SubAddon.Left;
            set { if (Windows.SubAddon.Left == value) return; Windows.SubAddon.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? SubAddonWindowTop
        {
            get => Windows.SubAddon.Top;
            set { if (Windows.SubAddon.Top == value) return; Windows.SubAddon.Top = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? ItemDropWindowLeft
        {
            get => Windows.ItemDropHelper.Left;
            set { if (Windows.ItemDropHelper.Left == value) return; Windows.ItemDropHelper.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ItemDropWindowTop
        {
            get => Windows.ItemDropHelper.Top;
            set { if (Windows.ItemDropHelper.Top == value) return; Windows.ItemDropHelper.Top = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? BuffTrackerWindowLeft
        {
            get => Windows.BuffTracker.Left;
            set { if (Windows.BuffTracker.Left == value) return; Windows.BuffTracker.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? BuffTrackerWindowTop
        {
            get => Windows.BuffTracker.Top;
            set { if (Windows.BuffTracker.Top == value) return; Windows.BuffTracker.Top = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? ItemCalendarWindowLeft
        {
            get => Windows.ItemCalendar.Left;
            set { if (Windows.ItemCalendar.Left == value) return; Windows.ItemCalendar.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ItemCalendarWindowTop
        {
            get => Windows.ItemCalendar.Top;
            set { if (Windows.ItemCalendar.Top == value) return; Windows.ItemCalendar.Top = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? AbandonRoadSummaryWindowLeft
        {
            get => Windows.AbandonRoadSummary.Left;
            set { if (Windows.AbandonRoadSummary.Left == value) return; Windows.AbandonRoadSummary.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? AbandonRoadSummaryWindowTop
        {
            get => Windows.AbandonRoadSummary.Top;
            set { if (Windows.AbandonRoadSummary.Top == value) return; Windows.AbandonRoadSummary.Top = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? ShoutToastWindowLeft
        {
            get => Windows.ShoutToast.Left;
            set { if (Windows.ShoutToast.Left == value) return; Windows.ShoutToast.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ShoutToastWindowTop
        {
            get => Windows.ShoutToast.Top;
            set { if (Windows.ShoutToast.Top == value) return; Windows.ShoutToast.Top = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? MessengerToastWindowLeft
        {
            get => Windows.MessengerToast.Left;
            set { if (Windows.MessengerToast.Left == value) return; Windows.MessengerToast.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? MessengerToastWindowTop
        {
            get => Windows.MessengerToast.Top;
            set { if (Windows.MessengerToast.Top == value) return; Windows.MessengerToast.Top = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? RecaptureSupplyWindowLeft
        {
            get => Windows.RecaptureSupply.Left;
            set { if (Windows.RecaptureSupply.Left == value) return; Windows.RecaptureSupply.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? RecaptureSupplyWindowTop
        {
            get => Windows.RecaptureSupply.Top;
            set { if (Windows.RecaptureSupply.Top == value) return; Windows.RecaptureSupply.Top = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? RecaptureSupplyWindowWidth
        {
            get => Windows.RecaptureSupply.Width;
            set { if (Windows.RecaptureSupply.Width == value) return; Windows.RecaptureSupply.Width = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? RecaptureSupplyWindowHeight
        {
            get => Windows.RecaptureSupply.Height;
            set { if (Windows.RecaptureSupply.Height == value) return; Windows.RecaptureSupply.Height = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? ExperienceLimitAlertWindowLeft
        {
            get => Windows.ExperienceLimitAlert.Left;
            set { if (Windows.ExperienceLimitAlert.Left == value) return; Windows.ExperienceLimitAlert.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ExperienceLimitAlertWindowTop
        {
            get => Windows.ExperienceLimitAlert.Top;
            set { if (Windows.ExperienceLimitAlert.Top == value) return; Windows.ExperienceLimitAlert.Top = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? BossAlertToastWindowLeft
        {
            get => Windows.BossAlertToast.Left;
            set { if (Windows.BossAlertToast.Left == value) return; Windows.BossAlertToast.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? BossAlertToastWindowTop
        {
            get => Windows.BossAlertToast.Top;
            set { if (Windows.BossAlertToast.Top == value) return; Windows.BossAlertToast.Top = value; OnPropertyChanged(); }
        }
        // 통합 알림 스택(외치기·던전·경험치·아이템·보스) 기준 위치
        [JsonIgnore]
        public double? ToastStackLeft
        {
            get => Windows.ToastStack.Left;
            set { if (Windows.ToastStack.Left == value) return; Windows.ToastStack.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ToastStackTop
        {
            get => Windows.ToastStack.Top;
            set { if (Windows.ToastStack.Top == value) return; Windows.ToastStack.Top = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? BossAlertToastWindowWidth
        {
            get => Windows.BossAlertToast.Width;
            set { if (Windows.BossAlertToast.Width == value) return; Windows.BossAlertToast.Width = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? BossAlertToastWindowHeight
        {
            get => Windows.BossAlertToast.Height;
            set { if (Windows.BossAlertToast.Height == value) return; Windows.BossAlertToast.Height = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? DungeonCountDisplayWindowLeft
        {
            get => Windows.DungeonCountDisplay.Left;
            set { if (Windows.DungeonCountDisplay.Left == value) return; Windows.DungeonCountDisplay.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? DungeonCountDisplayWindowTop
        {
            get => Windows.DungeonCountDisplay.Top;
            set { if (Windows.DungeonCountDisplay.Top == value) return; Windows.DungeonCountDisplay.Top = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? ExpTrackerWindowLeft
        {
            get => Windows.ExpTracker.Left;
            set { if (Windows.ExpTracker.Left == value) return; Windows.ExpTracker.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ExpTrackerWindowTop
        {
            get => Windows.ExpTracker.Top;
            set { if (Windows.ExpTracker.Top == value) return; Windows.ExpTracker.Top = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ExpTrackerWindowRight
        {
            get => Windows.ExpTracker.Right;
            set { if (Windows.ExpTracker.Right == value) return; Windows.ExpTracker.Right = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? ChatCloneWindow1Left
        {
            get => Windows.Clone1.Left;
            set { if (Windows.Clone1.Left == value) return; Windows.Clone1.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ChatCloneWindow1Top
        {
            get => Windows.Clone1.Top;
            set { if (Windows.Clone1.Top == value) return; Windows.Clone1.Top = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ChatCloneWindow1Width
        {
            get => Windows.Clone1.Width;
            set { if (Windows.Clone1.Width == value) return; Windows.Clone1.Width = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ChatCloneWindow1Height
        {
            get => Windows.Clone1.Height;
            set { if (Windows.Clone1.Height == value) return; Windows.Clone1.Height = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ChatCloneWindow2Left
        {
            get => Windows.Clone2.Left;
            set { if (Windows.Clone2.Left == value) return; Windows.Clone2.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ChatCloneWindow2Top
        {
            get => Windows.Clone2.Top;
            set { if (Windows.Clone2.Top == value) return; Windows.Clone2.Top = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ChatCloneWindow2Width
        {
            get => Windows.Clone2.Width;
            set { if (Windows.Clone2.Width == value) return; Windows.Clone2.Width = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ChatCloneWindow2Height
        {
            get => Windows.Clone2.Height;
            set { if (Windows.Clone2.Height == value) return; Windows.Clone2.Height = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? ShoutReplayWindowLeft
        {
            get => Windows.ShoutReplay.Left;
            set { if (Windows.ShoutReplay.Left == value) return; Windows.ShoutReplay.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ShoutReplayWindowTop
        {
            get => Windows.ShoutReplay.Top;
            set { if (Windows.ShoutReplay.Top == value) return; Windows.ShoutReplay.Top = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ShoutReplayWindowWidth
        {
            get => Windows.ShoutReplay.Width;
            set { if (Windows.ShoutReplay.Width == value) return; Windows.ShoutReplay.Width = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? ShoutReplayWindowHeight
        {
            get => Windows.ShoutReplay.Height;
            set { if (Windows.ShoutReplay.Height == value) return; Windows.ShoutReplay.Height = value; OnPropertyChanged(); }
        }

        [JsonIgnore]
        public double? MemoOverlayWindowLeft
        {
            get => Windows.Memo.Left;
            set { if (Windows.Memo.Left == value) return; Windows.Memo.Left = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double? MemoOverlayWindowTop
        {
            get => Windows.Memo.Top;
            set { if (Windows.Memo.Top == value) return; Windows.Memo.Top = value; OnPropertyChanged(); }
        }

        /// <summary>잠금 해제 인스펙터에서 지정한 창별 투명도(10~100%). 키 = 창 타입명(서브 채팅창은 슬롯 포함).</summary>
        [JsonIgnore]
        public Dictionary<string, double> WindowOpacityPercents
        {
            get => Windows.OpacityPercents;
            set
            {
                Windows.OpacityPercents = value ?? new Dictionary<string, double>();
                OnPropertyChanged();
            }
        }

        public void SetBuffTrackerWindowPosition(double? left, double? top, bool notify)
        {
            Windows.BuffTracker.Left = left;
            Windows.BuffTracker.Top = top;

            if (notify)
                OnPropertyChanged(nameof(BuffTrackerWindowLeft));

            if (notify)
                OnPropertyChanged(nameof(BuffTrackerWindowTop));
        }

        #endregion

        #region 화면/UI (facade)

        /// <summary>오버레이 항상 위. 끄면 처음 실행 때만 위에 두고, 이후에는 일반 창처럼 동작.</summary>
        [JsonIgnore]
        public bool OverlaysAlwaysOnTop { get => Ui.OverlaysAlwaysOnTop; set { Ui.OverlaysAlwaysOnTop = value; OnPropertyChanged(); } }

        /// <summary>메인·서브 채팅창과 자동으로 뜨는 창의 통합 배경 불투명도(%). 20~100. 텍스트는 항상 불투명.</summary>
        [JsonIgnore]
        public double OverlayOpacityPercent
        {
            get => Ui.OverlayOpacityPercent;
            set
            {
                double clamped = Math.Clamp(value, 20.0, 100.0);
                if (Math.Abs(Ui.OverlayOpacityPercent - clamped) < 0.001) return;
                Ui.OverlayOpacityPercent = clamped;
                OnPropertyChanged();
            }
        }

        /// <summary>따로 여는 창(달력·컨텐츠·어밴던)의 배경 불투명도(%). 키는 OverlayOpacityService의 그룹 키.</summary>
        [JsonIgnore]
        public Dictionary<string, double> OverlayOpacityByGroup
        {
            get => Ui.OverlayOpacityByGroup;
            set
            {
                Ui.OverlayOpacityByGroup.Clear();
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                        Ui.OverlayOpacityByGroup[pair.Key] = Math.Clamp(pair.Value, 20.0, 100.0);
                    }
                }
                OnPropertyChanged();
            }
        }

        /// <summary>그룹 불투명도(%). 저장값이 없으면 메인 채팅창 값을 따른다.</summary>
        public double GetOverlayOpacity(string group)
        {
            if (!string.IsNullOrEmpty(group) && Ui.OverlayOpacityByGroup.TryGetValue(group, out double stored))
                return Math.Clamp(stored, 20.0, 100.0);
            return OverlayOpacityPercent;
        }

        /// <summary>그룹 불투명도(%)를 저장한다. 값이 실제로 바뀌었으면 true.</summary>
        public bool SetOverlayOpacity(string group, double percent)
        {
            if (string.IsNullOrEmpty(group)) return false;

            double clamped = Math.Clamp(percent, 20.0, 100.0);
            if (Ui.OverlayOpacityByGroup.TryGetValue(group, out double current)
                && Math.Abs(current - clamped) < 0.001)
                return false;

            Ui.OverlayOpacityByGroup[group] = clamped;
            OnPropertyChanged(nameof(OverlayOpacityByGroup));
            return true;
        }

        /// <summary>잠금 해제 모드에서 채팅창끼리 가장자리에 자석처럼 붙는 스냅 기능.</summary>
        [JsonIgnore]
        public bool WindowSnapEnabled { get => Ui.WindowSnapEnabled; set { Ui.WindowSnapEnabled = value; OnPropertyChanged(); } }

        /// <summary>메뉴 바 고정: true면 자동 접힘 없이 메뉴가 상시 표시된다 (아이콘 클릭으로 전환).</summary>
        [JsonIgnore]
        public bool MenuWindowPinned { get => Ui.MenuBar.Pinned; set { Ui.MenuBar.Pinned = value; OnPropertyChanged(); } }

        /// <summary>메뉴 바 방향: false=세로형(기본), true=가로형.</summary>
        [JsonIgnore]
        public bool MenuWindowHorizontal { get => Ui.MenuBar.Horizontal; set { Ui.MenuBar.Horizontal = value; OnPropertyChanged(); } }

        [JsonIgnore]
        public bool ShowDailyWeeklyContentOverlay
        {
            get => Ui.DailyWeekly.Show;
            set { if (Ui.DailyWeekly.Show == value) return; Ui.DailyWeekly.Show = value; OnPropertyChanged(); }
        }
        /// <summary>일일/주간 컨텐츠 창 자동 접기: 지정 시간 후 제목 표시줄만 남긴다.</summary>
        [JsonIgnore]
        public bool DailyWeeklyAutoCollapseEnabled { get => Ui.DailyWeekly.AutoCollapseEnabled; set { Ui.DailyWeekly.AutoCollapseEnabled = value; OnPropertyChanged(); } }
        /// <summary>자동 접기 전 표시 유지 시간(초).</summary>
        [JsonIgnore]
        public int DailyWeeklyAutoCollapseSeconds { get => Ui.DailyWeekly.AutoCollapseSeconds; set { Ui.DailyWeekly.AutoCollapseSeconds = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public double DailyWeeklyContentFontSize
        {
            get => Ui.DailyWeekly.FontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(28.0, value));
                if (Math.Abs(Ui.DailyWeekly.FontSize - clamped) < 0.0001) return;
                Ui.DailyWeekly.FontSize = clamped;
                OnPropertyChanged();
            }
        }

        /// <summary>달력 날짜 칸 아이템을 그림(true)/텍스트(false)로 표시.</summary>
        [JsonIgnore]
        public bool ItemCalendarUseIcons
        {
            get => Ui.Calendar.UseIcons;
            set { if (Ui.Calendar.UseIcons == value) return; Ui.Calendar.UseIcons = value; OnPropertyChanged(); }
        }
        /// <summary>달력(아이템 획득 내역) 본문 기준 폰트 크기. 헤더의 슬라이더로 조절.</summary>
        [JsonIgnore]
        public double ItemCalendarFontSize
        {
            get => Ui.Calendar.FontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(28.0, value));
                if (Math.Abs(Ui.Calendar.FontSize - clamped) < 0.0001) return;
                Ui.Calendar.FontSize = clamped;
                OnPropertyChanged();
            }
        }

        [JsonIgnore]
        public string MemoOverlayText
        {
            get => Ui.Memo.Text;
            set
            {
                value ??= string.Empty;
                if (Ui.Memo.Text == value) return;
                Ui.Memo.Text = value;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public bool MemoOverlayTextOnlyMode
        {
            get => Ui.Memo.TextOnlyMode;
            set { if (Ui.Memo.TextOnlyMode == value) return; Ui.Memo.TextOnlyMode = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public double MemoOverlayFontSize
        {
            get => Ui.Memo.FontSize;
            set
            {
                if (Math.Abs(Ui.Memo.FontSize - value) < 0.0001) return;
                Ui.Memo.FontSize = value;
                OnPropertyChanged();
            }
        }
        [JsonIgnore]
        public bool MemoOverlayBold
        {
            get => Ui.Memo.Bold;
            set { if (Ui.Memo.Bold == value) return; Ui.Memo.Bold = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public bool MemoOverlayItalic
        {
            get => Ui.Memo.Italic;
            set { if (Ui.Memo.Italic == value) return; Ui.Memo.Italic = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public string MemoOverlayColorKey
        {
            get => Ui.Memo.ColorKey;
            set
            {
                value ??= "White";
                if (Ui.Memo.ColorKey == value) return;
                Ui.Memo.ColorKey = value;
                OnPropertyChanged();
            }
        }

        /// <summary>1:1 채팅 에타레벨 확인 창 본문 폰트 크기.</summary>
        [JsonIgnore]
        public double MessengerEtaFontSize
        {
            get => Ui.MessengerEtaFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(40.0, value));
                if (Math.Abs(Ui.MessengerEtaFontSize - clamped) < 0.0001) return;
                Ui.MessengerEtaFontSize = clamped;
                OnPropertyChanged();
            }
        }

        #endregion

        #region 단축키 (facade)

        [JsonIgnore]
        public string ExitHotKey
        {
            get => Hotkeys.Exit;
            set { if (Hotkeys.Exit == value) return; Hotkeys.Exit = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public string ToggleOverlayHotKey
        {
            get => Hotkeys.ToggleOverlay;
            set { if (Hotkeys.ToggleOverlay == value) return; Hotkeys.ToggleOverlay = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public string ToggleDailyWeeklyContentHotKey
        {
            get => Hotkeys.ToggleDailyWeekly;
            set { if (Hotkeys.ToggleDailyWeekly == value) return; Hotkeys.ToggleDailyWeekly = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public string ToggleSettingsHotKey
        {
            get => Hotkeys.ToggleSettings;
            set { if (Hotkeys.ToggleSettings == value) return; Hotkeys.ToggleSettings = value; OnPropertyChanged(); }
        }
        /// <summary>모든 창을 트레이로 보내기/복원 토글 단축키.</summary>
        [JsonIgnore]
        public string ToggleTrayAllHotKey
        {
            get => Hotkeys.ToggleTrayAll;
            set { if (Hotkeys.ToggleTrayAll == value) return; Hotkeys.ToggleTrayAll = value; OnPropertyChanged(); }
        }
        /// <summary>잠금 해제 모드 토글 단축키.</summary>
        [JsonIgnore]
        public string ToggleUnlockHotKey
        {
            get => Hotkeys.ToggleUnlock;
            set { if (Hotkeys.ToggleUnlock == value) return; Hotkeys.ToggleUnlock = value; OnPropertyChanged(); }
        }

        #endregion

        #region 시스템 (facade)

        [JsonIgnore]
        public string ChatLogFolderPath
        {
            get => SystemConfig.ChatLogFolderPath;
            set { if (SystemConfig.ChatLogFolderPath == value) return; SystemConfig.ChatLogFolderPath = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public bool EnableDebugLogging { get => _enableDebugLogging; set { _enableDebugLogging = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool InitialSetupWizardCompleted
        {
            get => SystemConfig.WizardCompleted;
            set { if (SystemConfig.WizardCompleted == value) return; SystemConfig.WizardCompleted = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public bool StartupLogReadCanceled
        {
            get => SystemConfig.StartupLogReadCanceled;
            set { if (SystemConfig.StartupLogReadCanceled == value) return; SystemConfig.StartupLogReadCanceled = value; OnPropertyChanged(); }
        }
        [JsonIgnore]
        public bool StartupTodayOnlyBootstrapCompleted
        {
            get => SystemConfig.StartupTodayOnlyBootstrapCompleted;
            set { if (SystemConfig.StartupTodayOnlyBootstrapCompleted == value) return; SystemConfig.StartupTodayOnlyBootstrapCompleted = value; OnPropertyChanged(); }
        }

        #endregion

        #region Interface

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        #endregion
    }
}
