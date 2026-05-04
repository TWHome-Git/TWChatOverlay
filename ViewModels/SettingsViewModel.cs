using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.Views;
using WinColor = System.Drawing.Color;
using WinForms = System.Windows.Forms;

namespace TWChatOverlay.ViewModels
{
    /// <summary>
    /// 설정 관리 ViewModel
    /// </summary>
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ChatSettings _settings;
        private readonly Action<string>? _onColorsUpdated;
        private readonly Action? _onExit;
        private readonly Action? _onSettingsReset;
        private readonly Action? _onHotKeysChanged;
        private int _selectedPresetNumber = 1;

        public ICommand ColorPickCommand { get; }
        public ICommand InitSettingsCommand { get; }
        public ICommand ExitAppCommand { get; }
        public ICommand SaveOrLoadPresetCommand { get; }
        public ICommand ApplyHotkeysCommand { get; }
        public ICommand CancelHotkeysCommand { get; }
        public ICommand ResetHotkeysToDefaultCommand { get; }

        #region Properties

        public ObservableCollection<string> AvailableFonts { get; }

        public bool ShowNormal
        {
            get => _settings.ShowNormal;
            set
            {
                if (_settings.ShowNormal != value)
                {
                    _settings.ShowNormal = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool AlwaysVisible
        {
            get => _settings.AlwaysVisible;
            set
            {
                if (_settings.AlwaysVisible != value)
                {
                    _settings.AlwaysVisible = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool EnableDebugLogging
        {
            get => _settings.EnableDebugLogging;
            set
            {
                if (_settings.EnableDebugLogging != value)
                {
                    _settings.EnableDebugLogging = value;
                    AppLogger.IsEnabled = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool ShowTeam
        {
            get => _settings.ShowTeam;
            set
            {
                if (_settings.ShowTeam != value)
                {
                    _settings.ShowTeam = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool ShowClub
        {
            get => _settings.ShowClub;
            set
            {
                if (_settings.ShowClub != value)
                {
                    _settings.ShowClub = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool ShowClubBoss
        {
            get => _settings.ShowClubBoss;
            set
            {
                if (_settings.ShowClubBoss != value)
                {
                    _settings.ShowClubBoss = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool ShowShout
        {
            get => _settings.ShowShout;
            set
            {
                if (_settings.ShowShout != value)
                {
                    _settings.ShowShout = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool ShowEtaLevel
        {
            get => _settings.ShowEtaLevel;
            set
            {
                if (_settings.ShowEtaLevel != value)
                {
                    _settings.ShowEtaLevel = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool ShowEtaCharacter
        {
            get => _settings.ShowEtaCharacter;
            set
            {
                if (_settings.ShowEtaCharacter != value)
                {
                    _settings.ShowEtaCharacter = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool ShowShoutToastPopup
        {
            get => _settings.ShowShoutToastPopup;
            set
            {
                if (_settings.ShowShoutToastPopup != value)
                {
                    _settings.ShowShoutToastPopup = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool AutoCopyShoutNickname
        {
            get => _settings.AutoCopyShoutNickname;
            set
            {
                if (_settings.AutoCopyShoutNickname != value)
                {
                    _settings.AutoCopyShoutNickname = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public int ShoutToastDurationSeconds
        {
            get => _settings.ShoutToastDurationSeconds;
            set
            {
                if (_settings.ShoutToastDurationSeconds != value)
                {
                    _settings.ShoutToastDurationSeconds = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public double ShoutToastFontSize
        {
            get => _settings.ShoutToastFontSize;
            set
            {
                if (!_settings.ShoutToastFontSize.Equals(value))
                {
                    _settings.ShoutToastFontSize = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public bool ShowSystem
        {
            get => _settings.ShowSystem;
            set
            {
                if (_settings.ShowSystem != value)
                {
                    _settings.ShowSystem = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public Brush NormalColor
        {
            get => StringToBrush(_settings.NormalColor);
        }

        public Brush TeamColor
        {
            get => StringToBrush(_settings.TeamColor);
        }

        public Brush ClubColor
        {
            get => StringToBrush(_settings.ClubColor);
        }

        public Brush ShoutColor
        {
            get => StringToBrush(_settings.ShoutColor);
        }

        public Brush SystemColor
        {
            get => StringToBrush(_settings.SystemColor);
        }

        public double FontSize
        {
            get => _settings.FontSize;
            set
            {
                if (!_settings.FontSize.Equals(value))
                {
                    _settings.FontSize = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public string FontFamily
        {
            get => _settings.FontFamily;
            set
            {
                if (_settings.FontFamily != value)
                {
                    _settings.FontFamily = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public double LineMargin
        {
            get => _settings.LineMargin;
            set
            {
                if (!_settings.LineMargin.Equals(value))
                {
                    _settings.LineMargin = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public double LineMarginLeft
        {
            get => _settings.LineMarginLeft;
            set
            {
                if (!_settings.LineMarginLeft.Equals(value))
                {
                    _settings.LineMarginLeft = value;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public int SelectedPresetNumber
        {
            get => _selectedPresetNumber;
            set
            {
                if (_selectedPresetNumber != value)
                {
                    _selectedPresetNumber = value;
                    _settings.LastSelectedPresetNumber = value;
                    OnPropertyChanged();
                    ApplySelectedPresetToOffsets();
                    SaveSettings();
                    AppLogger.Info($"Selected preset changed to {_selectedPresetNumber}.");
                }
            }
        }

        public string CurrentPositionDisplay => _settings.CurrentPositionDisplay;

        public string ExitHotKey
        {
            get => _settings.ExitHotKey;
            set
            {
                if (_settings.ExitHotKey == value) return;
                _settings.ExitHotKey = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public string ToggleOverlayHotKey
        {
            get => _settings.ToggleOverlayHotKey;
            set
            {
                if (_settings.ToggleOverlayHotKey == value) return;
                _settings.ToggleOverlayHotKey = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public string ToggleAddonHotKey
        {
            get => _settings.ToggleAddonHotKey;
            set
            {
                if (_settings.ToggleAddonHotKey == value) return;
                _settings.ToggleAddonHotKey = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public string ToggleAlwaysVisibleHotKey
        {
            get => _settings.ToggleAlwaysVisibleHotKey;
            set
            {
                if (_settings.ToggleAlwaysVisibleHotKey == value) return;
                _settings.ToggleAlwaysVisibleHotKey = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public string ToggleDailyWeeklyContentHotKey
        {
            get => _settings.ToggleDailyWeeklyContentHotKey;
            set
            {
                if (_settings.ToggleDailyWeeklyContentHotKey == value) return;
                _settings.ToggleDailyWeeklyContentHotKey = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public string ToggleEtaRankingHotKey
        {
            get => _settings.ToggleEtaRankingHotKey;
            set
            {
                if (_settings.ToggleEtaRankingHotKey == value) return;
                _settings.ToggleEtaRankingHotKey = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public string ToggleCoefficientHotKey
        {
            get => _settings.ToggleCoefficientHotKey;
            set
            {
                if (_settings.ToggleCoefficientHotKey == value) return;
                _settings.ToggleCoefficientHotKey = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public string ToggleEquipmentDbHotKey
        {
            get => _settings.ToggleEquipmentDbHotKey;
            set
            {
                if (_settings.ToggleEquipmentDbHotKey == value) return;
                _settings.ToggleEquipmentDbHotKey = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public string ToggleEncryptHotKey
        {
            get => _settings.ToggleEncryptHotKey;
            set
            {
                if (_settings.ToggleEncryptHotKey == value) return;
                _settings.ToggleEncryptHotKey = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public string ToggleSettingsHotKey
        {
            get => _settings.ToggleSettingsHotKey;
            set
            {
                if (_settings.ToggleSettingsHotKey == value) return;
                _settings.ToggleSettingsHotKey = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public string ChatLogFolderPath
        {
            get => _settings.ChatLogFolderPath;
            set
            {
                if (_settings.ChatLogFolderPath == value) return;
                _settings.ChatLogFolderPath = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        #endregion

        public SettingsViewModel(ChatSettings settings, Action<string>? onColorsUpdated = null,
                                 Action? onExit = null, Action? onSettingsReset = null,
                                 Action? onHotKeysChanged = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _onColorsUpdated = onColorsUpdated;
            _onExit = onExit;
            _onSettingsReset = onSettingsReset;
            _onHotKeysChanged = onHotKeysChanged;

            ColorPickCommand = new RelayCommand<string?>(ExecuteColorPick);
            InitSettingsCommand = new RelayCommand<object?>(_ => ExecuteInitSettings());
            ExitAppCommand = new RelayCommand<object?>(_ => ExecuteExitApp());
            SaveOrLoadPresetCommand = new RelayCommand<string?>(ExecuteSaveOrLoadPreset);
            ApplyHotkeysCommand = new RelayCommand<object?>(_ => ExecuteApplyHotkeys());
            CancelHotkeysCommand = new RelayCommand<object?>(_ => ExecuteCancelHotkeys());
            ResetHotkeysToDefaultCommand = new RelayCommand<object?>(_ => ExecuteResetHotkeysToDefault());

            AvailableFonts = new ObservableCollection<string>(FontService.GetAvailableFonts());
            _selectedPresetNumber = _settings.LastSelectedPresetNumber;
            ApplySelectedPresetToOffsets();
            _settings.PropertyChanged += SettingsOnPropertyChanged;
            AppLogger.Info("SettingsViewModel initialized.");
        }

        private void SettingsOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatSettings.LineMarginLeft))
            {
                OnPropertyChanged(nameof(LineMarginLeft));
                OnPropertyChanged(nameof(ExitHotKey));
                OnPropertyChanged(nameof(ToggleOverlayHotKey));
                OnPropertyChanged(nameof(ToggleAddonHotKey));
                OnPropertyChanged(nameof(ToggleAlwaysVisibleHotKey));
                OnPropertyChanged(nameof(ToggleDailyWeeklyContentHotKey));
            }
            else if (e.PropertyName == nameof(ChatSettings.LineMargin))
            {
                OnPropertyChanged(nameof(LineMargin));
            }
            else if (e.PropertyName == nameof(ChatSettings.CurrentPositionDisplay))
            {
                OnPropertyChanged(nameof(CurrentPositionDisplay));
            }
            else if (e.PropertyName == nameof(ChatSettings.AlwaysVisible))
            {
                OnPropertyChanged(nameof(AlwaysVisible));
            }
        }

        /// <summary>
        /// 색상 선택 명령어
        /// </summary>
        private void ExecuteColorPick(string? colorType)
        {
            if (string.IsNullOrEmpty(colorType)) return;

            var currentBrush = colorType switch
            {
                "Normal" => NormalColor,
                "Team" => TeamColor,
                "Club" => ClubColor,
                "Shout" => ShoutColor,
                "System" => SystemColor,
                _ => null
            };

            using var dialog = new WinForms.ColorDialog();
            if (currentBrush is SolidColorBrush brush)
            {
                var c = brush.Color;
                dialog.Color = WinColor.FromArgb(c.A, c.R, c.G, c.B);
            }

            if (dialog.ShowDialog() == WinForms.DialogResult.OK)
            {
                string hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                _settings.UpdateColor(colorType, hex);
                AppLogger.Info($"Updated color setting '{colorType}' to {hex}.");

                OnPropertyChanged(nameof(NormalColor));
                OnPropertyChanged(nameof(TeamColor));
                OnPropertyChanged(nameof(ClubColor));
                OnPropertyChanged(nameof(ShoutColor));
                OnPropertyChanged(nameof(SystemColor));

                _onColorsUpdated?.Invoke(colorType);
                SaveSettings();
            }
        }

        /// <summary>
        /// 설정 초기화
        /// </summary>
        private void ExecuteInitSettings()
        {
            AppLogger.Warn("Resetting settings to default values.");
            _settings.ResetToDefault();

            OnPropertyChanged(nameof(ShowNormal));
            OnPropertyChanged(nameof(ShowTeam));
            OnPropertyChanged(nameof(ShowClub));
            OnPropertyChanged(nameof(ShowClubBoss));
            OnPropertyChanged(nameof(ShowShout));
            OnPropertyChanged(nameof(ShowSystem));
            OnPropertyChanged(nameof(ShowEtaLevel));
            OnPropertyChanged(nameof(ShowEtaCharacter));
            OnPropertyChanged(nameof(ShowShoutToastPopup));
            OnPropertyChanged(nameof(AutoCopyShoutNickname));
            OnPropertyChanged(nameof(ShoutToastDurationSeconds));
            OnPropertyChanged(nameof(ShoutToastFontSize));
            OnPropertyChanged(nameof(AlwaysVisible));
            OnPropertyChanged(nameof(EnableDebugLogging));
            OnPropertyChanged(nameof(NormalColor));
            OnPropertyChanged(nameof(TeamColor));
            OnPropertyChanged(nameof(ClubColor));
            OnPropertyChanged(nameof(ShoutColor));
            OnPropertyChanged(nameof(SystemColor));
            OnPropertyChanged(nameof(FontSize));
            OnPropertyChanged(nameof(FontFamily));
            OnPropertyChanged(nameof(LineMargin));
            OnPropertyChanged(nameof(LineMarginLeft));
            OnPropertyChanged(nameof(ExitHotKey));
            OnPropertyChanged(nameof(ToggleOverlayHotKey));
            OnPropertyChanged(nameof(ToggleAddonHotKey));
            OnPropertyChanged(nameof(ToggleAlwaysVisibleHotKey));
            OnPropertyChanged(nameof(ToggleDailyWeeklyContentHotKey));
            OnPropertyChanged(nameof(ToggleEtaRankingHotKey));
            OnPropertyChanged(nameof(ToggleCoefficientHotKey));
            OnPropertyChanged(nameof(ToggleEquipmentDbHotKey));
            OnPropertyChanged(nameof(ToggleEncryptHotKey));
            OnPropertyChanged(nameof(ToggleSettingsHotKey));

            SaveSettings();
            _onSettingsReset?.Invoke();
        }

        private void ExecuteApplyHotkeys()
        {
            try
            {
                HotKeySettingsService.NormalizeDuplicates(_settings, OnPropertyChanged);
                // Save immediately
                ConfigService.Save(_settings);
            }
            catch (Exception ex) { AppLogger.Warn("Immediate hotkey settings save failed.", ex); }

            try
            {
                _onHotKeysChanged?.Invoke();
            }
            catch (Exception ex) { AppLogger.Warn("Applying hotkeys from settings view failed.", ex); }

            AppLogger.Info("Hotkey settings applied.");
        }

        private void ExecuteCancelHotkeys()
        {
            try
            {
                var saved = ConfigService.Load();
                if (saved == null) return;

                _settings.ExitHotKey = saved.ExitHotKey;
                _settings.ToggleOverlayHotKey = saved.ToggleOverlayHotKey;
                _settings.ToggleAddonHotKey = saved.ToggleAddonHotKey;
                _settings.ToggleAlwaysVisibleHotKey = saved.ToggleAlwaysVisibleHotKey;
                _settings.ToggleDailyWeeklyContentHotKey = saved.ToggleDailyWeeklyContentHotKey;
                _settings.ToggleEtaRankingHotKey = saved.ToggleEtaRankingHotKey;
                _settings.ToggleCoefficientHotKey = saved.ToggleCoefficientHotKey;
                _settings.ToggleEquipmentDbHotKey = saved.ToggleEquipmentDbHotKey;
                _settings.ToggleEncryptHotKey = saved.ToggleEncryptHotKey;
                _settings.ToggleSettingsHotKey = saved.ToggleSettingsHotKey;

                OnPropertyChanged(nameof(ExitHotKey));
                OnPropertyChanged(nameof(ToggleOverlayHotKey));
                OnPropertyChanged(nameof(ToggleAddonHotKey));
                OnPropertyChanged(nameof(ToggleAlwaysVisibleHotKey));
                OnPropertyChanged(nameof(ToggleDailyWeeklyContentHotKey));
                OnPropertyChanged(nameof(ToggleEtaRankingHotKey));
                OnPropertyChanged(nameof(ToggleCoefficientHotKey));
                OnPropertyChanged(nameof(ToggleEquipmentDbHotKey));
                OnPropertyChanged(nameof(ToggleEncryptHotKey));
                OnPropertyChanged(nameof(ToggleSettingsHotKey));
                AppLogger.Info("Hotkey settings reverted to last saved values.");
            }
            catch (Exception ex) { AppLogger.Warn("Cancelling hotkey edits failed.", ex); }
        }

        private void ExecuteResetHotkeysToDefault()
        {
            HotKeySettingsService.ClearAll(_settings, OnPropertyChanged);

            SaveSettings();
            try
            {
                _onHotKeysChanged?.Invoke();
            }
            catch (Exception ex) { AppLogger.Warn("Applying default hotkeys failed.", ex); }

            AppLogger.Info("Hotkeys reset to default values.");
        }

        /// <summary>
        /// 프로그램 종료
        /// </summary>
        private void ExecuteExitApp()
        {
            AppLogger.Warn("Exit command invoked from settings view.");
            _onExit?.Invoke();
        }

        /// <summary>
        /// 설정 저장
        /// </summary>
        private void SaveSettings()
        {
            try
            {
                ConfigService.SaveDeferred(_settings);
            }
            catch
            {
                try { ConfigService.SaveDeferred(_settings); }
                catch (Exception ex) { AppLogger.Warn("Fallback settings save failed.", ex); }
            }
        }

        /// <summary>
        /// Hex 색상 문자열을 Brush로 변환
        /// </summary>
        private static Brush StringToBrush(string hex)
        {
            try
            {
                return new BrushConverter().ConvertFromString(hex) as SolidColorBrush ?? Brushes.White;
            }
            catch
            {
                return Brushes.White;
            }
        }

        /// <summary>
        /// 프리셋 저장 또는 로드
        /// </summary>
        public void ExecuteSaveOrLoadPreset(string? action)
        {
            if (string.IsNullOrEmpty(action)) return;

            var mainWindow = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
            if (mainWindow == null) return;

            switch (action)
            {
                case "Save":
                    _settings.SavePreset(_selectedPresetNumber, mainWindow.Left, mainWindow.Top, _settings.LineMarginLeft, _settings.LineMargin);
                    _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);
                    OnPropertyChanged(nameof(CurrentPositionDisplay));
                    ConfigService.Save(_settings);
                    AppLogger.Info($"Saved preset {_selectedPresetNumber}.");
                    MessageBox.Show($"프리셋 {_selectedPresetNumber}에 현재 위치가 저장되었습니다.");
                    break;
                case "Load":
                    var preset = _settings.GetPreset(_selectedPresetNumber);
                    if (preset != null && preset.HasMarginData)
                    {
                        _settings.LineMarginLeft = preset.LineMarginLeft;
                        _settings.LineMargin = preset.LineMargin;
                        OnPropertyChanged(nameof(LineMarginLeft));
                        OnPropertyChanged(nameof(LineMargin));

                        if (!OverlayPresetPositionService.TryApplyByMargin(mainWindow, _settings))
                        {
                            mainWindow.Left = preset.Left;
                            mainWindow.Top = preset.Top;
                        }

                        _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);
                        OnPropertyChanged(nameof(CurrentPositionDisplay));
                        ConfigService.Save(_settings);
                        AppLogger.Info($"Loaded preset {_selectedPresetNumber}.");
                    }
                    else
                    {
                        AppLogger.Warn($"Preset {_selectedPresetNumber} has no saved coordinates.");
                        MessageBox.Show($"프리셋 {_selectedPresetNumber}에는 저장된 좌표가 없습니다.");
                    }
                    break;
            }
        }

        private void ApplySelectedPresetToOffsets()
        {
            var preset = _settings.GetPreset(_selectedPresetNumber);
            if (preset == null)
            {
                return;
            }

            if (!preset.HasMarginData)
            {
                AppLogger.Debug($"Preset {_selectedPresetNumber} is empty. Skipping auto-apply.");
                return;
            }

            _settings.LineMarginLeft = preset.LineMarginLeft;
            _settings.LineMargin = preset.LineMargin;
            _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);

            OnPropertyChanged(nameof(LineMarginLeft));
            OnPropertyChanged(nameof(LineMargin));
            OnPropertyChanged(nameof(CurrentPositionDisplay));
        }

        public void ResolveHotKeyConflict(string targetPropertyName, string? hotKeyValue)
        {
            HotKeySettingsService.ResolveConflict(_settings, targetPropertyName, hotKeyValue, OnPropertyChanged);
        }
    }
}
