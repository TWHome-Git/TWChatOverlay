using System;
using System.Windows;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 경험치 누적 알림 전용 창을 표시합니다.
    /// </summary>
    public static class ExperienceAlertWindowService
    {
        private static ExperienceAlertWindow? _window;

        public static void Show(string message, ChatSettings settings)
        {
            ShowWindow(message, settings, requireAlertEnabled: true);
        }

        public static void ShowPositionPreview(ChatSettings settings, bool force = false)
        {
            if (settings == null || (!force && !settings.ShowExperienceLimitAlertWindow))
                return;

            ShowWindow("경험치 누적 알림 위치", settings, requireAlertEnabled: false);
        }

        private static void ShowWindow(string message, ChatSettings settings, bool requireAlertEnabled)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;
            if (settings == null || (requireAlertEnabled && !settings.EnableExperienceLimitAlert))
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window == null || !_window.IsLoaded)
                {
                    _window = new ExperienceAlertWindow(settings);
                    _window.Closed += (_, _) => _window = null;
                }
                else
                {
                    _window.SetSettings(settings);
                }

                _window.SetMessage(message);
                PositionWindow(_window, settings);

                if (!_window.IsVisible)
                {
                    _window.Show();
                }

                _window.BringToFront();
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

        public static void SaveCurrentPosition(ChatSettings settings)
        {
            if (settings == null)
                return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (_window == null || !_window.IsVisible)
                    return;

                settings.ExperienceLimitAlertWindowLeft = _window.Left;
                settings.ExperienceLimitAlertWindowTop = _window.Top;
            });

            ConfigService.Save(settings);
        }

        private static void PositionWindow(Window window, ChatSettings settings)
        {
            if (settings.ExperienceLimitAlertWindowLeft.HasValue &&
                settings.ExperienceLimitAlertWindowTop.HasValue)
            {
                window.Left = settings.ExperienceLimitAlertWindowLeft.Value;
                window.Top = settings.ExperienceLimitAlertWindowTop.Value;
                return;
            }

            Rect workArea = SystemParameters.WorkArea;
            double left = workArea.Left + ((workArea.Width - window.Width) / 2.0);
            double top = workArea.Top + 48;

            window.Left = Math.Max(workArea.Left, left);
            window.Top = Math.Max(workArea.Top, top);
        }
    }
}
