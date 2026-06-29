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
            if (!_settings.ShowBuffTrackerWindow)
                return;

            if (e.ButtonState != MouseButtonState.Pressed)
                return;

            try { DragMove(); } catch { }
        }

        private void ApplyMousePassthroughStyle()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW);
            }
            catch { }
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
