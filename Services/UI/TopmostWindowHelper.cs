using System;
using System.Windows;
using System.Windows.Interop;

namespace TWChatOverlay.Services
{
    public static class TopmostWindowHelper
    {
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
    }
}
