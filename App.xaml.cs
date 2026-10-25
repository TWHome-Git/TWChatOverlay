using Microsoft.Extensions.DependencyInjection;
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

#if DEBUG
            // 도움말 예시 이미지 생성 모드: 창을 띄우지 않고 PNG만 만들고 종료 (HelpShotRenderer는 Release 빌드에서 제외된다)
            int shotArg = Array.IndexOf(e.Args, "--render-help-shots");
            if (shotArg >= 0)
            {
                string outDir = shotArg + 1 < e.Args.Length
                    ? e.Args[shotArg + 1]
                    : System.IO.Path.Combine(System.IO.Path.GetTempPath(), "twchat_helpshots");
                try { HelpShotRenderer.RenderAll(outDir); }
                catch (Exception ex)
                {
                    AppLogger.Error("Help shot rendering failed.", ex);
                    // 디버그 로깅이 꺼져 있어도 원인을 확인할 수 있게 출력 폴더에 남긴다
                    try { System.IO.File.WriteAllText(System.IO.Path.Combine(outDir, "_render_error.txt"), ex.ToString()); } catch { }
                }
                Shutdown();
                return;
            }
#endif

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

            // 컴포지션 루트: 설정을 한 번 읽고, 창들이 쓰는 핵심 서비스를 여기서 만든다 (MainWindow는 Get으로 꺼낸다)
            bool settingsFileMissing = !ConfigService.SettingsFileExists();
            Models.ChatSettings settings = ConfigService.Load();
            AppServices.Initialize(services =>
            {
                services.AddSingleton(new StartupState(settingsFileMissing));
                services.AddSingleton(settings);
                services.AddSingleton<IIdTagService, IdTagService>();
                services.AddSingleton<IOverlayOpacityService, OverlayOpacityService>();
                services.AddSingleton<ExperienceService>();
                services.AddSingleton<ExpHuntSessionService>();
                services.AddSingleton<LogAnalysisService>();
                services.AddSingleton<MainLogPipelineCoordinator>();
                services.AddSingleton<ExperienceEssenceAlertService>();
                services.AddSingleton<DungeonCountDisplayService>();
                services.AddSingleton<ReadableLogArchiveService>();
                services.AddSingleton<MessengerLogWatcherService>();
                services.AddSingleton<BuffTrackerService>();
                services.AddSingleton<LogService>();
                services.AddSingleton<TreasuryHistoryService>();
                services.AddSingleton<EtaRankingService>();
                services.AddSingleton<UiLockService>();
                services.AddSingleton<ContentTimerService>();
                services.AddSingleton<ForegroundTopmostGuard>();
                services.AddSingleton<ScalerTopmostGuard>();
                services.AddSingleton<ItemDropToastService>();
                services.AddSingleton<NotificationService>();
                services.AddSingleton<WeeklySeedRewardService>();
                services.AddSingleton<DropItemResolver>();
                services.AddSingleton<ToastStackService>();
                services.AddSingleton<ChatWindowHub>();
                services.AddSingleton<RecaptureSupplyPadOrderService>();
                services.AddSingleton<RecaptureSupplyAlertService>();
                services.AddSingleton<ExperienceAlertWindowService>();
                services.AddSingleton<TrayAllWindowsService>();
                services.AddSingleton<BlacklistService>();
                services.AddSingleton<ShoutToastService>();
                services.AddSingleton<DungeonCountDisplayWindowService>();
                services.AddSingleton<MessengerEtaToastService>();
            });

            EtaProfileResolver.InitializeAsync();
            AppServices.Get<BlacklistService>().Initialize();
            AppServices.Get<IIdTagService>().Initialize();
            _ = AppServices.Get<RecaptureSupplyAlertService>().PreloadAsync();
            AppServices.Get<ForegroundTopmostGuard>().Initialize();
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
            // 오버레이 창(OverlayWindowBase)은 닫히며 스스로 마지막 위치를 설정에 쓰고 지연 저장(SaveDeferred)을 건다.
            // 그 대기분을 디스크 사본을 다시 읽기 전에 먼저 기록한다.
            try { TWChatOverlay.Services.ConfigService.FlushPendingSave(); } catch { }
            try
            {
                Models.ChatSettings cfg;

                // 시작이 DI 초기화 전에 끝난 경우(중복 실행 등)에만 디스크 사본을 읽는다
                try
                {
                    cfg = AppServices.TryGet<Models.ChatSettings>() ?? TWChatOverlay.Services.ConfigService.Load();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to reload persisted settings during shutdown. Using in-memory defaults as last resort.", ex);
                    cfg = new Models.ChatSettings();
                }

                // 메뉴 바·서브 메뉴만 베이스(OverlayWindowBase)가 아닌 일반 Window라 여기서 직접 저장한다
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
                }
                TWChatOverlay.Services.ConfigService.Save(cfg);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to persist window positions during shutdown.", ex);
            }

            // 두 번째 인스턴스처럼 DI가 초기화되지 않은 채 종료될 수 있다
            AppServices.TryGet<ForegroundTopmostGuard>()?.Shutdown();
            AppServices.TryGet<ScalerTopmostGuard>()?.Detach();
            AppServices.TryGet<NotificationService>()?.DeleteCachedAudioFiles();
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();     
            AppLogger.Info("Application shutdown completed.");
            base.OnExit(e);
        }
    }
}
