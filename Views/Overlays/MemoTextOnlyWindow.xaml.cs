using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class MemoTextOnlyWindow : Window
    {
        private readonly DispatcherTimer _visibilityTimer;

        public MemoTextOnlyWindow()
        {
            InitializeComponent();
            WindowFontService.Apply(this);
            _visibilityTimer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(250) };
            _visibilityTimer.Tick += (_, _) => UpdateVisibilityByGameWindowState();
            Loaded += (_, _) =>
            {
                _visibilityTimer.Start();
                UpdateTextBounds();
                EnableMouseClickThrough();
            };
            Closed += (_, _) => _visibilityTimer.Stop();
            SizeChanged += (_, _) => UpdateTextBounds();
        }

        public void SetText(string text)
        {
            MemoText.Text = text;
            UpdateTextBounds();
        }
        public void SetStyle(Brush foreground, double fontSize, FontWeight weight, FontStyle style)
        {
            MemoText.Foreground = foreground;
            MemoText.FontSize = fontSize;
            MemoText.FontWeight = weight;
            MemoText.FontStyle = style;
            UpdateTextBounds();
        }
        public void SetBackgroundOpacity(double opacity)
        {
            byte alpha = (byte)System.Math.Clamp((int)System.Math.Round((opacity / 100.0) * 255.0), 0, 255);
            MemoText.Background = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0));
            OverlayBackground.Background = Brushes.Transparent;
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) Close();
            else DragMove();
        }

        private void EnableMouseClickThrough()
        {
            IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            int applied = exStyle | NativeMethods.WS_EX_TRANSPARENT;
            if (applied != exStyle)
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, applied);
        }

        private void UpdateVisibilityByGameWindowState()
        {
            bool shouldShow = IsTalesWeaverWindowActive();
            if (shouldShow && Visibility != Visibility.Visible)
                Visibility = Visibility.Visible;
            else if (!shouldShow && Visibility != Visibility.Hidden)
                Visibility = Visibility.Hidden;
        }

        private static bool IsTalesWeaverWindowActive()
        {
            var hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == System.IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return false;

            int len = NativeMethods.GetWindowTextLength(hwnd);
            if (len <= 0)
                return false;

            var sb = new System.Text.StringBuilder(len + 1);
            _ = NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString();

            return title.Contains("TalesWeaver", System.StringComparison.OrdinalIgnoreCase) ||
                   title.Contains("테일즈위버", System.StringComparison.Ordinal);
        }

        private void UpdateTextBounds()
        {
            double maxWidth = System.Math.Max(0, ActualWidth - MemoText.Margin.Left - MemoText.Margin.Right);
            MemoText.MaxWidth = maxWidth;
        }
    }
}
