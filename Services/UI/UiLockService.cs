using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 잠금 해제 모드(Unlock Mode): 오버레이 창 이동은 이 모드에서만 허용됩니다.
    /// 모드가 켜지면 화면 전체가 살짝 어두워지며 격자가 표시되고(클릭 통과),
    /// 주 모니터 상단 중앙에 안내/완료 배너가 뜹니다. 모드를 끝내면 모든 창이 잠깁니다.
    /// </summary>
    public static class UiLockService
    {
        private static BackdropWindow? _backdrop;
        private static BannerWindow? _banner;

        public static bool IsUnlocked { get; private set; }

        /// <summary>모드 변경 시 발생. 인자 = 새 잠금 해제 상태.</summary>
        public static event Action<bool>? UnlockChanged;

        public static void Toggle() => Set(!IsUnlocked);

        public static void Set(bool unlocked)
        {
            if (IsUnlocked == unlocked) return;
            IsUnlocked = unlocked;

            try
            {
                if (unlocked)
                {
                    _backdrop ??= new BackdropWindow();
                    _banner ??= new BannerWindow();
                    _backdrop.Show();
                    _banner.Show();
                }
                else
                {
                    _banner?.Hide();
                    _backdrop?.Hide();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Unlock overlay toggle failed.", ex);
            }

            AppLogger.Info($"UI unlock mode -> {unlocked}.");
            UnlockChanged?.Invoke(unlocked);
        }

        /// <summary>
        /// 전체 화면 딤 + 격자 배경. WS_EX_TRANSPARENT로 모든 클릭이 아래 창으로 통과한다.
        /// </summary>
        private sealed class BackdropWindow : Window
        {
            public BackdropWindow()
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                Left = SystemParameters.VirtualScreenLeft;
                Top = SystemParameters.VirtualScreenTop;
                Width = SystemParameters.VirtualScreenWidth;
                Height = SystemParameters.VirtualScreenHeight;
                IsHitTestVisible = false;
                Focusable = false;

                var root = new Grid();
                // 살짝 회색빛 딤 — 격자가 눈에 띄도록
                root.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(0x3C, 0x18, 0x1B, 0x1A))
                });
                root.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Opacity = 0.45,
                    Fill = CreateGridBrush()
                });
                Content = root;

                SourceInitialized += (_, _) => MakeClickThrough();
            }

            /// <summary>WS_EX_TRANSPARENT + WS_EX_NOACTIVATE: 모든 마우스 입력이 아래 창으로 통과.</summary>
            private void MakeClickThrough()
            {
                try
                {
                    var handle = new WindowInteropHelper(this).Handle;
                    const int GWL_EXSTYLE = -20;
                    const int WS_EX_TRANSPARENT = 0x00000020;
                    const int WS_EX_NOACTIVATE = 0x08000000;
                    const int WS_EX_TOOLWINDOW = 0x00000080;
                    int ex = NativeMethods.GetWindowLong(handle, GWL_EXSTYLE);
                    NativeMethods.SetWindowLong(handle, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Backdrop click-through setup failed.", ex);
                }
            }

            private static DrawingBrush CreateGridBrush()
            {
                var line = Color.FromArgb(0x58, 0x0C, 0xD2, 0x9D);
                var pen = new Pen(new SolidColorBrush(line), 1);
                var drawing = new GeometryDrawing
                {
                    Pen = pen,
                    Geometry = new GeometryGroup
                    {
                        Children =
                        {
                            new LineGeometry(new Point(0, 0), new Point(32, 0)),
                            new LineGeometry(new Point(0, 0), new Point(0, 32)),
                        }
                    }
                };
                var brush = new DrawingBrush(drawing)
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, 32, 32),
                    ViewportUnits = BrushMappingMode.Absolute,
                };
                brush.Freeze();
                return brush;
            }
        }

        /// <summary>주 모니터 상단 중앙의 안내 + 완료 버튼 배너.</summary>
        private sealed class BannerWindow : Window
        {
            public BannerWindow()
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                SizeToContent = SizeToContent.WidthAndHeight;

                var banner = new Border
                {
                    Padding = new Thickness(14, 8, 10, 8),
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(1),
                };
                banner.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
                banner.SetResourceReference(Border.BorderBrushProperty, "OverlayAccentBorderBrush");

                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                var text = new TextBlock
                {
                    Text = "잠금 해제 모드 — 창을 드래그해 배치하세요",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                };
                text.SetResourceReference(TextBlock.ForegroundProperty, "OverlayInfoTextBrush");
                panel.Children.Add(text);

                var doneButton = new Button
                {
                    Content = "완료",
                    Padding = new Thickness(16, 4, 16, 4),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand,
                    Foreground = Brushes.White,
                };
                doneButton.SetResourceReference(FrameworkElement.StyleProperty, "SimButtonStyle");
                doneButton.Click += (_, _) => Set(false);
                panel.Children.Add(doneButton);

                banner.Child = panel;
                Content = banner;

                // 주 모니터 상단 중앙 배치 (크기 확정 후)
                Loaded += (_, _) => PositionTopCenter();
                SizeChanged += (_, _) => PositionTopCenter();

                PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.Escape) Set(false);
                };
            }

            private void PositionTopCenter()
            {
                double width = ActualWidth > 0 ? ActualWidth : 320;
                Left = (SystemParameters.PrimaryScreenWidth - width) / 2.0;
                Top = 16;
            }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        }
    }
}
