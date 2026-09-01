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
    public partial class MainWindow : Window, IMainWindowHost
    {
        #region Fields
        private DailyWeeklyContentWindow? _dailyWeeklyContentOverlay;
        private ItemCalendarWindow? _itemCalendarWindow;
        private AbandonRoadSummaryWindow? _AbandonRoadSummaryWindow;
        private ExpTrackerWindow? _expTrackerWindow;
        private ExpTrackerViewModel? _expTrackerViewModel;
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
        private bool _hasRestoredChatCloneWindows;

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
        private LogAnalysisPipeline? _logAnalysisPipeline;
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

        /// <summary>시작 시 설정 파일이 없었는지(진짜 최초 실행) — 마법사의 공장 기본값 적용 조건.</summary>
        internal bool SettingsFileMissingOnStartup => _settingsFileMissingOnStartup;
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

        // IMainWindowHost: Services 계층이 View 타입 대신 인터페이스로 접근하도록 노출.
        ChatSettings? IMainWindowHost.HostSettings => DataContext as ChatSettings;
        void IMainWindowHost.RequestTopmostRefresh() => RequestTopmostRefresh();

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
            _logTabBufferStore = ChatWindowHub.SharedLogBuffers;
            _tabDisplayStateResolver = new TabDisplayStateResolver();
            _mainTabAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _mainTabAutoHideTimer.Tick += (_, _) => HideMainTabs();

            _settings = ConfigService.Load();
            UiLockService.SnapEnabled = _settings.WindowSnapEnabled;
            OverlayOpacityService.Initialize(_settings);
            if (!_settings.InitialSetupWizardCompleted)
            {
                _pendingInitialSetupWizard = true;
            }
            _currentTabTag = NormalizeMainTabTag(_settings.MainWindowChatTabTag);
            _ = IgnoredChatMessageService.EnsureLoadedAsync();
            this.DataContext = _settings;
            MainWindowHost.Current = this;
            this.Closed += (_, _) =>
            {
                if (ReferenceEquals(MainWindowHost.Current, this))
                    MainWindowHost.Current = null;
            };
            _logAnalysisService = new LogAnalysisService(_settings);
            _logPipelineCoordinator = new MainLogPipelineCoordinator(_settings, _logAnalysisService);
            // 파싱·분석을 백그라운드로 — UI 스레드는 분석 결과를 소비만 한다.
            // 아카이브 기록(파일 IO)은 UI가 필요 없으므로 분석 스레드에서 바로 처리한다.
            _logAnalysisPipeline = new LogAnalysisPipeline(
                _logPipelineCoordinator,
                Dispatcher,
                ProcessUiLogBatch,
                backgroundHandler: evt =>
                {
                    if (!evt.IsRealTime && !evt.IsStartupBackfill)
                        return; // 과거 로그 표시 전용은 집계/아카이브 부수효과 없음

                    var primary = evt.Analysis.Primary;
                    if (!primary.IsSuccess)
                        return;

                    _readableLogArchiveService?.AppendFromAnalysis(
                        DateTime.Today,
                        primary,
                        IsContentCompletionRelevantLog(primary.Parsed.FormattedText));
                });
            _settingsViewModel = new SettingsViewModel(_settings, OnColorsUpdatedFromSettings, ConfirmExit, OnSettingsResetFromSettings, ApplyHotKeys, ExecuteManualLogReloadFromSettingsAsync);

            _expService = new ExperienceService(_settings);
            _expTrackerViewModel = new ExpTrackerViewModel(_expService, _settings);
            _expService.SessionState.PropertyChanged += ExpSessionState_PropertyChanged;
            _expService.TrackerActiveChanged += () => Dispatcher.BeginInvoke(new Action(RefreshExpTrackerWindow), DispatcherPriority.Background);
            _expTrackerViewModel.UpdateDisplay();
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
            _logService = new LogService(_expService, _settings);
            TryLoadTestDropItemJsonForSession();
            DropItemResolver.InitializeAsync(_settings);
            _logService.OnNewLogRead += (logItem) => _logAnalysisPipeline?.Enqueue(logItem);
            _logService.InitialLogsLoaded += () =>
            {
                Dispatcher.BeginInvoke(new Action(() => RequestRefreshLogDisplay()), DispatcherPriority.ApplicationIdle);
            };
            BlacklistService.BlacklistChanged += () =>
            {
                Dispatcher.BeginInvoke(new Action(() => RequestRefreshLogDisplay()), DispatcherPriority.Background);
            };
            IdTagService.IdTagsChanged += () =>
            {
                Dispatcher.BeginInvoke(new Action(() => RequestRefreshLogDisplay()), DispatcherPriority.Background);
            };
            this.Deactivated += (s, e) => ReleaseMouseForce();
            this.Activated += (s, e) => ReleaseMouseForce();
            this.Activated += (_, _) => Dispatcher.BeginInvoke(new Action(EnsureMainWindowTopmost), DispatcherPriority.Background);
            this.Deactivated += (_, _) => Dispatcher.BeginInvoke(new Action(EnsureMainWindowTopmost), DispatcherPriority.Background);
            this.StateChanged += MainWindow_StateChanged;
            this.StateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(EnsureMainWindowTopmost), DispatcherPriority.Background);
            this.IsVisibleChanged += (_, _) => Dispatcher.BeginInvoke(new Action(EnsureMainWindowTopmost), DispatcherPriority.Background);
            // Owned 창(서브 채팅창)은 메인 창의 Closed보다 먼저 닫히므로,
            // Closing 시점에 종료를 표시해야 "사용자가 닫음"으로 오인해 IsOpen=false를 저장하지 않는다
            this.Closing += (_, _) => ChatWindowHub.BeginShutdown();
            this.Closed += MainWindow_Closed;
            UiLockService.UnlockChanged += OnUiUnlockChanged;
            UiLockService.WindowAdjusted += OnUnlockWindowAdjusted;
            AppLogger.Info("Main window initialized.");

            ShowStartupLoadingWindow();
            Dispatcher.BeginInvoke(
                new Action(() => _ = InitializeStartupDataAsync()),
                DispatcherPriority.ApplicationIdle);
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            
            try { UiLockService.UnlockChanged -= OnUiUnlockChanged; } catch { }
            try { UiLockService.WindowAdjusted -= OnUnlockWindowAdjusted; } catch { }
            try { _mainTabAutoHideTimer.Stop(); } catch { }
            try { _logAnalysisPipeline?.Dispose(); } catch { }
            try
            {
                _settings.MainWindowChatTabTag = _currentTabTag;
            }
            catch { }
            try { ChatWindowHub.BeginShutdown(); } catch { }
            try { ExperienceAlertWindowService.SaveCurrentPosition(_settings); } catch { }
            try { DungeonCountDisplayWindowService.SaveCurrentPosition(_settings); } catch { }
            try { _buffTrackerService.PropertyChanged -= BuffTrackerService_PropertyChanged; } catch { }
            try { BuffTrackerWindow.Instance?.Close(); } catch { }
            try { BuffTrackerHelperWindow.Instance?.Close(); } catch { }
            try { CloseExpTrackerWindow(); } catch { }
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
            try { CancelPendingReflectionEndAlerts(); } catch { }
            try { _expService.SessionState.PropertyChanged -= ExpSessionState_PropertyChanged; } catch { }
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
            try { ConfigService.Save(_settings); } catch { }
        }

        public SettingsViewModel SettingsViewModelInstance => _settingsViewModel;
        public bool IsDailyWeeklyVisible => _dailyWeeklyContentOverlay?.IsVisible == true;
        public bool IsItemCalendarVisible => _itemCalendarWindow?.IsVisible == true;
        public bool IsSettingsPositionMode => _isSettingsPositionMode || _isAddonPositionMode;

        private void ExpSessionState_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            _expTrackerViewModel?.UpdateDisplay();
            if (e.PropertyName == nameof(ExpSessionState.TotalExpDisplay) ||
                e.PropertyName == nameof(ExpSessionState.GainCountDisplay) ||
                e.PropertyName == nameof(ExpSessionState.HasLastExp) ||
                e.PropertyName == nameof(ExpSessionState.LastGainedExpDisplay))
            {
                Dispatcher.BeginInvoke(new Action(RefreshExpTrackerWindow), DispatcherPriority.Background);
            }
        }

        private void RefreshExpTrackerWindow()
        {
            // 트레이로 최소화된 동안에는 경험치 갱신이 창을 다시 띄우지 않게 한다
            if (TrayAllWindowsService.IsTrayed)
                return;

            // 잠금 해제, 또는 추가 기능 > 경험치 추적 > 일반 탭에서만 미리보기로 표시한다
            bool previewMode = UiLockService.IsUnlocked ||
                               (_isAddonPositionMode && _addonPositionPreviewTabIndex == 10);

            if (_settings.ShowExpTracker && (previewMode || _expService.IsTrackerActive))
            {
                ShowExpTrackerWindow();
            }
            else
            {
                CloseExpTrackerWindow();
            }
        }

        private void ShowExpTrackerWindow()
        {
            if (_expTrackerWindow == null || !_expTrackerWindow.IsLoaded)
            {
                _expTrackerWindow = new ExpTrackerWindow(_expTrackerViewModel)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual
                };
                if (IsLoaded)
                    _expTrackerWindow.Owner = this;
                if (_expTrackerViewModel != null)
                    _expTrackerWindow.DataContext = _expTrackerViewModel;
                _expTrackerWindow.Closed += (_, _) => _expTrackerWindow = null;
            }

            _expTrackerWindow.ApplyStoredPosition(_settings.ExpTrackerWindowLeft, _settings.ExpTrackerWindowTop, _settings.ExpTrackerWindowRight);

            if (!_expTrackerWindow.IsVisible)
                _expTrackerWindow.Show();
        }

        private void CloseExpTrackerWindow()
        {
            try { _expTrackerWindow?.Close(); } catch { }
            _expTrackerWindow = null;
        }

        private void BuffTrackerService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BuffTrackerService.HasAnyActiveBuffs))
                return;

            Dispatcher.BeginInvoke(new Action(ApplyBuffTrackerWindowSettings), DispatcherPriority.Background);
        }

        private void OnColorsUpdatedFromSettings(string _)
        {
            _logTabBufferStore.UpdateAllBrushes(log => ChatBrushResolver.Resolve(_settings, log.Category, log.IsClubBossMessage));
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
                        case HotKeyService.TOGGLE_SETTINGS_ID:
                            TriggerMenuButton("BtnSettings");
                            break;
                        case HotKeyService.TOGGLE_DAILY_WEEKLY_CONTENT_ID:
                            TriggerMenuButton("BtnDailyWeekly");
                            break;
                        case HotKeyService.TOGGLE_TRAY_ALL_ID:
                            TrayAllWindowsService.Toggle();
                            break;
                        case HotKeyService.TOGGLE_UNLOCK_ID:
                            UiLockService.Toggle();
                            break;
                    }
                };

                _stickyService = new WindowStickyService(this, _settings);
                _stickyService.AuxiliaryWindowVisibilityChanged += StickyService_AuxiliaryWindowVisibilityChanged;
                TrayAllWindowsService.TrayStateChanged += _ => _stickyService?.UpdatePositionImmediately();
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

                ApplyMainTabState(_currentTabTag, persistSettings: false, refreshLogDisplay: false);

                Dispatcher.BeginInvoke(new Action(CompleteInitialPresentation), DispatcherPriority.ApplicationIdle);

                AppLogger.Info("Native services initialized successfully.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("서비스 시작 중 오류.", ex);
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
                UiLockService.ApplyStoredOpacity(this); // 창별 지정 투명도가 있으면 그 값으로
                IsHitTestVisible = true;
                Visibility = Visibility.Visible;
                _stickyService?.UpdatePositionImmediately();
                EnsureMenuWindowVisible();
                RestoreSavedChatCloneWindows();
            }

        }

        /// <summary>설정 창의 '설정 마법사' 항목에서 마법사를 다시 실행한다.</summary>
        public void ShowSetupWizardOnDemand()
        {
            _pendingInitialSetupWizard = true;
            TryShowInitialSetupWizardIfNeeded();
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
            RestoreSavedChatCloneWindows();
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
                // 1단계(전경): 최근 1주일 로그만 즉시 처리해 빠르게 시작한다.
                // 그보다 과거 로그는 시작 완료 후 백그라운드에서 이어서 처리한다.
                DateTime recentCutoff = DateTime.Today.AddDays(-7);
                UpdateStartupLoadingProgress(35, "최근 로그를 읽는 중입니다.");
                ReadableLogArchiveService.LogArchiveInitializationResult archiveResult = await Task.Run(async () =>
                {
                    // 4.x → 5.x 업그레이드: 구버전 Logs 폴더를 1회 삭제하고 아래에서 새로 재구축한다
                    _readableLogArchiveService.ResetLogsFolderForV5IfNeeded();

                    Func<DateTime, bool> dateFilter = onlyToday
                        ? (d => d.Date == DateTime.Today)
                        : (d => d.Date >= recentCutoff);
                    ReadableLogArchiveService.LogArchiveInitializationResult result = await _readableLogArchiveService.EnsureInitializedFromRawLogsAsync(
                        _settings.ChatLogFolderPath,
                        _logAnalysisService,
                        IsContentCompletionRelevantLog,
                        (dateText, current, total) =>
                        {
                            double ratio = total <= 0 ? 0 : (double)current / total;
                            double progress = 35 + (ratio * 50.0);
                            UpdateStartupLoadingProgress(progress, "최근 로그를 읽는 중입니다.", dateText);
                        },
                        dateFilter,
                        cancellationToken,
                        updateCheckpoint: false).ConfigureAwait(false);

                    _readableLogArchiveService.MigrateContentArchiveIfNeeded();
                    return result;
                }, cancellationToken).ConfigureAwait(false);

                _AbandonWeeklySummary = _readableLogArchiveService.LoadAbandonWeeklySummary(DateTime.Today);
                _AbandonWeeklySummaryWeekKey = GetIsoWeekKey(DateTime.Today);
                _settings.StartupLogReadCanceled = false;
                ConfigService.SaveDeferred(_settings);

                if (archiveResult.HasTimedOutFiles)
                {
                    UpdateStartupLoadingProgress(85, "일부 로그 파일이 1분 이상 멈춰서 다음 파일로 넘어갔습니다.");
                    await ShowLogReadTimeoutWarningAsync(archiveResult).ConfigureAwait(false);
                }
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

                await Dispatcher.InvokeAsync(() => RequestRefreshLogDisplay(), DispatcherPriority.Background);
            }

            UpdateStartupLoadingProgress(100, "초기화가 완료되었습니다.");
            CloseStartupLoadingWindow();

            // 2단계(백그라운드): 1주일 이전 과거 로그를 조용히 이어서 아카이브한다.
            StartBackgroundLogBackfill();
        }

        private bool _backgroundLogBackfillStarted;

        /// <summary>1주일 이전 과거 로그를 백그라운드에서 아카이브한다. (시작 시 최근 로그만 전경 처리)</summary>
        private void StartBackgroundLogBackfill()
        {
            if (_backgroundLogBackfillStarted)
                return;
            _backgroundLogBackfillStarted = true;

            _ = Task.Run(async () =>
            {
                try
                {
                    AppLogger.Info("Background log backfill started (older than 7 days).");
                    await _readableLogArchiveService.EnsureInitializedFromRawLogsAsync(
                        _settings.ChatLogFolderPath,
                        _logAnalysisService,
                        IsContentCompletionRelevantLog,
                        onProgressText: null,
                        sourceDateFilter: null,
                        cancellationToken: CancellationToken.None,
                        updateCheckpoint: true).ConfigureAwait(false);
                    AppLogger.Info("Background log backfill completed.");

                    // 과거 데이터가 채워졌으니 어밴던 주간 합계 등을 최신 상태로 갱신
                    await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            _AbandonWeeklySummary = _readableLogArchiveService.LoadAbandonWeeklySummary(DateTime.Today);
                            _AbandonWeeklySummaryWeekKey = GetIsoWeekKey(DateTime.Today);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("Failed to refresh abandon summary after backfill.", ex);
                        }
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Background log backfill failed.", ex);
                }
            });
        }

        private async Task InitializeStartupDataAsync()
        {
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                UpdateStartupLoadingProgress(10, "업데이트를 확인하는 중입니다.");
                AppLogger.Info("Startup data initialization: beginning update check.");
                var updateResult = await AppServices.Get<IUpdateService>().CheckForUpdateAsync(forceInstallLatest: false, showNoUpdateMessage: false);
                AppLogger.Info($"Startup data initialization: update check completed with result={updateResult}.");
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
#if DEBUG
                    ChatLatencyHud.EnsureVisible(); // 디버그: 지연 HUD를 시작부터 표시
#endif
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
            _startupLoadingWindow.UpdateProgress(5, "초기화 진행 중...");
            if (LogDisplay != null)
            {
                LogDisplay.BeginChange();
                try
                {
                    LogDisplay.Document.Blocks.Clear();
                }
                finally
                {
                    LogDisplay.EndChange();
                    LogDisplay.UpdateLayout();
                }
            }
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

        private Task ShowLogReadTimeoutWarningAsync(ReadableLogArchiveService.LogArchiveInitializationResult result)
        {
            if (!result.HasTimedOutFiles)
                return Task.CompletedTask;

            string firstFileName = Path.GetFileName(result.TimedOutFiles[0]);
            string message;
            if (result.TimedOutFiles.Count == 1)
            {
                message = $"일부 로그 파일 읽기가 1분 이상 걸려 '{firstFileName}' 파일을 건너뛰고 다음 단계로 진행했습니다.";
            }
            else
            {
                string sampleFiles = string.Join(Environment.NewLine, result.TimedOutFiles.Take(3).Select(Path.GetFileName));
                if (result.TimedOutFiles.Count > 3)
                    sampleFiles += $"{Environment.NewLine}... 외 {result.TimedOutFiles.Count - 3}개";

                message = $"일부 로그 파일 읽기가 1분 이상 걸려 {result.TimedOutFiles.Count}개 파일을 건너뛰고 다음 단계로 진행했습니다.{Environment.NewLine}{Environment.NewLine}{sampleFiles}";
            }

            return Dispatcher.InvokeAsync(() =>
            {
                MessageBox.Show(this, message, "로그 읽기", MessageBoxButton.OK, MessageBoxImage.Warning);
            }).Task;
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
                ChatWindowHub.BeginShutdown();
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

        internal void RequestTopmostRefresh()
        {
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;

            try
            {
                Dispatcher.BeginInvoke(new Action(EnsureMainWindowTopmost), DispatcherPriority.Background);
            }
            catch
            {
            }
        }

        private void EnsureMainWindowTopmost()
        {
            if (!IsVisible || WindowState == WindowState.Minimized)
                return;

            try
            {
                TopmostWindowHelper.EnsureTopmost(this);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to reassert main window topmost state.", ex);
            }
        }


    }
}
