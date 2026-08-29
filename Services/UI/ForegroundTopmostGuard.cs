using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 알트탭 등으로 다른 프로그램(게임)이 포그라운드가 되면 전체 화면 창이
    /// topmost 오버레이 위로 올라와 가리는 경우가 있다. (WPF Topmost 속성은 여전히 true라
    /// EnsureTopmost로는 복구되지 않음)
    /// 포그라운드 전환 이벤트를 감지해 표시 중인 topmost 창들을 다시 최상단 밴드로 올린다.
    /// </summary>
    public static class ForegroundTopmostGuard
    {
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private static WinEventDelegate? _callback; // 훅이 살아 있는 동안 GC 수집 방지
        private static IntPtr _hook = IntPtr.Zero;
        private static DispatcherTimer? _reassertTimer;

        /// <summary>App.OnStartup에서 호출. 다른 프로세스의 포그라운드 전환을 감시한다.</summary>
        public static void Initialize()
        {
            if (_hook != IntPtr.Zero)
                return;

            try
            {
                _callback = OnForegroundChanged;
                _hook = SetWinEventHook(
                    EVENT_SYSTEM_FOREGROUND,
                    EVENT_SYSTEM_FOREGROUND,
                    IntPtr.Zero,
                    _callback,
                    0,
                    0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

                if (_hook == IntPtr.Zero)
                    AppLogger.Warn("Foreground topmost guard hook failed to install.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to initialize foreground topmost guard.", ex);
            }
        }

        public static void Shutdown()
        {
            try
            {
                if (_hook != IntPtr.Zero)
                {
                    UnhookWinEvent(_hook);
                    _hook = IntPtr.Zero;
                }
                _callback = null;
                _reassertTimer?.Stop();
            }
            catch { }
        }

        private static void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            try
            {
                // 전체 화면 전환이 끝난 뒤에 재적용되도록 짧게 지연 (연속 전환은 마지막 한 번만)
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher == null || dispatcher.HasShutdownStarted)
                    return;

                dispatcher.BeginInvoke(new Action(ScheduleReassert), DispatcherPriority.Background);
            }
            catch { }
        }

        private static void ScheduleReassert()
        {
            if (_reassertTimer == null)
            {
                _reassertTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                _reassertTimer.Tick += (_, _) =>
                {
                    _reassertTimer!.Stop();
                    ReassertTopmostWindows();
                };
            }

            _reassertTimer.Stop();
            _reassertTimer.Start();
        }

        private static void ReassertTopmostWindows()
        {
            try
            {
                if (TrayAllWindowsService.IsTrayed)
                    return;

                // 잠금 해제 모드는 배경/배너/인스펙터가 자체적으로 z-순서를 관리한다
                if (UiLockService.IsUnlocked)
                    return;

                // 게임(테일즈위버·전체 화면)이 전경 → 오버레이를 최상단으로 복구.
                // 그 외 일반 앱(브라우저 등)이 전경 → 설정이 켜져 있으면 오버레이를 최상단 밴드에서 내려
                // 그 앱을 가리지 않게 한다. (비-topmost 밴드 맨 위라 게임보다는 여전히 위)
                if (!IsForegroundFullscreen() && !IsForegroundTalesWeaver())
                {
                    var settings = ToastPresentationHelper.FindSharedSettings();
                    if (settings?.YieldOverlaysToOtherApps == true)
                        DemoteTopmostWindows();
                    return;
                }

                Window? settingsHost = null;
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is SubMenuWindow && window.IsVisible)
                    {
                        settingsHost = window;
                        continue; // 마지막에 올려 설정 창이 오버레이 위에 남게 한다
                    }

                    if (!window.IsVisible || !window.Topmost)
                        continue;

                    TopmostWindowHelper.BringToTopmost(window);
                }

                if (settingsHost != null)
                    TopmostWindowHelper.BringToTopmost(settingsHost);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to reassert topmost overlays after foreground change.", ex);
            }
        }

        /// <summary>표시 중인 topmost 오버레이들을 비-topmost 밴드로 내린다. WPF Topmost 속성은 그대로 둬
        /// 게임이 전경으로 돌아오면 기존 복구 루프가 다시 최상단으로 올린다.</summary>
        private static void DemoteTopmostWindows()
        {
            const int SWP_NOMOVE = 0x0002;
            const int SWP_NOSIZE = 0x0001;
            const int SWP_NOACTIVATE = 0x0010;

            foreach (Window window in Application.Current.Windows)
            {
                if (!window.IsVisible || !window.Topmost)
                    continue;

                // 메뉴 바는 앱의 진입점이라 항상 접근 가능해야 한다 — 양보 대상에서 제외
                if (window is TWChatOverlay.Views.MenuWindow)
                    continue;

                try
                {
                    var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                    if (handle == IntPtr.Zero)
                        continue;

                    NativeMethods.SetWindowPos(handle, NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                catch { }
            }
        }

        /// <summary>전경 창이 테일즈위버 게임 프로세스의 창이면 true. 창모드여도 가드가 개입하게 한다.</summary>
        private static bool IsForegroundTalesWeaver()
        {
            try
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                    return false;

                NativeMethods.GetWindowThreadProcessId(foreground, out uint pid);
                if (pid == 0)
                    return false;

                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                if (process.ProcessName.Contains("talesweaver", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // 게임가드 등으로 프로세스 조회가 막히면 아래 창 제목 검사로 폴백
            }

            return ForegroundTitleContainsTalesWeaver();
        }

        /// <summary>전경 창 제목에 Talesweaver가 들어 있으면 true. 프로세스 조회가 막혀도 동작하는 폴백.</summary>
        private static bool ForegroundTitleContainsTalesWeaver()
        {
            try
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                    return false;

                var title = new System.Text.StringBuilder(128);
                if (GetWindowTextW(foreground, title, title.Capacity) <= 0)
                    return false;

                return title.ToString().Contains("talesweaver", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>전경 창이 자기 모니터를 사실상 가득 덮고 있으면(전체 화면/보더리스) true.</summary>
        private static bool IsForegroundFullscreen()
        {
            try
            {
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                    return false;

                if (!GetWindowRect(foreground, out RECT rect))
                    return false;

                IntPtr monitor = MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero)
                    return false;

                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(monitor, ref info))
                    return false;

                // 최대화 창의 보이지 않는 테두리를 감안해 2px 여유를 둔다
                return rect.Left <= info.rcMonitor.Left + 2
                    && rect.Top <= info.rcMonitor.Top + 2
                    && rect.Right >= info.rcMonitor.Right - 2
                    && rect.Bottom >= info.rcMonitor.Bottom - 2;
            }
            catch
            {
                return false;
            }
        }
    }
}
