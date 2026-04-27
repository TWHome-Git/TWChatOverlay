using System.Linq;
using System.Windows;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 메시지 박스가 다른 창 뒤로 숨지 않도록 소유자와 최상단 상태를 보정합니다.
    /// </summary>
    public static class DialogService
    {
        public static MessageBoxResult ShowTopmost(
            Window? owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult = MessageBoxResult.None)
        {
            Window? resolvedOwner = ResolveOwner(owner);
            if (resolvedOwner != null)
            {
                return ShowWithOwner(resolvedOwner, messageBoxText, caption, button, icon, defaultResult);
            }

            var tempOwner = new Window
            {
                Width = 0,
                Height = 0,
                Left = SystemParameters.VirtualScreenLeft,
                Top = SystemParameters.VirtualScreenTop,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = true,
                AllowsTransparency = true,
                Opacity = 0,
                Topmost = true
            };

            try
            {
                tempOwner.Show();
                tempOwner.Activate();
                return MessageBox.Show(tempOwner, messageBoxText, caption, button, icon, defaultResult);
            }
            finally
            {
                try { tempOwner.Close(); } catch { }
            }
        }

        private static MessageBoxResult ShowWithOwner(
            Window owner,
            string messageBoxText,
            string caption,
            MessageBoxButton button,
            MessageBoxImage icon,
            MessageBoxResult defaultResult)
        {
            bool originalTopmost = owner.Topmost;

            try
            {
                owner.Topmost = true;
                owner.Activate();
                return MessageBox.Show(owner, messageBoxText, caption, button, icon, defaultResult);
            }
            finally
            {
                try
                {
                    owner.Topmost = originalTopmost;
                    if (owner.IsVisible)
                    {
                        owner.Activate();
                    }
                }
                catch { }
            }
        }

        private static Window? ResolveOwner(Window? owner)
        {
            if (owner != null && owner.IsLoaded && owner.IsVisible)
            {
                return owner;
            }

            return Application.Current?.Windows
                .OfType<Window>()
                .Where(window => window.IsVisible)
                .OrderByDescending(window => window.IsActive)
                .FirstOrDefault();
        }
    }
}
