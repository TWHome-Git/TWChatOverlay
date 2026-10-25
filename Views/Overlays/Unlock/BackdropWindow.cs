using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Unlock
{
    /// <summary>
    /// 전체 화면 격자 배경. 배경은 완전 투명이라 뒤 화면이 그대로 보이고,
    /// 최상단(게임 위)에 떠서 배치 기준 격자가 보인다. 클릭은 아래 창으로 통과.
    /// </summary>
    internal sealed class BackdropWindow : Window
    {
        public BackdropWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent; // 기본값(흰색)이 깔리면 화면 전체가 불투명해짐
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
            // 배경은 완전 투명, 격자만 표시
            root.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Opacity = 0.5,
                Fill = CreateGridBrush()
            });
            Content = root;

            SourceInitialized += (_, _) => UnlockWindowStyle.ApplyClickThrough(this);
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
}
