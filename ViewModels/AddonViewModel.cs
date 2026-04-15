using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.ViewModels
{
    /// <summary>
    /// 추가 기능 설정을 관리하는 ViewModel입니다.
    /// </summary>
    public class AddonViewModel : ViewModelBase
    {
        private readonly ChatSettings _settings;
        private readonly BossAlarmCardViewModelProvider _bossAlarmCardProvider;

        private bool _useAlertColor;
        private bool _useAlertSound;
        private bool _useMagicCircleAlert;
        private string _keywordInput;
        private bool _showExpTracker;
        private bool _isExpAlarmEnabled;
        private long _expAlarmThresholdMan;
        private bool _showDailyWeeklyContentOverlay;
        private bool _showEtosDirectionAlert;
        private bool _showEtosHelperWindow;
        private bool _showItemDropAlert;
        private bool _showItemDropHelperWindow;
        private bool _enableBuffTrackerAlert;
        private bool _showBuffTrackerWindow;
        private double _itemDropAlertVolumePercent;
        private double _highlightAlertVolumePercent;
        private double _magicCircleAlertVolumePercent;
        private double _expBuffAlertVolumePercent;
        private double _bossAlertVolumePercent;

        public ObservableCollection<BossAlarmCardViewModel> BossAlarmCards { get; } = new();

        public bool UseAlertColor
        {
            get => _useAlertColor;
            set => SetSetting(ref _useAlertColor, value, (settings, newValue) => settings.UseAlertColor = newValue);
        }

        public bool UseAlertSound
        {
            get => _useAlertSound;
            set => SetSetting(ref _useAlertSound, value, (settings, newValue) => settings.UseAlertSound = newValue);
        }

        public bool UseMagicCircleAlert
        {
            get => _useMagicCircleAlert;
            set => SetSetting(ref _useMagicCircleAlert, value, (settings, newValue) => settings.UseMagicCircleAlert = newValue);
        }

        public string KeywordInput
        {
            get => _keywordInput;
            set => SetSetting(ref _keywordInput, value ?? string.Empty, (settings, newValue) => settings.KeywordInput = newValue);
        }

        public bool ShowExpTracker
        {
            get => _showExpTracker;
            set => SetSetting(ref _showExpTracker, value, (settings, newValue) => settings.ShowExpTracker = newValue);
        }

        public bool IsExpAlarmEnabled
        {
            get => _isExpAlarmEnabled;
            set => SetSetting(ref _isExpAlarmEnabled, value, (settings, newValue) => settings.IsExpAlarmEnabled = newValue);
        }

        public int ExpAlarmThresholdMan
        {
            get => (int)(_expAlarmThresholdMan / 10000);
            set
            {
                long newThreshold = value * 10000L;
                SetSetting(ref _expAlarmThresholdMan, newThreshold, (settings, threshold) => settings.ExpAlarmThreshold = threshold);
            }
        }

        public bool ShowDailyWeeklyContentOverlay
        {
            get => _showDailyWeeklyContentOverlay;
            set => SetSetting(ref _showDailyWeeklyContentOverlay, value, (settings, newValue) => settings.ShowDailyWeeklyContentOverlay = newValue);
        }

        public bool ShowEtosDirectionAlert
        {
            get => _showEtosDirectionAlert;
            set => SetSetting(ref _showEtosDirectionAlert, value, (settings, newValue) => settings.ShowEtosDirectionAlert = newValue);
        }

        public bool ShowEtosHelperWindow
        {
            get => _showEtosHelperWindow;
            set => SetSetting(ref _showEtosHelperWindow, value, (settings, newValue) => settings.ShowEtosHelperWindow = newValue);
        }

        public bool ShowItemDropAlert
        {
            get => _showItemDropAlert;
            set => SetSetting(ref _showItemDropAlert, value, (settings, newValue) => settings.ShowItemDropAlert = newValue);
        }

        public bool ShowItemDropHelperWindow
        {
            get => _showItemDropHelperWindow;
            set => SetSetting(ref _showItemDropHelperWindow, value, (settings, newValue) => settings.ShowItemDropHelperWindow = newValue);
        }

        public bool EnableBuffTrackerAlert
        {
            get => _enableBuffTrackerAlert;
            set => SetSetting(ref _enableBuffTrackerAlert, value, (settings, newValue) => settings.EnableBuffTrackerAlert = newValue);
        }

        public bool ShowBuffTrackerWindow
        {
            get => _showBuffTrackerWindow;
            set => SetSetting(ref _showBuffTrackerWindow, value, (settings, newValue) => settings.ShowBuffTrackerWindow = newValue);
        }

        public double ItemDropAlertVolumePercent
        {
            get => _itemDropAlertVolumePercent;
            set => SetSetting(ref _itemDropAlertVolumePercent, value, (settings, newValue) => settings.ItemDropAlertVolumePercent = newValue);
        }

        public double HighlightAlertVolumePercent
        {
            get => _highlightAlertVolumePercent;
            set => SetSetting(ref _highlightAlertVolumePercent, value, (settings, newValue) => settings.HighlightAlertVolumePercent = newValue);
        }

        public double MagicCircleAlertVolumePercent
        {
            get => _magicCircleAlertVolumePercent;
            set => SetSetting(ref _magicCircleAlertVolumePercent, value, (settings, newValue) => settings.MagicCircleAlertVolumePercent = newValue);
        }

        public double ExpBuffAlertVolumePercent
        {
            get => _expBuffAlertVolumePercent;
            set => SetSetting(ref _expBuffAlertVolumePercent, value, (settings, newValue) => settings.ExpBuffAlertVolumePercent = newValue);
        }

        public double BossAlertVolumePercent
        {
            get => _bossAlertVolumePercent;
            set => SetSetting(ref _bossAlertVolumePercent, value, (settings, newValue) => settings.BossAlertVolumePercent = newValue);
        }

        public AddonViewModel(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _bossAlarmCardProvider = new BossAlarmCardViewModelProvider(_settings);

            _useAlertColor = _settings.UseAlertColor;
            _useAlertSound = _settings.UseAlertSound;
            _useMagicCircleAlert = _settings.UseMagicCircleAlert;
            _keywordInput = _settings.KeywordInput;
            _showExpTracker = _settings.ShowExpTracker;
            _isExpAlarmEnabled = _settings.IsExpAlarmEnabled;
            _expAlarmThresholdMan = _settings.ExpAlarmThreshold;
            _showDailyWeeklyContentOverlay = _settings.ShowDailyWeeklyContentOverlay;
            _showEtosDirectionAlert = _settings.ShowEtosDirectionAlert;
            _showEtosHelperWindow = _settings.ShowEtosHelperWindow;
            _showItemDropAlert = _settings.ShowItemDropAlert;
            _showItemDropHelperWindow = _settings.ShowItemDropHelperWindow;
            _enableBuffTrackerAlert = _settings.EnableBuffTrackerAlert;
            _showBuffTrackerWindow = _settings.ShowBuffTrackerWindow;
            _itemDropAlertVolumePercent = _settings.ItemDropAlertVolumePercent;
            _highlightAlertVolumePercent = _settings.HighlightAlertVolumePercent;
            _magicCircleAlertVolumePercent = _settings.MagicCircleAlertVolumePercent;
            _expBuffAlertVolumePercent = _settings.ExpBuffAlertVolumePercent;
            _bossAlertVolumePercent = _settings.BossAlertVolumePercent;

            ReplaceBossAlarmCards(_bossAlarmCardProvider.CreateCards());
            _ = InitializeBossAlarmCardsAsync();
        }

        private void SaveSettings()
        {
            ConfigService.SaveDeferred(_settings);
        }

        private bool SetSetting<T>(ref T field, T value, Action<ChatSettings, T> apply, [CallerMemberName] string? propertyName = null)
        {
            if (!SetProperty(ref field, value, propertyName))
            {
                return false;
            }

            apply(_settings, value);
            SaveSettings();
            return true;
        }

        private void ReplaceBossAlarmCards(IEnumerable<BossAlarmCardViewModel> cards)
        {
            BossAlarmCards.Clear();
            foreach (var card in cards)
            {
                BossAlarmCards.Add(card);
            }
        }

        private async Task InitializeBossAlarmCardsAsync()
        {
            var cards = await _bossAlarmCardProvider.LoadCardsAsync();
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => ReplaceBossAlarmCards(cards)));
        }

        public async Task RefreshBossAlarmCardsAsync(bool forceRefresh = false)
        {
            var cards = await _bossAlarmCardProvider.LoadCardsAsync(forceRefresh);
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => ReplaceBossAlarmCards(cards)));
        }
    }
}
