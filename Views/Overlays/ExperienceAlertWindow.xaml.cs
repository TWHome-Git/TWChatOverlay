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
            MessageTextBlock.FontSize = _settings.ExperienceAlertFontSize;
            LocationChanged += (_, _) => SyncPositionToSettings(notify: false);
        }

        public void SetSettings(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            MessageTextBlock.FontSize = _settings.ExperienceAlertFontSize;
        }

        public void SetMessage(string message)
        {
            MessageTextBlock.Text = message;
        }

        /// <summary>잠금 해제 인스펙터에서 폰트 크기 변경 시 즉시 반영.</summary>
        public void SetFontSize(double size)
        {
            MessageTextBlock.FontSize = size;
            PreviewLabel.FontSize = size;
        }

        /// <summary>위치 미리보기: 통일 라벨("경험치 누적 알림창")만 표시.</summary>
        public void SetPreviewMode(bool isPreview)
        {
            NormalContent.Visibility = isPreview ? Visibility.Collapsed : Visibility.Visible;
            PreviewLabel.Visibility = isPreview ? Visibility.Visible : Visibility.Collapsed;
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
            if (!UiLockService.IsUnlocked) return;
            UiLockService.Select(this);
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
            if (_settings == null || !IsVisible)
                return;

            _settings.ExperienceLimitAlertWindowLeft = Left;
            _settings.ExperienceLimitAlertWindowTop = Top;

            if (_isDragging || notify)
                ConfigService.SaveDeferred(_settings);
        }
    }
}
