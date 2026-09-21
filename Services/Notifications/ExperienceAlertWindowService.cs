using System;
using System.Windows;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 경험치 누적 알림 전용 창을 표시합니다.
    /// </summary>
    public sealed class ExperienceAlertWindowService
    {
        private ExperienceAlertWindow? _window;
        private Func<ExperienceAlertStateSnapshot>? _stateSnapshotProvider;
        private Action<ExperienceAlertStateSnapshot>? _stateSnapshotApplyAction;

        public void ConfigureStateBridge(
            Func<ExperienceAlertStateSnapshot>? stateSnapshotProvider,
            Action<ExperienceAlertStateSnapshot>? stateSnapshotApplyAction)
        {
            _stateSnapshotProvider = stateSnapshotProvider;
            _stateSnapshotApplyAction = stateSnapshotApplyAction;
        }

        public bool TryGetStateSnapshot(ChatSettings settings, out ExperienceAlertStateSnapshot snapshot)
        {
            if (settings == null)
            {
                snapshot = new ExperienceAlertStateSnapshot();
                return false;
            }

            snapshot = GetCurrentSnapshot(settings);
            return true;
        }

        public bool ApplyStateSnapshot(ExperienceAlertStateSnapshot snapshot)
        {
            if (snapshot == null || _stateSnapshotApplyAction == null)
                return false;

            _stateSnapshotApplyAction(snapshot);
            return true;
        }

        public void Show(string message, ChatSettings settings)
        {
            if (AppServices.Get<TrayAllWindowsService>().IsTrayed)
                return; // 트레이 최소화 중에는 알림 창을 띄우지 않는다

            ShowWindow(message, settings, requireAlertEnabled: true);
        }

        /// <summary>통합 알림 스택 앵커 미리보기로 위임.</summary>
        public void ShowPositionPreview(ChatSettings settings, bool force = false)
        {
            if (settings == null || (!force && !settings.ShowExperienceLimitAlertWindow))
                return;

            AppServices.Get<ToastStackService>().ShowPositionPreview(settings);
        }

        private void ShowWindow(string message, ChatSettings settings, bool requireAlertEnabled, bool isPreview = false)
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

                // 통합 알림 스택: 자리를 먼저 받고 그 자리에서 보인다.
                // Show 뒤에 붙이면 옛 위치에서 잠깐 보이는 사이 다른 알림이 같은 칸을 잡아 겹쳤다.
                var (left, top) = AppServices.Get<ToastStackService>().Attach(_window);
                _window.Left = left;
                _window.Top = top;

                if (!_window.IsVisible)
                {
                    _window.Show();
                }

                _window.BringToFront();
            }));
        }

        /// <summary>설정 슬라이더 변경을 열려 있는 알림 창에 즉시 반영한다.</summary>
        public void ApplyFontSize(double size)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_window != null && _window.IsLoaded)
                    _window.SetFontSize(size);
            }));
        }

        public void Close()
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

        public void RefreshState(ChatSettings settings)
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

        public void SaveCurrentPosition(ChatSettings settings)
            => AppServices.Get<ToastStackService>().SaveCurrentPosition(settings);

        private ExperienceAlertStateSnapshot GetCurrentSnapshot(ChatSettings settings)
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
