using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class ExperienceWeeklyRefreshPromptWindow : Window
    {
        private static readonly Regex DigitsOnlyRegex = new(@"^\d+$", RegexOptions.Compiled);
        public event EventHandler? RefreshRequested;

        public ExperienceWeeklyRefreshPromptWindow()
        {
            InitializeComponent();
            WindowFontService.Apply(this);
        }

        public void SetBusy(bool isBusy)
        {
            RefreshButton.IsEnabled = !isBusy;
            ExpEokTextBox.IsEnabled = !isBusy;
            RefreshButton.Content = isBusy ? "갱신 중..." : "현재 누적 경험치 갱신";
        }

        public bool TryGetEnteredExp(out long expValue)
        {
            expValue = 0;
            string text = (ExpEokTextBox.Text ?? string.Empty).Trim().Replace(",", string.Empty).Replace("억", string.Empty);
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long eok))
                return false;
            if (eok < 0)
                return false;
            expValue = checked(eok * 100_000_000L);
            return true;
        }

        public void SetStatus(string text, bool isSuccess = false)
        {
            StatusTextBlock.Text = text ?? string.Empty;
            StatusTextBlock.Foreground = isSuccess
                ? new SolidColorBrush(Color.FromRgb(0x8D, 0xE8, 0xA1))
                : new SolidColorBrush(Color.FromRgb(0x9F, 0xD0, 0xFF));
        }

        public void PositionToTalesWeaverCenter()
        {
            var workArea = SystemParameters.WorkArea;
            double left = workArea.Left + ((workArea.Width - Width) / 2.0);
            double top = workArea.Top + ((workArea.Height - Height) / 2.0);

            IntPtr gameHwnd = OverlayHelper.FindTalesWeaverWindow();
            if (gameHwnd != IntPtr.Zero)
            {
                var rect = OverlayHelper.GetActualRect(gameHwnd);
                var dpi = VisualTreeHelper.GetDpi(this);
                double dpiX = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
                double dpiY = dpi.DpiScaleY <= 0 ? 1.0 : dpi.DpiScaleY;
                double gameLeft = rect.Left / dpiX;
                double gameTop = rect.Top / dpiY;
                double gameWidth = (rect.Right - rect.Left) / dpiX;
                double gameHeight = (rect.Bottom - rect.Top) / dpiY;
                double targetWidth = ActualWidth > 0 ? ActualWidth : Width;
                double targetHeight = ActualHeight > 0 ? ActualHeight : Height;

                left = gameLeft + ((gameWidth - targetWidth) / 2.0);
                top = gameTop + ((gameHeight - targetHeight) / 2.0);
            }

            Left = left;
            Top = top;
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

            try { DragMove(); } catch { }
        }

        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        private void ExpEokTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !DigitsOnlyRegex.IsMatch(e.Text);
        }

        private void ExpEokTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            string text = (e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty).Trim();
            if (!DigitsOnlyRegex.IsMatch(text))
                e.CancelCommand();
        }
    }
}
