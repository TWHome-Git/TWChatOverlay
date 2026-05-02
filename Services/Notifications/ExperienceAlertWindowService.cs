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
        private static Func<ExperienceAlertStateSnapshot>? _stateSnapshotProvider;
        private static Action<ExperienceAlertStateSnapshot>? _stateSnapshotApplyAction;

        public static void ConfigureStateBridge(
            Func<ExperienceAlertStateSnapshot>? stateSnapshotProvider,
            Action<ExperienceAlertStateSnapshot>? stateSnapshotApplyAction)
        {
            _stateSnapshotProvider = stateSnapshotProvider;
            _stateSnapshotApplyAction = stateSnapshotApplyAction;
        }

        public static bool TryGetStateSnapshot(ChatSettings settings, out ExperienceAlertStateSnapshot snapshot)
        {
            if (settings == null)
            {
                snapshot = new ExperienceAlertStateSnapshot();
                return false;
            }

            snapshot = GetCurrentSnapshot(settings);
            return true;
        }

        public static bool ApplyStateSnapshot(ExperienceAlertStateSnapshot snapshot)
        {
            if (snapshot == null || _stateSnapshotApplyAction == null)
                return false;

            _stateSnapshotApplyAction(snapshot);
            return true;
        }

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

        public static void RefreshState(ChatSettings settings)
        {
            if (settings == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window == null || !_window.IsLoaded)
                    return;

                _window.SetSettings(settings);
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

        private static ExperienceAlertStateSnapshot GetCurrentSnapshot(ChatSettings settings)
        {
            var snapshot = _stateSnapshotProvider?.Invoke();
            if (snapshot != null)
                return snapshot;

            return new ExperienceAlertStateSnapshot
            {
                IsProfileMode = settings.EnableCharacterProfiles,
                TotalExp = 0,
                Profile1Exp = 0,
                Profile2Exp = 0,
                Profile1Label = string.IsNullOrWhiteSpace(settings.Profile1DisplayName) ? "프로필1" : settings.Profile1DisplayName,
                Profile2Label = string.IsNullOrWhiteSpace(settings.Profile2DisplayName) ? "프로필2" : settings.Profile2DisplayName
            };
        }
    }
}
