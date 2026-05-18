using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading;
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
        private AbandonRoadSummaryWindow? _AbandonRoadSummaryWindow;
        private ExperienceService _expService;
        private HotKeyService? _hotKeyService;
        private WindowStickyService? _stickyService;
        private BossAlarmSchedulerService? _bossAlarmSchedulerService;
        private BuffTrackerService _buffTrackerService;
        private ExperienceEssenceAlertService _experienceEssenceAlertService;
        private DungeonCountDisplayService _dungeonCountDisplayService;
        private ReadableLogArchiveService _readableLogArchiveService;
        private MessengerLogWatcherService _messengerLogWatcherService;
        private ChatSettings _settings;
        private LogService? _logService;
        private LogAnalysisService _logAnalysisService;
        private MainLogPipelineCoordinator _logPipelineCoordinator;
        private SettingsViewModel _settingsViewModel;
        private bool _hasCompletedInitialPresentation;
        private bool _canShowAuxiliaryWindows = true;
        private bool _isSettingsPositionMode;
        private bool _isAddonPositionMode;
        private int _addonPositionPreviewTabIndex = -1;
        private bool _isWizardChatPositionMode;
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
        private bool _isRefreshLogDisplayScheduled;
        private AbandonSummaryValue _AbandonWeeklySummary = new();
        private string _AbandonWeeklySummaryWeekKey = string.Empty;
        private readonly object _defaultDropItemFilterLock = new();
        private DropItemResolver.DropItemFilterSnapshot? _defaultDropItemFilterSnapshot;
        private StartupLoadingWindow? _startupLoadingWindow;
        private InitialSetupWizardWindow? _initialSetupWizardWindow;
        private readonly bool _settingsFileMissingOnStartup;
        private bool _pendingInitialSetupWizard;
        private bool _isInitialSetupWizardRunning;
        private CancellationTokenSource? _startupLogInitCts;
        private bool _startupLogInitRunning;
        private bool _restartRequestedAfterWizardCompletion;
        private bool _restartLaunchTriggered;

        private static readonly Regex AbandonEntryFeeRegex = new(
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
            Topmost = true;
            _settingsFileMissingOnStartup = !ConfigService.SettingsFileExists();
            _pendingInitialSetupWizard = _settingsFileMissingOnStartup;
            _uiLogBatchDispatcher = new UiLogBatchDispatcher(Dispatcher, 60);
            _logTabBufferStore = ChatWindowHub.SharedLogBuffers;
            _tabDisplayStateResolver = new TabDisplayStateResolver();
            _mainTabAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _mainTabAutoHideTimer.Tick += (_, _) => HideMainTabs();

            _settings = ConfigService.Load();
            if (!_settings.InitialSetupWizardCompleted)
            {
                _pendingInitialSetupWizard = true;
            }
            _ = IgnoredChatMessageService.EnsureLoadedAsync();
            ApplyStartupPreset();
            this.DataContext = _settings;
            _logAnalysisService = new LogAnalysisService(_settings);
            _logPipelineCoordinator = new MainLogPipelineCoordinator(_settings, _logAnalysisService);
            _settingsViewModel = new SettingsViewModel(_settings, OnColorsUpdatedFromSettings, ConfirmExit, OnSettingsResetFromSettings, ApplyHotKeys, ExecuteManualLogReloadFromSettingsAsync);
            SettingsDisplay.DataContext = _settingsViewModel;
            SettingsDisplay.OnlyChatMode = true;
            SettingsDisplay.SetCompactMode(true);

            _expService = new ExperienceService(_settings);
            _experienceEssenceAlertService = new ExperienceEssenceAlertService(_settings);
            ExperienceAlertWindowService.ConfigureStateBridge(
                () => _experienceEssenceAlertService.GetStateSnapshot(),
                snapshot => _experienceEssenceAlertService.ApplyStateSnapshot(snapshot));
            _dungeonCountDisplayService = new DungeonCountDisplayService(_settings);
            _readableLogArchiveService = new ReadableLogArchiveService();
            _messengerLogWatcherService = new MessengerLogWatcherService(_settings);
            _messengerLogWatcherService.Start();
            _buffTrackerService = new BuffTrackerService(_settings);
            _buffTrackerService.PropertyChanged += BuffTrackerService_PropertyChanged;
            ExpTrackerPanel.DataContext = _expService.SessionState;
            _logService = new LogService(_expService, _settings);
            TryLoadTestDropItemJsonForSession();
            DropItemResolver.InitializeAsync(_settings);
            _logService.OnNewLogRead += (logItem) =>
            {
                _uiLogBatchDispatcher.Enqueue(logItem.Html, logItem.IsRealTime, logItem.IsStartupBackfill, ProcessUiLogBatch);
            };
            _logService.InitialLogsLoaded += () =>
            {
                Dispatcher.BeginInvoke(new Action(RequestRefreshLogDisplay), DispatcherPriority.ApplicationIdle);
            };
            BlacklistService.BlacklistChanged += () =>
            {
                Dispatcher.BeginInvoke(new Action(RequestRefreshLogDisplay), DispatcherPriority.Background);
            };
            this.Deactivated += (s, e) => ReleaseMouseForce();
            this.Activated += (s, e) => ReleaseMouseForce();
            this.Activated += (_, _) => Dispatcher.BeginInvoke(new Action(EnsureMainWindowTopmost), DispatcherPriority.Background);
            this.Deactivated += (_, _) => Dispatcher.BeginInvoke(new Action(EnsureMainWindowTopmost), DispatcherPriority.Background);
            this.StateChanged += MainWindow_StateChanged;
            this.StateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(EnsureMainWindowTopmost), DispatcherPriority.Background);
            this.IsVisibleChanged += (_, _) => Dispatcher.BeginInvoke(new Action(EnsureMainWindowTopmost), DispatcherPriority.Background);
            this.Closed += MainWindow_Closed;
            AppLogger.Info("Main window initialized.");

            ShowStartupLoadingWindow();
            Dispatcher.BeginInvoke(
                new Action(() => _ = InitializeStartupDataAsync()),
                DispatcherPriority.ApplicationIdle);
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
            try { _AbandonRoadSummaryWindow?.Close(); } catch { }
            try { _startupLogInitCts?.Cancel(); } catch { }
            try { _startupLogInitCts?.Dispose(); } catch { }
            try { _logService?.Dispose(); } catch { }
            try { _expService?.Stop(); } catch { }
            try { _buffTrackerService?.Dispose(); } catch { }
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
            try { _messengerLogWatcherService?.Dispose(); } catch { }
            try { _hotKeyService?.Dispose(); } catch { }
        }

        public SettingsViewModel SettingsViewModelInstance => _settingsViewModel;
        public bool IsDailyWeeklyVisible => _dailyWeeklyContentOverlay?.IsVisible == true;
        public bool IsItemCalendarVisible => _itemCalendarWindow?.IsVisible == true;
        public bool IsSettingsPositionMode => _isSettingsPositionMode || _isAddonPositionMode;

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

            try
            {
                foreach (var sub in Application.Current.Windows.OfType<SubMenuWindow>().ToList())
                {
                    try { sub.Hide(); } catch { }
                }
            }
            catch { }

            _pendingInitialSetupWizard = true;
            Dispatcher.BeginInvoke(new Action(TryShowInitialSetupWizardIfNeeded), DispatcherPriority.Background);
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
                _expService.Reset();
                _expService.Start();
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
                if (_pendingInitialSetupWizard)
                {
                    TryShowInitialSetupWizardIfNeeded();
                    return;
                }

                _stickyService?.UpdatePositionNow();

                if (!IsVisible)
                {
                    Show();
                }

                Opacity = 1;
                IsHitTestVisible = true;
                Visibility = Visibility.Visible;
                _stickyService?.UpdatePositionImmediately();
                EnsureMenuWindowVisible();
            }

        }

        private void TryShowInitialSetupWizardIfNeeded()
        {
            if (!_pendingInitialSetupWizard || _isInitialSetupWizardRunning)
                return;

            _isInitialSetupWizardRunning = true;
            _pendingInitialSetupWizard = false;

            try
            {
                try { Hide(); } catch { }
                Opacity = 0;
                IsHitTestVisible = false;

                try
                {
                    var menu = Application.Current.Windows.OfType<MenuWindow>().FirstOrDefault();
                    menu?.Hide();
                }
                catch { }

                try
                {
                    foreach (var sub in Application.Current.Windows.OfType<SubMenuWindow>().ToList())
                    {
                        try { sub.Hide(); } catch { }
                    }
                }
                catch { }

                _initialSetupWizardWindow = new InitialSetupWizardWindow(_settings, this);
                _initialSetupWizardWindow.Owner = null;
                _initialSetupWizardWindow.Topmost = true;
                _initialSetupWizardWindow.WizardFinished += InitialSetupWizardWindow_WizardFinished;
                _initialSetupWizardWindow.LogPathConfirmed += InitialSetupWizardWindow_LogPathConfirmed;
                _initialSetupWizardWindow.Closed += InitialSetupWizardWindow_Closed;
                _initialSetupWizardWindow.Show();
                _initialSetupWizardWindow.Activate();
                _initialSetupWizardWindow.Focus();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to open initial setup wizard.", ex);
                RevealMainUiAfterWizard();
            }
        }

        private void InitialSetupWizardWindow_WizardFinished(object? sender, bool completed)
        {
            AppLogger.Info($"Initial setup wizard closed. Completed={completed}");
            if (!completed)
                return;

            try
            {
                _settings.InitialSetupWizardCompleted = true;
                ConfigService.Save(_settings);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to persist initial setup completion state.", ex);
            }

            _restartRequestedAfterWizardCompletion = true;

            if (!_startupLogInitRunning)
            {
                RestartApplicationAfterInitialSetupWizard();
            }
        }

        private void InitialSetupWizardWindow_Closed(object? sender, EventArgs e)
        {
            if (_initialSetupWizardWindow != null)
            {
                _initialSetupWizardWindow.WizardFinished -= InitialSetupWizardWindow_WizardFinished;
                _initialSetupWizardWindow.LogPathConfirmed -= InitialSetupWizardWindow_LogPathConfirmed;
                _initialSetupWizardWindow.Closed -= InitialSetupWizardWindow_Closed;
            }

            _initialSetupWizardWindow = null;

            if (_restartRequestedAfterWizardCompletion)
            {
                if (!_startupLogInitRunning)
                {
                    RestartApplicationAfterInitialSetupWizard();
                }

                return;
            }

            RevealMainUiAfterWizard();
        }

        private void InitialSetupWizardWindow_LogPathConfirmed(object? sender, string selectedPath)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_isLogServiceInitialized || _startupLogInitRunning)
                    return;

                _readableLogArchiveService.ClearArchiveLogsAndResetCheckpoint();
                _ = RunDeferredLogInitializationAsync(isFirstRun: false);
            }), DispatcherPriority.Background);
        }

        private void RevealMainUiAfterWizard()
        {
            _isInitialSetupWizardRunning = false;

            if (!IsVisible)
            {
                Show();
            }

            Opacity = 1;
            IsHitTestVisible = true;
            Visibility = Visibility.Visible;
            _stickyService?.UpdatePositionImmediately();
            EnsureMenuWindowVisible();
        }

        private void EnsureMenuWindowVisible()
        {
            try
            {
                var menu = Application.Current.Windows.OfType<MenuWindow>().FirstOrDefault();
                if (menu == null)
                {
                    menu = new MenuWindow();
                    menu.Topmost = true;
                    menu.Show();
                }
                else if (!menu.IsVisible)
                {
                    menu.Show();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to show menu window after startup.", ex);
            }
        }

        private void StickyService_AuxiliaryWindowVisibilityChanged(bool canShow)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _canShowAuxiliaryWindows = canShow;
                ApplyBuffTrackerWindowSettings();
                ApplyDailyWeeklyWindowVisibility();
                ApplyAbandonRoadSummaryWindowVisibility();
            }), DispatcherPriority.Background);
        }

        private async Task InitializeLogServiceAfterEtaProfilesAsync(bool onlyToday, CancellationToken cancellationToken)
        {
            UpdateStartupLoadingProgress(15, "외부 설정을 준비하는 중입니다.");
            try
            {
                await EtaProfileResolver.EnsureLoadedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ETA profile load failed before log initialization. Logs will still be initialized.", ex);
            }

            try
            {
                UpdateStartupLoadingProgress(35, "원본 로그를 읽는 중입니다.");
                await Task.Run(async () =>
                {
                    Func<DateTime, bool>? dateFilter = onlyToday ? (d => d.Date == DateTime.Today) : null;
                    await _readableLogArchiveService.EnsureInitializedFromRawLogsAsync(
                        _settings.ChatLogFolderPath,
                        _logAnalysisService,
                        IsContentCompletionRelevantLog,
                        (dateText, current, total) =>
                        {
                            double ratio = total <= 0 ? 0 : (double)current / total;
                            double progress = 35 + (ratio * 50.0);
                            UpdateStartupLoadingProgress(progress, "원본 로그를 읽는 중입니다.", dateText);
                        },
                        dateFilter,
                        cancellationToken).ConfigureAwait(false);

                    _readableLogArchiveService.MigrateContentArchiveIfNeeded();
                }, cancellationToken).ConfigureAwait(false);

                _AbandonWeeklySummary = _readableLogArchiveService.LoadAbandonWeeklySummary(DateTime.Today);
                _AbandonWeeklySummaryWeekKey = GetIsoWeekKey(DateTime.Today);
                _settings.StartupLogReadCanceled = false;
                ConfigService.SaveDeferred(_settings);
            }
            catch (OperationCanceledException)
            {
                _settings.StartupLogReadCanceled = true;
                ConfigService.SaveDeferred(_settings);
                throw;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to initialize dedicated Logs archive from source chat logs.", ex);
            }

            if (_logService != null && !_isLogServiceInitialized)
            {
                UpdateStartupLoadingProgress(85, "채팅 로그 서비스를 시작하는 중입니다.");
                await Task.Run(() => _logService.Initialize()).ConfigureAwait(false);
                _isLogServiceInitialized = true;

                if (_startLogServiceWhenInitialized)
                {
                    await Task.Run(() => _logService.Start()).ConfigureAwait(false);
                    _startLogServiceWhenInitialized = false;
                }
            }

            UpdateStartupLoadingProgress(100, "초기화가 완료되었습니다.");
            CloseStartupLoadingWindow();
        }

        private async Task InitializeStartupDataAsync()
        {
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                UpdateStartupLoadingProgress(10, "업데이트를 확인하는 중입니다.");
                var updateResult = await UpdateService.CheckForUpdateAsync(forceInstallLatest: false, showNoUpdateMessage: false);
                if (updateResult == UpdateCheckResult.UpdateApplied)
                {
                    return;
                }

                bool needsWizardLogPath = _pendingInitialSetupWizard;
                bool shouldRunStartupLogInitialization = !needsWizardLogPath || _settings.StartupLogReadCanceled;

                await Dispatcher.InvokeAsync(() =>
                {
                    ApplyInitialSettings();
                    ApplySubAddonWindowSettings();
                    ApplyItemDropHelperWindowSettings();
                    ApplyBuffTrackerWindowSettings();
                    ApplyBuffTrackerHelperWindowSettings();
                    TryPrewarmDisplayWindows();
                    InitializeNativeServices();
                }, DispatcherPriority.Background);

                if (shouldRunStartupLogInitialization)
                {
                    bool isFirstRun = !_settings.StartupTodayOnlyBootstrapCompleted;
                    await RunDeferredLogInitializationAsync(isFirstRun).ConfigureAwait(false);
                }
                else
                {
                    CloseStartupLoadingWindow();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Startup initialization failed.", ex);
                UpdateStartupLoadingProgress(100, "초기화 중 오류가 발생했습니다.");
                CloseStartupLoadingWindow();
            }
        }

        private void ShowStartupLoadingWindow()
        {
            if (_startupLoadingWindow != null)
                return;

            _startupLoadingWindow = new StartupLoadingWindow();
            _startupLoadingWindow.CancelRequested += StartupLoadingWindow_CancelRequested;
            _startupLoadingWindow.Show();
            _startupLoadingWindow.SetCancelEnabled(true);
            _startupLoadingWindow.UpdateProgress(5, "초기화 진행중...");
        }

        private void UpdateStartupLoadingProgress(double value, string statusText)
            => UpdateStartupLoadingProgress(value, statusText, string.Empty);

        private void UpdateStartupLoadingProgress(double value, string statusText, string dateText)
        {
            if (_startupLoadingWindow == null)
                return;

            if (!_startupLoadingWindow.Dispatcher.CheckAccess())
            {
                _startupLoadingWindow.Dispatcher.BeginInvoke(new Action(() => UpdateStartupLoadingProgress(value, statusText, dateText)));
                return;
            }

            _startupLoadingWindow.UpdateProgress(value, statusText, dateText);
        }

        private void CloseStartupLoadingWindow()
        {
            if (_startupLoadingWindow == null)
                return;

            if (!_startupLoadingWindow.Dispatcher.CheckAccess())
            {
                _startupLoadingWindow.Dispatcher.BeginInvoke(new Action(CloseStartupLoadingWindow));
                return;
            }

            _startupLoadingWindow.CancelRequested -= StartupLoadingWindow_CancelRequested;
            _startupLoadingWindow.Close();
            _startupLoadingWindow = null;
        }

        private async Task RunDeferredLogInitializationAsync(bool isFirstRun, bool force = false)
        {
            if (_startupLogInitRunning || (_isLogServiceInitialized && !force))
                return;

            _startupLogInitRunning = true;
            _startupLogInitCts?.Dispose();
            _startupLogInitCts = new CancellationTokenSource();

            ShowStartupLoadingWindow();
            UpdateStartupLoadingProgress(12, "로그 초기화를 준비하는 중입니다.");

            try
            {
                bool onlyToday = isFirstRun && !_settings.StartupLogReadCanceled;
                await InitializeLogServiceAfterEtaProfilesAsync(onlyToday, _startupLogInitCts.Token).ConfigureAwait(false);
                if (onlyToday)
                {
                    _settings.StartupTodayOnlyBootstrapCompleted = true;
                    ConfigService.SaveDeferred(_settings);
                }
            }
            catch (OperationCanceledException)
            {
                UpdateStartupLoadingProgress(100, "로그 읽기를 취소했습니다. 다음 실행 시 다시 진행됩니다.");
            }
            finally
            {
                _startupLogInitRunning = false;
                CloseStartupLoadingWindow();

                if (_restartRequestedAfterWizardCompletion)
                {
                    _ = Dispatcher.BeginInvoke(new Action(RestartApplicationAfterInitialSetupWizard), DispatcherPriority.Background);
                }
            }
        }

        private void StartupLoadingWindow_CancelRequested(object? sender, EventArgs e)
        {
            try
            {
                _startupLogInitCts?.Cancel();
            }
            catch
            {
            }
        }

        private void RestartApplicationAfterInitialSetupWizard()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(RestartApplicationAfterInitialSetupWizard), DispatcherPriority.Background);
                return;
            }

            if (!_restartRequestedAfterWizardCompletion || _restartLaunchTriggered)
                return;

            _restartLaunchTriggered = true;

            try
            {
                string? executablePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                {
                    executablePath = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
                    throw new InvalidOperationException("Unable to resolve current executable path for restart.");

                string cmdArgs = $"/c timeout /t 1 /nobreak >nul && start \"\" \"{executablePath}\"";

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = cmdArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });

                AppLogger.Info("Restarting application after initial setup wizard completion.");
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                _restartLaunchTriggered = false;
                _restartRequestedAfterWizardCompletion = false;
                AppLogger.Warn("Failed to restart after initial setup wizard completion.", ex);
                RevealMainUiAfterWizard();
            }
        }

        private async Task<bool> ExecuteManualLogReloadFromSettingsAsync()
        {
            bool restartLogService = false;
            try
            {
                if (_startupLogInitRunning)
                {
                    AppLogger.Info("Manual log reload skipped because startup log initialization is already running.");
                    return false;
                }

                if (string.IsNullOrWhiteSpace(_settings.ChatLogFolderPath) || !Directory.Exists(_settings.ChatLogFolderPath))
                {
                    AppLogger.Warn($"Manual log reload skipped because chat log folder path is invalid. Path='{_settings.ChatLogFolderPath}'");
                    return false;
                }

                if (_isLogServiceInitialized && _logService != null)
                {
                    restartLogService = true;
                    _logService.Stop();
                }

                _readableLogArchiveService.ClearArchiveLogsAndResetCheckpoint();
                await RunDeferredLogInitializationAsync(isFirstRun: false, force: true).ConfigureAwait(true);
                return !_settings.StartupLogReadCanceled;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Manual log reload request failed.", ex);
                return false;
            }
            finally
            {
                if (restartLogService && _logService != null)
                {
                    try
                    {
                        _logService.Start();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("Failed to restart log service after manual log reload.", ex);
                    }
                }
            }
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

        private void TryPrewarmDisplayWindows()
        {
            try
            {
                ShoutToastService.GetOrCreatePreviewWindow(_settings);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Display prewarm failed.", ex);
            }
        }

        private void EnsureMainWindowTopmost()
        {
            if (!IsVisible || WindowState == WindowState.Minimized)
                return;

            try
            {
                Topmost = true;
                TopmostWindowHelper.BringToTopmost(this);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to reassert main window topmost state.", ex);
            }
        }


    }
}
