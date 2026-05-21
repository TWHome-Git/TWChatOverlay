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

            if (_overlayWindow.Opacity != 1)
            {
                _overlayWindow.Opacity = 1;
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
