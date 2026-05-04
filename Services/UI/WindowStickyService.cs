using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 오버레이 창을 게임 창 위치에 맞춰 고정/표시 제어합니다.
    /// </summary>
    public class WindowStickyService
    {
        private readonly Window _overlayWindow;
        private readonly ChatSettings _settings;
        private readonly DispatcherTimer _stickyTimer;
        private readonly Timer _foregroundTimer;
        private readonly object _foregroundStateLock = new();

        private IntPtr _gameHwnd = IntPtr.Zero;
        private OverlayHelper.RECT? _lastGameRect;
        private bool _forceHidden;
        private bool _positionTrackingEnabled = true;
        private bool _stableForegroundAllowed;
        private bool _pendingForegroundAllowed;
        private bool _lastAppliedTopmost;
        private bool? _lastCanShowAuxiliaryWindows;
        private DateTime _foregroundChangedAt = DateTime.MinValue;

        private static readonly TimeSpan ForegroundStabilizationDelay = TimeSpan.FromMilliseconds(80);
        private static readonly TimeSpan ForegroundPollInterval = TimeSpan.FromMilliseconds(40);

        public double _dpiX = 1.0;
        public double _dpiY = 1.0;
        public event Action<bool>? AuxiliaryWindowVisibilityChanged;

        public WindowStickyService(Window window, ChatSettings settings)
        {
            _overlayWindow = window;
            _settings = settings;

            _stickyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _stickyTimer.Tick += (_, _) => UpdatePosition();
            _foregroundTimer = new Timer(_ => PollForegroundWindow(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        public void Start()
        {
            UpdateDpi();
            _foregroundTimer.Change(TimeSpan.Zero, ForegroundPollInterval);
            _stickyTimer.Start();
            UpdatePositionNow();
        }

        public void Stop()
        {
            _foregroundTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
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

        public void ResetGameWindow()
        {
            _gameHwnd = IntPtr.Zero;
            _lastGameRect = null;

            lock (_foregroundStateLock)
            {
                _stableForegroundAllowed = false;
                _pendingForegroundAllowed = false;
                _foregroundChangedAt = DateTime.MinValue;
            }

            _lastCanShowAuxiliaryWindows = null;
        }

        private void UpdatePosition()
        {
            if (_overlayWindow is Views.MainWindow mainWindow && mainWindow.IsSettingsPositionMode)
            {
                if (_overlayWindow.Visibility != Visibility.Visible)
                {
                    _overlayWindow.Visibility = Visibility.Visible;
                }

                if (_overlayWindow.Opacity != 1)
                {
                    _overlayWindow.Opacity = 1;
                }

                ApplyTopmostState(true);
                NotifyAuxiliaryWindowVisibilityChanged(true);
                return;
            }

            if (_forceHidden)
            {
                ApplyTopmostState(false);
                if (_overlayWindow.Visibility != Visibility.Collapsed)
                {
                    _overlayWindow.Visibility = Visibility.Collapsed;
                }
                return;
            }

            EnsureGameWindowHandle();

            bool hasGameWindow = _gameHwnd != IntPtr.Zero;
            bool isGameMinimized = hasGameWindow && NativeMethods.IsIconic(_gameHwnd);
            bool isForegroundAllowed = GetStableForegroundAllowedState();

            if (!hasGameWindow)
            {
                ApplyTopmostState(false);
                if (_overlayWindow.Visibility != Visibility.Collapsed)
                {
                    _overlayWindow.Visibility = Visibility.Collapsed;
                }
                if (_overlayWindow.Opacity != 0)
                {
                    _overlayWindow.Opacity = 0;
                }
                _lastGameRect = null;
                NotifyAuxiliaryWindowVisibilityChanged(_overlayWindow.Visibility == Visibility.Visible);
                return;
            }

            if (isGameMinimized && !_settings.AlwaysVisible)
            {
                ApplyTopmostState(false);
                if (_overlayWindow.Visibility != Visibility.Collapsed)
                {
                    _overlayWindow.Visibility = Visibility.Collapsed;
                }
                if (_overlayWindow.Opacity != 0)
                {
                    _overlayWindow.Opacity = 0;
                }
                _lastGameRect = null;
                NotifyAuxiliaryWindowVisibilityChanged(_overlayWindow.Visibility == Visibility.Visible);
                return;
            }

            if (_overlayWindow.Visibility != Visibility.Visible)
            {
                _overlayWindow.Visibility = Visibility.Visible;
            }

            if (_overlayWindow.Opacity != 1)
            {
                _overlayWindow.Opacity = 1;
            }

            bool shouldTopmost = _settings.AlwaysVisible || (hasGameWindow && !isGameMinimized && isForegroundAllowed);
            ApplyTopmostState(shouldTopmost);

            bool shouldTrackPosition = _positionTrackingEnabled && hasGameWindow && !isGameMinimized;
            if (!shouldTrackPosition)
            {
                NotifyAuxiliaryWindowVisibilityChanged(_overlayWindow.Visibility == Visibility.Visible);
                return;
            }

            UpdateDpi();
            var rect = OverlayHelper.GetActualRect(_gameHwnd);
            bool rectChanged = !_lastGameRect.HasValue || !AreRectsEqual(_lastGameRect.Value, rect);
            _lastGameRect = rect;

            if (!rectChanged)
            {
                NotifyAuxiliaryWindowVisibilityChanged(_overlayWindow.Visibility == Visibility.Visible);
                return;
            }

            double gameWidth = (rect.Right - rect.Left) / _dpiX;
            double gameHeight = (rect.Bottom - rect.Top) / _dpiY;
            double gameLeft = rect.Left / _dpiX;
            double gameTop = rect.Top / _dpiY;

            double marginBottom = _settings.LineMargin;
            double marginLeft = _settings.LineMarginLeft;

            double windowWidth = _overlayWindow.ActualWidth > 0 ? _overlayWindow.ActualWidth : _settings.WindowWidth;
            double windowHeight = _overlayWindow.ActualHeight > 0 ? _overlayWindow.ActualHeight : _settings.WindowHeight;

            double targetLeft = gameLeft + (gameWidth / 2.0) - (windowWidth / 2.0) + (marginLeft / _dpiX);
            double targetTop = gameTop + gameHeight - windowHeight - (marginBottom / _dpiY);

            if (Math.Abs(_overlayWindow.Left - targetLeft) > 0.1)
            {
                _overlayWindow.Left = targetLeft;
            }

            if (Math.Abs(_overlayWindow.Top - targetTop) > 0.1)
            {
                _overlayWindow.Top = targetTop;
            }

            NotifyAuxiliaryWindowVisibilityChanged(_overlayWindow.Visibility == Visibility.Visible);
        }

        private void EnsureGameWindowHandle()
        {
            if (_gameHwnd != IntPtr.Zero &&
                NativeMethods.IsWindow(_gameHwnd) &&
                OverlayHelper.IsWindowVisible(_gameHwnd))
            {
                return;
            }

            _gameHwnd = OverlayHelper.FindTalesWeaverWindow();
            if (_gameHwnd == IntPtr.Zero)
            {
                _lastGameRect = null;
            }
        }

        private void UpdateDpi()
        {
            var hwnd = new WindowInteropHelper(_overlayWindow).Handle;
            var source = PresentationSource.FromVisual(_overlayWindow);

            if (source?.CompositionTarget != null)
            {
                _dpiX = source.CompositionTarget.TransformToDevice.M11;
                _dpiY = source.CompositionTarget.TransformToDevice.M22;
            }
            else if (hwnd != IntPtr.Zero)
            {
                uint dpi = NativeMethods.GetDpiForWindow(hwnd);
                _dpiX = _dpiY = dpi / 96.0;
            }

            if (_dpiX <= 0) _dpiX = 1.0;
            if (_dpiY <= 0) _dpiY = 1.0;
        }

        private void PollForegroundWindow()
        {
            bool observedForegroundAllowed = OverlayHelper.IsForegroundAllowedOverlayWindow();

            lock (_foregroundStateLock)
            {
                if (observedForegroundAllowed == _stableForegroundAllowed)
                {
                    _pendingForegroundAllowed = observedForegroundAllowed;
                    _foregroundChangedAt = DateTime.MinValue;
                    return;
                }

                if (observedForegroundAllowed != _pendingForegroundAllowed)
                {
                    _pendingForegroundAllowed = observedForegroundAllowed;
                    _foregroundChangedAt = DateTime.UtcNow;
                    return;
                }

                if (_foregroundChangedAt == DateTime.MinValue ||
                    (DateTime.UtcNow - _foregroundChangedAt) < ForegroundStabilizationDelay)
                {
                    return;
                }

                _stableForegroundAllowed = observedForegroundAllowed;
                _foregroundChangedAt = DateTime.MinValue;
            }

            try
            {
                UpdatePositionImmediately();
            }
            catch
            {
            }
        }

        private bool GetStableForegroundAllowedState()
        {
            lock (_foregroundStateLock)
            {
                return _stableForegroundAllowed;
            }
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

        private void ApplyTopmostState(bool shouldTopmost)
        {
            IntPtr hwnd = new WindowInteropHelper(_overlayWindow).Handle;

            if (shouldTopmost)
            {
                if (!_overlayWindow.Topmost)
                {
                    _overlayWindow.Topmost = true;
                }

                if (hwnd != IntPtr.Zero)
                {
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

                _lastAppliedTopmost = true;
                return;
            }

            if (_overlayWindow.Topmost)
            {
                _overlayWindow.Topmost = false;
            }

            if (hwnd == IntPtr.Zero)
            {
                _lastAppliedTopmost = false;
                return;
            }

            NativeMethods.SetWindowPos(
                hwnd,
                NativeMethods.HWND_NOTOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_NOOWNERZORDER);

            if (_lastAppliedTopmost)
            {
                NativeMethods.SetWindowPos(
                    hwnd,
                    NativeMethods.HWND_BOTTOM,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE |
                    NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_NOOWNERZORDER);
            }

            _lastAppliedTopmost = false;
        }

        private static bool AreRectsEqual(OverlayHelper.RECT left, OverlayHelper.RECT right)
        {
            return left.Left == right.Left &&
                   left.Top == right.Top &&
                   left.Right == right.Right &&
                   left.Bottom == right.Bottom;
        }
    }
}
