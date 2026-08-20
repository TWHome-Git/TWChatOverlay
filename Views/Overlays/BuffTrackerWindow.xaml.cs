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
            WindowFontService.Apply(this);
            Instance = this;
            _tracker = tracker;
            _settings = settings;
            DataContext = tracker;
            _tracker.PropertyChanged += Tracker_PropertyChanged;
            _tracker.ActiveRareBuffs.CollectionChanged += TrackerBuffs_CollectionChanged;
            _tracker.ActiveExpBuffs.CollectionChanged += TrackerBuffs_CollectionChanged;
            UiLockService.UnlockChanged += OnUnlockChanged;
            ApplyVisibility();
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

            if (_settings.EnableBuffTrackerAlert && _tracker.HasAnyActiveBuffs)
            {
                if (!IsVisible)
                    Show();

                BringToFront();
                QueueTopmostRefresh();
            }
            else if (IsVisible)
            {
                Hide();
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
            try { Dispatcher.Invoke(ApplyMousePassthroughStyle); } catch { }
        }

        private void BringToFront()
        {
            if (!IsVisible)
                return;

            TopmostWindowHelper.BringToTopmost(this);
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
                        BringToFront();
                    }
                }),
                DispatcherPriority.Background);
        }
    }
}
