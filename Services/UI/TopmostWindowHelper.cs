using System;
using System.Windows;
using System.Windows.Interop;

namespace TWChatOverlay.Services
{
    public static class TopmostWindowHelper
    {
        public static void BringToTopmost(Window? window)
        {
            if (window == null)
                return;

            try
            {
                window.Topmost = false;
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
