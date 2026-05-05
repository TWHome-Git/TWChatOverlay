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

            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            EtaProfileResolver.InitializeAsync();
            BlacklistService.Initialize();
            _ = RecaptureSupplyAlertService.PreloadAsync();
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

            try
            {
                Views.MenuWindow? menu = null;
                foreach (Window w in Current.Windows)
                {
                    if (w is Views.MenuWindow existingMenu)
                    {
                        menu = existingMenu;
                        break;
                    }
                }

                if (menu == null)
                {
                    menu = new Views.MenuWindow();
                    try
                    {
                        var settings = TWChatOverlay.Services.ConfigService.Load();
                        if (settings.MenuWindowLeft.HasValue && settings.MenuWindowTop.HasValue)
                        {
                            menu.WindowStartupLocation = WindowStartupLocation.Manual;
                            menu.Left = settings.MenuWindowLeft.Value;
                            menu.Top = settings.MenuWindowTop.Value;
                        }
                        else if (main != null)
                        {
                            menu.Left = main.Left;
                            menu.Top = main.Top;
                            menu.Owner = main;
                        }
                        else
                        {
                            menu.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("Failed to position menu window.", ex);
                    }

                    menu.Topmost = true;
                    menu.Show();
                }
                else
                {
                    menu.Topmost = true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to create menu window.", ex);
            }

            try
            {
                Views.MemoOverlayWindow? memo = null;
                foreach (Window w in Current.Windows)
                {
                    if (w is Views.MemoOverlayWindow existingMemo)
                    {
                        memo = existingMemo;
                        break;
                    }
                }

                if (memo == null)
                {
                    memo = new Views.MemoOverlayWindow();
                    memo.Show();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to create memo overlay window at startup.", ex);
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

            Task.Run(() => UpdateService.CheckForUpdateAsync()).ContinueWith(t =>
            {
                if (t.Exception != null)
                    AppLogger.Warn("Update check failed.", t.Exception, "CheckForUpdateAsync");
            }, TaskContinuationOptions.OnlyOnFaulted);
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
                }
                TWChatOverlay.Services.ConfigService.Save(cfg);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to persist window positions during shutdown.", ex);
            }

            EtaProfileResolver.DeleteCache();
            NotificationService.DeleteCachedAudioFiles();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            AppLogger.Info("Application shutdown completed.");
            base.OnExit(e);
        }
    }
}
