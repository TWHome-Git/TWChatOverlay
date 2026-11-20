using System.Windows;
using System.Windows.Input;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class UpdateDialogWindow : Window
    {
        public UpdateDialogWindow(
            string windowTitle,
            string headline,
            string currentVersion,
            string latestVersion,
            string releaseNotes,
            string footerHint,
            string updateButtonText,
            string? cancelButtonText = null)
        {
            InitializeComponent();
            WindowFontService.Apply(this);

            Title = windowTitle;
            WindowTitleText.Text = windowTitle;
            HeadlineText.Text = headline;
            HeadlineText.Visibility = string.IsNullOrWhiteSpace(headline) ? Visibility.Collapsed : Visibility.Visible;
            CurrentVersionText.Text = currentVersion;
            LatestVersionText.Text = latestVersion;
            ReleaseNotesTextBox.Text = string.IsNullOrWhiteSpace(releaseNotes)
                ? "이번 릴리스에 공개된 변경 사항이 없습니다."
                : releaseNotes.Trim();
            FooterHintText.Text = footerHint;
            UpdateButton.Content = updateButtonText;
            // 필수 업데이트에서는 "취소" 대신 무슨 일이 일어나는지 적는다 ("앱 종료")
            if (!string.IsNullOrWhiteSpace(cancelButtonText))
                CancelButton.Content = cancelButtonText;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
