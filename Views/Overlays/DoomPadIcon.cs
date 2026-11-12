using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 운명 선고 발판 하나를 나타내는 그림: 둥근 색 버튼 위에 흰 화살표, 아래에 차례 번호
    /// (2026-11-12 사용자가 보여 준 게임 그림 느낌 — 흰 테 · 색 버튼 · 굵은 흰 화살표).
    /// 방향과 색: 기쁨 ← 초록 · 슬픔 ↑ 주황 · 즐거움 ↓ 빨강 · 분노 → 파랑.
    /// 밟은 것은 흐리게, 다음에 밟을 것은 금색 테로 빛나게 그린다. <see cref="DoomSentenceCaptureWindow"/>가 순서대로 늘어놓는다.
    /// </summary>
    internal static class DoomPadIcon
    {
        /// <summary>발판마다 방향(오른쪽 화살표를 돌리는 각도)과 버튼 색.</summary>
        private static (double Angle, Color Color) StyleOf(DoomPad pad) => pad switch
        {
            DoomPad.Joy => (180, Color.FromRgb(0x35, 0xC2, 0x3E)),      // ← 초록
            DoomPad.Sorrow => (270, Color.FromRgb(0xF7, 0x9A, 0x1C)),   // ↑ 주황
            DoomPad.Pleasure => (90, Color.FromRgb(0xE8, 0x36, 0x32)),  // ↓ 빨강
            _ => (0, Color.FromRgb(0x22, 0xA6, 0xEA)),                  // → 파랑
        };

        // 오른쪽을 가리키는 굵은 화살표 (100x100 안)
        private static readonly Geometry ArrowGeometry = CreateArrowGeometry();

        private static Geometry CreateArrowGeometry()
        {
            Geometry geometry = Geometry.Parse("M 12,37 L 52,37 L 52,14 L 92,50 L 52,86 L 52,63 L 12,63 Z");
            geometry.Freeze();
            return geometry;
        }

        public static FrameworkElement Create(DoomPad pad, int number, double size, double gap, bool done, bool current)
        {
            (double angle, Color color) = StyleOf(pad);

            var button = new Grid { Width = size, Height = size };

            // 바깥 흰 테 (다음에 밟을 것은 금색으로 두껍게 빛난다)
            button.Children.Add(new Ellipse
            {
                Fill = current ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x3A)) : Brushes.White,
                Effect = new DropShadowEffect
                {
                    Color = current ? Color.FromRgb(0xFF, 0xE0, 0x60) : Colors.Black,
                    BlurRadius = current ? size * 0.28 : size * 0.12,
                    ShadowDepth = current ? 0 : size * 0.03,
                    Opacity = current ? 0.95 : 0.55,
                },
            });

            // 색 버튼: 위 왼쪽이 밝고 가장자리가 어두운 둥근 면
            var face = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.35, 0.28),
                Center = new Point(0.5, 0.5),
                RadiusX = 0.62,
                RadiusY = 0.62,
            };
            face.GradientStops.Add(new GradientStop(Lighten(color, 0.45), 0.0));
            face.GradientStops.Add(new GradientStop(color, 0.55));
            face.GradientStops.Add(new GradientStop(Darken(color, 0.3), 1.0));
            face.Freeze();
            button.Children.Add(new Ellipse
            {
                Margin = new Thickness(size * (current ? 0.09 : 0.07)),
                Fill = face,
                Stroke = new SolidColorBrush(Darken(color, 0.4)),
                StrokeThickness = Math.Max(1, size * 0.015),
            });

            // 흰 화살표
            button.Children.Add(new Path
            {
                Data = ArrowGeometry,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromArgb(0x70, 0, 0, 0)),
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(size * 0.22),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(angle),
            });

            // 아래에 차례 번호
            var label = new TextBlock
            {
                Text = number.ToString(),
                FontSize = Math.Max(11, size * 0.2),
                FontWeight = FontWeights.Bold,
                Foreground = current ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x3A)) : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, size * 0.02, 0, 0),
                ToolTip = DoomSentenceOrderParser.NameOf(pad),
            };

            var cell = new StackPanel
            {
                Margin = new Thickness(gap, gap * 0.6, gap, 0),
                Opacity = done ? 0.25 : 1.0, // 밟은 것은 흐리게
            };
            cell.Children.Add(button);
            cell.Children.Add(label);
            return cell;
        }

        private static Color Lighten(Color color, double amount) => Color.FromRgb(
            (byte)(color.R + (255 - color.R) * amount),
            (byte)(color.G + (255 - color.G) * amount),
            (byte)(color.B + (255 - color.B) * amount));

        private static Color Darken(Color color, double amount) => Color.FromRgb(
            (byte)(color.R * (1 - amount)),
            (byte)(color.G * (1 - amount)),
            (byte)(color.B * (1 - amount)));
    }
}
