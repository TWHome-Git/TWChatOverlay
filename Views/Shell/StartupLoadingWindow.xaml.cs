using System.Windows;

namespace TWChatOverlay.Views
{
    public partial class StartupLoadingWindow : Window
    {
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
    }
}
