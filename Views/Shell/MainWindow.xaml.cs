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
        private ExpTrackerWindow? _expTrackerWindow;
        private ExpTrackerViewModel? _expTrackerViewModel;
        private ExperienceService _expService;
        private ExpHuntSessionService _huntSessionService;
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

        private string _currentTabTag = "Basic";
        private LogAnalysisPipeline? _logAnalysisPipeline;
        private readonly LogTabBufferStore _logTabBufferStore;
        private readonly TabDisplayStateResolver _tabDisplayStateResolver;

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
            _settingsFileMissingOnStartup = AppServices.Get<StartupState>().SettingsFileMissing;
            _pendingInitialSetupWizard = _settingsFileMissingOnStartup;
            _logTabBufferStore = AppServices.Get<ChatWindowHub>().SharedLogBuffers;
            _tabDisplayStateResolver = new TabDisplayStateResolver();
            _mainTabAutoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _mainTabAutoHideTimer.Tick += (_, _) => HideMainTabs();

            _settings = AppServices.Get<ChatSettings>();
            AppServices.Get<UiLockService>().SnapEnabled = _settings.WindowSnapEnabled;
            AppServices.Get<IOverlayOpacityService>().Initialize();
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
            _logAnalysisService = AppServices.Get<LogAnalysisService>();
            _logPipelineCoordinator = AppServices.Get<MainLogPipelineCoordinator>();
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

                    // 클리어 보상 시드는 줄이 들어올 때 바로 기록한다 (통계 창은 보관본만 읽는다)
                    AppServices.Get<WeeklySeedRewardService>().ObserveLiveLine(evt.Source.SourcePath, evt.Html, evt.Source.CheckpointPosition);

                    var primary = evt.Analysis.Primary;
                    if (!primary.IsSuccess)
                        return;

                    _readableLogArchiveService?.AppendFromAnalysis(
                        DateTime.Today,
                        primary,
                        IsContentCompletionRelevantLog(primary.Parsed.FormattedText));
                });
            _settingsViewModel = new SettingsViewModel(_settings, OnColorsUpdatedFromSettings, ConfirmExit, OnSettingsResetFromSettings, ApplyHotKeys, ExecuteManualLogReloadFromSettingsAsync, OnSettingsReplacedFromSettings);

            _expService = AppServices.Get<ExperienceService>();
            _huntSessionService = AppServices.Get<ExpHuntSessionService>();
            _expTrackerViewModel = new ExpTrackerViewModel(_expService, _settings);
            _expService.SessionState.PropertyChanged += ExpSessionState_PropertyChanged;
            _expService.TrackerActiveChanged += () => Dispatcher.BeginInvoke(new Action(RefreshExpTrackerWindow), DispatcherPriority.Background);
            _expTrackerViewModel.UpdateDisplay();
            _experienceEssenceAlertService = AppServices.Get<ExperienceEssenceAlertService>();
            AppServices.Get<ExperienceAlertWindowService>().ConfigureStateBridge(
                () => _experienceEssenceAlertService.GetStateSnapshot(),
                snapshot => _experienceEssenceAlertService.ApplyStateSnapshot(snapshot));
            _dungeonCountDisplayService = AppServices.Get<DungeonCountDisplayService>();
            _readableLogArchiveService = AppServices.Get<ReadableLogArchiveService>();
            _messengerLogWatcherService = AppServices.Get<MessengerLogWatcherService>();
            _messengerLogWatcherService.Start();
            _buffTrackerService = AppServices.Get<BuffTrackerService>();
            _buffTrackerService.PropertyChanged += BuffTrackerService_PropertyChanged;
            _logService = AppServices.Get<LogService>();
            TryLoadTestDropItemJsonForSession();
            AppServices.Get<DropItemResolver>().InitializeAsync(_settings);
            _logService.OnNewLogRead += (logItem) => _logAnalysisPipeline?.Enqueue(logItem);
            _logService.InitialLogsLoaded += () =>
            {
                Dispatcher.BeginInvoke(new Action(() => RequestRefreshLogDisplay()), DispatcherPriority.ApplicationIdle);
                // 시작·날짜 전환 시점: 앱이 못 본 구간(꺼져 있던 동안, 오늘 켜기 전)의 시드 줄을 한 번 보충한다.
                // LogService가 실시간 읽기 시작 위치를 정한 뒤에 돌아야 그 사이 줄이 빠지지 않는다.
                _ = AppServices.Get<WeeklySeedRewardService>().CatchUpAsync(_settings.ChatLogFolderPath);
            };
            AppServices.Get<BlacklistService>().BlacklistChanged += () =>
            {
                Dispatcher.BeginInvoke(new Action(() => RequestRefreshLogDisplay()), DispatcherPriority.Background);
            };
            AppServices.Get<IIdTagService>().IdTagsChanged += () =>
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
            this.Closing += (_, _) => AppServices.Get<ChatWindowHub>().BeginShutdown();
            this.Closed += MainWindow_Closed;
            AppServices.Get<UiLockService>().UnlockChanged += OnUiUnlockChanged;
            AppServices.Get<UiLockService>().WindowAdjusted += OnUnlockWindowAdjusted;
            AppLogger.Info("Main window initialized.");

            ShowStartupLoadingWindow();
            Dispatcher.BeginInvoke(
                new Action(() => _ = InitializeStartupDataAsync()),
                DispatcherPriority.ApplicationIdle);
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            
            try { AppServices.Get<UiLockService>().UnlockChanged -= OnUiUnlockChanged; } catch { }
            try { AppServices.Get<UiLockService>().WindowAdjusted -= OnUnlockWindowAdjusted; } catch { }
            try { _mainTabAutoHideTimer.Stop(); } catch { }
            try { _logAnalysisPipeline?.Dispose(); } catch { }
            try
            {
                _settings.MainWindowChatTabTag = _currentTabTag;
            }
            catch { }
            try { AppServices.Get<ChatWindowHub>().BeginShutdown(); } catch { }
            try { AppServices.Get<ExperienceAlertWindowService>().SaveCurrentPosition(_settings); } catch { }
            try { AppServices.Get<DungeonCountDisplayWindowService>().SaveCurrentPosition(_settings); } catch { }
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
            if (AppServices.Get<TrayAllWindowsService>().IsTrayed)
                return;

            // 잠금 해제, 또는 추가 기능 > 경험치 추적 > 일반 탭에서만 미리보기로 표시한다
            bool previewMode = AppServices.Get<UiLockService>().IsUnlocked ||
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
            _logTabBufferStore.UpdateAllBrushes(log => ChatBrushResolver.Resolve(_settings, log));
            AppServices.Get<ChatWindowHub>().NotifyBuffersChanged();

            RequestRefreshLogDisplay();
        }

        /// <summary>설정이 통째로 바뀐 뒤(프로필 불러오기/파일 불러오기) 화면·창 위치·핫키를 다시 맞춘다. 마법사는 띄우지 않는다.</summary>
        private void OnSettingsReplacedFromSettings()
        {
            ApplyInitialSettings();
            ReapplyStoredWindowPositions();
            RequestRefreshLogDisplay();
            try { ApplyHotKeys(); }
            catch (Exception ex) { AppLogger.Warn("Failed to reapply hotkeys after settings replacement.", ex); }

            try
            {
                foreach (var sub in Application.Current.Windows.OfType<SubMenuWindow>().ToList())
                {
                    try { sub.Hide(); } catch { }
                }
            }
            catch { }
        }

        /// <summary>설정 초기화 뒤: 화면을 다시 맞추고 설정 마법사를 다시 띄운다.</summary>
        private void OnSettingsResetFromSettings()
        {
            OnSettingsReplacedFromSettings();

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
                            AppServices.Get<TrayAllWindowsService>().Toggle();
                            break;
                        case HotKeyService.TOGGLE_UNLOCK_ID:
                            AppServices.Get<UiLockService>().Toggle();
                            break;
                    }
                };

                _stickyService = new WindowStickyService(this, _settings);
                _stickyService.AuxiliaryWindowVisibilityChanged += StickyService_AuxiliaryWindowVisibilityChanged;
                AppServices.Get<TrayAllWindowsService>().TrayStateChanged += trayed =>
                {
                    _stickyService?.UpdatePositionImmediately();

                    // 트레이로 숨은 동안에는 버프 창이 스스로 표시를 바꾸지 않으므로,
                    // 복원 시점에 지금 버프 상태로 다시 판단한다 (숨을 때 닫혀 있었거나 그 사이 버프가 바뀐 경우).
                    if (!trayed)
                        Dispatcher.BeginInvoke(new Action(ApplyBuffTrackerWindowSettings), DispatcherPriority.Background);
                };
                _stickyService.Start();
                _stickyService.UpdatePositionImmediately();
                _bossAlarmSchedulerService = new BossAlarmSchedulerService(_settings);
                _bossAlarmSchedulerService.Start();
                _expService.Reset();
                _expService.Start();
                _huntSessionService.Start();
                // 지난 로그의 사냥 판은 처음 한 번만 훑는다 (이후에는 새 날짜만)
                _ = _huntSessionService.BackfillAsync(_settings.ChatLogFolderPath);
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

        private void StickyService_AuxiliaryWindowVisibilityChanged(bool canShow)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _canShowAuxiliaryWindows = canShow;
                ApplyBuffTrackerWindowSettings();
                ApplyAbandonRoadSummaryWindowVisibility();
            }), DispatcherPriority.Background);
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
