using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 게임이 전경으로 오면서 전체 화면 창이 topmost 오버레이 위로 올라와 가리는 경우가 있다.
    /// (WPF Topmost 속성은 여전히 true라 EnsureTopmost로는 복구되지 않음)
    /// 포그라운드 전환 이벤트를 감지해 표시 중인 topmost 창들을 다시 최상단 밴드로 올린다.
    ///
    /// 단, 전경으로 온 창이 게임(설정의 대상 프로세스)일 때만 그렇게 한다. 캡처 도구 같은 다른 앱이
    /// 전경이 될 때도 올리면 그 앱의 전체 화면 오버레이 위로 우리 창이 튀어나온다.
    /// </summary>
    public static class ForegroundTopmostGuard
    {
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

        // 프로세스 이름을 못 얻을 때 창 제목으로 한 번 더 본다. 게임 클라이언트 제목은 "Talesweaver Client Version …"이다.
        private const string GameTitleKeyword = "talesweaver";

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private static WinEventDelegate? _callback; // 훅이 살아 있는 동안 GC 수집 방지
        private static IntPtr _hook = IntPtr.Zero;
        private static DispatcherTimer? _reassertTimer;
        private static IntPtr _pendingForeground = IntPtr.Zero; // 마지막으로 전경이 된 창

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

                _pendingForeground = hwnd;
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

                //  - 항상 위 ON: 게임이 전경이 될 때 오버레이를 최상단 밴드로 재승격
                //  - 항상 위 OFF: 아무것도 하지 않는다 — 시작 때의 z-순서를 그대로 두고 OS에 맡긴다
                var settings = ToastPresentationHelper.FindSharedSettings();
                if (settings?.OverlaysAlwaysOnTop == false)
                    return;

                // 지연 뒤 실제 전경 창을 다시 읽는다. 250ms 사이에 또 바뀌었을 수 있다.
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                    foreground = _pendingForeground;
                if (!IsGuardTarget(foreground, settings?.TopmostGuardProcessNames))
                    return;

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

        /// <summary>전경 창이 설정의 대상 프로그램(기본: 게임 클라이언트)인지. 목록이 비어 있으면 예전처럼 모두 대상으로 본다.</summary>
        private static bool IsGuardTarget(IntPtr hwnd, string? processNames)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            string[] names = (processNames ?? string.Empty)
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (names.Length == 0)
                return true;

            string? processName = TryGetProcessName(hwnd);
            if (processName != null)
            {
                foreach (string name in names)
                {
                    string wanted = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
                    if (string.Equals(processName, wanted, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            // 프로세스 이름이 바뀌어도 게임 창 제목으로 잡히게 한 번 더 본다
            string? title = TryGetWindowTitle(hwnd);
            return title != null && title.Contains(GameTitleKeyword, StringComparison.OrdinalIgnoreCase);
        }

        private static string? TryGetProcessName(IntPtr hwnd)
        {
            try
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0)
                    return null;
                using var process = Process.GetProcessById((int)pid);
                return process.ProcessName;
            }
            catch
            {
                return null; // 이미 끝난 프로세스거나 접근 권한이 없는 경우
            }
        }

        private static string? TryGetWindowTitle(IntPtr hwnd)
        {
            try
            {
                int length = NativeMethods.GetWindowTextLength(hwnd);
                if (length <= 0)
                    return null;
                var buffer = new StringBuilder(length + 1);
                NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
                return buffer.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
