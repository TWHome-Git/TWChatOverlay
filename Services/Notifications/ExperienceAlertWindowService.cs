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
            if (TrayAllWindowsService.IsTrayed)
                return; // 트레이 최소화 중에는 알림 창을 띄우지 않는다

            ShowWindow(message, settings, requireAlertEnabled: true);
        }

        /// <summary>통합 알림 스택 앵커 미리보기로 위임.</summary>
        public static void ShowPositionPreview(ChatSettings settings, bool force = false)
        {
            if (settings == null || (!force && !settings.ShowExperienceLimitAlertWindow))
                return;

            ToastStackService.ShowPositionPreview(settings);
        }

        private static void ShowWindow(string message, ChatSettings settings, bool requireAlertEnabled, bool isPreview = false)
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
                _window.SetPreviewMode(isPreview);

                if (!_window.IsVisible)
                {
                    _window.Show();
                }

                // 통합 알림 스택: 앵커 위치에서 다른 알림들 아래로 배치
                var (left, top) = ToastStackService.Attach(_window);
                _window.Left = left;
                _window.Top = top;

                _window.BringToFront();
            }));
        }

        /// <summary>설정 슬라이더 변경을 열려 있는 알림 창에 즉시 반영한다.</summary>
        public static void ApplyFontSize(double size)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window != null && _window.IsLoaded)
                    _window.SetFontSize(size);
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
            => ToastStackService.SaveCurrentPosition(settings);

        private static ExperienceAlertStateSnapshot GetCurrentSnapshot(ChatSettings settings)
        {
            var snapshot = _stateSnapshotProvider?.Invoke();
            if (snapshot != null)
                return snapshot;

            return new ExperienceAlertStateSnapshot
            {
                TotalExp = 0
            };
        }
    }
}
