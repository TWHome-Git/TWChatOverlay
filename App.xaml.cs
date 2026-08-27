using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TWChatOverlay.Services;

namespace TWChatOverlay
{
    /// <summary>
    /// 애플리케이션 시작/종료 라이프사이클을 관리합니다.
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            AppLogger.Info("Application startup initiated.");

            // 렌더링 모드: 기본은 소프트웨어. 작은 오버레이 창들이라 GPU 가속의 이점이 없는 반면,
            // 하드웨어 경로는 D3D 드라이버가 창마다 잡는 네이티브 메모리가 커서(측정상 Private Bytes 약 -130MB)
            // 소프트웨어 렌더링을 기본으로 한다. 되돌리려면 --hardware-render 인자 또는 TWCHAT_HARDWARE_RENDER=1.
            bool hardwareRender = Array.Exists(e.Args, a => string.Equals(a, "--hardware-render", StringComparison.OrdinalIgnoreCase))
                || string.Equals(Environment.GetEnvironmentVariable("TWCHAT_HARDWARE_RENDER"), "1", StringComparison.Ordinal);
            if (!hardwareRender)
            {
                System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                AppLogger.Info("Render mode: SoftwareOnly (default).");
            }

            _mutex = new Mutex(true, "TWChatOverlay_SingleInstance", out bool isNewInstance);

            if (!isNewInstance)
            {
                AppLogger.Warn("Startup cancelled because another instance is already running.");
                MessageBox.Show("TWChatOverlay가 이미 실행 중입니다.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Current.Shutdown();
                return;
            }

            DispatcherUnhandledException += (s, ex) =>
            {
                AppLogger.Fatal("Unhandled dispatcher exception.", ex.Exception, "DispatcherUnhandledException");
                ex.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                if (ex.ExceptionObject is Exception exception)
                {
                    AppLogger.Fatal("Unhandled AppDomain exception.", exception, "UnhandledException");
                }
                else
                {
                    AppLogger.Fatal($"Unhandled AppDomain exception object: {ex.ExceptionObject}", "UnhandledException");
                }
            };

            TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                AppLogger.Error("Unobserved task exception.", ex.Exception, "UnobservedTaskException");
                ex.SetObserved();
            };

            // 설정 폴더에 쓰기 권한이 없으면(예: Program Files 설치) 설정이 조용히 유실되므로 미리 알린다
            if (!ConfigService.VerifyWritable(out string? writeError))
            {
                AppLogger.Warn($"Settings folder is not writable: {writeError}");
                MessageBox.Show(
                    $"프로그램 폴더에 설정을 저장할 수 없습니다.\n\n원인: {writeError}\n\n" +
                    "이대로 사용하면 설정이 저장되지 않습니다. 폴더를 문서/바탕화면 등 쓰기 가능한 위치로 옮기거나, 관리자 권한으로 실행해 주세요.",
                    "설정 저장 불가",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            AppServices.Initialize();
            EtaProfileResolver.InitializeAsync();
            BlacklistService.Initialize();
            IdTagService.Initialize();
            _ = RecaptureSupplyAlertService.PreloadAsync();
            SecondaryWindowTopmostRefreshService.Initialize();
            ForegroundTopmostGuard.Initialize();
            base.OnStartup(e);
            AppLogger.Info("Core services initialized.");

            try
            {
                if (Views.SubAddonWindow.Instance == null)
                {
                    var helper = new Views.SubAddonWindow();
                    helper.Left = SystemParameters.WorkArea.Width - helper.Width - 10;
                    helper.Top = 10;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to create helper window.", ex);
            }

            Views.MainWindow? main = null;
            try
            {
                foreach (Window w in Current.Windows)
                {
                    if (w is Views.MainWindow existingMain)
                    {
                        main = existingMain;
                        break;
                    }
                }

                if (main == null)
                {
                    main = new Views.MainWindow();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to create main window.", ex);
            }


#if DEBUG
            try
            {
                Views.DebugLogTestWindow.ShowOrActivate();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to create debug log test window.", ex);
            }
#endif

            
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AppLogger.Info("Application shutdown initiated.");
            try
            {
                Models.ChatSettings? cfg = null;

                foreach (Window w in Current.Windows)
                {
                    if (w is Views.MainWindow main && main.DataContext is Models.ChatSettings sharedSettings)
                    {
                        cfg = sharedSettings;
                        break;
                    }
                }

                if (cfg == null)
                {
                    try
                    {
                        cfg = TWChatOverlay.Services.ConfigService.Load();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("Failed to reload persisted settings during shutdown. Using in-memory defaults as last resort.", ex);
                        cfg = new Models.ChatSettings();
                    }
                }

                foreach (Window w in Current.Windows)
                {
                    if (w is Views.MenuWindow menu)
                    {
                        cfg.MenuWindowLeft = menu.Left;
                        cfg.MenuWindowTop = menu.Top;
                    }
                    else if (w is Views.SubMenuWindow sub)
                    {
                        cfg.SubMenuWindowLeft = sub.Left;
                        cfg.SubMenuWindowTop = sub.Top;
                    }
                    else if (w is Views.DailyWeeklyContentWindow dw)
                    {
                        cfg.DailyWeeklyContentOverlayLeft = dw.Left;
                        cfg.DailyWeeklyContentOverlayTop = dw.Top;
                    }
                    else if (w is Views.ItemCalendarWindow itemCalendar)
                    {
                        cfg.ItemCalendarWindowLeft = itemCalendar.Left;
                        cfg.ItemCalendarWindowTop = itemCalendar.Top;
                    }
                    else if (w is Views.AbandonRoadSummaryWindow Abandon)
                    {
                        cfg.AbandonRoadSummaryWindowLeft = Abandon.Left;
                        cfg.AbandonRoadSummaryWindowTop = Abandon.Top;
                    }
                    else if (w is Views.ExpTrackerWindow exp)
                    {
                        // ExpTracker는 스스로 SaveDeferred 하지만 종료 시 확정 저장이 없어 안전망 추가.
                        exp.PersistPositionNow();
                    }
                    else if (w is Views.BuffTrackerWindow buff)
                    {
                        // BuffTracker는 헬퍼 창이 열렸을 때만 저장되므로 종료 시 직접 저장.
                        if (buff.WindowState != WindowState.Minimized)
                            cfg.SetBuffTrackerWindowPosition(buff.Left, buff.Top, false);
                    }
                }
                TWChatOverlay.Services.ConfigService.Save(cfg);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to persist window positions during shutdown.", ex);
            }

            ForegroundTopmostGuard.Shutdown();
            EtaProfileResolver.DeleteCache();
            NotificationService.DeleteCachedAudioFiles();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();     
            AppLogger.Info("Application shutdown completed.");
            base.OnExit(e);
        }
    }
}
