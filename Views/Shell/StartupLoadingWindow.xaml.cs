using System;
using System.Windows;

namespace TWChatOverlay.Views
{
    public partial class StartupLoadingWindow : Window
    {
        public event EventHandler? CancelRequested;

        public StartupLoadingWindow()
        {
            InitializeComponent();
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
