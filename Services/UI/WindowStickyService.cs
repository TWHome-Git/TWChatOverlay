using System;
using System.Windows;
using System.Windows.Threading;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 오버레이 창의 표시/위치/최상단 상태를 제어합니다.
    /// 게임 창 감지 없이 설정 좌표 기준으로 동작합니다.
    /// </summary>
    public class WindowStickyService
    {
        private readonly Window _overlayWindow;
        private readonly ChatSettings _settings;
        private readonly DispatcherTimer _stickyTimer;

        private bool _forceHidden;
        private bool _positionTrackingEnabled = true;
        private bool? _lastCanShowAuxiliaryWindows;

        public event Action<bool>? AuxiliaryWindowVisibilityChanged;

        public WindowStickyService(Window window, ChatSettings settings)
        {
            _overlayWindow = window;
            _settings = settings;
            _stickyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _stickyTimer.Tick += (_, _) => UpdatePosition();
        }

        public void Start()
        {
            _stickyTimer.Start();
            UpdatePositionNow();
        }

        public void Stop()
        {
            _stickyTimer.Stop();
        }

        public void UpdatePositionImmediately()
        {
            _overlayWindow.Dispatcher.BeginInvoke(UpdatePosition, DispatcherPriority.Render);
        }

        public void UpdatePositionNow()
        {
            if (_overlayWindow.Dispatcher.CheckAccess())
            {
                UpdatePosition();
                return;
            }

            _overlayWindow.Dispatcher.Invoke(UpdatePosition, DispatcherPriority.Send);
        }

        public void SetForceHidden(bool forceHidden)
        {
            _forceHidden = forceHidden;
            UpdatePositionImmediately();
        }

        public void SetPositionTrackingEnabled(bool enabled)
        {
            _positionTrackingEnabled = enabled;
            UpdatePositionImmediately();
        }

        private void UpdatePosition()
        {
            // 모든 창 트레이 중에는 어떤 모드(설정 모드 포함)에서도 자동으로 다시 띄우지 않는다
            if (TrayAllWindowsService.IsTrayed)
            {
                return;
            }

            if (_overlayWindow is Views.MainWindow mainWindow && mainWindow.IsSettingsPositionMode)
            {
                ShowOverlay();
                ApplyTopmost();
                NotifyAuxiliaryWindowVisibilityChanged(true);
                return;
            }

            if (_forceHidden)
            {
                HideOverlay();
                NotifyAuxiliaryWindowVisibilityChanged(false);
                return;
            }

            ShowOverlay();
            ApplyTopmost();

            // 잠금 해제 모드에서는 드래그 중인 창을 저장 좌표로 되돌리지 않는다.
            if (UiLockService.IsUnlocked)
            {
                NotifyAuxiliaryWindowVisibilityChanged(true);
                return;
            }

            if (_positionTrackingEnabled)
            {
                double targetLeft = _settings.LineMarginLeft;
                double targetTop = _settings.LineMargin;

                if (Math.Abs(_overlayWindow.Left - targetLeft) > 0.1)
                {
                    _overlayWindow.Left = targetLeft;
                }

                if (Math.Abs(_overlayWindow.Top - targetTop) > 0.1)
                {
                    _overlayWindow.Top = targetTop;
                }
            }

            NotifyAuxiliaryWindowVisibilityChanged(true);
        }

        private void ShowOverlay()
        {
            if (_overlayWindow.Visibility != Visibility.Visible)
            {
                _overlayWindow.Visibility = Visibility.Visible;
            }

            // 잠금 해제 인스펙터에서 지정한 창별 투명도가 있으면 그 값을 목표로 한다
            // (1로 강제 복원하면 지정 투명도가 0.1초마다 무효화된다)
            double targetOpacity = 1.0;
            var opacityPercents = _settings.WindowOpacityPercents;
            if (opacityPercents != null &&
                opacityPercents.TryGetValue(_overlayWindow.GetType().Name, out double percent))
            {
                targetOpacity = Math.Max(0.1, Math.Min(1.0, percent / 100.0));
            }

            if (Math.Abs(_overlayWindow.Opacity - targetOpacity) > 0.001)
            {
                _overlayWindow.Opacity = targetOpacity;
            }
        }

        private void HideOverlay()
        {
            if (_overlayWindow.Visibility != Visibility.Collapsed)
            {
                _overlayWindow.Visibility = Visibility.Collapsed;
            }

            if (_overlayWindow.Opacity != 0)
            {
                _overlayWindow.Opacity = 0;
            }
        }

        private void ApplyTopmost()
        {
            TopmostWindowHelper.EnsureTopmost(_overlayWindow);
        }

        private void NotifyAuxiliaryWindowVisibilityChanged(bool canShow)
        {
            if (_lastCanShowAuxiliaryWindows == canShow)
            {
                return;
            }

            _lastCanShowAuxiliaryWindows = canShow;

            try
            {
                AuxiliaryWindowVisibilityChanged?.Invoke(canShow);
            }
            catch
            {
            }
        }
    }
}
