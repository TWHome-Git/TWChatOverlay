using System;
using System.Collections.Generic;
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
    /// 그리고 오버레이가 실제로 가려졌을 때만 올린다 — 이미 맨 위인데 다시 올리면 복귀할 때마다 깜박인다.
    /// </summary>
    public sealed class ForegroundTopmostGuard
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

        private WinEventDelegate? _callback; // 훅이 살아 있는 동안 GC 수집 방지
        private IntPtr _hook = IntPtr.Zero;
        private DispatcherTimer? _reassertTimer;
        private IntPtr _pendingForeground = IntPtr.Zero; // 마지막으로 전경이 된 창

        /// <summary>App.OnStartup에서 호출. 다른 프로세스의 포그라운드 전환을 감시한다.</summary>
        public void Initialize()
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

        public void Shutdown()
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

        private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
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

        private void ScheduleReassert()
        {
            // 확대 프로그램(Magpie)은 게임이 전경이 되는 순간 확대 화면을 올리므로 그쪽 점검을 먼저 짧게 돌린다
            AppServices.TryGet<ScalerTopmostGuard>()?.NotifyForegroundChanged();

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

        private void ReassertTopmostWindows()
        {
            try
            {
                if (!CanReassert(out var settings))
                    return;

                // 지연 뒤 실제 전경 창을 다시 읽는다. 250ms 사이에 또 바뀌었을 수 있다.
                IntPtr foreground = NativeMethods.GetForegroundWindow();
                if (foreground == IntPtr.Zero)
                    foreground = _pendingForeground;
                if (!IsGuardTarget(foreground, settings?.TopmostGuardProcessNames))
                    return;

                // 이미 맨 위에 있으면 건드리지 않는다. 다른 창에 갔다가 돌아올 때마다 다시 올리면
                // z-순서가 바뀌는 순간 오버레이가 깜박인다. 실제로 가려졌을 때(게임·확대 창이 위로 올라옴,
                // Windows가 topmost를 떼어 냄)만 올린다.
                if (!TopmostWindowHelper.IsAnyOverlayCovered())
                    return;

                BringVisibleTopmostWindowsToTop();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to reassert topmost overlays after foreground change.", ex);
            }
        }

        /// <summary>
        /// 전경 창과 상관없이 지금 바로 오버레이를 최상단으로 다시 올린다. 규칙(트레이·잠금 해제·항상 위 OFF)은 같다.
        /// 확대 프로그램(Magpie) 화면처럼 전경 변화 없이 다른 topmost 창이 오버레이를 덮을 때 ScalerTopmostGuard가 부른다.
        /// </summary>
        /// <returns>규칙상 올릴 수 있어 올렸으면 true.</returns>
        public bool ReassertNow()
        {
            try
            {
                if (!CanReassert(out _))
                    return false;

                BringVisibleTopmostWindowsToTop();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to reassert topmost overlays.", ex);
                return false;
            }
        }

        private static bool CanReassert(out Models.ChatSettings? settings)
        {
            settings = null;
            if (AppServices.Get<TrayAllWindowsService>().IsTrayed)
                return false;

            // 잠금 해제 모드는 배경/배너/인스펙터가 자체적으로 z-순서를 관리한다
            if (AppServices.Get<UiLockService>().IsUnlocked)
                return false;

            //  - 항상 위 ON: 게임이 전경이 될 때 오버레이를 최상단 밴드로 재승격
            //  - 항상 위 OFF: 아무것도 하지 않는다 — 시작 때의 z-순서를 그대로 두고 OS에 맡긴다
            settings = ToastPresentationHelper.FindSharedSettings();
            return settings?.OverlaysAlwaysOnTop != false;
        }

        private static void BringVisibleTopmostWindowsToTop()
        {
            // 예전에 창 목록 순서대로 하나씩 올리던 것과 같은 최종 순서(나중 창이 위, 설정 창은 맨 위)를 한 번에 적용한다
            Window? settingsHost = null;
            var overlays = new List<Window>();
            foreach (Window window in Application.Current.Windows)
            {
                if (window is SubMenuWindow && window.IsVisible)
                {
                    settingsHost = window;
                    continue; // 설정 창이 오버레이 위에 남게 맨 위에 둔다
                }

                if (!window.IsVisible || !window.Topmost)
                    continue;

                overlays.Add(window);
            }

            var topFirst = new List<Window>(overlays.Count + 1);
            if (settingsHost != null)
                topFirst.Add(settingsHost);
            for (int i = overlays.Count - 1; i >= 0; i--)
                topFirst.Add(overlays[i]);

            TopmostWindowHelper.BringToTopmostInOrder(topFirst);
        }

        /// <summary>
        /// 전경 창이 설정의 대상 프로그램(기본: 게임 클라이언트)인지. 목록이 비어 있으면 예전처럼 모두 대상으로 본다.
        /// 확대 프로그램(Magpie)의 확대 화면 창도 대상이다 — 확대 중에는 그 창이 전경이 되어 오버레이를 덮을 수 있다.
        /// </summary>
        private bool IsGuardTarget(IntPtr hwnd, string? processNames)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            if (ScalerTopmostGuard.IsScalerWindow(hwnd))
                return true;

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
