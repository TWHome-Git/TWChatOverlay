using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class RecaptureSupplyWindow : Window
    {
        private const double MinimumWindowWidth = 240;
        private const double MinimumWindowHeight = 180;
        private const int ResizeBorderThickness = 6;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTCLIENT = 1;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        public RecaptureSupplyWindow(string imagePath)
        {
            InitializeComponent();
            SourceInitialized += RecaptureSupplyWindow_SourceInitialized;
            LoadImage(imagePath);
        }

        private void RecaptureSupplyWindow_SourceInitialized(object? sender, EventArgs e)
        {
            try
            {
                HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
            }
            catch { }
        }

        private void LoadImage(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                    throw new FileNotFoundException("Recapture supply image cache was missing.", imagePath);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                SupplyImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load recapture supply image.", ex);
                Close();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    DragMove();
                }
            }
            catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
            {
                if (msg != WM_NCHITTEST)
                    return IntPtr.Zero;

                Point screenPoint = new(
                    unchecked((short)(lParam.ToInt64() & 0xFFFF)),
                    unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF)));

                Point localPoint = PointFromScreen(screenPoint);

                if (WindowState == WindowState.Maximized)
                    return IntPtr.Zero;

                bool left = localPoint.X <= ResizeBorderThickness;
                bool right = localPoint.X >= ActualWidth - ResizeBorderThickness;
                bool top = localPoint.Y <= ResizeBorderThickness;
                bool bottom = localPoint.Y >= ActualHeight - ResizeBorderThickness;

                if (left && top) { handled = true; return (IntPtr)HTTOPLEFT; }
                if (right && top) { handled = true; return (IntPtr)HTTOPRIGHT; }
                if (left && bottom) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
                if (right && bottom) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
                if (left) { handled = true; return (IntPtr)HTLEFT; }
                if (right) { handled = true; return (IntPtr)HTRIGHT; }
                if (top) { handled = true; return (IntPtr)HTTOP; }
                if (bottom) { handled = true; return (IntPtr)HTBOTTOM; }
            }
            catch { }

            return IntPtr.Zero;
        }
    }
}
