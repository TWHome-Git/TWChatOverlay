using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 달력 하루 칸의 아이템 아이콘을 네모 칸으로 촘촘히 놓는다 (그림 모드).
    /// 주어진 넓이 안에 전부 들어가는 가장 큰 칸 크기를 고르고, 가장 작은 칸으로도 다 못 넣으면
    /// 마지막 칸에 "+N"(남은 종류 수)을 그린다. 그 위에 마우스를 올리면 남은 아이템 이름이 뜬다.
    /// </summary>
    public sealed class CalendarIconPanel : Panel
    {
        /// <summary>칸 크기 후보 (큰 것부터). Gap은 칸 사이 간격.</summary>
        private static readonly (double Slot, double Gap)[] SlotSizes = { (26, 3), (22, 3), (18, 2), (15, 2) };
        /// <summary>이보다 작은 칸에서는 개수 글자를 작게 쓴다.</summary>
        private const double CompactBelow = 20;

        /// <summary>칸이 작아 개수 글자를 줄여야 하는지. 패널이 정해 아이템 틀이 읽는다.</summary>
        public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached(
            "IsCompact", typeof(bool), typeof(CalendarIconPanel), new FrameworkPropertyMetadata(false));

        public static bool GetIsCompact(DependencyObject element) => (bool)element.GetValue(IsCompactProperty);
        public static void SetIsCompact(DependencyObject element, bool value) => element.SetValue(IsCompactProperty, value);

        private double _slot = SlotSizes[0].Slot;
        private double _gap = SlotSizes[0].Gap;
        private int _columns = 1;
        private int _shown;
        private int _hidden;
        private Rect _overflowRect = Rect.Empty;

        public CalendarIconPanel()
        {
            // "+N" 칸 위에서 도구 설명이 뜨려면 빈 곳도 마우스를 받아야 한다
            Background = Brushes.Transparent;
        }

        /// <summary>
        /// 넓이 안에 <paramref name="count"/>개가 다 들어가는 가장 큰 칸을 고른다.
        /// 가장 작은 칸으로도 모자라면 한 칸을 "+N"에 내주고 나머지를 채운다.
        /// </summary>
        internal static (double Slot, double Gap, int Columns, int Shown) ChooseLayout(int count, double width, double height)
        {
            (double Slot, double Gap) smallest = SlotSizes[^1];
            foreach ((double slot, double gap) in SlotSizes)
            {
                (int columns, long capacity) = Capacity(slot, gap, width, height);
                if (capacity >= count)
                    return (slot, gap, columns, count);
            }

            (int smallColumns, long smallCapacity) = Capacity(smallest.Slot, smallest.Gap, width, height);
            int shown = (int)Math.Max(0, Math.Min(count, smallCapacity) - 1);
            return (smallest.Slot, smallest.Gap, smallColumns, shown);
        }

        private static (int Columns, long Capacity) Capacity(double slot, double gap, double width, double height)
        {
            int columns = double.IsInfinity(width) ? int.MaxValue : Math.Max(1, (int)Math.Floor((width + gap) / (slot + gap)));
            long rows = double.IsInfinity(height) ? int.MaxValue : Math.Max(1, (long)Math.Floor((height + gap) / (slot + gap)));
            return (columns, Math.Min(int.MaxValue, columns * rows));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            int count = InternalChildren.Count;
            (_slot, _gap, _columns, _shown) = ChooseLayout(count, availableSize.Width, availableSize.Height);
            _hidden = count - _shown;

            bool compact = _slot < CompactBelow;
            var slotSize = new Size(_slot, _slot);
            foreach (UIElement child in InternalChildren)
            {
                if (GetIsCompact(child) != compact)
                    SetIsCompact(child, compact);
                child.Measure(slotSize);
            }

            int cells = _shown + (_hidden > 0 ? 1 : 0);
            if (cells == 0)
                return new Size(0, 0);

            // 너비 제한이 없으면(무한) 한 줄에 전부 놓는다
            _columns = Math.Max(1, Math.Min(_columns, cells));
            int usedRows = (cells + _columns - 1) / _columns;
            return new Size(_columns * (_slot + _gap) - _gap, usedRows * (_slot + _gap) - _gap);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            var hiddenNames = new List<string>();
            for (int i = 0; i < InternalChildren.Count; i++)
            {
                UIElement child = InternalChildren[i];
                if (i < _shown)
                {
                    child.Arrange(CellRect(i));
                }
                else
                {
                    // 자리가 없는 아이템은 그리지 않는다
                    child.Arrange(new Rect(0, 0, 0, 0));
                    if (child is FrameworkElement { DataContext: ItemCalendarEntryViewModel entry })
                        hiddenNames.Add(entry.DisplayText);
                }
            }

            _overflowRect = _hidden > 0 ? CellRect(_shown) : Rect.Empty;
            if (hiddenNames.Count > 0)
                ToolTip = string.Join(Environment.NewLine, hiddenNames);
            else
                ClearValue(ToolTipProperty);

            InvalidateVisual();
            return finalSize;
        }

        private Rect CellRect(int index)
            => new((index % _columns) * (_slot + _gap), (index / _columns) * (_slot + _gap), _slot, _slot);

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            if (_hidden <= 0 || _overflowRect.IsEmpty)
                return;

            Brush background = TryFindResource("OverlayCardBackgroundBrush") as Brush ?? Brushes.Transparent;
            Brush border = TryFindResource("OverlayCardBorderBrush") as Brush ?? Brushes.Gray;
            Brush foreground = TryFindResource("OverlayMutedTextBrush") as Brush ?? Brushes.Gray;

            // 테두리 1px이 번지지 않게 반 픽셀 안쪽에 그린다
            var box = new Rect(_overflowRect.X + 0.5, _overflowRect.Y + 0.5, _overflowRect.Width - 1, _overflowRect.Height - 1);
            dc.DrawRoundedRectangle(background, new Pen(border, 1), box, 4, 4);

            var text = new FormattedText(
                "+" + _hidden.ToString(CultureInfo.InvariantCulture),
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                _slot < CompactBelow ? 8.5 : 10,
                foreground,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point(
                _overflowRect.X + (_overflowRect.Width - text.Width) / 2,
                _overflowRect.Y + (_overflowRect.Height - text.Height) / 2));
        }
    }
}
