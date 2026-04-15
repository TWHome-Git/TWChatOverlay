using System;
using System.Windows;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 경험치 누적 알림 전용 창을 표시합니다.
    /// </summary>
    public static class ExperienceAlertWindowService
    {
        private static ExperienceAlertWindow? _window;

        public static void Show(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window == null || !_window.IsLoaded)
                {
                    _window = new ExperienceAlertWindow();
                    _window.Closed += (_, _) => _window = null;
                }

                _window.SetMessage(message);
                PositionWindow(_window);

                if (!_window.IsVisible)
                {
                    _window.Show();
                }
                else
                {
                    _window.BringToFront();
                }
            }));
        }

        public static void Close()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window == null)
                    return;

                if (_window.IsVisible)
                {
                    _window.Close();
                }

                _window = null;
            }));
        }

        private static void PositionWindow(Window window)
        {
            Rect workArea = SystemParameters.WorkArea;
            double left = workArea.Left + ((workArea.Width - window.Width) / 2.0);
            double top = workArea.Top + 48;

            window.Left = Math.Max(workArea.Left, left);
            window.Top = Math.Max(workArea.Top, top);
        }
    }
}
