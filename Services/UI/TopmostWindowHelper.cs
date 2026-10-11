using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TWChatOverlay.Services
{
    public static class TopmostWindowHelper
    {
        private const uint GW_HWNDPREV = 3;
        private const int WS_EX_TOPMOST = 0x00000008;
        private const int DWMWA_CLOAKED = 14;

        // z-순서를 거슬러 올라갈 최대 창 수 (비정상 순환 방지용 상한)
        private const int MaxZOrderWalk = 2048;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr BeginDeferWindowPos(int nNumWindows);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr DeferWindowPos(IntPtr hWinPosInfo, IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EndDeferWindowPos(IntPtr hWinPosInfo);

        public static void EnsureTopmost(Window? window)
        {
            if (window == null || window.Topmost)
                return;

            BringToTopmost(window);
        }

        public static void BringToTopmost(Window? window)
        {
            if (window == null)
                return;

            try
            {
                if (!window.Topmost)
                    window.Topmost = true;

                // 설정 창이 떠 있는 동안 등록된 오버레이는 설정 창 바로 아래까지만 올린다
                if (SettingsHostZOrder.IsRegistered(window) && SettingsHostZOrder.PlaceBelowHost(window))
                    return;

                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                NativeMethods.SetWindowPos(
                    hwnd,
                    NativeMethods.HWND_TOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE |
                    NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_NOOWNERZORDER);
            }
            catch { }
        }

        /// <summary>
        /// 여러 창을 한 번에 topmost 밴드 맨 위로 올린다. <paramref name="topFirst"/>의 앞쪽 창이 더 위에 놓인다.
        /// 창마다 따로 올리면 그 사이 화면에 일부만 올라온 상태가 비쳐 깜박일 수 있어, DeferWindowPos로 한꺼번에 적용한다.
        /// 한꺼번에 적용하지 못하면 예전처럼 아래 창부터 하나씩 올린다.
        /// </summary>
        public static void BringToTopmostInOrder(IReadOnlyList<Window> topFirst)
        {
            if (topFirst.Count == 0)
                return;

            var handles = new List<IntPtr>(topFirst.Count);
            foreach (Window window in topFirst)
            {
                if (!window.Topmost)
                    window.Topmost = true;
                IntPtr hwnd = new WindowInteropHelper(window).EnsureHandle();
                if (hwnd != IntPtr.Zero)
                    handles.Add(hwnd);
            }

            const uint flags =
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_NOOWNERZORDER;

            try
            {
                IntPtr hdwp = BeginDeferWindowPos(handles.Count);
                // 맨 위 창은 topmost 밴드 맨 위로, 나머지는 바로 위 창 아래에 차례로 끼운다 (topmost 창 아래에 끼우면 topmost가 유지된다)
                IntPtr insertAfter = NativeMethods.HWND_TOPMOST;
                foreach (IntPtr hwnd in handles)
                {
                    if (hdwp == IntPtr.Zero)
                        break;
                    hdwp = DeferWindowPos(hdwp, hwnd, insertAfter, 0, 0, 0, 0, flags);
                    insertAfter = hwnd;
                }

                if (hdwp != IntPtr.Zero && EndDeferWindowPos(hdwp))
                    return;
            }
            catch { }

            // 한꺼번에 적용하지 못했으면 아래 창부터 하나씩 올려 같은 순서를 만든다
            for (int i = handles.Count - 1; i >= 0; i--)
            {
                try
                {
                    NativeMethods.SetWindowPos(handles[i], NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, flags);
                }
                catch { }
            }
        }

        /// <summary>
        /// 표시 중인 topmost 오버레이 가운데 다른 프로그램 창에 실제로 가려진 것이 있는지.
        /// 이미 맨 위에 있는데 다시 올리면 z-순서를 건드리는 순간 깜박일 수 있어, 재승격 전에 확인한다.
        ///  - 다른 프로세스의 보이는 창이 z-순서상 위에 있고 화면에서 겹치면 가려진 것으로 본다.
        ///  - Windows가 topmost 스타일을 떼어 낸 경우(WPF Topmost 속성은 그대로 true)도 가려진 것으로 본다.
        /// </summary>
        /// <param name="isCoveringWindow">위에 있는 창 가운데 어떤 창을 '가리는 창'으로 볼지. null이면 <see cref="IsOpaqueOtherWindow"/>.</param>
        public static bool IsAnyOverlayCovered(Func<IntPtr, bool>? isCoveringWindow = null)
        {
            uint ownProcessId = (uint)Environment.ProcessId;
            isCoveringWindow ??= IsOpaqueOtherWindow;

            foreach (Window window in Application.Current.Windows)
            {
                if (!window.IsVisible || !window.Topmost)
                    continue;

                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero)
                    continue;

                if ((NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE) & WS_EX_TOPMOST) == 0)
                    return true;

                if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect) || rect.Right <= rect.Left || rect.Bottom <= rect.Top)
                    continue;

                if (IsCoveredInZOrder(hwnd, rect, ownProcessId, isCoveringWindow))
                    return true;
            }

            return false;
        }

        private static bool IsCoveredInZOrder(IntPtr hwnd, NativeMethods.RECT rect, uint ownProcessId, Func<IntPtr, bool> isCoveringWindow)
        {
            IntPtr cursor = GetWindow(hwnd, GW_HWNDPREV);
            for (int walked = 0; cursor != IntPtr.Zero && walked < MaxZOrderWalk; walked++, cursor = GetWindow(cursor, GW_HWNDPREV))
            {
                if (!IsWindowVisible(cursor))
                    continue;

                // 우리 창끼리의 순서는 재승격 대상이 아니다 (설정 창이 오버레이 위에 있는 것 등)
                NativeMethods.GetWindowThreadProcessId(cursor, out uint processId);
                if (processId == ownProcessId)
                    continue;

                if (!NativeMethods.GetWindowRect(cursor, out NativeMethods.RECT other) ||
                    other.Left >= rect.Right || other.Right <= rect.Left || other.Top >= rect.Bottom || other.Bottom <= rect.Top)
                    continue;

                if (isCoveringWindow(cursor))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 다른 프로그램 창이 실제로 화면을 가리는 창인지. DWM이 숨겨 둔(cloaked) 창, 클릭이 통과하는 투명 오버레이
        /// (그래픽 드라이버·녹화 프로그램 오버레이 등)는 가린다고 보지 않는다. 확대 프로그램(Magpie·Lossless Scaling)의 확대 화면 창은
        /// 클릭 통과 스타일이어도 화면 전체를 그리므로 가리는 창이다 (Lossless Scaling은 게임이 전경이면 topmost+클릭 통과가 된다).
        /// </summary>
        public static bool IsOpaqueOtherWindow(IntPtr hwnd)
        {
            if (ScalerTopmostGuard.IsScalerWindow(hwnd))
                return true;

            try
            {
                if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                    return false;
            }
            catch { }

            return (NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TRANSPARENT) == 0;
        }
    }
}
