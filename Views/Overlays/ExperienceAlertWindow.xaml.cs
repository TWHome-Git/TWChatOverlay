using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class ExperienceAlertWindow : Window
    {
        private ChatSettings _settings;
        private bool _isDragging;

        public ExperienceAlertWindow(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            InitializeComponent();
            WindowFontService.Apply(this);
            LocationChanged += (_, _) => SyncPositionToSettings(notify: false);
        }

        public void SetSettings(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public void SetMessage(string message)
        {
            MessageTextBlock.Text = message;
        }

        public void BringToFront()
        {
            TopmostWindowHelper.BringToTopmost(this);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyToolWindowStyle();
        }

        protected override void OnClosed(EventArgs e)
        {
            SyncPositionToSettings(notify: true);
            base.OnClosed(e);
        }

        private void ApplyToolWindowStyle()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW);
            }
            catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed || !IsVisible)
                return;

            _isDragging = true;
            try { DragMove(); } catch { }
            finally
            {
                _isDragging = false;
                SyncPositionToSettings(notify: true);
            }
        }

        private void SyncPositionToSettings(bool notify)
        {
            if (_settings == null || !_settings.ShowExperienceLimitAlertWindow || !IsVisible)
                return;

            _settings.ExperienceLimitAlertWindowLeft = Left;
            _settings.ExperienceLimitAlertWindowTop = Top;

            if (_isDragging || notify)
                ConfigService.SaveDeferred(_settings);
        }
    }
}
