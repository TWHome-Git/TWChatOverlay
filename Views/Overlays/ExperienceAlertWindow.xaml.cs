using System;
using System.Windows;
using System.Windows.Interop;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class ExperienceAlertWindow : Window
    {
        public ExperienceAlertWindow()
        {
            InitializeComponent();
        }

        public void SetMessage(string message)
        {
            MessageTextBlock.Text = message;
        }

        public void BringToFront()
        {
            Topmost = false;
            Topmost = true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyToolWindowStyle();
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
    }
}
