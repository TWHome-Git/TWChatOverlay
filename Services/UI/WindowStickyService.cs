using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 오버레이 창의 표시/위치 상태를 제어합니다.
    /// 게임 창 감지 없이 설정 좌표 기준으로 동작합니다.
    /// 상시 폴링 없이, 상태가 바뀔 수 있는 계기(창 이동·표시 변경·설정 변경·트레이·잠금 해제)마다 한 번씩 맞춥니다.
    /// </summary>
    public class WindowStickyService
    {
        private readonly Window _overlayWindow;
        private readonly ChatSettings _settings;
        private static readonly DependencyPropertyDescriptor OpacityDescriptor =
            DependencyPropertyDescriptor.FromProperty(UIElement.OpacityProperty, typeof(Window));

        private bool _forceHidden;
        private bool _started;
        private bool _updating;
        private bool? _lastCanShowAuxiliaryWindows;

        public event Action<bool>? AuxiliaryWindowVisibilityChanged;

        public WindowStickyService(Window window, ChatSettings settings)
        {
            _overlayWindow = window;
            _settings = settings;
        }

        public void Start()
        {
            if (!_started)
            {
                _started = true;
                _overlayWindow.LocationChanged += OnWindowStateTrigger;
                _overlayWindow.IsVisibleChanged += OnWindowVisibilityTrigger;
                // Opacity 애니메이션 등 다른 코드가 바꿔 놓은 투명도도 되돌린다 (예전 타이머의 '복원' 역할)
                OpacityDescriptor.AddValueChanged(_overlayWindow, OnWindowStateTrigger);
                _settings.PropertyChanged += OnSettingsPropertyChanged;
                AppServices.Get<TrayAllWindowsService>().TrayStateChanged += OnBoolStateTrigger;
                // 잠금 해제 인스펙터는 WindowOpacityPercents 딕셔너리를 제자리에서 고치므로 설정 알림이 오지 않는다
                // → 잠금으로 돌아오는 시점에 다시 맞춘다
                AppServices.Get<UiLockService>().UnlockChanged += OnBoolStateTrigger;
            }

            UpdatePositionNow();
        }

        public void Stop()
        {
            if (!_started)
                return;

            _started = false;
            _overlayWindow.LocationChanged -= OnWindowStateTrigger;
            _overlayWindow.IsVisibleChanged -= OnWindowVisibilityTrigger;
            OpacityDescriptor.RemoveValueChanged(_overlayWindow, OnWindowStateTrigger);
            _settings.PropertyChanged -= OnSettingsPropertyChanged;
            try { AppServices.Get<TrayAllWindowsService>().TrayStateChanged -= OnBoolStateTrigger; } catch { }
            try { AppServices.Get<UiLockService>().UnlockChanged -= OnBoolStateTrigger; } catch { }
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

        private void OnWindowStateTrigger(object? sender, EventArgs e)
        {
            // 우리가 Left/Top/Opacity를 되돌리는 중에 다시 들어온 알림은 무시한다
            if (!_updating)
                UpdatePosition();
        }

        private void OnWindowVisibilityTrigger(object sender, DependencyPropertyChangedEventArgs e) => OnWindowStateTrigger(sender, EventArgs.Empty);

        private void OnBoolStateTrigger(bool _) => UpdatePositionImmediately();

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 빈 이름 = 설정 전체 교체(프로필 불러오기·초기화)
            if (string.IsNullOrEmpty(e.PropertyName)
                || e.PropertyName == nameof(ChatSettings.LineMargin)
                || e.PropertyName == nameof(ChatSettings.LineMarginLeft)
                || e.PropertyName == nameof(ChatSettings.WindowOpacityPercents))
            {
                UpdatePositionImmediately();
            }
        }

        private void UpdatePosition()
        {
            if (_updating)
                return;

            _updating = true;
            try
            {
                UpdatePositionCore();
            }
            finally
            {
                _updating = false;
            }
        }

        private void UpdatePositionCore()
        {
            // 모든 창 트레이 중에는 어떤 모드(설정 모드 포함)에서도 자동으로 다시 띄우지 않는다
            if (AppServices.Get<TrayAllWindowsService>().IsTrayed)
            {
                return;
            }

            if (_overlayWindow is Views.MainWindow mainWindow && mainWindow.IsSettingsPositionMode)
            {
                ShowOverlay();
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

            // 잠금 해제 모드에서는 드래그 중인 창을 저장 좌표로 되돌리지 않는다.
            if (AppServices.Get<UiLockService>().IsUnlocked)
            {
                NotifyAuxiliaryWindowVisibilityChanged(true);
                return;
            }

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

            NotifyAuxiliaryWindowVisibilityChanged(true);
        }

        private void ShowOverlay()
        {
            if (_overlayWindow.Visibility != Visibility.Visible)
            {
                _overlayWindow.Visibility = Visibility.Visible;
            }

            // 잠금 해제 인스펙터에서 지정한 창별 투명도가 있으면 그 값을 목표로 한다
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
