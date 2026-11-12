using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.TextFormatting;
using System.Windows.Threading;

namespace TWChatOverlay.Views
{
    /// <summary>채팅 한 줄의 표시 내용. 조각마다 색을 따로 줄 수 있다 (색이 없으면 줄 색). 글꼴은 표시 영역이 정한다.</summary>
    public sealed class ChatLineContent
    {
        public ChatLineContent(IReadOnlyList<(string Text, Brush? Brush)> segments, Brush foreground)
        {
            Segments = segments;
            Foreground = foreground;
        }

        public IReadOnlyList<(string Text, Brush? Brush)> Segments { get; }
        public Brush Foreground { get; }
        /// <summary>줄 배경 (강조·마법진·블랙리스트). 없으면 null.</summary>
        public Brush? Background { get; init; }
        public bool Bold { get; init; }
    }

    /// <summary>
    /// 채팅 줄을 직접 그리는 표시 영역. 줄마다 따로 그려 두고(<see cref="DrawingVisual"/>), 새 줄이 오면 그 줄만 글자 배치를 한다.
    /// 나머지 줄은 위치만 옮긴다. RichTextBox(FlowDocument)는 줄 하나가 바뀔 때마다 문서 배치를 다시 해서
    /// 갱신 한 번에 UI 스레드 4ms 안팎이 들었다 — 이 방식은 약 0.7ms다.
    /// 스크롤 막대는 바깥 <see cref="ScrollViewer"/>가 맡고, 여기서는 범위와 위치를 알려 준다 (<see cref="IScrollInfo"/>).
    /// 휠·넘기는 키(방향키, PageUp/PageDown, Home/End)와 글 선택·복사(Ctrl+C, 오른쪽 메뉴)·전체 선택(Ctrl+A)은 여기서 처리한다.
    /// </summary>
    public sealed class ChatLineDisplay : FrameworkElement, IScrollInfo
    {
        /// <summary>줄 아래 간격. 예전 문단 Margin(0,0,0,1)과 같다.</summary>
        private const double LineGap = 1.0;
        // 줄 자리는 문서 좌표로 계속 쌓인다. 값이 커지면 그리는 쪽(단정도 실수)에서 자리가 어긋나므로
        // (약 1,677만 px부터 줄이 픽셀 단위로 밀린다) 이만큼 쌓일 때마다 좌표를 앞으로 당긴다.
        private const double RebaseThreshold = 50_000;

        public static readonly DependencyProperty FontFamilyProperty =
            TextElement.FontFamilyProperty.AddOwner(typeof(ChatLineDisplay), new FrameworkPropertyMetadata(OnFontChanged));

        public static readonly DependencyProperty FontSizeProperty =
            TextElement.FontSizeProperty.AddOwner(typeof(ChatLineDisplay), new FrameworkPropertyMetadata(OnFontChanged));

        /// <summary>글줄 하나 (줄바꿈으로 나뉜 한 줄).</summary>
        private readonly struct Row
        {
            public Row(TextLine text, double top, int start) { Text = text; Top = top; Start = start; }
            public TextLine Text { get; }
            /// <summary>채팅 줄 안에서의 위쪽 Y.</summary>
            public double Top { get; }
            /// <summary>이 글줄이 시작하는 글자 위치.</summary>
            public int Start { get; }
        }

        private sealed class Line
        {
            public ChatLineContent Content = null!;
            public string Text = string.Empty;
            public Row[] Rows = Array.Empty<Row>();
            public readonly DrawingVisual Visual = new();
            /// <summary>줄 배경 (강조·마법진·블랙리스트). 배경이 있는 줄에만 만든다.</summary>
            public DrawingVisual? Background;
            /// <summary>문서 좌표의 위쪽 Y. 앞 줄을 지워도 바뀌지 않게 누적 좌표를 쓴다.</summary>
            public double Top;
            public double Height;
        }

        private readonly struct TextPosition
        {
            public TextPosition(Line line, int index) { Line = line; Index = index; }
            public Line Line { get; }
            public int Index { get; }
        }

        private readonly VisualCollection _children;
        // _root는 스크롤 위치만큼 통째로 옮긴다. 그 아래에 문서 좌표로, 아래부터 줄 배경 → 선택 표시 → 글자 순으로 쌓는다.
        // 선택 표시가 글자 아래에 있어야 선택해도 글자색이 탁해지지 않는다.
        private readonly ContainerVisual _root = new();
        private readonly ContainerVisual _backgroundsHost = new();
        private readonly DrawingVisual _selectionVisual = new();
        private readonly ContainerVisual _linesHost = new();
        private readonly List<Line> _lines = new();
        private readonly TextFormatter _formatter = TextFormatter.Create(TextFormattingMode.Ideal);
        private readonly DispatcherTimer _dragScrollTimer;
        private static readonly Brush SelectionBrush = CreateSelectionBrush();

        private double _contentTop;     // 첫 줄의 위쪽 (문서 좌표)
        private double _contentBottom;  // 다음 줄이 놓일 자리 (문서 좌표)
        private double _viewTop;        // 화면 맨 위에 보이는 문서 좌표
        private double _wrapWidth;      // 줄바꿈 너비 (0이면 아직 크기가 정해지지 않음)
        private double _pixelsPerDip = 1.0;
        private int _maxLines = 200;
        private int _updateDepth;
        private bool _metricsDirty;
        private Typeface? _normalTypeface;
        private Typeface? _boldTypeface;

        private TextPosition? _selectionAnchor;
        private TextPosition? _selectionActive;
        private bool _isSelecting;

        public ChatLineDisplay()
        {
            _children = new VisualCollection(this);
            _root.Children.Add(_backgroundsHost);
            _root.Children.Add(_selectionVisual);
            _root.Children.Add(_linesHost);
            _children.Add(_root);

            ClipToBounds = true;
            Focusable = true;
            FocusVisualStyle = null;
            Cursor = Cursors.IBeam;
            SnapsToDevicePixels = true;

            _dragScrollTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(40) };
            _dragScrollTimer.Tick += OnDragScrollTick;

            var copy = new MenuItem { Header = "복사", InputGestureText = "Ctrl+C" };
            copy.Click += (_, _) => CopySelection();
            var selectAll = new MenuItem { Header = "전체 선택", InputGestureText = "Ctrl+A" };
            selectAll.Click += (_, _) => SelectAll();
            copy.SetResourceReference(StyleProperty, "OverlayMenuItemStyle");
            selectAll.SetResourceReference(StyleProperty, "OverlayMenuItemStyle");
            var menu = new ContextMenu { Items = { copy, selectAll } };
            menu.SetResourceReference(StyleProperty, "OverlayContextMenuStyle");
            ContextMenu = menu;
            ContextMenuOpening += (_, _) =>
            {
                copy.IsEnabled = HasSelection;
                menu.FontFamily = FontFamily;
            };
        }

        /// <summary>스크롤 범위·위치가 바뀌었다 (맨 아래를 보고 있는지 표시하는 쪽에서 쓴다).</summary>
        public event EventHandler? ScrollMetricsChanged;

        public FontFamily FontFamily
        {
            get => (FontFamily)GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public double FontSize
        {
            get => (double)GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        /// <summary>남겨 둘 줄 수. 넘으면 오래된 줄부터 지운다.</summary>
        public int MaxLines
        {
            get => _maxLines;
            set
            {
                _maxLines = Math.Max(1, value);
                if (TrimExcess())
                    OnContentChanged();
            }
        }

        public int LineCount => _lines.Count;
        /// <summary>스크롤할 수 있는 최대 거리.</summary>
        public double ScrollableHeight => Math.Max(0, ExtentHeight - ViewportHeight);
        /// <summary>맨 아래를 보고 있는지 (내용이 화면보다 짧으면 항상 true).</summary>
        public bool IsAtEnd => ScrollableHeight <= 0 || VerticalOffset >= ScrollableHeight - 1.0;
        public bool HasSelection => _selectionAnchor.HasValue && _selectionActive.HasValue && !IsSamePosition(_selectionAnchor.Value, _selectionActive.Value);

        protected override int VisualChildrenCount => _children.Count;
        protected override Visual GetVisualChild(int index) => _children[index];

        // 글자가 없는 빈 곳에서도 휠·클릭을 받는다
        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
            => new PointHitTestResult(this, hitTestParameters.HitPoint);

        protected override Size MeasureOverride(Size availableSize)
            => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
                   double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);

        protected override Size ArrangeOverride(Size finalSize) => finalSize;

        #region 내용 넣기

        /// <summary>여러 줄을 넣는 동안 스크롤 계산과 알림을 한 번으로 모은다. <see cref="EndUpdate"/>와 짝을 맞춘다.</summary>
        public void BeginUpdate() => _updateDepth++;

        public void EndUpdate()
        {
            if (_updateDepth > 0)
                _updateDepth--;
            if (_updateDepth == 0 && _metricsDirty)
                ApplyView();
        }

        public void AddLine(ChatLineContent content)
        {
            if (content == null)
                throw new ArgumentNullException(nameof(content));

            var sb = new StringBuilder();
            foreach (var (text, _) in content.Segments)
                sb.Append(text);

            var line = new Line { Content = content, Text = sb.ToString() };
            if (content.Background != null)
            {
                line.Background = new DrawingVisual();
                _backgroundsHost.Children.Add(line.Background);
            }
            FormatLine(line);
            line.Top = _contentBottom;
            PlaceLine(line);
            _contentBottom += line.Height;
            _lines.Add(line);
            _linesHost.Children.Add(line.Visual);
            TrimExcess();
            OnContentChanged();
        }

        /// <summary>줄(글자와 배경)을 문서 좌표의 제자리에 놓는다. 화면 픽셀에 맞춘다.</summary>
        private void PlaceLine(Line line)
        {
            var offset = new Vector(0, SnapY(line.Top));
            line.Visual.Offset = offset;
            if (line.Background != null)
                line.Background.Offset = offset;
        }

        /// <summary>줄 수가 <see cref="MaxLines"/>를 넘으면 오래된 줄부터 지운다. 지웠으면 true.</summary>
        private bool TrimExcess()
        {
            int remove = _lines.Count - _maxLines;
            if (remove <= 0)
                return false;

            bool selectionTouched = false;
            for (int i = 0; i < remove; i++)
            {
                Line line = _lines[i];
                if ((_selectionAnchor.HasValue && ReferenceEquals(_selectionAnchor.Value.Line, line)) ||
                    (_selectionActive.HasValue && ReferenceEquals(_selectionActive.Value.Line, line)))
                    selectionTouched = true;
                DisposeRows(line);
                if (line.Background != null)
                    _backgroundsHost.Children.Remove(line.Background);
            }

            _lines.RemoveRange(0, remove);
            _linesHost.Children.RemoveRange(0, remove);
            _contentTop = _lines.Count > 0 ? _lines[0].Top : _contentBottom;

            if (selectionTouched)
                ClampSelectionAfterTrim();
            RebaseIfNeeded();
            return true;
        }

        /// <summary>
        /// 오래 켜 두면 좌표가 끝없이 커지지 않게, 지워진 줄만큼 쌓인 좌표를 앞으로 당긴다. 보이는 모습은 그대로다.
        /// </summary>
        private void RebaseIfNeeded()
        {
            if (_contentTop < RebaseThreshold)
                return;

            // 96의 배수만큼만 옮긴다 — 어떤 화면 배율에서도 화면 픽셀의 정수 배라서 줄마다 맞춰 둔 픽셀 자리가 그대로 유지된다
            double shift = Math.Floor(_contentTop / 96) * 96;
            foreach (Line line in _lines)
            {
                line.Top -= shift;
                PlaceLine(line);
            }
            _contentTop -= shift;
            _contentBottom -= shift;
            _viewTop -= shift;
            UpdateSelectionVisual();
        }

        public void Clear()
        {
            foreach (Line line in _lines)
                DisposeRows(line);
            _lines.Clear();
            _linesHost.Children.Clear();
            _backgroundsHost.Children.Clear();
            _contentTop = _contentBottom = _viewTop = 0;
            ClearSelection();
            OnContentChanged();
        }

        private void OnContentChanged()
        {
            _metricsDirty = true;
            if (_updateDepth == 0)
                ApplyView();
        }

        private static void DisposeRows(Line line)
        {
            foreach (Row row in line.Rows)
                row.Text.Dispose();
            line.Rows = Array.Empty<Row>();
        }

        private Typeface GetTypeface(bool bold) => bold
            ? _boldTypeface ??= new Typeface(FontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal)
            : _normalTypeface ??= new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        /// <summary>한 줄의 글자 배치를 하고 그려 둔다. 글줄(<see cref="Row"/>)은 글자 위치 찾기와 선택 표시에 다시 쓴다.</summary>
        private void FormatLine(Line line)
        {
            DisposeRows(line);
            ChatLineContent content = line.Content;
            bool wrap = _wrapWidth > 0;
            var source = new LineTextSource(line.Text, content, GetTypeface(content.Bold), FontSize, _pixelsPerDip);
            var paragraph = new LineParagraphProperties(source.DefaultProperties, wrap);

            var rows = new List<Row>(2);
            double height = 0;
            double width = 0;
            int position = 0;
            TextLineBreak? lineBreak = null;
            do
            {
                TextLine text = _formatter.FormatLine(source, position, wrap ? _wrapWidth : 0, paragraph, lineBreak);
                rows.Add(new Row(text, height, position));
                height += text.Height;
                width = Math.Max(width, text.WidthIncludingTrailingWhitespace);
                if (text.Length <= 0)
                    break;
                position += text.Length;
                lineBreak = text.GetTextLineBreak();
            }
            while (position < line.Text.Length);

            line.Rows = rows.ToArray();
            // 높이는 글꼴이 정한 값 그대로 쌓는다 (예전 문단 간격과 같게). 그리는 자리만 화면 픽셀에 맞춘다 — SnapY.
            line.Height = height + LineGap;

            using (DrawingContext dc = line.Visual.RenderOpen())
            {
                foreach (Row row in line.Rows)
                    row.Text.Draw(dc, new Point(0, row.Top), InvertAxes.None);
            }

            if (line.Background != null)
            {
                using DrawingContext dc = line.Background.RenderOpen();
                dc.DrawRectangle(content.Background, null, new Rect(0, 0, Math.Max(_wrapWidth, width), height));
            }
        }

        /// <summary>너비·화면 배율·글꼴이 바뀌어 모든 줄의 글자 배치를 다시 한다. 보던 자리는 유지한다.</summary>
        private void Reflow(bool wasAtEnd)
        {
            Line? anchor = FindLineAt(_viewTop);
            double anchorDelta = anchor != null ? _viewTop - anchor.Top : 0;

            double y = 0;
            foreach (Line line in _lines)
            {
                FormatLine(line);
                line.Top = y;
                PlaceLine(line);
                y += line.Height;
            }
            _contentTop = 0;
            _contentBottom = y;
            _viewTop = wasAtEnd || anchor == null ? _contentTop + ScrollableHeight : anchor.Top + Math.Min(anchorDelta, anchor.Height);
            UpdateSelectionVisual();
            _metricsDirty = true;
            ApplyView();
        }

        private static void OnFontChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var display = (ChatLineDisplay)d;
            display._normalTypeface = display._boldTypeface = null;
            if (display._lines.Count > 0)
                display.Reflow(display.IsAtEnd);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            // 크기가 바뀌기 전에 맨 아래를 보고 있었으면 바뀐 뒤에도 맨 아래에 붙인다
            double previousScrollable = Math.Max(0, ExtentHeight - sizeInfo.PreviousSize.Height);
            bool wasAtEnd = previousScrollable <= 0 || _viewTop - _contentTop >= previousScrollable - 1.0;

            if (sizeInfo.WidthChanged)
            {
                _wrapWidth = Math.Max(0, sizeInfo.NewSize.Width);
                _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                Reflow(wasAtEnd);
            }

            if (wasAtEnd)
                _viewTop = _contentTop + ScrollableHeight;
            _metricsDirty = true;
            ApplyView();
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            bool wasAtEnd = IsAtEnd;
            _pixelsPerDip = newDpi.PixelsPerDip;
            Reflow(wasAtEnd);
        }

        #endregion

        #region 스크롤 (IScrollInfo)

        public bool CanVerticallyScroll { get; set; } = true;
        public bool CanHorizontallyScroll { get; set; }
        public ScrollViewer? ScrollOwner { get; set; }

        /// <summary>내용 전체 높이.</summary>
        public double ExtentHeight => _contentBottom - _contentTop;
        /// <summary>보이는 영역 높이.</summary>
        public double ViewportHeight => ActualHeight;
        /// <summary>지금 스크롤 위치 (0 = 맨 위).</summary>
        public double VerticalOffset => Math.Max(0, Math.Min(ScrollableHeight, _viewTop - _contentTop));
        // 가로로는 넘기지 않는다 — 줄바꿈 너비가 곧 보이는 너비다
        public double ExtentWidth => ActualWidth;
        public double ViewportWidth => ActualWidth;
        public double HorizontalOffset => 0;

        /// <summary>한 번에 넘기는 한 줄의 높이 (글꼴 기준).</summary>
        private double ScrollLineHeight => Math.Max(8, FontFamily.LineSpacing * FontSize);
        private double ScrollPageHeight => Math.Max(ScrollLineHeight, ViewportHeight - ScrollLineHeight);
        private static int WheelLines => SystemParameters.WheelScrollLines > 0 ? SystemParameters.WheelScrollLines : 3;

        public void LineUp() => SetVerticalOffset(VerticalOffset - ScrollLineHeight);
        public void LineDown() => SetVerticalOffset(VerticalOffset + ScrollLineHeight);
        public void PageUp() => SetVerticalOffset(VerticalOffset - ScrollPageHeight);
        public void PageDown() => SetVerticalOffset(VerticalOffset + ScrollPageHeight);
        public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - WheelLines * ScrollLineHeight);
        public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + WheelLines * ScrollLineHeight);
        public void LineLeft() { }
        public void LineRight() { }
        public void PageLeft() { }
        public void PageRight() { }
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }
        public void SetHorizontalOffset(double offset) { }
        public Rect MakeVisible(Visual visual, Rect rectangle) => rectangle;

        /// <summary>스크롤 위치를 정한다 (0 = 맨 위). 범위를 벗어난 값은 끝에 맞춘다.</summary>
        public void SetVerticalOffset(double offset)
        {
            if (double.IsNaN(offset))
                return;
            _viewTop = _contentTop + offset;
            _metricsDirty = true;
            if (_updateDepth == 0)
                ApplyView();
        }

        public void ScrollToEnd() => SetVerticalOffset(double.PositiveInfinity);

        /// <summary>
        /// 문서 좌표를 화면 픽셀 경계에 맞춘다 — 글자가 반 픽셀에 걸려 번지지 않게.
        /// 딱 반 픽셀에 걸린 값은 계산 오차와 상관없이 늘 같은 쪽(아래)으로 간다. Math.Round는 그런 값을 짝수 쪽으로 보내서
        /// 좌표를 통째로 옮기면(RebaseIfNeeded) 줄이 1픽셀 달라질 수 있다.
        /// </summary>
        private double SnapY(double y) => Math.Floor(y * _pixelsPerDip + 0.5 + 1e-7) / _pixelsPerDip;

        private void ApplyView()
        {
            _metricsDirty = false;
            double max = ScrollableHeight;
            _viewTop = Math.Max(_contentTop, Math.Min(_contentTop + max, _viewTop));
            // 글자가 번지지 않게 화면 픽셀에 맞춘다
            _root.Offset = new Vector(0, -SnapY(_viewTop));

            if (ScrollOwner != null)
            {
                // 스크롤 뷰어는 배치가 한 번 돈 뒤에 범위·위치를 읽어 막대에 옮긴다
                InvalidateArrange();
                ScrollOwner.InvalidateScrollInfo();
            }
            ScrollMetricsChanged?.Invoke(this, EventArgs.Empty);
        }

        // 휠은 여기서 받는다 — 스크롤 뷰어는 굴린 양과 상관없이 한 번에 정해진 만큼만 넘겨서 터치패드에서 너무 빨리 넘어간다
        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (ScrollableHeight <= 0)
                return;

            SetVerticalOffset(VerticalOffset - e.Delta / 120.0 * WheelLines * ScrollLineHeight);
            e.Handled = true;
        }

        /// <summary>문서 좌표 y가 걸치는 줄. 내용 밖이면 가장 가까운 줄.</summary>
        private Line? FindLineAt(double y)
        {
            if (_lines.Count == 0)
                return null;

            int lo = 0, hi = _lines.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_lines[mid].Top <= y)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return _lines[lo];
        }

        #endregion

        #region 선택·복사

        private static Brush CreateSelectionBrush()
        {
            // 앱 강조색(민트)을 반투명으로
            Color accent = Application.Current?.TryFindResource("AccentColor") is Color c ? c : Color.FromRgb(0x0C, 0xD2, 0x9D);
            var brush = new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B));
            brush.Freeze();
            return brush;
        }

        private static bool IsSamePosition(TextPosition a, TextPosition b)
            => ReferenceEquals(a.Line, b.Line) && a.Index == b.Index;

        public void ClearSelection()
        {
            _selectionAnchor = _selectionActive = null;
            UpdateSelectionVisual();
        }

        public void SelectAll()
        {
            if (_lines.Count == 0)
            {
                ClearSelection();
                return;
            }
            _selectionAnchor = new TextPosition(_lines[0], 0);
            _selectionActive = new TextPosition(_lines[^1], _lines[^1].Text.Length);
            UpdateSelectionVisual();
        }

        /// <summary>선택한 글. 줄 사이는 줄바꿈으로 잇는다.</summary>
        public string GetSelectedText()
        {
            if (!TryGetOrderedSelection(out int startLine, out int startIndex, out int endLine, out int endIndex))
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = startLine; i <= endLine; i++)
            {
                string text = _lines[i].Text;
                int from = i == startLine ? Math.Min(startIndex, text.Length) : 0;
                int to = i == endLine ? Math.Min(endIndex, text.Length) : text.Length;
                if (i > startLine)
                    sb.Append("\r\n");
                if (to > from)
                    sb.Append(text, from, to - from);
            }
            return sb.ToString();
        }

        public void CopySelection()
        {
            string text = GetSelectedText();
            if (text.Length == 0)
                return;
            try
            {
                Clipboard.SetText(text);
            }
            catch (Exception ex)
            {
                // 다른 프로그램이 클립보드를 잡고 있으면 실패할 수 있다
                Services.AppLogger.Warn("Failed to copy chat selection to the clipboard.", ex);
            }
        }

        private bool TryGetOrderedSelection(out int startLine, out int startIndex, out int endLine, out int endIndex)
        {
            startLine = startIndex = endLine = endIndex = 0;
            if (!_selectionAnchor.HasValue || !_selectionActive.HasValue)
                return false;

            int a = _lines.IndexOf(_selectionAnchor.Value.Line);
            int b = _lines.IndexOf(_selectionActive.Value.Line);
            if (a < 0 || b < 0)
                return false;

            bool anchorFirst = a < b || (a == b && _selectionAnchor.Value.Index <= _selectionActive.Value.Index);
            (startLine, startIndex) = anchorFirst ? (a, _selectionAnchor.Value.Index) : (b, _selectionActive.Value.Index);
            (endLine, endIndex) = anchorFirst ? (b, _selectionActive.Value.Index) : (a, _selectionAnchor.Value.Index);
            return startLine != endLine || startIndex != endIndex;
        }

        private void ClampSelectionAfterTrim()
        {
            if (_lines.Count == 0)
            {
                _selectionAnchor = _selectionActive = null;
            }
            else
            {
                // 지워진 줄에 걸쳐 있던 끝은 남은 첫 줄의 맨 앞으로 옮긴다
                if (_selectionAnchor.HasValue && !_lines.Contains(_selectionAnchor.Value.Line))
                    _selectionAnchor = new TextPosition(_lines[0], 0);
                if (_selectionActive.HasValue && !_lines.Contains(_selectionActive.Value.Line))
                    _selectionActive = new TextPosition(_lines[0], 0);
            }
            UpdateSelectionVisual();
        }

        private void UpdateSelectionVisual()
        {
            using DrawingContext dc = _selectionVisual.RenderOpen();
            if (!TryGetOrderedSelection(out int startLine, out int startIndex, out int endLine, out int endIndex))
                return;

            // 한 도형으로 모아 한 번에 칠한다 — 글줄끼리 맞닿은 가장자리에 줄이 비치지 않는다
            var group = new GeometryGroup { FillRule = FillRule.Nonzero };
            for (int i = startLine; i <= endLine; i++)
            {
                Line line = _lines[i];
                int length = line.Text.Length;
                int from = i == startLine ? Math.Min(startIndex, length) : 0;
                int to = i == endLine ? Math.Min(endIndex, length) : length;
                if (to <= from)
                    continue;

                double lineTop = SnapY(line.Top);
                foreach (Row row in line.Rows)
                {
                    int rowFrom = Math.Max(from, row.Start);
                    int rowTo = Math.Min(to, row.Start + row.Text.Length);
                    if (rowTo <= rowFrom)
                        continue;

                    foreach (TextBounds bounds in row.Text.GetTextBounds(rowFrom, rowTo - rowFrom))
                    {
                        Rect rect = bounds.Rectangle;
                        if (rect.IsEmpty || rect.Width <= 0)
                            continue;
                        rect.Offset(0, lineTop + row.Top);
                        group.Children.Add(new RectangleGeometry(rect));
                    }
                }
            }

            if (group.Children.Count > 0)
                dc.DrawGeometry(SelectionBrush, null, group);
        }

        /// <summary>화면 좌표의 점에 가장 가까운 글자 위치.</summary>
        private TextPosition? HitTestPosition(Point point)
        {
            if (_lines.Count == 0)
                return null;

            double y = point.Y - _root.Offset.Y; // 문서 좌표
            if (y < _contentTop)
                return new TextPosition(_lines[0], 0);
            if (y >= _contentBottom)
                return new TextPosition(_lines[^1], _lines[^1].Text.Length);

            Line line = FindLineAt(y)!;
            return new TextPosition(line, HitTestIndex(line, new Point(point.X, y - line.Top)));
        }

        /// <summary>줄 안의 점(줄 좌표)에 해당하는 글자 위치. 글자의 오른쪽 절반이면 그 글자 뒤.</summary>
        private static int HitTestIndex(Line line, Point local)
        {
            if (line.Rows.Length == 0)
                return 0;

            Row row = line.Rows[^1];
            foreach (Row candidate in line.Rows)
            {
                if (local.Y < candidate.Top + candidate.Text.Height)
                {
                    row = candidate;
                    break;
                }
            }

            CharacterHit hit = row.Text.GetCharacterHitFromDistance(local.X);
            return Math.Max(0, Math.Min(line.Text.Length, hit.FirstCharacterIndex + hit.TrailingLength));
        }

        private void ExtendSelectionTo(Point point)
        {
            TextPosition? position = HitTestPosition(point);
            if (!position.HasValue)
                return;
            _selectionActive = position;
            UpdateSelectionVisual();
        }

        private void StopSelecting()
        {
            _isSelecting = false;
            _dragScrollTimer.Stop();
        }

        private void OnDragScrollTick(object? sender, EventArgs e)
        {
            if (!_isSelecting || Mouse.LeftButton != MouseButtonState.Pressed)
            {
                _dragScrollTimer.Stop();
                return;
            }
            AutoScrollSelection(Mouse.GetPosition(this));
        }

        /// <summary>
        /// 끌어서 선택하는 중 마우스가 영역 위·아래로 나가 있으면 그쪽으로 넘기고 선택 끝도 따라 옮긴다.
        /// 마우스를 멈추고 있어도 계속 넘어가도록 타이머가 부른다. 멀리 끌수록 빨리 넘긴다.
        /// </summary>
        private void AutoScrollSelection(Point point)
        {
            double distance = point.Y < 0 ? point.Y : point.Y > ActualHeight ? point.Y - ActualHeight : 0;
            if (distance == 0)
                return;

            double before = VerticalOffset;
            SetVerticalOffset(before + Math.Sign(distance) * Math.Min(120, 8 + Math.Abs(distance) * 0.5));
            if (VerticalOffset != before)
                ExtendSelectionTo(point);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            Focus();
            TextPosition? position = HitTestPosition(e.GetPosition(this));
            if (!position.HasValue)
                return;

            if (e.ClickCount >= 2)
            {
                // 두 번 누르면 그 줄 전체
                Line line = position.Value.Line;
                _selectionAnchor = new TextPosition(line, 0);
                _selectionActive = new TextPosition(line, line.Text.Length);
                StopSelecting();
            }
            else
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
                    _selectionActive = position;
                else
                    _selectionAnchor = _selectionActive = position;
                _isSelecting = true;
                CaptureMouse();
                _dragScrollTimer.Start();
            }

            UpdateSelectionVisual();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_isSelecting || e.LeftButton != MouseButtonState.Pressed)
                return;
            ExtendSelectionTo(e.GetPosition(this));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_isSelecting)
                return;
            StopSelecting();
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            StopSelecting();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (control && (e.Key == Key.C || e.Key == Key.Insert))
            {
                CopySelection();
                e.Handled = true;
            }
            else if (control && e.Key == Key.A)
            {
                SelectAll();
                e.Handled = true;
            }
            else if (!control && e.Key == Key.Escape && HasSelection)
            {
                ClearSelection();
                e.Handled = true;
            }
            else if (ScrollableHeight > 0)
            {
                // 스크롤 뷰어에 맡기면 방향키를 누를 때 옆 버튼으로 포커스를 옮기려 들 수 있어, 넘기는 키는 여기서 받는다
                switch (e.Key)
                {
                    case Key.Up: LineUp(); break;
                    case Key.Down: LineDown(); break;
                    case Key.PageUp: PageUp(); break;
                    case Key.PageDown: PageDown(); break;
                    case Key.Home: SetVerticalOffset(0); break;
                    case Key.End: ScrollToEnd(); break;
                    default: return;
                }
                e.Handled = true;
            }
        }

        #endregion

        #region 글자 배치 (TextFormatter에 넘기는 내용)

        /// <summary>한 줄의 글과 조각별 색. 색이 같은 이웃 조각은 한 덩어리로 넘긴다.</summary>
        private sealed class LineTextSource : TextSource
        {
            private readonly string _text;
            private readonly List<(int End, TextRunProperties Properties)> _runs = new();

            public LineTextSource(string text, ChatLineContent content, Typeface typeface, double fontSize, double pixelsPerDip)
            {
                _text = text;
                CultureInfo culture = CultureInfo.CurrentUICulture;
                DefaultProperties = new LineRunProperties(typeface, fontSize, content.Foreground, culture, pixelsPerDip);

                int end = 0;
                Brush? previous = null;
                foreach (var (segmentText, segmentBrush) in content.Segments)
                {
                    if (segmentText.Length == 0)
                        continue;
                    end += segmentText.Length;
                    Brush brush = segmentBrush ?? content.Foreground;
                    if (_runs.Count > 0 && ReferenceEquals(previous, brush))
                    {
                        _runs[^1] = (end, _runs[^1].Properties);
                        continue;
                    }

                    TextRunProperties properties = ReferenceEquals(brush, content.Foreground)
                        ? DefaultProperties
                        : new LineRunProperties(typeface, fontSize, brush, culture, pixelsPerDip);
                    _runs.Add((end, properties));
                    previous = brush;
                }
            }

            public TextRunProperties DefaultProperties { get; }

            public override TextRun GetTextRun(int textSourceCharacterIndex)
            {
                if (textSourceCharacterIndex < _text.Length)
                {
                    foreach (var (end, properties) in _runs)
                    {
                        if (textSourceCharacterIndex < end)
                            return new TextCharacters(_text, textSourceCharacterIndex, end - textSourceCharacterIndex, properties);
                    }
                }
                return new TextEndOfParagraph(1);
            }

            public override TextSpan<CultureSpecificCharacterBufferRange> GetPrecedingText(int textSourceCharacterIndexLimit)
            {
                int limit = Math.Max(0, Math.Min(_text.Length, textSourceCharacterIndexLimit));
                CharacterBufferRange range = limit > 0 ? new CharacterBufferRange(_text, 0, limit) : CharacterBufferRange.Empty;
                return new TextSpan<CultureSpecificCharacterBufferRange>(limit, new CultureSpecificCharacterBufferRange(DefaultProperties.CultureInfo, range));
            }

            public override int GetTextEffectCharacterIndexFromTextSourceCharacterIndex(int textSourceCharacterIndex)
                => textSourceCharacterIndex;
        }

        private sealed class LineRunProperties : TextRunProperties
        {
            private readonly Typeface _typeface;
            private readonly double _fontSize;
            private readonly Brush _foreground;
            private readonly CultureInfo _culture;

            public LineRunProperties(Typeface typeface, double fontSize, Brush foreground, CultureInfo culture, double pixelsPerDip)
            {
                _typeface = typeface;
                _fontSize = fontSize;
                _foreground = foreground;
                _culture = culture;
                PixelsPerDip = pixelsPerDip;
            }

            public override Typeface Typeface => _typeface;
            public override double FontRenderingEmSize => _fontSize;
            public override double FontHintingEmSize => _fontSize;
            public override TextDecorationCollection TextDecorations => null!;
            public override Brush ForegroundBrush => _foreground;
            public override Brush BackgroundBrush => null!;
            public override CultureInfo CultureInfo => _culture;
            public override TextEffectCollection TextEffects => null!;
        }

        private sealed class LineParagraphProperties : TextParagraphProperties
        {
            private readonly TextRunProperties _defaultProperties;
            private readonly bool _wrap;

            public LineParagraphProperties(TextRunProperties defaultProperties, bool wrap)
            {
                _defaultProperties = defaultProperties;
                _wrap = wrap;
            }

            public override FlowDirection FlowDirection => FlowDirection.LeftToRight;
            public override TextAlignment TextAlignment => TextAlignment.Left;
            public override double LineHeight => 0;
            public override bool FirstLineInParagraph => false;
            public override TextRunProperties DefaultTextRunProperties => _defaultProperties;
            public override TextWrapping TextWrapping => _wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
            public override TextMarkerProperties TextMarkerProperties => null!;
            public override double Indent => 0;
        }

        #endregion
    }
}
