using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class BuffTrackerWindow : Window
    {
        public static BuffTrackerWindow? Instance { get; private set; }

        private readonly BuffTrackerService _tracker;
        private readonly ChatSettings _settings;
        private bool _isTopmostRefreshQueued;

        public BuffTrackerWindow(BuffTrackerService tracker, ChatSettings settings)
        {
            InitializeComponent();
            SettingsHostZOrder.Register(this); // 설정 창이 열려 있으면 그 아래로 표시
            WindowFontService.Apply(this);
            Instance = this;
            _tracker = tracker;
            _settings = settings;
            DataContext = tracker;
            _tracker.PropertyChanged += Tracker_PropertyChanged;
            _tracker.ActiveRareBuffs.CollectionChanged += TrackerBuffs_CollectionChanged;
            _tracker.ActiveExpBuffs.CollectionChanged += TrackerBuffs_CollectionChanged;
            UiLockService.UnlockChanged += OnUnlockChanged;
            // 표시는 호출자(ApplyBuffTrackerWindowSettings)가 위치를 맞춘 뒤 ApplyVisibility로 결정한다
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyMousePassthroughStyle();
        }

        protected override void OnClosed(EventArgs e)
        {
            _tracker.PropertyChanged -= Tracker_PropertyChanged;
            _tracker.ActiveRareBuffs.CollectionChanged -= TrackerBuffs_CollectionChanged;
            _tracker.ActiveExpBuffs.CollectionChanged -= TrackerBuffs_CollectionChanged;
            UiLockService.UnlockChanged -= OnUnlockChanged;

            if (ReferenceEquals(Instance, this))
                Instance = null;

            base.OnClosed(e);
        }

        private void Tracker_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(BuffTrackerService.HasAnyActiveBuffs))
            {
                Dispatcher.BeginInvoke(new Action(ApplyVisibility));
            }
        }

        private void TrackerBuffs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // The tracker refreshes these collections every second while buffs are active.
            QueueTopmostRefresh();
        }

        public void ApplyVisibility()
        {
            // 트레이로 최소화된 동안에는 버프 변화가 창을 다시 띄우지 않게 한다
            if (TrayAllWindowsService.IsTrayed)
                return;

            // 잠금 해제 중에는 최대 크기 미리보기(도우미 창)가 대신 표시된다
            if (UiLockService.IsUnlocked)
                return;

            if (_settings.EnableBuffTrackerAlert && _tracker.HasAnyActiveBuffs)
            {
                if (!IsVisible)
                    Show();

                BringToFront();
                QueueTopmostRefresh();
            }
            else if (IsVisible)
            {
                // 대기 중인 창을 상주시키지 않는다 — 버프가 다시 뜨면 MainWindow가 새로 만든다
                Close();
            }
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!UiLockService.IsUnlocked) return;
            UiLockService.Select(this);
            if (e.ButtonState != MouseButtonState.Pressed)
                return;

            WindowDragBehavior.BeginDrag(this, e);

            // 드래그가 끝난 현재 위치를 공유 설정에 저장하고 도우미 창과 동기화
            _settings.SetBuffTrackerWindowPosition(Left, Top, notify: false);
            var helper = BuffTrackerHelperWindow.Instance;
            if (helper != null)
            {
                helper.Left = Left;
                helper.Top = Top;
            }
            ConfigService.SaveDeferred(_settings);
            e.Handled = true;
        }

        /// <summary>평소에는 클릭 통과, 잠금 해제 모드에서는 잡고 끌 수 있게 통과를 해제한다.</summary>
        private void ApplyMousePassthroughStyle()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE) | NativeMethods.WS_EX_TOOLWINDOW;

                if (UiLockService.IsUnlocked)
                    exStyle &= ~NativeMethods.WS_EX_TRANSPARENT;
                else
                    exStyle |= NativeMethods.WS_EX_TRANSPARENT;

                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
            }
            catch { }
        }

        private void OnUnlockChanged(bool unlocked)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    ApplyMousePassthroughStyle();
                    if (unlocked)
                        Close(); // 미리보기(도우미)가 대신 표시되고, 해제 후 MainWindow가 다시 만든다
                    else
                        ApplyVisibility();
                });
            }
            catch { }
        }

        private void BringToFront()
        {
            if (!IsVisible)
                return;

            // 설정 창이 열려 있는 동안에는 최상단을 양보해 설정 화면을 가리지 않는다
            if (IsSettingsHostVisible())
            {
                if (Topmost)
                    Topmost = false;
                return;
            }

            TopmostWindowHelper.BringToTopmost(this);
        }

        /// <summary>설정/서브 메뉴 호스트 창이 화면에 떠 있는지.</summary>
        private static bool IsSettingsHostVisible()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is SubMenuWindow && window.IsVisible)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private void QueueTopmostRefresh()
        {
            if (_isTopmostRefreshQueued)
                return;

            if (!IsVisible || !_settings.EnableBuffTrackerAlert || !_tracker.HasAnyActiveBuffs)
                return;

            _isTopmostRefreshQueued = true;
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    _isTopmostRefreshQueued = false;
                    if (IsVisible && _settings.EnableBuffTrackerAlert && _tracker.HasAnyActiveBuffs)
                    {
                        BringToFront(); // 설정 창이 닫히면 다음 틱(1초 이내)에 최상단 복귀
                    }
                }),
                DispatcherPriority.Background);
        }
    }
}
