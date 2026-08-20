using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 잠금 해제 모드(Unlock Mode): 오버레이 창 이동은 이 모드에서만 허용됩니다.
    /// 모드가 켜지면 화면 전체에 은은한 격자 배경과 안내/완료 버튼이 표시되고,
    /// 각 창을 드래그해 배치할 수 있습니다. 모드를 끝내면 모든 창이 잠깁니다.
    /// </summary>
    public static class UiLockService
    {
        private static UnlockGridWindow? _gridWindow;

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
                    _gridWindow ??= new UnlockGridWindow();
                    _gridWindow.Show();
                }
                else
                {
                    _gridWindow?.Hide();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Unlock grid window toggle failed.", ex);
            }

            AppLogger.Info($"UI unlock mode -> {unlocked}.");
            UnlockChanged?.Invoke(unlocked);
        }

        /// <summary>
        /// 잠금 해제 모드 배경: 전체 화면 격자 + 상단 안내/완료 버튼.
        /// 격자 영역은 클릭이 통과(hit-test 없음)하고, 완료 버튼만 입력을 받는다.
        /// </summary>
        private sealed class UnlockGridWindow : Window
        {
            public UnlockGridWindow()
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
                Background = Brushes.Transparent;

                // 배경 브러시를 두지 않아야(null) 격자 영역의 클릭이 아래 창으로 통과한다.
                var root = new Grid { Background = null };

                // 은은한 격자 (32px 간격, 민트 라인)
                var gridRect = new System.Windows.Shapes.Rectangle
                {
                    IsHitTestVisible = false,
                    Opacity = 0.35,
                    Fill = CreateGridBrush()
                };
                root.Children.Add(gridRect);

                // 상단 중앙 안내 + 완료 버튼
                var banner = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 18, 0, 0),
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
                root.Children.Add(banner);

                Content = root;

                // ESC로 종료
                PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.Escape) Set(false);
                };
            }

            private static DrawingBrush CreateGridBrush()
            {
                var line = Color.FromArgb(0x50, 0x0C, 0xD2, 0x9D);
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
    }
}
