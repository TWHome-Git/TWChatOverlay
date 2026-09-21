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
                    ContentTimerService.EnsureVisibleForDebug(_settings); // 디버그: 던전 타이머 창을 항상 표시
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

                // 던전 타이머: 최근 며칠치 로그 파일에서 지난 판 기록을 복원한다 (백그라운드, 실패해도 시작에 영향 없음)
                _ = ContentTimerService.BackfillFromLogsAsync(_settings.ChatLogFolderPath);
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
    }
}
