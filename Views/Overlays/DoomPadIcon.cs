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
    /// 운명 선고 발판 하나를 나타내는 그림: 바탕 없이(창 배경 그대로) 모서리가 둥근 색 화살표, 아래에 차례 번호
    /// (2026-11-12 사용자: 둥근 색 버튼 → 흰 바탕에 색 화살표 → 흰 바탕은 별로, 배경과 같게 투명하게).
    /// 방향과 색(게임의 가면 색): 기쁨 ← 핑크 · 슬픔 ↑ 보라 · 즐거움 ↓ 파랑 · 분노 → 빨강.
    /// 밟은 것은 흐리게, 다음에 밟을 것은 금색 테로 빛나게 그린다. <see cref="DoomSentenceCaptureWindow"/>가 순서대로 늘어놓는다.
    /// </summary>
    internal static class DoomPadIcon
    {
        /// <summary>
        /// 감정마다의 색 — 게임의 가면 색을 따른다 (2026-11-12 사용자: 12시 보라 · 9시 핑크 · 6시 파랑 · 3시 빨강).
        /// 가면 안내 창의 글자색도 이 값을 쓴다.
        /// </summary>
        public static Color ColorOf(DoomPad pad) => pad switch
        {
            DoomPad.Joy => Color.FromRgb(0xF2, 0x5C, 0xC0),       // 기쁨: 핑크
            DoomPad.Sorrow => Color.FromRgb(0x9B, 0x55, 0xE8),    // 슬픔: 보라
            DoomPad.Pleasure => Color.FromRgb(0x2E, 0x8E, 0xF2),  // 즐거움: 파랑
            _ => Color.FromRgb(0xE8, 0x3A, 0x2E),                 // 분노: 빨강
        };

        /// <summary>발판마다 방향(오른쪽 화살표를 돌리는 각도)과 버튼 색.</summary>
        private static (double Angle, Color Color) StyleOf(DoomPad pad) => pad switch
        {
            DoomPad.Joy => (180, ColorOf(pad)),      // ← 9시
            DoomPad.Sorrow => (270, ColorOf(pad)),   // ↑ 12시
            DoomPad.Pleasure => (90, ColorOf(pad)),  // ↓ 6시
            _ => (0, ColorOf(pad)),                  // → 3시
        };

        // 오른쪽을 가리키는 굵은 화살표 (100x100 안)
        private static readonly Geometry ArrowGeometry = CreateArrowGeometry();

        private static Geometry CreateArrowGeometry()
        {
            Geometry geometry = Geometry.Parse("M 14,38 L 52,38 L 52,18 L 88,50 L 52,82 L 52,62 L 14,62 Z");
            geometry.Freeze();
            return geometry;
        }

        public static FrameworkElement Create(DoomPad pad, int number, double size, double gap, bool done, bool current)
        {
            (double angle, Color color) = StyleOf(pad);

            var button = new Grid { Width = size, Height = size };

            // 바탕은 따로 칠하지 않는다 — 창 배경이 그대로 비친다 (2026-11-12 사용자: 흰 바탕은 별로, 배경과 같게 · 투명하게).
            // 다음에 밟을 것만 금색 고리가 빛난다
            if (current)
            {
                button.Children.Add(new Ellipse
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x3A)),
                    StrokeThickness = Math.Max(2, size * 0.055),
                    Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(0xFF, 0xE0, 0x60),
                        BlurRadius = size * 0.22,
                        ShadowDepth = 0,
                        Opacity = 0.9,
                    },
                });
            }

            // 색 화살표. 같은 색의 굵은 둥근 테를 둘러 모서리를 둥글린다 (테 두께만큼 도형이 커지므로 여백을 그만큼 더 준다)
            var arrowBrush = new SolidColorBrush(color);
            arrowBrush.Freeze();
            double round = size * 0.1;
            button.Children.Add(new Path
            {
                Data = ArrowGeometry,
                Fill = arrowBrush,
                Stroke = arrowBrush,
                StrokeThickness = round,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(size * 0.17),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(angle),
                // 어두운 그림자를 깔아 창 배경이 밝거나 반투명해도 화살표가 묻히지 않게 한다
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = size * 0.1,
                    ShadowDepth = size * 0.02,
                    Opacity = 0.6,
                },
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


    }
}
