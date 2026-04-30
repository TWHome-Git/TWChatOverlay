using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.Services.LogAnalysis;
using TWChatOverlay.ViewModels;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 채팅 오버레이 메인 창의 UI/서비스 연동을 담당합니다.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Fields
        private DailyWeeklyContentWindow? _dailyWeeklyContentOverlay;
        private ItemCalendarWindow? _itemCalendarWindow;
        private AbaddonRoadSummaryWindow? _abaddonRoadSummaryWindow;
        private ExperienceService _expService;
        private HotKeyService? _hotKeyService;
        private WindowStickyService? _stickyService;
        private BossAlarmSchedulerService? _bossAlarmSchedulerService;
        private BuffTrackerService _buffTrackerService;
        private ExperienceEssenceAlertService _experienceEssenceAlertService;
        private DungeonCountDisplayService _dungeonCountDisplayService;
        private ChatSettings _settings;
        private LogService? _logService;
        private LogAnalysisService _logAnalysisService;
        private MainLogPipelineCoordinator _logPipelineCoordinator;
        private SettingsViewModel _settingsViewModel;
        private bool _hasCompletedInitialPresentation;
        private bool _canShowAuxiliaryWindows = true;
        private bool _isSettingsPositionMode;
        private readonly DispatcherTimer _mainTabAutoHideTimer;
        private bool _isLogServiceInitialized;
        private bool _startLogServiceWhenInitialized;

        private bool _isOverlayVisible = true;
        /// <summary>
        /// Public read-only view of overlay visibility for other windows to subscribe to.
        /// </summary>
        public bool IsOverlayVisible => _isOverlayVisible;

        /// <summary>
        /// Raised when overlay visibility changes. The event argument is the new visibility value.
        /// </summary>
        public event EventHandler<bool>? OverlayVisibilityChanged;
        public event EventHandler<bool>? DailyWeeklyVisibilityChanged;
        public event EventHandler<bool>? ItemCalendarVisibilityChanged;
        private string _currentTabTag = "Basic";
        private readonly UiLogBatchDispatcher _uiLogBatchDispatcher;
        private readonly LogTabBufferStore _logTabBufferStore;
        private readonly TabDisplayStateResolver _tabDisplayStateResolver;
        private DateTime _loadedItemMonthStart = DateTime.MinValue;
        private DateTime _loadedAbaddonMonthStart = DateTime.MinValue;
        private bool _isItemMonthLogsLoading;
        private bool _isHistoricalItemWarmupRunning;
        private bool _isHistoricalAbaddonWarmupRunning;
        private bool _isRefreshLogDisplayScheduled;
        private AbaddonSummaryValue _abaddonWeeklySummary = new();
        private readonly object _pendingMonthlyItemSnapshotsLock = new();
        private readonly List<ItemLogSnapshotEntry> _pendingMonthlyItemSnapshots = new();
        private DropItemResolver.DropItemFilterSnapshot? _defaultDropItemFilterSnapshot;
        private int _activeCharacterProfileSlot = 1;
        private readonly Dictionary<int, ExperienceService> _profileExpServices = new();
        private readonly Dictionary<int, BuffTrackerService> _profileBuffTrackerServices = new();

        private static readonly Regex AbaddonEntryFeeRegex = new(
            @"입장료\s*(?<value>[\d,]+)\s*만\s*Seed",
            RegexOptions.Compiled);
        private static readonly Regex MagicStoneGainRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개",
            RegexOptions.Compiled);
        private static readonly Regex MagicStoneLossRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개를\s*빼앗겼습니다",
            RegexOptions.Compiled);
        private static readonly Regex HtmlFontTagRegex = new(
            @"<font\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string SeedIconUri = "pack://application:,,,/Data/images/Item/시드.png";
        private const string LowMagicStoneIconUri = "pack://application:,,,/Data/images/Item/하급마정석.png";
        private const string MiddleMagicStoneIconUri = "pack://application:,,,/Data/images/Item/중급마정석.png";
        private const string HighMagicStoneIconUri = "pack://application:,,,/Data/images/Item/상급마정석.png";
        private const string TopMagicStoneIconUri = "pack://application:,,,/Data/images/Item/최상급마정석.png";
        private static readonly string ItemLogDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Itemlog");

        public static readonly DependencyProperty CurrentFontProperty =
            DependencyProperty.Register("CurrentFont", typeof(FontFamily), typeof(MainWindow));

        public FontFamily CurrentFont
        {
            get => (FontFamily)GetValue(CurrentFontProperty);
            set => SetValue(CurrentFontProperty, value);
        }

        public static readonly DependencyProperty CurrentFontSizeProperty =
            DependencyProperty.Register("CurrentFontSize", typeof(double), typeof(MainWindow));

        public double CurrentFontSize
        {
            get => (double)GetValue(CurrentFontSizeProperty);
            set => SetValue(CurrentFontSizeProperty, value);
        }

        private RichTextBox? LogDisplay => ChatDisplay?.LogDisplayControl;
        #endregion

        public MainWindow()
        {
            InitializeComponent();
            Opacity = 0;
            IsHitTestVisible = false;
            Topmost = false;
            _uiLogBatchDispatcher = new UiLogBatchDispatcher(Dispatcher, 60);
            _logTabBufferStore = ChatWindowHub.SharedLogBuffers;
            _tabDisplayStateResolver = new TabDisplayStateResolver();
            _mainTabAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _mainTabAutoHideTimer.Tick += (_, _) => HideMainTabs();

            _settings = ConfigService.Load();
            ApplyStartupPreset();
            this.DataContext = _settings;
            _logAnalysisService = new LogAnalysisService(_settings);
            _logPipelineCoordinator = new MainLogPipelineCoordinator(_settings, _logAnalysisService);
            _settingsViewModel = new SettingsViewModel(_settings, OnColorsUpdatedFromSettings, ConfirmExit, OnSettingsResetFromSettings, ApplyHotKeys);
            SettingsDisplay.DataContext = _settingsViewModel;
            SettingsDisplay.OnlyChatMode = true;
            SettingsDisplay.SetCompactMode(true);

            _expService = new ExperienceService(_settings);
            _experienceEssenceAlertService = new ExperienceEssenceAlertService(_settings);
            _dungeonCountDisplayService = new DungeonCountDisplayService(_settings);
            _buffTrackerService = new BuffTrackerService(_settings);
            _profileExpServices[1] = new ExperienceService(_settings, suppressAlert: true);
            _profileExpServices[2] = new ExperienceService(_settings, suppressAlert: true);
            _profileBuffTrackerServices[1] = new BuffTrackerService(_settings, suppressEndSound: true);
            _profileBuffTrackerServices[2] = new BuffTrackerService(_settings, suppressEndSound: true);
            _buffTrackerService.PropertyChanged += BuffTrackerService_PropertyChanged;
            ExpTrackerPanel.DataContext = _expService.SessionState;
            _logService = new LogService(_expService, _settings);
            TryLoadTestDropItemJsonForSession();
            DropItemResolver.InitializeAsync(_settings);
            _logService.OnNewLogRead += (html) =>
            {
                _uiLogBatchDispatcher.Enqueue(html, _expService.IsReady, ProcessUiLogBatch);
            };
            _logService.InitialLogsLoaded += () =>
            {
                Dispatcher.BeginInvoke(new Action(RequestRefreshLogDisplay), DispatcherPriority.ApplicationIdle);
            };
            BlacklistService.BlacklistChanged += () =>
            {
                Dispatcher.BeginInvoke(new Action(RequestRefreshLogDisplay), DispatcherPriority.Background);
            };
            _ = InitializeLogServiceAfterEtaProfilesAsync();
            MonthlyReadableLogExportService.CleanupLegacyArtifacts();
            ApplyInitialSettings();
            ApplySubAddonWindowSettings();
            ApplyItemDropHelperWindowSettings();
            ApplyBuffTrackerWindowSettings();
            ApplyBuffTrackerHelperWindowSettings();
            StartHistoricalAbaddonWarmup();

            Dispatcher.BeginInvoke(new Action(() => InitializeNativeServices()), DispatcherPriority.Loaded);

            this.Deactivated += (s, e) => ReleaseMouseForce();
            this.Activated += (s, e) => ReleaseMouseForce();
            this.StateChanged += MainWindow_StateChanged;
            this.Closed += MainWindow_Closed;
            AppLogger.Info("Main window initialized.");
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try { _mainTabAutoHideTimer.Stop(); } catch { }
            try { ExperienceAlertWindowService.SaveCurrentPosition(_settings); } catch { }
            try { DungeonCountDisplayWindowService.SaveCurrentPosition(_settings); } catch { }
            try { _buffTrackerService.PropertyChanged -= BuffTrackerService_PropertyChanged; } catch { }
            try { BuffTrackerWindow.Instance?.Close(); } catch { }
            try { BuffTrackerHelperWindow.Instance?.Close(); } catch { }
            try
            {
                foreach (Window window in Application.Current.Windows.OfType<ChatCloneWindow>().ToList())
                {
                    try { window.Close(); } catch { }
                }
            }
            catch { }
            try { _abaddonRoadSummaryWindow?.Close(); } catch { }
            try { FlushPendingMonthlyItemSnapshots(); } catch { }
            try { _logService?.Dispose(); } catch { }
            try { _expService?.Stop(); } catch { }
            try { _buffTrackerService?.Dispose(); } catch { }
            try { foreach (var profileExp in _profileExpServices.Values) profileExp.Stop(); } catch { }
            try { foreach (var profileBuff in _profileBuffTrackerServices.Values) profileBuff.Dispose(); } catch { }
            try
            {
                if (_stickyService != null)
                {
                    _stickyService.AuxiliaryWindowVisibilityChanged -= StickyService_AuxiliaryWindowVisibilityChanged;
                }
            }
            catch { }
            try { _stickyService?.Stop(); } catch { }
            try { _bossAlarmSchedulerService?.Stop(); } catch { }
            try { _hotKeyService?.Dispose(); } catch { }
        }

        public SettingsViewModel SettingsViewModelInstance => _settingsViewModel;
        public bool IsDailyWeeklyVisible => _dailyWeeklyContentOverlay?.IsVisible == true;
        public bool IsItemCalendarVisible => _itemCalendarWindow?.IsVisible == true;

        private void RestoreExperienceEssenceAlertState()
        {
            try
            {
                var logPaths = GetAllChatLogPaths();
                _experienceEssenceAlertService.RestoreFromRecentLogs(logPaths, _logAnalysisService);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to restore experience essence alert state from recent logs.", ex);
            }
        }

        private IEnumerable<string> GetAllChatLogPaths()
        {
            if (string.IsNullOrWhiteSpace(_settings.ChatLogFolderPath) ||
                !Directory.Exists(_settings.ChatLogFolderPath))
                yield break;

            foreach (string path in Directory.EnumerateFiles(_settings.ChatLogFolderPath, "TWChatLog_*.html")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }

        private void BuffTrackerService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BuffTrackerService.HasAnyActiveBuffs))
                return;

            Dispatcher.BeginInvoke(new Action(ApplyBuffTrackerWindowSettings), DispatcherPriority.Background);
        }

        private void OnColorsUpdatedFromSettings(string _)
        {
            _logTabBufferStore.UpdateAllBrushes(category => ChatBrushResolver.Resolve(_settings, category));
            ChatWindowHub.NotifyBuffersChanged();

            RequestRefreshLogDisplay();
        }

        private void OnSettingsResetFromSettings()
        {
            ApplyInitialSettings();
            RequestRefreshLogDisplay();
            try { ApplyHotKeys(); }
            catch (Exception ex) { AppLogger.Warn("Failed to reapply hotkeys after settings reset.", ex); }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            NativeMethods.SetWindowLong(helper.Handle, NativeMethods.GWL_EXSTYLE,
                NativeMethods.GetWindowLong(helper.Handle, NativeMethods.GWL_EXSTYLE) | 0x00000080);
        }

        private void InitializeNativeServices()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).EnsureHandle();

                _hotKeyService = new HotKeyService(handle);
                ApplyHotKeys();

                _hotKeyService.HotKeyPressed += (id) =>
                {
                    if (ShouldSuppressGlobalHotKeys())
                    {
                        AppLogger.Debug($"Suppressed hotkey action id={id} while editing hotkey settings.");
                        return;
                    }

                    AppLogger.Info($"Handling hotkey action id={id}.");
                    switch (id)
                    {
                        case HotKeyService.EXIT_HOTKEY_ID:
                            ConfirmExit();
                            break;
                        case HotKeyService.TOGGLE_OVERLAY_ID:
                            ToggleOverlayVisibility();
                            break;
                        case HotKeyService.TOGGLE_ADDON_ID:
                            TriggerMenuButton("BtnAddon");
                            break;
                        case HotKeyService.TOGGLE_ETA_RANKING_ID:
                            TriggerMenuButton("BtnEtaRanking");
                            break;
                        case HotKeyService.TOGGLE_COEFFICIENT_ID:
                            TriggerMenuButton("BtnCoefficient");
                            break;
                        case HotKeyService.TOGGLE_EQUIPMENTDB_ID:
                            TriggerMenuButton("BtnEquipmentDb");
                            break;
                        case HotKeyService.TOGGLE_ENCRYPT_ID:
                            TriggerMenuButton("BtnEncrypt");
                            break;
                        case HotKeyService.TOGGLE_SETTINGS_ID:
                            TriggerMenuButton("BtnSettings");
                            break;
                        case HotKeyService.TOGGLE_ALWAYS_VISIBLE_ID:
                            _settings.AlwaysVisible = !_settings.AlwaysVisible;
                            AppLogger.Info($"AlwaysVisible toggled to {_settings.AlwaysVisible}.");
                            break;
                        case HotKeyService.TOGGLE_DAILY_WEEKLY_CONTENT_ID:
                            TriggerMenuButton("BtnDailyWeekly");
                            break;
                    }
                };

                _stickyService = new WindowStickyService(this, _settings);
                _stickyService.AuxiliaryWindowVisibilityChanged += StickyService_AuxiliaryWindowVisibilityChanged;
                _stickyService.Start();
                _stickyService.UpdatePositionImmediately();
                _bossAlarmSchedulerService = new BossAlarmSchedulerService(_settings);
                _bossAlarmSchedulerService.Start();
                _expService.Start();
                foreach (var profileExp in _profileExpServices.Values)
                    profileExp.Start();
                StartLogServiceWhenReady();

                _settings.PropertyChanged += OnSettingsPropertyChanged;

                foreach (Window w in Application.Current.Windows)
                {
                    if (w is SubMenuWindow sub)
                    {
                        var settingsView = new SettingsView();
                        settingsView.DataContext = _settingsViewModel;
                        sub.ShowHostContent(settingsView, "설정");
                        sub.Hide();
                        break;
                    }
                }

                if (_settings.ShowDailyWeeklyContentOverlay)
                    ShowDailyWeeklyWindow();

                Dispatcher.BeginInvoke(new Action(CompleteInitialPresentation), DispatcherPriority.ApplicationIdle);

                AppLogger.Info("Native services initialized successfully.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"서비스 시작 중 오류: {ex.Message}");
            }
        }

        private void CompleteInitialPresentation()
        {
            if (_hasCompletedInitialPresentation)
            {
                return;
            }

            _hasCompletedInitialPresentation = true;

            if (_isOverlayVisible)
            {
                _stickyService?.UpdatePositionNow();

                if (!IsVisible)
                {
                    Show();
                }

                Opacity = 1;
                IsHitTestVisible = true;
                Visibility = Visibility.Visible;
                _stickyService?.UpdatePositionImmediately();
            }

            Task.Run(() => RestoreExperienceEssenceAlertState());
            StartHistoricalItemWarmup();
        }

        private void StickyService_AuxiliaryWindowVisibilityChanged(bool canShow)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _canShowAuxiliaryWindows = canShow;
                ApplyBuffTrackerWindowSettings();
                ApplyDailyWeeklyWindowVisibility();
            }), DispatcherPriority.Background);
        }

        private async Task InitializeLogServiceAfterEtaProfilesAsync()
        {
            try
            {
                await EtaProfileResolver.EnsureLoadedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ETA profile load failed before log initialization. Logs will still be initialized.", ex);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (_logService == null || _isLogServiceInitialized)
                    return;

                _logService.Initialize();
                _isLogServiceInitialized = true;

                if (_startLogServiceWhenInitialized)
                {
                    _logService.Start();
                    _startLogServiceWhenInitialized = false;
                }
            }, DispatcherPriority.Loaded);
        }

        private void StartLogServiceWhenReady()
        {
            if (_logService == null)
                return;

            if (_isLogServiceInitialized)
            {
                _logService.Start();
                return;
            }

            _startLogServiceWhenInitialized = true;
        }





    }
}
