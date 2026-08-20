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
        /// 전체 화면 딤 + 격자 배경. 항상 모든 창(게임 포함)의 맨 뒤에 깔려
        /// 게임 화면을 가리지 않고, 빈 바탕 영역에만 격자가 보인다. 클릭은 통과.
        /// </summary>
        private sealed class BackdropWindow : Window
        {
            public BackdropWindow()
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = false;
                ResizeMode = ResizeMode.NoResize;
                Left = SystemParameters.VirtualScreenLeft;
                Top = SystemParameters.VirtualScreenTop;
                Width = SystemParameters.VirtualScreenWidth;
                Height = SystemParameters.VirtualScreenHeight;
                IsHitTestVisible = false;
                Focusable = false;

                var root = new Grid();
                // 배경은 완전 투명, 격자만 표시
                root.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Opacity = 0.5,
                    Fill = CreateGridBrush()
                });
                Content = root;

                SourceInitialized += (_, _) =>
                {
                    MakeClickThrough();
                    PinToBottom();
                };
                IsVisibleChanged += (_, e) =>
                {
                    if (e.NewValue is true) SendToBottom();
                };
            }

            /// <summary>
            /// WM_WINDOWPOSCHANGING을 가로채 z-order 변경이 생길 때마다 HWND_BOTTOM으로 강제.
            /// 한 번 내리는 것만으로는 이후 다른 코드/OS가 다시 올릴 수 있어 항상-맨뒤로 고정한다.
            /// </summary>
            private void PinToBottom()
            {
                try
                {
                    var source = (HwndSource?)PresentationSource.FromVisual(this);
                    source?.AddHook(ForceBottomHook);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Backdrop bottom-pin hook setup failed.", ex);
                }
            }

            private static IntPtr ForceBottomHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                const int WM_WINDOWPOSCHANGING = 0x0046;
                if (msg == WM_WINDOWPOSCHANGING)
                {
                    var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.WINDOWPOS>(lParam);
                    const int SWP_NOZORDER = 0x0004;
                    pos.hwndInsertAfter = new IntPtr(1); // HWND_BOTTOM
                    pos.flags &= ~SWP_NOZORDER;
                    System.Runtime.InteropServices.Marshal.StructureToPtr(pos, lParam, false);
                }
                return IntPtr.Zero;
            }

            /// <summary>모든 창(게임 포함)의 맨 뒤로 보낸다.</summary>
            private void SendToBottom()
            {
                try
                {
                    var handle = new WindowInteropHelper(this).Handle;
                    if (handle == IntPtr.Zero) return;

                    var HWND_BOTTOM = new IntPtr(1);
                    const int SWP_NOMOVE = 0x0002;
                    const int SWP_NOSIZE = 0x0001;
                    const int SWP_NOACTIVATE = 0x0010;
                    NativeMethods.SetWindowPos(handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Backdrop send-to-bottom failed.", ex);
                }
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
                var line = Color.FromArgb(0x70, 0x0C, 0xD2, 0x9D);
                var pen = new Pen(new SolidColorBrush(line), 2);
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

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int flags);

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            public struct WINDOWPOS
            {
                public IntPtr hwnd;
                public IntPtr hwndInsertAfter;
                public int x;
                public int y;
                public int cx;
                public int cy;
                public int flags;
            }
        }
    }
}
