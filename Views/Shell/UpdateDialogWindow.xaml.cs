using System;
using System.Windows;
using System.Windows.Input;

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
            string updateButtonText)
        {
            InitializeComponent();

            Title = windowTitle;
            WindowTitleText.Text = windowTitle;
            HeadlineText.Text = headline;
            CurrentVersionText.Text = currentVersion;
            LatestVersionText.Text = latestVersion;
            ReleaseNotesTextBox.Text = string.IsNullOrWhiteSpace(releaseNotes)
                ? "이번 릴리스에 공개된 변경 사항이 없습니다."
                : releaseNotes.Trim();
            FooterHintText.Text = footerHint;
            UpdateButton.Content = updateButtonText;
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
