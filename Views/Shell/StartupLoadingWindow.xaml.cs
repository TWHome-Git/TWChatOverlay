using System;
using System.Windows;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class StartupLoadingWindow : Window
    {
        public event EventHandler? CancelRequested;

        public StartupLoadingWindow()
        {
            InitializeComponent();
            WindowFontService.Apply(this); // 다른 창과 같은 앱 글꼴 (빠져 있어 시스템 기본 글꼴로 보였다)
        }

        public void UpdateProgress(double value, string statusText)
        {
            double clamped = value < 0 ? 0 : value > 100 ? 100 : value;
            ProgressBar.Value = clamped;
            StatusText.Text = statusText;
        }

        public void UpdateProgress(double value, string statusText, string dateText)
        {
            double clamped = value < 0 ? 0 : value > 100 ? 100 : value;
            ProgressBar.Value = clamped;
            StatusText.Text = statusText;
            DateText.Text = string.IsNullOrWhiteSpace(dateText)
                ? string.Empty
                : $"현재 처리: {dateText}";
        }

        public void SetCancelEnabled(bool enabled)
        {
            CancelButton.IsEnabled = enabled;
            CancelButton.Content = enabled ? "취소" : "취소됨";
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            SetCancelEnabled(false);
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
