using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>시작 시퀀스: 로딩 창, 설정 마법사, 로그 서비스 초기화·백필, 마법사 후 재시작.</summary>
    public partial class MainWindow
    {
        // ── 시작 상태 ──
        private bool _hasCompletedInitialPresentation;
        private bool _isLogServiceInitialized;
        private bool _startLogServiceWhenInitialized;
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
            _ = Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (_isLogServiceInitialized || _startupLogInitRunning)
                    return;

                await _readableLogArchiveService.ClearArchiveLogsAndResetCheckpointAsync(selectedPath).ConfigureAwait(true);
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

                if (!_hasRestoredMemoWindow)
                {
                    _hasRestoredMemoWindow = true;
                    menu.RestoreMemoIfWasOpen();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to show menu window after startup.", ex);
            }
        }

        /// <param name="silent">true면 로딩 창 대신 메뉴 아이콘 아래 표시로 진행을 보인다 (로그 다시 읽기).</param>
        private async Task InitializeLogServiceAfterEtaProfilesAsync(bool onlyToday, CancellationToken cancellationToken, bool silent = false)
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
                if (silent)
                    BeginLogReadProgress();
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
                            if (silent)
                                ReportLogReadProgress(dateText, current, total);
                        },
                        dateFilter,
                        cancellationToken,
                        updateCheckpoint: false,
                        extraConsumers: GetStartupScanConsumers()).ConfigureAwait(false);

                    // 4.x → 5.x: 컨텐츠 아카이브의 경험치 줄 제거(1회)
                    _readableLogArchiveService.MigrateContentArchiveIfNeeded();
                    return result;
                }, cancellationToken).ConfigureAwait(false);

                // 컨텐츠 아카이브가 방금 (재)구축됐을 수 있으니 열려 있는 일일/주간 창은 다시 센다
                try { _ = Dispatcher.InvokeAsync(() => { _ = _dailyWeeklyContentOverlay?.ScanHistoricalLogsAsync(); }); } catch { }
                _AbandonWeeklySummary = _readableLogArchiveService.LoadAbandonWeeklySummary(DateTime.Today);
                _AbandonWeeklySummaryWeekKey = LogTextClassifier.GetIsoWeekKey(DateTime.Today);
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

        // 배경 패스가 도는 중인지 / 도는 중에 다시 요청됐는지(로그 다시 읽기). 예전에는 '한 번 시작됨' 플래그라
        // 로그 다시 읽기가 아카이브를 비운 뒤 전경(최근 1주일) 패스만 돌아 그 이전 기록이 모두 빠졌다.
        private bool _backgroundLogBackfillRunning;
        private bool _backgroundLogBackfillPending;
        private IReadOnlyList<IRawLogScanConsumer>? _startupScanConsumers;

        /// <summary>
        /// 시작 시 원본 로그 패스에 함께 올라타는 소비자들 — 사냥 기록·던전 타이머·시드·보물창고.
        /// 전경(최근분)과 배경(나머지) 두 패스가 같은 인스턴스를 쓰므로 "어디까지 읽었나"가 두 패스에 걸쳐 이어진다.
        /// 예전에는 각 서비스가 같은 파일을 따로 열어 읽었다.
        /// </summary>
        private IReadOnlyList<IRawLogScanConsumer> GetStartupScanConsumers()
        {
            if (_startupScanConsumers != null)
                return _startupScanConsumers;

            string folder = _settings.ChatLogFolderPath;
            _startupScanConsumers = new IRawLogScanConsumer[]
            {
                // 지난 로그의 사냥 판은 처음 한 번만 훑는다 (이후에는 새 날짜만)
                _huntSessionService.CreateScanConsumer(folder),
                // 던전 타이머: 지난주 월요일부터의 판 복원 + 오늘 파일로 실시간 추적기 복원 (최초 1회는 전체)
                AppServices.Get<ContentTimerService>().CreateScanConsumer(folder),
                // 클리어 보상 시드: 최근 14일 중 앱이 못 본 지난 날짜 (오늘은 LogService 시작 뒤 CatchUpAsync가 읽는다)
                AppServices.Get<WeeklySeedRewardService>().CreateScanConsumer(),
                // 보물창고: 이번 주 중 보관본에 없는 날짜
                AppServices.Get<TreasuryHistoryService>().CreateScanConsumer(),
            };
            return _startupScanConsumers;
        }

        private bool _startupLogTailFed;

        /// <summary>
        /// 시작 패스가 오늘 파일을 읽은 끝과 LogService 실시간 시작 위치 사이에 쌓인 줄을 한 번 이어 넣는다.
        /// LogService.Initialize 안(InitialLogsLoaded, Start 전)에서 동기로 돌아 실시간 줄보다 먼저 들어간다.
        /// </summary>
        private void FeedStartupLogTailOnce()
        {
            if (_startupLogTailFed || _startupScanConsumers == null || _logService == null)
                return;
            _startupLogTailFed = true;

            try
            {
                RawLogScanService.FeedTail(_startupScanConsumers, _logService.CurrentLogPath, _logService.RealtimeStartPosition);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to feed the startup log tail to scan consumers.", ex);
            }
        }

        /// <summary>
        /// 1주일 이전 과거 로그를 백그라운드에서 아카이브한다. (시작 시 최근 로그만 전경 처리)
        /// 시작과 로그 다시 읽기 때마다 부른다. 이미 도는 중이면 끝난 뒤 한 번 더 돈다.
        /// </summary>
        private void StartBackgroundLogBackfill()
        {
            lock (_backgroundLogBackfillLock)
            {
                if (_backgroundLogBackfillRunning)
                {
                    _backgroundLogBackfillPending = true;
                    return;
                }
                _backgroundLogBackfillRunning = true;
                _backgroundLogBackfillCts?.Dispose();
                _backgroundLogBackfillCts = new CancellationTokenSource();
            }

            CancellationToken token = _backgroundLogBackfillCts.Token;
            _backgroundLogBackfillTask = Task.Run(async () =>
            {
                try
                {
                    AppLogger.Info("Background log backfill started (older than 7 days).");
                    BeginLogReadProgress();
                    await _readableLogArchiveService.EnsureInitializedFromRawLogsAsync(
                        _settings.ChatLogFolderPath,
                        _logAnalysisService,
                        IsContentCompletionRelevantLog,
                        onProgressText: ReportLogReadProgress,
                        sourceDateFilter: null,
                        cancellationToken: token,
                        updateCheckpoint: true,
                        extraConsumers: GetStartupScanConsumers()).ConfigureAwait(false);
                    AppLogger.Info("Background log backfill completed.");

                    // 과거 데이터가 채워졌으니 어밴던 주간 합계 등을 최신 상태로 갱신
                    await Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            // 컨텐츠 아카이브가 방금 (재)구축됐을 수 있으니 열려 있는 일일/주간 창은 다시 센다
                try { _ = Dispatcher.InvokeAsync(() => { _ = _dailyWeeklyContentOverlay?.ScanHistoricalLogsAsync(); }); } catch { }
                _AbandonWeeklySummary = _readableLogArchiveService.LoadAbandonWeeklySummary(DateTime.Today);
                            _AbandonWeeklySummaryWeekKey = LogTextClassifier.GetIsoWeekKey(DateTime.Today);
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("Failed to refresh abandon summary after backfill.", ex);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    AppLogger.Info("Background log backfill canceled.");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Background log backfill failed.", ex);
                }
                finally
                {
                    bool rerun;
                    lock (_backgroundLogBackfillLock)
                    {
                        _backgroundLogBackfillRunning = false;
                        rerun = _backgroundLogBackfillPending;
                        _backgroundLogBackfillPending = false;
                    }
                    if (rerun)
                        StartBackgroundLogBackfill();
                    else
                        EndLogReadProgress();
                }
            });
        }

        private readonly object _backgroundLogBackfillLock = new();

        // ===== 뒤에서 로그 읽는 표시 (메뉴 아이콘 아래) =====
        // 할 일이 없는 패스(체크포인트가 최신)도 파일 목록을 한 바퀴 돌며 진행을 알리므로, 잠깐 넘게 걸릴 때만 보인다.
        private static readonly TimeSpan LogReadIndicatorDelay = TimeSpan.FromMilliseconds(700);
        private readonly object _logReadProgressLock = new();
        private DateTime _logReadStartedAt;
        private (string Date, int Current, int Total)? _pendingLogReadProgress;
        private bool _logReadProgressPosted;
        private bool _logReadIndicatorActive;

        private void BeginLogReadProgress()
        {
            lock (_logReadProgressLock)
            {
                _logReadStartedAt = DateTime.UtcNow;
                _logReadIndicatorActive = true;
            }
        }

        /// <summary>스캔 진행 콜백(백그라운드 스레드). 최신 값만 남겨 UI에는 한 번에 하나씩 올린다.</summary>
        private void ReportLogReadProgress(string dateText, int current, int total)
        {
            lock (_logReadProgressLock)
            {
                if (!_logReadIndicatorActive || DateTime.UtcNow - _logReadStartedAt < LogReadIndicatorDelay)
                    return;
                string date = dateText.Length >= 10 ? dateText[..10] : dateText;
                _pendingLogReadProgress = (date, current, total);
                if (_logReadProgressPosted)
                    return;
                _logReadProgressPosted = true;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                (string Date, int Current, int Total)? progress;
                lock (_logReadProgressLock)
                {
                    progress = _logReadIndicatorActive ? _pendingLogReadProgress : null;
                    _logReadProgressPosted = false;
                }
                if (progress is { } p)
                    Application.Current.Windows.OfType<MenuWindow>().FirstOrDefault()?.SetLogReadProgress(p.Current, p.Total, p.Date);
            }), DispatcherPriority.Background);
        }

        private void EndLogReadProgress()
        {
            lock (_logReadProgressLock)
            {
                _logReadIndicatorActive = false;
                _pendingLogReadProgress = null;
            }

            Dispatcher.BeginInvoke(new Action(() =>
                Application.Current.Windows.OfType<MenuWindow>().FirstOrDefault()?.SetLogReadProgress(0, 0, string.Empty)));
        }
        private CancellationTokenSource? _backgroundLogBackfillCts;
        private Task _backgroundLogBackfillTask = Task.CompletedTask;

        /// <summary>
        /// 도는 중인 배경 패스를 멈추고 끝날 때까지 기다린다. 로그 다시 읽기가 아카이브를 비우기 전에 부른다 —
        /// 배경 패스가 아카이브 잠금을 쥔 채 과거 로그 전체를 읽는 동안 다시 읽기의 최근 로그 단계가 그 끝을 기다리며 멈춰 있었다.
        /// 멈춘 패스는 다시 읽기가 끝날 때 처음부터 다시 돈다.
        /// </summary>
        private async Task CancelBackgroundLogBackfillAsync()
        {
            Task running;
            lock (_backgroundLogBackfillLock)
            {
                if (!_backgroundLogBackfillRunning)
                    return;
                _backgroundLogBackfillPending = false; // 다시 읽기 끝에서 새로 시작한다
                _backgroundLogBackfillCts?.Cancel();
                running = _backgroundLogBackfillTask;
            }

            try { await running.ConfigureAwait(true); } catch { }
        }

        private async Task InitializeStartupDataAsync()
        {
            try
            {
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                UpdateStartupLoadingProgress(10, "업데이트를 확인하는 중입니다.");
                AppLogger.Info("Startup data initialization: beginning update check.");
                // 업데이트 확인(네트워크, 최대 8초)은 창 초기화를 막지 않도록 병렬로 돌리고, 로그 초기화 직전에만 결과를 기다린다
                Task<UpdateCheckResult> updateTask = AppServices.Get<IUpdateService>().CheckForUpdateAsync(forceInstallLatest: false, showNoUpdateMessage: false);

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
                    AppServices.Get<ContentTimerService>().EnsureVisibleForDebug(_settings); // 디버그: 던전 타이머 창을 항상 표시
#endif
                }, DispatcherPriority.Background);

                var updateResult = await updateTask;
                AppLogger.Info($"Startup data initialization: update check completed with result={updateResult}.");
                if (updateResult == UpdateCheckResult.UpdateApplied)
                {
                    return;
                }

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

        private async Task RunDeferredLogInitializationAsync(bool isFirstRun, bool force = false, bool silent = false)
        {
            if (_startupLogInitRunning || (_isLogServiceInitialized && !force))
                return;

            _startupLogInitRunning = true;
            _startupLogInitCts?.Dispose();
            _startupLogInitCts = new CancellationTokenSource();

            if (!silent)
                ShowStartupLoadingWindow();
            UpdateStartupLoadingProgress(12, "로그 초기화를 준비하는 중입니다.");

            try
            {
                bool onlyToday = isFirstRun && !_settings.StartupLogReadCanceled;
                await InitializeLogServiceAfterEtaProfilesAsync(onlyToday, _startupLogInitCts.Token, silent).ConfigureAwait(false);
                if (onlyToday)
                {
                    _settings.StartupTodayOnlyBootstrapCompleted = true;
                    ConfigService.SaveDeferred(_settings);
                }
                // 던전 타이머·사냥 기록·시드·보물창고의 과거 로그 복원은 위 아카이브 패스(전경·배경)에 함께 올라탄다 — 파일마다 한 번만 읽는다
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
                AppServices.Get<ChatWindowHub>().BeginShutdown();
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

                await CancelBackgroundLogBackfillAsync().ConfigureAwait(true);
                await _readableLogArchiveService.ClearArchiveLogsAndResetCheckpointAsync(_settings.ChatLogFolderPath).ConfigureAwait(true);
                // 로딩 창 없이 다시 읽는다 — 진행은 메뉴 아이콘 아래 표시(최근 1주일 → 이어서 지난 로그 전체)
                await RunDeferredLogInitializationAsync(isFirstRun: false, force: true, silent: true).ConfigureAwait(true);
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
                AppServices.Get<ShoutToastService>().GetOrCreatePreviewWindow(_settings);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Display prewarm failed.", ex);
            }
        }
    }
}
