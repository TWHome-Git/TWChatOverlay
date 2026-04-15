using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class BuffTrackerWindow : Window
    {
        public static BuffTrackerWindow? Instance { get; private set; }

        private readonly BuffTrackerService _tracker;
        private readonly ChatSettings _settings;

        public BuffTrackerWindow(BuffTrackerService tracker, ChatSettings settings)
        {
            InitializeComponent();
            Instance = this;
            _tracker = tracker;
            _settings = settings;
            DataContext = tracker;
            _tracker.PropertyChanged += Tracker_PropertyChanged;
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

        public void ApplyVisibility()
        {
            if (_settings.EnableBuffTrackerAlert && _tracker.HasAnyActiveBuffs)
            {
                if (!IsVisible)
                    Show();
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
    }
}
