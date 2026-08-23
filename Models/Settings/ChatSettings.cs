using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace TWChatOverlay.Models
{
    /// <summary>
    /// 애플리케이션의 설정 클래스
    /// </summary>
    public partial class ChatSettings : INotifyPropertyChanged
    {
        public ChatSettings()
        {
            ApplyDefaultValues();
        }

        #region Fields

        private string _normalColor = "#FFFFFF";
        private string _teamColor = "#00BFFF";
        private string _clubColor = "#00FF00";
        private string _systemColor = "#FFFF00";
        private string _shoutColor = "#FF8000";
        private bool _decorationColorSync = true;
        private string _etaLevelColor = "#FFD84A";
        private string _etaCharacterColor = "#5AC8E8";
        private string _timestampColor = "#9AA0A6";
        private bool _showEtaLevel = true;
        private bool _showEtaCharacter = true;
        private bool _showIdTag = true;
        private bool _showTimestamp = true;
        private bool _showShoutToastPopup = true;
        private bool _autoCopyShoutNickname = false;
        private int _shoutToastDurationSeconds = 5;
        private double _shoutToastFontSize = 18.0;
        private double _dailyWeeklyContentFontSize = 12.0;
        private string _chatLogFolderPath = @"C:\Nexon\TalesWeaver\ChatLog";
        private string _keywordInput = "";
        private string _fontFamily = "사용자 설정";
        private bool _useAlertColor = false;
        private bool _useAlertSound = false;
        private bool _useMagicCircleAlert = false;
        private bool _showExpTracker = false;
        private bool _enableExperienceLimitAlert = false;
        private bool _showExperienceLimitAlertWindow = false;
        private bool _showDailyWeeklyContentOverlay = false;
        private bool _showEtosDirectionAlert = false;
        private bool _enableReflectionPatternAlert = false;
        private bool _enableAbandonRoadCountAlert = false;
        private bool _showAbandonRoadSummaryWindow = false;
        private bool _enableCravingPleasureCountAlert = false;
        private bool _showDungeonCountDisplayWindow = false;
        private bool _showItemDropAlert = false;
        private bool _showEtosHelperWindow = false;
        private bool _menuWindowHorizontal = false;
        private bool _menuWindowPinned = false;
        private bool _windowSnapEnabled = false;
        private bool _dailyWeeklyAutoCollapseEnabled = false;
        private int _dailyWeeklyAutoCollapseSeconds = 10;
        private bool _showItemDropHelperWindow = false;
        private bool _useCustomDropItemFilter = false;
        private string _customDropItemJson = string.Empty;
        private bool _enableBuffTrackerAlert = false;
        private bool _enableBuffTrackerEndSound = false;
        private bool _showBuffTrackerWindow = false;
        private double _buffTrackerEndSoundVolume = 1.0;
        private double _itemDropAlertVolume = 0.1;
        private double _highlightAlertVolume = 1.0;
        private double _magicCircleAlertVolume = 1.0;
        private double _reflectionPatternAlertVolume = 1.0;
        private double _expBuffAlertVolume = 1.0;
        private double _bossAlertVolume = 1.0;
        private bool _alwaysVisible = false;
        private bool _enableDebugLogging = false;
        private string _exitHotKey = "";
        private string _toggleOverlayHotKey = "";
        private string _toggleAlwaysVisibleHotKey = "";
        private string _toggleDailyWeeklyContentHotKey = "";
        private string _toggleSettingsHotKey = "";
        private string _toggleTrayAllHotKey = "";
        private string _toggleUnlockHotKey = "";
        private double _overlayOpacityPercent = 96.0;
        private readonly Dictionary<string, double> _overlayOpacityByGroup = new(StringComparer.OrdinalIgnoreCase);
        private string _mainWindowChatTabTag = "Basic";
        private double? _dailyWeeklyContentOverlayLeft = 0.0;
        private double? _dailyWeeklyContentOverlayTop = 0.0;
        private double? _dailyWeeklyContentOverlayWidth = 280.0;
        private double? _dailyWeeklyContentOverlayHeight = 540.0;
        private double? _expTrackerWindowLeft = null;
        private double? _expTrackerWindowTop = null;
        private double? _expTrackerWindowRight = null;
        private double? _subAddonWindowLeft = 0.0;
        private double? _subAddonWindowTop = 0.0;
        private double? _itemDropWindowLeft = 0.0;
        private double? _itemDropWindowTop = 0.0;
        private double? _buffTrackerWindowLeft = 0.0;
        private double? _buffTrackerWindowTop = 0.0;
        private double? _itemCalendarWindowLeft = 0.0;
        private double? _itemCalendarWindowTop = 0.0;
        private double? _AbandonRoadSummaryWindowLeft = 0.0;
        private double? _AbandonRoadSummaryWindowTop = 0.0;
        private double? _shoutToastWindowLeft = null;
        private double? _shoutToastWindowTop = null;
        private double? _messengerToastWindowLeft = null;
        private double? _messengerToastWindowTop = null;
        private double? _recaptureSupplyWindowLeft = null;
        private double? _recaptureSupplyWindowTop = null;
        private double? _experienceLimitAlertWindowLeft = null;
        private double? _experienceLimitAlertWindowTop = null;
        private double? _dungeonCountDisplayWindowLeft = null;
        private double? _dungeonCountDisplayWindowTop = null;
        private double? _chatCloneWindow1Left = null;
        private double? _chatCloneWindow1Top = null;
        private double? _chatCloneWindow1Width = null;
        private double? _chatCloneWindow1Height = null;
        private double? _chatCloneWindow2Left = null;
        private double? _chatCloneWindow2Top = null;
        private double? _chatCloneWindow2Width = null;
        private double? _chatCloneWindow2Height = null;
        private string _chatCloneWindow1TabTag = "General";
        private string _chatCloneWindow2TabTag = "General";
        private bool _chatCloneWindow1IsOpen = false;
        private bool _chatCloneWindow2IsOpen = false;
        private double? _shoutReplayWindowLeft = null;
        private double? _shoutReplayWindowTop = null;
        private double? _memoOverlayWindowLeft = null;
        private double? _memoOverlayWindowTop = null;
        private string _memoOverlayText = string.Empty;
        private bool _memoOverlayTextOnlyMode = false;
        private double _memoOverlayFontSize = 20.0;
        private bool _memoOverlayBold = false;
        private bool _memoOverlayItalic = false;
        private string _memoOverlayColorKey = "White";
        private bool _initialSetupWizardCompleted = false;
        private bool _startupLogReadCanceled = false;
        private bool _startupTodayOnlyBootstrapCompleted = false;
        private bool _chatCloneWindow1FollowMainFont = true;
        private string _chatCloneWindow1FontFamily = string.Empty;
        private double? _chatCloneWindow1FontSize = null;
        private bool _chatCloneWindow2FollowMainFont = true;
        private string _chatCloneWindow2FontFamily = string.Empty;
        private double? _chatCloneWindow2FontSize = null;

        private double _fontSize = 17.0;
        private double _lineMargin = 0.0;
        private double _lineMarginLeft = 0.0;

        private long _expAlarmThreshold = 10000;
        private long _experienceLimitTotalExp = 0;
        private bool _experienceLimitStateInitialized = false;
        private string _experienceLimitLastRefreshWeekKey = string.Empty;
        private string _experienceLimitWeeklyPromptShownWeekKey = string.Empty;
        private int _AbandonRoadCountAlertDurationSeconds = 30;
        private int _lastSelectedPresetNumber = 1;

        private WindowPositionPreset _preset1 = new("프리셋 1", 0, 0);
        private WindowPositionPreset _preset2 = new("프리셋 2", 0, 0);
        private WindowPositionPreset _preset3 = new("프리셋 3", 0, 0);
        #endregion

        #region Properties

        [JsonPropertyOrder(1)]
        public bool ShowNormal { get; set; } = true;
        [JsonPropertyOrder(2)]
        public bool ShowShout { get; set; } = true;
        [JsonPropertyOrder(36)]
        public bool ShowEtaLevel { get => _showEtaLevel; set { _showEtaLevel = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(37)]
        public bool ShowEtaCharacter { get => _showEtaCharacter; set { _showEtaCharacter = value; OnPropertyChanged(); } }
        /// <summary>idtag.txt에 등록된 아이디 태그를 채팅에 [태그]로 표시.</summary>
        [JsonPropertyOrder(374)]
        public bool ShowIdTag { get => _showIdTag; set { _showIdTag = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(371)]
        public bool ShowTimestamp { get => _showTimestamp; set { _showTimestamp = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(38)]
        public bool ShowShoutToastPopup { get => _showShoutToastPopup; set { _showShoutToastPopup = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(39)]
        public bool AutoCopyShoutNickname { get => _autoCopyShoutNickname; set { _autoCopyShoutNickname = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(40)]
        public int ShoutToastDurationSeconds
        {
            get => _shoutToastDurationSeconds;
            set
            {
                int clamped = Math.Max(1, Math.Min(300, value));
                if (_shoutToastDurationSeconds == clamped) return;
                _shoutToastDurationSeconds = clamped;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(41)]
        public double ShoutToastFontSize
        {
            get => _shoutToastFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(40.0, value));
                if (Math.Abs(_shoutToastFontSize - clamped) < 0.0001) return;
                _shoutToastFontSize = clamped;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(3)]
        public bool ShowTeam { get; set; } = true;
        [JsonPropertyOrder(4)]
        public bool ShowWhisper { get; set; } = true;
        [JsonPropertyOrder(5)]
        public bool ShowSystem { get; set; } = true;
        [JsonPropertyOrder(6)]
        public bool ShowClub { get; set; } = true;
        [JsonPropertyOrder(7)]
        public bool ShowClubBoss { get; set; } = true;
        [JsonPropertyOrder(8)]
        public bool UseKeywordAlert { get; set; } = false;
        [JsonPropertyOrder(9)]
        public bool UseAlertColor { get => _useAlertColor; set { _useAlertColor = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(10)]
        public bool UseAlertSound { get => _useAlertSound; set { _useAlertSound = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(11)]
        public bool UseMagicCircleAlert { get => _useMagicCircleAlert; set { _useMagicCircleAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(12)]
        public bool ShowEtosDirectionAlert { get => _showEtosDirectionAlert; set { _showEtosDirectionAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(121)]
        public bool EnableReflectionPatternAlert { get => _enableReflectionPatternAlert; set { _enableReflectionPatternAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(13)]
        public bool EnableAbandonRoadCountAlert { get => _enableAbandonRoadCountAlert; set { _enableAbandonRoadCountAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(14)]
        public bool ShowAbandonRoadSummaryWindow { get => _showAbandonRoadSummaryWindow; set { _showAbandonRoadSummaryWindow = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(15)]
        public bool EnableCravingPleasureCountAlert { get => _enableCravingPleasureCountAlert; set { _enableCravingPleasureCountAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(16)]
        public bool ShowDungeonCountDisplayWindow { get => _showDungeonCountDisplayWindow; set { _showDungeonCountDisplayWindow = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(17)]
        public bool ShowItemDropAlert { get => _showItemDropAlert; set { _showItemDropAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(18)]
        public bool ShowEtosHelperWindow { get => _showEtosHelperWindow; set { _showEtosHelperWindow = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(19)]
        public bool ShowItemDropHelperWindow { get => _showItemDropHelperWindow; set { _showItemDropHelperWindow = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(20)]
        public bool UseCustomDropItemFilter { get => _useCustomDropItemFilter; set { _useCustomDropItemFilter = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(21)]
        public string CustomDropItemJson { get => _customDropItemJson; set { _customDropItemJson = value ?? string.Empty; OnPropertyChanged(); } }
        [JsonPropertyOrder(22)]
        public bool EnableBuffTrackerAlert { get => _enableBuffTrackerAlert; set { _enableBuffTrackerAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(23)]
        public bool EnableBuffTrackerEndSound { get => _enableBuffTrackerEndSound; set { _enableBuffTrackerEndSound = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(24)]
        public double BuffTrackerEndSoundVolume
        {
            get => _buffTrackerEndSoundVolume;
            set
            {
                double clamped = ClampVolume(value);
                if (Math.Abs(_buffTrackerEndSoundVolume - clamped) < 0.0001) return;
                _buffTrackerEndSoundVolume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BuffTrackerEndSoundVolumePercent));
            }
        }
        [JsonPropertyOrder(25)]
        public bool ShowBuffTrackerWindow { get => _showBuffTrackerWindow; set { _showBuffTrackerWindow = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(246)]
        public long ExperienceLimitTotalExp
        {
            get => _experienceLimitTotalExp;
            set
            {
                long clamped = Math.Max(0, value);
                if (_experienceLimitTotalExp == clamped) return;
                _experienceLimitTotalExp = clamped;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(249)]
        public bool ExperienceLimitStateInitialized
        {
            get => _experienceLimitStateInitialized;
            set
            {
                if (_experienceLimitStateInitialized == value) return;
                _experienceLimitStateInitialized = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(250)]
        public string ExperienceLimitLastRefreshWeekKey
        {
            get => _experienceLimitLastRefreshWeekKey;
            set
            {
                value ??= string.Empty;
                if (_experienceLimitLastRefreshWeekKey == value) return;
                _experienceLimitLastRefreshWeekKey = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(251)]
        public string ExperienceLimitWeeklyPromptShownWeekKey
        {
            get => _experienceLimitWeeklyPromptShownWeekKey;
            set
            {
                value ??= string.Empty;
                if (_experienceLimitWeeklyPromptShownWeekKey == value) return;
                _experienceLimitWeeklyPromptShownWeekKey = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(25)]
        public double ItemDropAlertVolume
        {
            get => _itemDropAlertVolume;
            set
            {
                double clamped = Math.Max(0.0, Math.Min(0.1, value));
                if (Math.Abs(_itemDropAlertVolume - clamped) < 0.0001) return;
                _itemDropAlertVolume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ItemDropAlertVolumePercent));
            }
        }
        [JsonPropertyOrder(26)]
        public double HighlightAlertVolume
        {
            get => _highlightAlertVolume;
            set
            {
                double clamped = ClampVolume(value);
                if (Math.Abs(_highlightAlertVolume - clamped) < 0.0001) return;
                _highlightAlertVolume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HighlightAlertVolumePercent));
            }
        }
        [JsonPropertyOrder(27)]
        public double MagicCircleAlertVolume
        {
            get => _magicCircleAlertVolume;
            set
            {
                double clamped = ClampVolume(value);
                if (Math.Abs(_magicCircleAlertVolume - clamped) < 0.0001) return;
                _magicCircleAlertVolume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MagicCircleAlertVolumePercent));
            }
        }
        [JsonPropertyOrder(271)]
        public double ReflectionPatternAlertVolume
        {
            get => _reflectionPatternAlertVolume;
            set
            {
                double clamped = ClampVolume(value);
                if (Math.Abs(_reflectionPatternAlertVolume - clamped) < 0.0001) return;
                _reflectionPatternAlertVolume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ReflectionPatternAlertVolumePercent));
            }
        }
        [JsonPropertyOrder(28)]
        public double ExpBuffAlertVolume
        {
            get => _expBuffAlertVolume;
            set
            {
                double clamped = ClampVolume(value);
                if (Math.Abs(_expBuffAlertVolume - clamped) < 0.0001) return;
                _expBuffAlertVolume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpBuffAlertVolumePercent));
            }
        }
        [JsonPropertyOrder(29)]
        public double BossAlertVolume
        {
            get => _bossAlertVolume;
            set
            {
                double clamped = ClampVolume(value);
                if (Math.Abs(_bossAlertVolume - clamped) < 0.0001) return;
                _bossAlertVolume = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BossAlertVolumePercent));
            }
        }
        [JsonPropertyOrder(26)]
        [JsonIgnore]
        public double BuffTrackerEndSoundVolumePercent
        {
            get => Math.Round(_buffTrackerEndSoundVolume * 100.0, 0);
            set => BuffTrackerEndSoundVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonPropertyOrder(27)]
        [JsonIgnore]
        public double ItemDropAlertVolumePercent
        {
            get => Math.Round(_itemDropAlertVolume * 1000.0, 0);
            set => ItemDropAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 1000.0;
        }
        [JsonPropertyOrder(28)]
        [JsonIgnore]
        public double HighlightAlertVolumePercent
        {
            get => Math.Round(_highlightAlertVolume * 100.0, 0);
            set => HighlightAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonPropertyOrder(29)]
        [JsonIgnore]
        public double MagicCircleAlertVolumePercent
        {
            get => Math.Round(_magicCircleAlertVolume * 100.0, 0);
            set => MagicCircleAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonPropertyOrder(291)]
        [JsonIgnore]
        public double ReflectionPatternAlertVolumePercent
        {
            get => Math.Round(_reflectionPatternAlertVolume * 100.0, 0);
            set => ReflectionPatternAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonPropertyOrder(30)]
        [JsonIgnore]
        public double ExpBuffAlertVolumePercent
        {
            get => Math.Round(_expBuffAlertVolume * 100.0, 0);
            set => ExpBuffAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonPropertyOrder(31)]
        [JsonIgnore]
        public double BossAlertVolumePercent
        {
            get => Math.Round(_bossAlertVolume * 100.0, 0);
            set => BossAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonPropertyOrder(25)]
        public bool ShowExpTracker
        {
            get => _showExpTracker;
            set
            {
                if (_showExpTracker == value) return;
                _showExpTracker = value;

                if (!_showExpTracker)
                {
                    IsExpAlarmEnabled = false;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(IsExpAlarmEnabled));
            }
        }

        [JsonPropertyOrder(26)]
        public double? ExpTrackerWindowLeft
        {
            get => _expTrackerWindowLeft;
            set
            {
                if (_expTrackerWindowLeft == value) return;
                _expTrackerWindowLeft = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(27)]
        public double? ExpTrackerWindowTop
        {
            get => _expTrackerWindowTop;
            set
            {
                if (_expTrackerWindowTop == value) return;
                _expTrackerWindowTop = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(28)]
        public double? ExpTrackerWindowRight
        {
            get => _expTrackerWindowRight;
            set
            {
                if (_expTrackerWindowRight == value) return;
                _expTrackerWindowRight = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(24)]
        public bool IsExpAlarmEnabled { get; set; } = false;
        [JsonPropertyOrder(25)]
        public bool EnableExperienceLimitAlert
        {
            get => _enableExperienceLimitAlert;
            set
            {
                if (_enableExperienceLimitAlert == value) return;
                _enableExperienceLimitAlert = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(26)]
        public bool ShowExperienceLimitAlertWindow
        {
            get => _showExperienceLimitAlertWindow;
            set
            {
                if (_showExperienceLimitAlertWindow == value) return;
                _showExperienceLimitAlertWindow = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(27)]
        public bool ShowDailyWeeklyContentOverlay
        {
            get => _showDailyWeeklyContentOverlay;
            set
            {
                if (_showDailyWeeklyContentOverlay == value) return;
                _showDailyWeeklyContentOverlay = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(28)]
        public bool AlwaysVisible { get => _alwaysVisible; set { _alwaysVisible = value; OnPropertyChanged(); } }
        [JsonIgnore]
        public bool EnableDebugLogging { get => _enableDebugLogging; set { _enableDebugLogging = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(20)]
        public string ChatLogFolderPath
        {
            get => _chatLogFolderPath;
            set
            {
                if (_chatLogFolderPath == value) return;
                _chatLogFolderPath = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(21)]
        public string KeywordInput
        {
            get => _keywordInput;
            set
            {
                _keywordInput = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(22)]
        public string FontFamily
        {
            get => _fontFamily;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "사용자 설정" : value;
                if (_fontFamily == normalized) return;
                _fontFamily = normalized;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(23)]
        public string NormalColor { get => _normalColor; set { _normalColor = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(24)]
        public string TeamColor { get => _teamColor; set { _teamColor = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(25)]
        public string ClubColor { get => _clubColor; set { _clubColor = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(26)]
        public string SystemColor { get => _systemColor; set { _systemColor = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(27)]
        public string ShoutColor { get => _shoutColor; set { _shoutColor = value; OnPropertyChanged(); } }

        /// <summary>에타 레벨/캐릭터/타임스탬프 색을 줄 색과 동기화. 끄면 아래 개별 색을 쓴다.</summary>
        public bool DecorationColorSync { get => _decorationColorSync; set { _decorationColorSync = value; OnPropertyChanged(); } }
        public string EtaLevelColor { get => _etaLevelColor; set { _etaLevelColor = value; OnPropertyChanged(); } }
        public string EtaCharacterColor { get => _etaCharacterColor; set { _etaCharacterColor = value; OnPropertyChanged(); } }
        public string TimestampColor { get => _timestampColor; set { _timestampColor = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(28)]
        public string ExitHotKey
        {
            get => _exitHotKey;
            set
            {
                if (_exitHotKey == value) return;
                _exitHotKey = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(29)]
        public string ToggleOverlayHotKey
        {
            get => _toggleOverlayHotKey;
            set
            {
                if (_toggleOverlayHotKey == value) return;
                _toggleOverlayHotKey = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(31)]
        public string ToggleAlwaysVisibleHotKey
        {
            get => _toggleAlwaysVisibleHotKey;
            set
            {
                if (_toggleAlwaysVisibleHotKey == value) return;
                _toggleAlwaysVisibleHotKey = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(32)]
        public string ToggleDailyWeeklyContentHotKey
        {
            get => _toggleDailyWeeklyContentHotKey;
            set
            {
                if (_toggleDailyWeeklyContentHotKey == value) return;
                _toggleDailyWeeklyContentHotKey = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(37)]
        public string ToggleSettingsHotKey
        {
            get => _toggleSettingsHotKey;
            set
            {
                if (_toggleSettingsHotKey == value) return;
                _toggleSettingsHotKey = value;
                OnPropertyChanged();
            }
        }

        /// <summary>모든 창을 트레이로 보내기/복원 토글 단축키.</summary>
        [JsonPropertyOrder(372)]
        public string ToggleTrayAllHotKey
        {
            get => _toggleTrayAllHotKey;
            set
            {
                if (_toggleTrayAllHotKey == value) return;
                _toggleTrayAllHotKey = value;
                OnPropertyChanged();
            }
        }

        /// <summary>잠금 해제 모드 토글 단축키.</summary>
        [JsonPropertyOrder(375)]
        public string ToggleUnlockHotKey
        {
            get => _toggleUnlockHotKey;
            set
            {
                if (_toggleUnlockHotKey == value) return;
                _toggleUnlockHotKey = value;
                OnPropertyChanged();
            }
        }

        /// <summary>메인·서브 채팅창과 자동으로 뜨는 창의 통합 배경 불투명도(%). 20~100. 텍스트는 항상 불투명.</summary>
        [JsonPropertyOrder(373)]
        public double OverlayOpacityPercent
        {
            get => _overlayOpacityPercent;
            set
            {
                double clamped = Math.Clamp(value, 20.0, 100.0);
                if (Math.Abs(_overlayOpacityPercent - clamped) < 0.001) return;
                _overlayOpacityPercent = clamped;
                OnPropertyChanged();
            }
        }

        /// <summary>따로 여는 창(달력·컨텐츠·어밴던)의 배경 불투명도(%). 키는 OverlayOpacityService의 그룹 키.</summary>
        [JsonPropertyOrder(376)]
        public Dictionary<string, double> OverlayOpacityByGroup
        {
            get => _overlayOpacityByGroup;
            set
            {
                _overlayOpacityByGroup.Clear();
                if (value != null)
                {
                    foreach (var pair in value)
                    {
                        if (string.IsNullOrWhiteSpace(pair.Key)) continue;
                        _overlayOpacityByGroup[pair.Key] = Math.Clamp(pair.Value, 20.0, 100.0);
                    }
                }
                OnPropertyChanged();
            }
        }

        /// <summary>그룹 불투명도(%). 저장값이 없으면 메인 채팅창 값을 따른다.</summary>
        public double GetOverlayOpacity(string group)
        {
            if (!string.IsNullOrEmpty(group) && _overlayOpacityByGroup.TryGetValue(group, out double stored))
                return Math.Clamp(stored, 20.0, 100.0);
            return OverlayOpacityPercent;
        }

        /// <summary>그룹 불투명도(%)를 저장한다. 값이 실제로 바뀌었으면 true.</summary>
        public bool SetOverlayOpacity(string group, double percent)
        {
            if (string.IsNullOrEmpty(group)) return false;

            double clamped = Math.Clamp(percent, 20.0, 100.0);
            if (_overlayOpacityByGroup.TryGetValue(group, out double current)
                && Math.Abs(current - clamped) < 0.001)
                return false;

            _overlayOpacityByGroup[group] = clamped;
            OnPropertyChanged(nameof(OverlayOpacityByGroup));
            return true;
        }

        [JsonPropertyOrder(38)]
        public string MainWindowChatTabTag
        {
            get => _mainWindowChatTabTag;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "Basic" : value;
                if (_mainWindowChatTabTag == normalized) return;
                _mainWindowChatTabTag = normalized;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(40)]
        [JsonIgnore]
        public List<string> AvailableFonts { get; } = new() { "나눔고딕", "굴림", "사용자 설정" };
        [JsonPropertyOrder(41)]
        public double WindowWidth { get; set; } = 650.0;
        [JsonPropertyOrder(42)]
        public double WindowHeight { get; set; } = 250.0;
        [JsonPropertyOrder(43)]
        public double FontSize { get => _fontSize; set { _fontSize = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(44)]
        public double LineMargin { get => _lineMargin; set { _lineMargin = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(45)]
        public double LineMarginLeft { get => _lineMarginLeft; set { _lineMarginLeft = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(46)]
        public long ExpAlarmThreshold { get => _expAlarmThreshold; set { _expAlarmThreshold = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(47)]
        public int AbandonRoadCountAlertDurationSeconds
        {
            get => _AbandonRoadCountAlertDurationSeconds;
            set
            {
                int clamped = Math.Max(1, Math.Min(300, value));
                if (_AbandonRoadCountAlertDurationSeconds == clamped) return;
                _AbandonRoadCountAlertDurationSeconds = clamped;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(900)]
        public int LastSelectedPresetNumber
        {
            get => _lastSelectedPresetNumber;
            set
            {
                int normalized = value is >= 1 and <= 3 ? value : 1;
                if (_lastSelectedPresetNumber == normalized) return;
                _lastSelectedPresetNumber = normalized;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(50)]
        public double? DailyWeeklyContentOverlayLeft
        {
            get => _dailyWeeklyContentOverlayLeft;
            set
            {
                if (_dailyWeeklyContentOverlayLeft == value) return;
                _dailyWeeklyContentOverlayLeft = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(51)]
        public double? DailyWeeklyContentOverlayTop
        {
            get => _dailyWeeklyContentOverlayTop;
            set
            {
                if (_dailyWeeklyContentOverlayTop == value) return;
                _dailyWeeklyContentOverlayTop = value;
                OnPropertyChanged();
            }
        }

        public double? DailyWeeklyContentOverlayWidth
        {
            get => _dailyWeeklyContentOverlayWidth;
            set
            {
                if (_dailyWeeklyContentOverlayWidth == value) return;
                _dailyWeeklyContentOverlayWidth = value;
                OnPropertyChanged();
            }
        }

        public double? DailyWeeklyContentOverlayHeight
        {
            get => _dailyWeeklyContentOverlayHeight;
            set
            {
                if (_dailyWeeklyContentOverlayHeight == value) return;
                _dailyWeeklyContentOverlayHeight = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(52)]
        public double? SubAddonWindowLeft
        {
            get => _subAddonWindowLeft;
            set
            {
                if (_subAddonWindowLeft == value) return;
                _subAddonWindowLeft = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(53)]
        public double? SubAddonWindowTop
        {
            get => _subAddonWindowTop;
            set
            {
                if (_subAddonWindowTop == value) return;
                _subAddonWindowTop = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(54)]
        public double? SubMenuWindowLeft { get; set; } = 0.0;
        [JsonPropertyOrder(55)]
        public double? SubMenuWindowTop { get; set; } = 0.0;
        [JsonPropertyOrder(56)]
        /// <summary>일일/주간 컨텐츠 창 자동 접기: 지정 시간 후 제목 표시줄만 남긴다.</summary>
        public bool DailyWeeklyAutoCollapseEnabled { get => _dailyWeeklyAutoCollapseEnabled; set { _dailyWeeklyAutoCollapseEnabled = value; OnPropertyChanged(); } }

        /// <summary>자동 접기 전 표시 유지 시간(초).</summary>
        public int DailyWeeklyAutoCollapseSeconds { get => _dailyWeeklyAutoCollapseSeconds; set { _dailyWeeklyAutoCollapseSeconds = value; OnPropertyChanged(); } }

        /// <summary>잠금 해제 모드에서 채팅창끼리 가장자리에 자석처럼 붙는 스냅 기능.</summary>
        public bool WindowSnapEnabled { get => _windowSnapEnabled; set { _windowSnapEnabled = value; OnPropertyChanged(); } }

        /// <summary>메뉴 바 고정: true면 자동 접힘 없이 메뉴가 상시 표시된다 (아이콘 클릭으로 전환).</summary>
        public bool MenuWindowPinned { get => _menuWindowPinned; set { _menuWindowPinned = value; OnPropertyChanged(); } }

        /// <summary>메뉴 바 방향: false=세로형(기본), true=가로형.</summary>
        public bool MenuWindowHorizontal { get => _menuWindowHorizontal; set { _menuWindowHorizontal = value; OnPropertyChanged(); } }

        public double? MenuWindowLeft { get; set; } = 0.0;
        [JsonPropertyOrder(57)]
        public double? MenuWindowTop { get; set; } = 0.0;
        [JsonPropertyOrder(58)]
        public double? ItemDropWindowLeft
        {
            get => _itemDropWindowLeft;
            set
            {
                if (_itemDropWindowLeft == value) return;
                _itemDropWindowLeft = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(59)]
        public double? ItemDropWindowTop
        {
            get => _itemDropWindowTop;
            set
            {
                if (_itemDropWindowTop == value) return;
                _itemDropWindowTop = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(60)]
        public double? BuffTrackerWindowLeft
        {
            get => _buffTrackerWindowLeft;
            set
            {
                if (_buffTrackerWindowLeft == value) return;
                _buffTrackerWindowLeft = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(61)]
        public double? BuffTrackerWindowTop
        {
            get => _buffTrackerWindowTop;
            set
            {
                if (_buffTrackerWindowTop == value) return;
                _buffTrackerWindowTop = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(62)]
        public double? ItemCalendarWindowLeft
        {
            get => _itemCalendarWindowLeft;
            set
            {
                if (_itemCalendarWindowLeft == value) return;
                _itemCalendarWindowLeft = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(63)]
        public double? ItemCalendarWindowTop
        {
            get => _itemCalendarWindowTop;
            set
            {
                if (_itemCalendarWindowTop == value) return;
                _itemCalendarWindowTop = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(64)]
        public double? AbandonRoadSummaryWindowLeft
        {
            get => _AbandonRoadSummaryWindowLeft;
            set
            {
                if (_AbandonRoadSummaryWindowLeft == value) return;
                _AbandonRoadSummaryWindowLeft = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(65)]
        public double? AbandonRoadSummaryWindowTop
        {
            get => _AbandonRoadSummaryWindowTop;
            set
            {
                if (_AbandonRoadSummaryWindowTop == value) return;
                _AbandonRoadSummaryWindowTop = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(66)]
        public double? RecaptureSupplyWindowLeft
        {
            get => _recaptureSupplyWindowLeft;
            set
            {
                if (_recaptureSupplyWindowLeft == value) return;
                _recaptureSupplyWindowLeft = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(67)]
        public double? RecaptureSupplyWindowTop
        {
            get => _recaptureSupplyWindowTop;
            set
            {
                if (_recaptureSupplyWindowTop == value) return;
                _recaptureSupplyWindowTop = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(68)]
        public double? ExperienceLimitAlertWindowLeft
        {
            get => _experienceLimitAlertWindowLeft;
            set
            {
                if (_experienceLimitAlertWindowLeft == value) return;
                _experienceLimitAlertWindowLeft = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(69)]
        public double? ExperienceLimitAlertWindowTop
        {
            get => _experienceLimitAlertWindowTop;
            set
            {
                if (_experienceLimitAlertWindowTop == value) return;
                _experienceLimitAlertWindowTop = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(70)]
        public double? DungeonCountDisplayWindowLeft
        {
            get => _dungeonCountDisplayWindowLeft;
            set
            {
                if (_dungeonCountDisplayWindowLeft == value) return;
                _dungeonCountDisplayWindowLeft = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(71)]
        public double? DungeonCountDisplayWindowTop
        {
            get => _dungeonCountDisplayWindowTop;
            set
            {
                if (_dungeonCountDisplayWindowTop == value) return;
                _dungeonCountDisplayWindowTop = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(73)]
        public double? ShoutToastWindowLeft
        {
            get => _shoutToastWindowLeft;
            set
            {
                if (_shoutToastWindowLeft == value) return;
                _shoutToastWindowLeft = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(74)]
        public double? ShoutToastWindowTop
        {
            get => _shoutToastWindowTop;
            set
            {
                if (_shoutToastWindowTop == value) return;
                _shoutToastWindowTop = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(411)]
        public double DailyWeeklyContentFontSize
        {
            get => _dailyWeeklyContentFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(28.0, value));
                if (Math.Abs(_dailyWeeklyContentFontSize - clamped) < 0.0001) return;
                _dailyWeeklyContentFontSize = clamped;
                OnPropertyChanged();
            }
        }

        private bool _itemCalendarUseIcons; // 기본 텍스트 모드

        /// <summary>달력 날짜 칸 아이템을 그림(true)/텍스트(false)로 표시.</summary>
        [JsonPropertyOrder(413)]
        public bool ItemCalendarUseIcons
        {
            get => _itemCalendarUseIcons;
            set
            {
                if (_itemCalendarUseIcons == value) return;
                _itemCalendarUseIcons = value;
                OnPropertyChanged();
            }
        }

        private double _shoutReplayFontSize = 14.0;

        /// <summary>외치기 로그 창 본문 폰트 크기.</summary>
        [JsonPropertyOrder(418)]
        public double ShoutReplayFontSize
        {
            get => _shoutReplayFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(28.0, value));
                if (Math.Abs(_shoutReplayFontSize - clamped) < 0.0001) return;
                _shoutReplayFontSize = clamped;
                OnPropertyChanged();
            }
        }

        private double _dungeonCountDisplayFontSize = 18.0;

        /// <summary>던전 카운트 알림창 본문 폰트 크기.</summary>
        [JsonPropertyOrder(414)]
        public double DungeonCountDisplayFontSize
        {
            get => _dungeonCountDisplayFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(40.0, value));
                if (Math.Abs(_dungeonCountDisplayFontSize - clamped) < 0.0001) return;
                _dungeonCountDisplayFontSize = clamped;
                OnPropertyChanged();
            }
        }

        private double _messengerEtaFontSize = 18.0;

        /// <summary>1:1 채팅 에타레벨 확인 창 본문 폰트 크기.</summary>
        [JsonPropertyOrder(417)]
        public double MessengerEtaFontSize
        {
            get => _messengerEtaFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(40.0, value));
                if (Math.Abs(_messengerEtaFontSize - clamped) < 0.0001) return;
                _messengerEtaFontSize = clamped;
                OnPropertyChanged();
            }
        }

        private double _experienceAlertFontSize = 18.0;

        /// <summary>경험치 누적 알림창 본문 폰트 크기.</summary>
        [JsonPropertyOrder(415)]
        public double ExperienceAlertFontSize
        {
            get => _experienceAlertFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(40.0, value));
                if (Math.Abs(_experienceAlertFontSize - clamped) < 0.0001) return;
                _experienceAlertFontSize = clamped;
                OnPropertyChanged();
            }
        }

        private double _itemDropToastFontSize = 18.0;

        /// <summary>아이템 드롭 알림 토스트 폰트 크기.</summary>
        [JsonPropertyOrder(416)]
        public double ItemDropToastFontSize
        {
            get => _itemDropToastFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(40.0, value));
                if (Math.Abs(_itemDropToastFontSize - clamped) < 0.0001) return;
                _itemDropToastFontSize = clamped;
                OnPropertyChanged();
            }
        }

        private double _itemCalendarFontSize = 11.0;

        /// <summary>달력(아이템 획득 내역) 본문 기준 폰트 크기. 헤더의 슬라이더로 조절.</summary>
        [JsonPropertyOrder(412)]
        public double ItemCalendarFontSize
        {
            get => _itemCalendarFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(28.0, value));
                if (Math.Abs(_itemCalendarFontSize - clamped) < 0.0001) return;
                _itemCalendarFontSize = clamped;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(741)]
        public double? MessengerToastWindowLeft
        {
            get => _messengerToastWindowLeft;
            set
            {
                if (_messengerToastWindowLeft == value) return;
                _messengerToastWindowLeft = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(742)]
        public double? MessengerToastWindowTop
        {
            get => _messengerToastWindowTop;
            set
            {
                if (_messengerToastWindowTop == value) return;
                _messengerToastWindowTop = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(75)]
        public double? ChatCloneWindow1Left
        {
            get => _chatCloneWindow1Left;
            set
            {
                if (_chatCloneWindow1Left == value) return;
                _chatCloneWindow1Left = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(76)]
        public double? ChatCloneWindow1Top
        {
            get => _chatCloneWindow1Top;
            set
            {
                if (_chatCloneWindow1Top == value) return;
                _chatCloneWindow1Top = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(77)]
        public double? ChatCloneWindow2Left
        {
            get => _chatCloneWindow2Left;
            set
            {
                if (_chatCloneWindow2Left == value) return;
                _chatCloneWindow2Left = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(78)]
        public double? ChatCloneWindow2Top
        {
            get => _chatCloneWindow2Top;
            set
            {
                if (_chatCloneWindow2Top == value) return;
                _chatCloneWindow2Top = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(99)]
        public double? ChatCloneWindow1Width
        {
            get => _chatCloneWindow1Width;
            set
            {
                if (_chatCloneWindow1Width == value) return;
                _chatCloneWindow1Width = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(100)]
        public double? ChatCloneWindow1Height
        {
            get => _chatCloneWindow1Height;
            set
            {
                if (_chatCloneWindow1Height == value) return;
                _chatCloneWindow1Height = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(101)]
        public double? ChatCloneWindow2Width
        {
            get => _chatCloneWindow2Width;
            set
            {
                if (_chatCloneWindow2Width == value) return;
                _chatCloneWindow2Width = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(102)]
        public double? ChatCloneWindow2Height
        {
            get => _chatCloneWindow2Height;
            set
            {
                if (_chatCloneWindow2Height == value) return;
                _chatCloneWindow2Height = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(85)]
        public double? ShoutReplayWindowLeft
        {
            get => _shoutReplayWindowLeft;
            set
            {
                if (_shoutReplayWindowLeft == value) return;
                _shoutReplayWindowLeft = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(86)]
        public double? ShoutReplayWindowTop
        {
            get => _shoutReplayWindowTop;
            set
            {
                if (_shoutReplayWindowTop == value) return;
                _shoutReplayWindowTop = value;
                OnPropertyChanged();
            }
        }

        private double? _shoutReplayWindowWidth;
        private double? _shoutReplayWindowHeight;

        [JsonPropertyOrder(88)]
        public double? ShoutReplayWindowWidth
        {
            get => _shoutReplayWindowWidth;
            set
            {
                if (_shoutReplayWindowWidth == value) return;
                _shoutReplayWindowWidth = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(89)]
        public double? ShoutReplayWindowHeight
        {
            get => _shoutReplayWindowHeight;
            set
            {
                if (_shoutReplayWindowHeight == value) return;
                _shoutReplayWindowHeight = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(87)]
        public double? MemoOverlayWindowLeft
        {
            get => _memoOverlayWindowLeft;
            set
            {
                if (_memoOverlayWindowLeft == value) return;
                _memoOverlayWindowLeft = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(88)]
        public double? MemoOverlayWindowTop
        {
            get => _memoOverlayWindowTop;
            set
            {
                if (_memoOverlayWindowTop == value) return;
                _memoOverlayWindowTop = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(89)]
        public string MemoOverlayText
        {
            get => _memoOverlayText;
            set
            {
                value ??= string.Empty;
                if (_memoOverlayText == value) return;
                _memoOverlayText = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(90)]
        public bool MemoOverlayTextOnlyMode
        {
            get => _memoOverlayTextOnlyMode;
            set
            {
                if (_memoOverlayTextOnlyMode == value) return;
                _memoOverlayTextOnlyMode = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(91)]
        public double MemoOverlayFontSize
        {
            get => _memoOverlayFontSize;
            set
            {
                if (Math.Abs(_memoOverlayFontSize - value) < 0.0001) return;
                _memoOverlayFontSize = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(92)]
        public bool MemoOverlayBold
        {
            get => _memoOverlayBold;
            set
            {
                if (_memoOverlayBold == value) return;
                _memoOverlayBold = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(93)]
        public bool MemoOverlayItalic
        {
            get => _memoOverlayItalic;
            set
            {
                if (_memoOverlayItalic == value) return;
                _memoOverlayItalic = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(94)]
        public string MemoOverlayColorKey
        {
            get => _memoOverlayColorKey;
            set
            {
                value ??= "White";
                if (_memoOverlayColorKey == value) return;
                _memoOverlayColorKey = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(79)]
        public bool ChatCloneWindow1FollowMainFont
        {
            get => _chatCloneWindow1FollowMainFont;
            set
            {
                if (_chatCloneWindow1FollowMainFont == value) return;
                _chatCloneWindow1FollowMainFont = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(80)]
        public string ChatCloneWindow1FontFamily
        {
            get => _chatCloneWindow1FontFamily;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "사용자 설정" : value;
                if (_chatCloneWindow1FontFamily == normalized) return;
                _chatCloneWindow1FontFamily = normalized;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(81)]
        public double? ChatCloneWindow1FontSize
        {
            get => _chatCloneWindow1FontSize;
            set
            {
                if (_chatCloneWindow1FontSize == value) return;
                _chatCloneWindow1FontSize = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(95)]
        public string ChatCloneWindow1TabTag
        {
            get => _chatCloneWindow1TabTag;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "General" : value;
                if (_chatCloneWindow1TabTag == normalized) return;
                _chatCloneWindow1TabTag = normalized;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(82)]
        public bool ChatCloneWindow2FollowMainFont
        {
            get => _chatCloneWindow2FollowMainFont;
            set
            {
                if (_chatCloneWindow2FollowMainFont == value) return;
                _chatCloneWindow2FollowMainFont = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(83)]
        public string ChatCloneWindow2FontFamily
        {
            get => _chatCloneWindow2FontFamily;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "사용자 설정" : value;
                if (_chatCloneWindow2FontFamily == normalized) return;
                _chatCloneWindow2FontFamily = normalized;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(84)]
        public double? ChatCloneWindow2FontSize
        {
            get => _chatCloneWindow2FontSize;
            set
            {
                if (_chatCloneWindow2FontSize == value) return;
                _chatCloneWindow2FontSize = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(96)]
        public string ChatCloneWindow2TabTag
        {
            get => _chatCloneWindow2TabTag;
            set
            {
                string normalized = string.IsNullOrWhiteSpace(value) ? "General" : value;
                if (_chatCloneWindow2TabTag == normalized) return;
                _chatCloneWindow2TabTag = normalized;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(97)]
        public bool ChatCloneWindow1IsOpen
        {
            get => _chatCloneWindow1IsOpen;
            set
            {
                if (_chatCloneWindow1IsOpen == value) return;
                _chatCloneWindow1IsOpen = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(98)]
        public bool ChatCloneWindow2IsOpen
        {
            get => _chatCloneWindow2IsOpen;
            set
            {
                if (_chatCloneWindow2IsOpen == value) return;
                _chatCloneWindow2IsOpen = value;
                OnPropertyChanged();
            }
        }

        private Dictionary<string, double> _windowOpacityPercents = new();

        /// <summary>잠금 해제 인스펙터에서 지정한 창별 투명도(10~100%). 키 = 창 타입명(서브 채팅창은 슬롯 포함).</summary>
        [JsonPropertyOrder(99)]
        public Dictionary<string, double> WindowOpacityPercents
        {
            get => _windowOpacityPercents;
            set
            {
                _windowOpacityPercents = value ?? new Dictionary<string, double>();
                OnPropertyChanged();
            }
        }

        public void SetBuffTrackerWindowPosition(double? left, double? top, bool notify)
        {
            _buffTrackerWindowLeft = left;
            _buffTrackerWindowTop = top;

            if (notify)
                OnPropertyChanged(nameof(BuffTrackerWindowLeft));

            if (notify)
                OnPropertyChanged(nameof(BuffTrackerWindowTop));
        }

        [JsonPropertyOrder(901)]
        public WindowPositionPreset Preset1
        {
            get => _preset1;
            set { _preset1 = value; OnPropertyChanged(); }
        }

        [JsonPropertyOrder(902)]
        public WindowPositionPreset Preset2
        {
            get => _preset2;
            set { _preset2 = value; OnPropertyChanged(); }
        }

        [JsonPropertyOrder(903)]
        public WindowPositionPreset Preset3
        {
            get => _preset3;
            set { _preset3 = value; OnPropertyChanged(); }
        }

        [JsonPropertyOrder(904)]
        public bool InitialSetupWizardCompleted
        {
            get => _initialSetupWizardCompleted;
            set
            {
                if (_initialSetupWizardCompleted == value) return;
                _initialSetupWizardCompleted = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(905)]
        public bool StartupLogReadCanceled
        {
            get => _startupLogReadCanceled;
            set
            {
                if (_startupLogReadCanceled == value) return;
                _startupLogReadCanceled = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(906)]
        public bool StartupTodayOnlyBootstrapCompleted
        {
            get => _startupTodayOnlyBootstrapCompleted;
            set
            {
                if (_startupTodayOnlyBootstrapCompleted == value) return;
                _startupTodayOnlyBootstrapCompleted = value;
                OnPropertyChanged();
            }
        }

        [JsonPropertyOrder(70)]
        public Dictionary<string, DungeonItemConfig> DungeonItemConfigs { get; set; } = CreateDefaultDungeonItemConfigs();

        [JsonPropertyOrder(71)]
        public Dictionary<string, BossAlertConfig> BossAlertConfigs { get; set; } = CreateDefaultBossAlertConfigs();

        [JsonIgnore]
        public double ExpAlarmThresholdMan
        {
            get => _expAlarmThreshold / 10000.0;
            set
            {
                double safeValue = value <= 0 ? 1.0 : value;
                ExpAlarmThreshold = (long)(safeValue * 10000);
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// 현재 창의 위치 표시 (읽기 전용)
        /// </summary>
        [JsonIgnore]
        public string CurrentPositionDisplay { get; set; } = "위치: 기본값";

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




