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
        private string _chatLogFolderPath = @"C:\Nexon\TalesWeaver\ChatLog";
        private string _keywordInput = "";
        private string _fontFamily = "사용자 설정";
        private bool _useAlertColor = true;
        private bool _useAlertSound = true;
        private bool _useMagicCircleAlert = true;
        private bool _showExpTracker = false;
        private bool _enableExperienceLimitAlert = false;
        private bool _showExperienceLimitAlertWindow = false;
        private bool _showDailyWeeklyContentOverlay = false;
        private bool _showEtosDirectionAlert = true;
        private bool _enableAbaddonRoadCountAlert = false;
        private bool _enableCravingPleasureCountAlert = false;
        private bool _showDungeonCountDisplayWindow = false;
        private bool _showItemDropAlert = true;
        private bool _showEtosHelperWindow = false;
        private bool _showItemDropHelperWindow = false;
        private bool _enableBuffTrackerAlert = false;
        private bool _showBuffTrackerWindow = false;
        private double _itemDropAlertVolume = 0.1;
        private double _highlightAlertVolume = 1.0;
        private double _magicCircleAlertVolume = 1.0;
        private double _expBuffAlertVolume = 1.0;
        private double _bossAlertVolume = 1.0;
        private bool _alwaysVisible = false;
        private bool _enableDebugLogging = false;
        private bool _enablePerformanceDiagnostics = false;
        private string _exitHotKey = "";
        private string _toggleOverlayHotKey = "";
        private string _toggleAddonHotKey = "";
        private string _toggleAlwaysVisibleHotKey = "";
        private string _toggleDailyWeeklyContentHotKey = "";
        private string _toggleEtaRankingHotKey = "";
        private string _toggleCoefficientHotKey = "";
        private string _toggleEquipmentDbHotKey = "";
        private string _toggleEncryptHotKey = "";
        private string _toggleSettingsHotKey = "";
        private double? _dailyWeeklyContentOverlayLeft = 0.0;
        private double? _dailyWeeklyContentOverlayTop = 0.0;
        private double? _subAddonWindowLeft = 0.0;
        private double? _subAddonWindowTop = 0.0;
        private double? _itemDropWindowLeft = 0.0;
        private double? _itemDropWindowTop = 0.0;
        private double? _buffTrackerWindowLeft = 0.0;
        private double? _buffTrackerWindowTop = 0.0;
        private double? _experienceLimitAlertWindowLeft = null;
        private double? _experienceLimitAlertWindowTop = null;
        private double? _dungeonCountDisplayWindowLeft = null;
        private double? _dungeonCountDisplayWindowTop = null;

        private double _fontSize = 17.0;
        private double _lineMargin = 0.0;
        private double _lineMarginLeft = 0.0;

        private long _expAlarmThreshold = 10000;
        private int _abaddonRoadCountAlertDurationSeconds = 30;
        private int _lastSelectedPresetNumber = 1;

        private WindowPositionPreset _preset1 = new("프리셋 1 (X: 0, Y: 0)", 110, 840, 0, 0);
        private WindowPositionPreset _preset2 = new("프리셋 2", 0, 0);
        private WindowPositionPreset _preset3 = new("프리셋 3", 0, 0);
        #endregion

        #region Properties

        [JsonPropertyOrder(1)]
        public bool ShowNormal { get; set; } = false;
        [JsonPropertyOrder(2)]
        public bool ShowShout { get; set; } = true;
        [JsonPropertyOrder(3)]
        public bool ShowTeam { get; set; } = true;
        [JsonPropertyOrder(4)]
        public bool ShowWhisper { get; set; } = true;
        [JsonPropertyOrder(5)]
        public bool ShowSystem { get; set; } = false;
        [JsonPropertyOrder(6)]
        public bool ShowClub { get; set; } = true;
        [JsonPropertyOrder(7)]
        public bool UseKeywordAlert { get; set; } = true;
        [JsonPropertyOrder(8)]
        public bool UseAlertColor { get => _useAlertColor; set { _useAlertColor = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(9)]
        public bool UseAlertSound { get => _useAlertSound; set { _useAlertSound = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(10)]
        public bool UseMagicCircleAlert { get => _useMagicCircleAlert; set { _useMagicCircleAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(11)]
        public bool ShowEtosDirectionAlert { get => _showEtosDirectionAlert; set { _showEtosDirectionAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(12)]
        public bool EnableAbaddonRoadCountAlert { get => _enableAbaddonRoadCountAlert; set { _enableAbaddonRoadCountAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(13)]
        public bool EnableCravingPleasureCountAlert { get => _enableCravingPleasureCountAlert; set { _enableCravingPleasureCountAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(14)]
        public bool ShowDungeonCountDisplayWindow { get => _showDungeonCountDisplayWindow; set { _showDungeonCountDisplayWindow = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(12)]
        public bool ShowItemDropAlert { get => _showItemDropAlert; set { _showItemDropAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(13)]
        public bool ShowEtosHelperWindow { get => _showEtosHelperWindow; set { _showEtosHelperWindow = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(14)]
        public bool ShowItemDropHelperWindow { get => _showItemDropHelperWindow; set { _showItemDropHelperWindow = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(15)]
        public bool EnableBuffTrackerAlert { get => _enableBuffTrackerAlert; set { _enableBuffTrackerAlert = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(16)]
        public bool ShowBuffTrackerWindow { get => _showBuffTrackerWindow; set { _showBuffTrackerWindow = value; OnPropertyChanged(); } }
        [JsonPropertyOrder(17)]
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
        [JsonPropertyOrder(16)]
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
        [JsonPropertyOrder(17)]
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
        [JsonPropertyOrder(18)]
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
        [JsonPropertyOrder(19)]
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
        [JsonPropertyOrder(20)]
        [JsonIgnore]
        public double ItemDropAlertVolumePercent
        {
            get => Math.Round(_itemDropAlertVolume * 100.0, 0);
            set => ItemDropAlertVolume = Math.Max(1.0, Math.Min(10.0, value)) / 100.0;
        }
        [JsonPropertyOrder(21)]
        [JsonIgnore]
        public double HighlightAlertVolumePercent
        {
            get => Math.Round(_highlightAlertVolume * 100.0, 0);
            set => HighlightAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonPropertyOrder(22)]
        [JsonIgnore]
        public double MagicCircleAlertVolumePercent
        {
            get => Math.Round(_magicCircleAlertVolume * 100.0, 0);
            set => MagicCircleAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonPropertyOrder(23)]
        [JsonIgnore]
        public double ExpBuffAlertVolumePercent
        {
            get => Math.Round(_expBuffAlertVolume * 100.0, 0);
            set => ExpBuffAlertVolume = Math.Max(0.0, Math.Min(100.0, value)) / 100.0;
        }
        [JsonPropertyOrder(24)]
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
        [JsonPropertyOrder(29)]
        public bool EnablePerformanceDiagnostics { get => _enablePerformanceDiagnostics; set { _enablePerformanceDiagnostics = value; OnPropertyChanged(); } }

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
        public string FontFamily { get => _fontFamily; set { _fontFamily = value; OnPropertyChanged(); } }
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
        [JsonPropertyOrder(30)]
        public string ToggleAddonHotKey
        {
            get => _toggleAddonHotKey;
            set
            {
                if (_toggleAddonHotKey == value) return;
                _toggleAddonHotKey = value;
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
        [JsonPropertyOrder(33)]
        public string ToggleEtaRankingHotKey
        {
            get => _toggleEtaRankingHotKey;
            set
            {
                if (_toggleEtaRankingHotKey == value) return;
                _toggleEtaRankingHotKey = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(34)]
        public string ToggleCoefficientHotKey
        {
            get => _toggleCoefficientHotKey;
            set
            {
                if (_toggleCoefficientHotKey == value) return;
                _toggleCoefficientHotKey = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(35)]
        public string ToggleEquipmentDbHotKey
        {
            get => _toggleEquipmentDbHotKey;
            set
            {
                if (_toggleEquipmentDbHotKey == value) return;
                _toggleEquipmentDbHotKey = value;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(36)]
        public string ToggleEncryptHotKey
        {
            get => _toggleEncryptHotKey;
            set
            {
                if (_toggleEncryptHotKey == value) return;
                _toggleEncryptHotKey = value;
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
        public int AbaddonRoadCountAlertDurationSeconds
        {
            get => _abaddonRoadCountAlertDurationSeconds;
            set
            {
                int clamped = Math.Max(1, Math.Min(300, value));
                if (_abaddonRoadCountAlertDurationSeconds == clamped) return;
                _abaddonRoadCountAlertDurationSeconds = clamped;
                OnPropertyChanged();
            }
        }
        [JsonPropertyOrder(48)]
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

        [JsonPropertyOrder(63)]
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

        [JsonPropertyOrder(64)]
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

        [JsonPropertyOrder(65)]
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

        public void SetBuffTrackerWindowPosition(double? left, double? top, bool notify)
        {
            _buffTrackerWindowLeft = left;
            _buffTrackerWindowTop = top;

            if (notify)
                OnPropertyChanged(nameof(BuffTrackerWindowLeft));

            if (notify)
                OnPropertyChanged(nameof(BuffTrackerWindowTop));
        }

        [JsonPropertyOrder(62)]
        public WindowPositionPreset Preset1
        {
            get => _preset1;
            set { _preset1 = value; OnPropertyChanged(); }
        }

        [JsonPropertyOrder(63)]
        public WindowPositionPreset Preset2
        {
            get => _preset2;
            set { _preset2 = value; OnPropertyChanged(); }
        }

        [JsonPropertyOrder(64)]
        public WindowPositionPreset Preset3
        {
            get => _preset3;
            set { _preset3 = value; OnPropertyChanged(); }
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
