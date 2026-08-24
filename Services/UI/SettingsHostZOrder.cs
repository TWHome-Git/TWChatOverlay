using System;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Interop;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 설정 창(SubMenuWindow)이 떠 있는 동안, 기능 오버레이 창이 설정 창을 덮지 않고
    /// 바로 한 단계 아래(z-순서)에 표시되도록 조정한다.
    /// </summary>
    public static class SettingsHostZOrder
    {
        private static readonly ConditionalWeakTable<Window, object> Registered = new();

        /// <summary>오버레이 창을 등록한다. 표시될 때마다 설정 창 아래로 내려간다.</summary>
        public static void Register(Window? window)
        {
            if (window == null)
                return;

            if (Registered.TryGetValue(window, out _))
                return;

            Registered.Add(window, new object());
            window.IsVisibleChanged += Window_IsVisibleChanged;
        }

        public static bool IsRegistered(Window? window)
            => window != null && Registered.TryGetValue(window, out _);

        private static void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is Window window && window.IsVisible)
            {
                // Show 직후의 z-순서 배치가 끝난 뒤 조정한다
                window.Dispatcher.BeginInvoke(new Action(() => PlaceBelowHost(window)));
            }
        }

        /// <summary>설정 창이 보이면 그 바로 아래로 내린다. 조정했으면 true.</summary>
        public static bool PlaceBelowHost(Window? window)
        {
            if (window == null || !window.IsVisible)
                return false;

            try
            {
                Window? host = FindVisibleHost();
                if (host == null || ReferenceEquals(host, window))
                    return false;

                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                IntPtr hostHwnd = new WindowInteropHelper(host).Handle;
                if (hwnd == IntPtr.Zero || hostHwnd == IntPtr.Zero)
                    return false;

                NativeMethods.SetWindowPos(
                    hwnd,
                    hostHwnd,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE |
                    NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_NOOWNERZORDER);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Window? FindVisibleHost()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is SubMenuWindow && window.IsVisible)
                        return window;
                }
            }
            catch { }

            return null;
        }
    }
}
