using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class MemoTextOnlyWindow : Window
    {
        public MemoTextOnlyWindow()
        {
            InitializeComponent();
            WindowFontService.Apply(this);
            Loaded += (_, _) =>
            {
                UpdateTextBounds();
                EnableMouseClickThrough();
            };
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
            byte alpha = (byte)Math.Clamp((int)Math.Round((opacity / 100.0) * 255.0), 0, 255);
            Color baseColor = Colors.Black;
            if (Application.Current?.Resources["OverlayShellBackgroundBrush"] is SolidColorBrush themedBrush)
                baseColor = themedBrush.Color;

            MemoText.Background = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            OverlayBackground.Background = Brushes.Transparent;
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Close();
            }
            else
            {
                DragMove();
            }
        }

        private void EnableMouseClickThrough()
        {
            IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            int applied = exStyle | NativeMethods.WS_EX_TRANSPARENT;
            if (applied != exStyle)
            {
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, applied);
            }
        }

        private void UpdateTextBounds()
        {
            double maxWidth = Math.Max(0, ActualWidth - MemoText.Margin.Left - MemoText.Margin.Right);
            MemoText.MaxWidth = maxWidth;
        }
    }
}
