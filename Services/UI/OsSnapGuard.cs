using System;
using System.Windows;
using System.Windows.Interop;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 테두리 없는(WindowStyle=None) 창을 화면 상단으로 드래그할 때 Windows의
    /// 끌어서 스냅(최대화)이 개입해 창이 최대화되거나 위치가 틀어지는 것을 막는다.
    /// WS_MAXIMIZEBOX 제거만으로는 부족함: WPF가 ResizeMode 변경 시 스타일을
    /// 다시 쓰면서 플래그가 복원되므로, 메시지 수준에서 최대화 명령을 차단한다.
    /// </summary>
    public static class OsSnapGuard
    {
        public static void Disable(Window window)
        {
            if (window == null) return;

            window.SourceInitialized += (_, _) => Hook(window);
            if (PresentationSource.FromVisual(window) != null)
                Hook(window);

            // 어떤 경로로든 최대화되면 즉시 원복 (스냅 잔여 경로 안전망)
            window.StateChanged += (_, _) =>
            {
                if (window.WindowState == WindowState.Maximized)
                    window.WindowState = WindowState.Normal;
            };
        }

        private static void Hook(Window window)
        {
            try
            {
                var source = (HwndSource?)PresentationSource.FromVisual(window);
                source?.AddHook(BlockMaximizeHook);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("OsSnapGuard hook failed.", ex);
            }
        }

        private static IntPtr BlockMaximizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_MAXIMIZE = 0xF030;

            if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_MAXIMIZE)
                handled = true;

            return IntPtr.Zero;
        }
    }
}
