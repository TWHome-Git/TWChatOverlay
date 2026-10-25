using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 코드로 조립하는 요약·기록 창(보물창고 통계·주간 득템 통계·던전 타이머 기록 추이·사냥 기록)이 함께 쓰는 크롬 조각.
    /// 제목·부제·[닫기](Styles의 WindowCloseButtonStyle)·루트 Border·구분선·카드·수치 타일을 한 곳에서 만든다.
    /// 색은 전부 Styles.xaml 리소스 참조라 테마가 바뀌면 따라간다. 민트 강조색만 여기 상수 하나로 둔다.
    /// </summary>
    internal static class OverlayChrome
    {
        /// <summary>강조색(민트). Styles.xaml의 OverlayTitleAccentTextBrush/OverlayAccentBorderBrush와 같은 값.</summary>
        public static readonly Color Mint = Color.FromRgb(0x0C, 0xD2, 0x9D);

        /// <summary>민트 단색 브러시(Frozen). 테두리·점·막대처럼 리소스 참조를 쓸 수 없는 자리에.</summary>
        public static readonly SolidColorBrush MintBrush = Frozen(Mint);

        /// <summary>민트에 알파를 섞은 브러시(Frozen). 선택 표시·물결 배경 등.</summary>
        public static SolidColorBrush MintWithAlpha(byte alpha) => Frozen(Color.FromArgb(alpha, Mint.R, Mint.G, Mint.B));

        public static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>창 제목 (16 SemiBold, 민트).</summary>
        public static TextBlock MakeTitle(string text)
        {
            var title = new TextBlock { Text = text, FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            title.SetResourceReference(TextBlock.ForegroundProperty, "OverlayTitleAccentTextBrush");
            return title;
        }

        /// <summary>제목 아래 작은 부제 (11, 힌트색).</summary>
        public static TextBlock MakeSubtitle(string text, Thickness? margin = null)
        {
            var subtitle = new TextBlock { Text = text, FontSize = 11, Margin = margin ?? new Thickness(0, 2, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
            return subtitle;
        }

        /// <summary>[닫기] 텍스트 버튼 — Styles.xaml WindowCloseButtonStyle (글자 '닫기' 고정, 22x40).</summary>
        public static Button MakeCloseButton(Action onClose, Thickness? margin = null)
        {
            var button = new Button { VerticalAlignment = VerticalAlignment.Top };
            if (margin.HasValue)
                button.Margin = margin.Value;
            button.SetResourceReference(FrameworkElement.StyleProperty, "WindowCloseButtonStyle");
            button.Click += (_, _) => { try { onClose(); } catch { } };
            return button;
        }

        /// <summary>
        /// 헤더 줄: 왼쪽에 <paramref name="left"/>(제목·부제 묶음), 오른쪽에 <paramref name="right"/>들을 차례로 Dock.Right.
        /// 보통 right의 첫 요소가 [닫기]다 (맨 오른쪽).
        /// </summary>
        public static DockPanel MakeHeader(UIElement left, Thickness margin, params UIElement[] right)
        {
            var header = new DockPanel { Margin = margin, Background = Brushes.Transparent };
            foreach (UIElement element in right)
            {
                DockPanel.SetDock(element, Dock.Right);
                header.Children.Add(element);
            }
            header.Children.Add(left);
            return header;
        }

        /// <summary>창 루트 Border: 1px 테두리, 반지름 12, 오버레이 창 배경·테두리색.</summary>
        public static Border MakeRootBorder(UIElement child, Thickness padding)
        {
            var root = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = padding,
                Child = child,
            };
            root.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
            root.SetResourceReference(Border.BorderBrushProperty, "OverlayWindowBorderBrush");
            return root;
        }

        /// <summary>가로 구분선 (1px, SeparatorBrush).</summary>
        public static Border MakeDivider(Thickness? margin = null)
        {
            var divider = new Border { Height = 1, Margin = margin ?? new Thickness(0, 6, 0, 6) };
            divider.SetResourceReference(Border.BackgroundProperty, "SeparatorBrush");
            return divider;
        }

        /// <summary>그래프·표를 담는 카드 (반지름 10, 카드 배경·테두리색).</summary>
        public static Border MakeCard(UIElement child, Thickness padding)
        {
            var card = new Border { Child = child, CornerRadius = new CornerRadius(10), Padding = padding, BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "OverlayCardBackgroundBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "OverlayCardBorderBrush");
            return card;
        }

        /// <summary>수치 타일: 작은 이름 · 큰 값 · 작은 보조 설명. 강조 타일은 왼쪽에 민트 띠 (글자 색은 그대로).</summary>
        public static FrameworkElement MakeTile(string label, string value, string sub, bool accent, double valueFontSize = 20)
        {
            var labelText = new TextBlock { Text = label, FontSize = 11 };
            labelText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
            var valueText = new TextBlock { Text = value, FontSize = valueFontSize, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 1, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis };
            valueText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var subText = new TextBlock { Text = sub, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            subText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayMutedTextBrush");

            var stack = new StackPanel();
            stack.Children.Add(labelText);
            stack.Children.Add(valueText);
            stack.Children.Add(subText);

            var tile = new Border
            {
                Child = stack,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 8, 10, 9),
                Margin = new Thickness(4, 0, 4, 0),
                BorderThickness = accent ? new Thickness(3, 1, 1, 1) : new Thickness(1),
            };
            tile.SetResourceReference(Border.BackgroundProperty, "OverlayCardBackgroundBrush");
            if (accent)
                tile.BorderBrush = MintBrush;
            else
                tile.SetResourceReference(Border.BorderBrushProperty, "OverlayCardBorderBrush");
            return tile;
        }

        /// <summary>
        /// 눌린 곳이 조작 컨트롤(버튼·목록·슬라이더·스크롤바) 위인지 — 그 위에서는 창을 끌지 않는다.
        /// 시각 트리 → 논리 트리 순으로 조상을 거슬러 오른다.
        /// </summary>
        public static bool IsOnInteractiveControl(DependencyObject? source)
            => FindAncestor(source, current => current is ButtonBase or ListBox or Slider or ScrollBar);

        /// <summary>조상 가운데 <paramref name="match"/>를 만족하는 것이 있는지 (자기 자신 포함).</summary>
        public static bool FindAncestor(DependencyObject? source, Func<DependencyObject, bool> match)
        {
            for (DependencyObject? current = source; current != null;
                 current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                     ? VisualTreeHelper.GetParent(current)
                     : LogicalTreeHelper.GetParent(current))
            {
                if (match(current))
                    return true;
            }
            return false;
        }
    }
}
