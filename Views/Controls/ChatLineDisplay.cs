using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TWChatOverlay.Views
{
    /// <summary>채팅 한 줄의 표시 내용. 조각마다 색을 따로 줄 수 있다 (색이 없으면 줄 색).</summary>
    public sealed class ChatLineContent
    {
        public ChatLineContent(IReadOnlyList<(string Text, Brush? Brush)> segments, Brush foreground, FontFamily fontFamily, double fontSize)
        {
            Segments = segments;
            Foreground = foreground;
            FontFamily = fontFamily;
            // 설정 파일이 깨져 크기가 0 이하로 들어오면 글자 배치가 예외를 던져 줄이 통째로 빠진다 — 기본 크기로 그린다
            FontSize = double.IsFinite(fontSize) && fontSize > 0 ? Math.Min(fontSize, 500) : 12;
        }

        public IReadOnlyList<(string Text, Brush? Brush)> Segments { get; }
        public Brush Foreground { get; }
        public FontFamily FontFamily { get; }
        public double FontSize { get; }
        /// <summary>줄 배경 (강조·마법진·블랙리스트). 없으면 null.</summary>
        public Brush? Background { get; init; }
        public bool Bold { get; init; }
    }

    /// <summary>
    /// 채팅 줄을 직접 그리는 표시 영역. 줄 하나가 DrawingVisual 하나라서 새 줄이 들어오면 그 줄만 글자 배치를 하고,
    /// 나머지 줄은 위치만 옮긴다. RichTextBox(FlowDocument)는 줄 하나가 바뀔 때마다 문서 배치를 다시 해서
    /// 갱신 한 번에 UI 스레드 4ms 안팎이 들었다 — 이 방식은 약 0.7ms다.
    /// 스크롤(휠·스크롤 막대)·글 선택·복사(Ctrl+C, 오른쪽 메뉴)·전체 선택(Ctrl+A)을 직접 처리한다.
    /// </summary>
    public sealed class ChatLineDisplay : FrameworkElement
    {
        /// <summary>줄 아래 간격. 예전 문단 Margin(0,0,0,1)과 같다.</summary>
        private const double LineGap = 1.0;
        // 줄 자리는 문서 좌표로 계속 쌓인다. 값이 커지면 그리는 쪽(단정도 실수)에서 자리가 어긋나므로
        // (약 1,677만 px부터 줄이 픽셀 단위로 밀린다) 이만큼 쌓일 때마다 좌표를 앞으로 당긴다.
        private const double RebaseThreshold = 50_000;

        private sealed class Line
        {
            public ChatLineContent Content = null!;
            public string Text = string.Empty;
            public FormattedText Formatted = null!;
            public readonly DrawingVisual Visual = new();
            /// <summary>문서 좌표의 위쪽 Y. 앞 줄을 지워도 바뀌지 않게 누적 좌표를 쓴다.</summary>
            public double Top;
            public double Height;
            /// <summary>글자별 영역(줄 좌표). 마우스로 글자를 고를 때 처음 한 번만 만든다.</summary>
            public Rect[]? CharBounds;
        }

        private readonly struct TextPosition
        {
            public TextPosition(Line line, int index) { Line = line; Index = index; }
            public Line Line { get; }
            public int Index { get; }
        }

        private readonly VisualCollection _children;
        // _root는 스크롤 위치만큼 통째로 옮긴다. 그 아래에 줄들과 선택 표시가 문서 좌표로 놓인다.
        private readonly ContainerVisual _root = new();
        private readonly ContainerVisual _linesHost = new();
        private readonly DrawingVisual _selectionVisual = new();
        private readonly List<Line> _lines = new();
        private static readonly Brush SelectionBrush = CreateSelectionBrush();

        private double _contentTop;     // 첫 줄의 위쪽 (문서 좌표)
        private double _contentBottom;  // 다음 줄이 놓일 자리 (문서 좌표)
        private double _viewTop;        // 화면 맨 위에 보이는 문서 좌표
        private double _wrapWidth;      // 줄바꿈 너비 (0이면 아직 크기가 정해지지 않음)
        private double _pixelsPerDip = 1.0;
        private int _updateDepth;
        private bool _metricsDirty;

        private TextPosition? _selectionAnchor;
        private TextPosition? _selectionActive;
        private bool _isSelecting;

        public ChatLineDisplay()
        {
            _children = new VisualCollection(this);
            _root.Children.Add(_linesHost);
            _root.Children.Add(_selectionVisual);
            _children.Add(_root);

            ClipToBounds = true;
            Focusable = true;
            FocusVisualStyle = null;
            Cursor = Cursors.IBeam;
            SnapsToDevicePixels = true;

            var copy = new MenuItem { Header = "복사", InputGestureText = "Ctrl+C" };
            copy.Click += (_, _) => CopySelection();
            var selectAll = new MenuItem { Header = "전체 선택", InputGestureText = "Ctrl+A" };
            selectAll.Click += (_, _) => SelectAll();
            ContextMenu = new ContextMenu { Items = { copy, selectAll } };
            ContextMenuOpening += (_, _) => copy.IsEnabled = HasSelection;
        }

        /// <summary>스크롤 범위·위치가 바뀌었다 (스크롤 막대와 자동 스크롤 표시 갱신용).</summary>
        public event EventHandler? ScrollMetricsChanged;

        public int LineCount => _lines.Count;
        /// <summary>내용 전체 높이.</summary>
        public double ExtentHeight => _contentBottom - _contentTop;
        /// <summary>보이는 영역 높이.</summary>
        public double ViewportHeight => ActualHeight;
        /// <summary>스크롤할 수 있는 최대 거리.</summary>
        public double ScrollableHeight => Math.Max(0, ExtentHeight - ViewportHeight);
        /// <summary>지금 스크롤 위치 (0 = 맨 위).</summary>
        public double VerticalOffset => Math.Max(0, Math.Min(ScrollableHeight, _viewTop - _contentTop));
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

            var line = new Line { Content = content };
            FormatLine(line);
            line.Top = _contentBottom;
            line.Visual.Offset = new Vector(0, SnapY(line.Top));
            _contentBottom += line.Height;
            _lines.Add(line);
            _linesHost.Children.Add(line.Visual);
            OnContentChanged();
        }

        /// <summary>줄 수가 <paramref name="maxLines"/>를 넘으면 오래된 줄부터 지운다.</summary>
        public void TrimTo(int maxLines)
        {
            if (maxLines < 0)
                maxLines = 0;
            int remove = _lines.Count - maxLines;
            if (remove <= 0)
                return;

            bool selectionTouched = false;
            for (int i = 0; i < remove; i++)
            {
                Line line = _lines[i];
                if ((_selectionAnchor.HasValue && ReferenceEquals(_selectionAnchor.Value.Line, line)) ||
                    (_selectionActive.HasValue && ReferenceEquals(_selectionActive.Value.Line, line)))
                    selectionTouched = true;
            }

            _lines.RemoveRange(0, remove);
            _linesHost.Children.RemoveRange(0, remove);
            _contentTop = _lines.Count > 0 ? _lines[0].Top : _contentBottom;

            if (selectionTouched)
                ClampSelectionAfterTrim();
            RebaseIfNeeded();
            OnContentChanged();
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
                line.Visual.Offset = new Vector(0, SnapY(line.Top));
            }
            _contentTop -= shift;
            _contentBottom -= shift;
            _viewTop -= shift;
            UpdateSelectionVisual();
        }

        public void Clear()
        {
            _lines.Clear();
            _linesHost.Children.Clear();
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

        private void FormatLine(Line line)
        {
            ChatLineContent content = line.Content;
            var sb = new StringBuilder();
            foreach (var (text, _) in content.Segments)
                sb.Append(text);
            line.Text = sb.ToString();

            var typeface = new Typeface(content.FontFamily, FontStyles.Normal, content.Bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            // 빈 줄도 한 줄 높이를 차지하게 한다
            var formatted = new FormattedText(
                line.Text.Length == 0 ? " " : line.Text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                typeface,
                content.FontSize,
                content.Foreground,
                _pixelsPerDip);
            if (_wrapWidth > 0)
                formatted.MaxTextWidth = _wrapWidth;

            int start = 0;
            foreach (var (text, brush) in content.Segments)
            {
                if (brush != null && text.Length > 0)
                    formatted.SetForegroundBrush(brush, start, text.Length);
                start += text.Length;
            }

            line.Formatted = formatted;
            line.CharBounds = null;
            double textHeight = formatted.Height;
            // 높이는 글꼴이 정한 값 그대로 쌓는다 (예전 문단 간격과 같게). 그리는 자리만 화면 픽셀에 맞춘다 — SnapY.
            line.Height = textHeight + LineGap;

            using DrawingContext dc = line.Visual.RenderOpen();
            if (content.Background != null)
                dc.DrawRectangle(content.Background, null, new Rect(0, 0, Math.Max(_wrapWidth, formatted.WidthIncludingTrailingWhitespace), textHeight));
            dc.DrawText(formatted, new Point(0, 0));
        }

        /// <summary>너비나 화면 배율이 바뀌어 모든 줄의 줄바꿈을 다시 잡는다. 보던 자리는 유지한다.</summary>
        private void Reflow(bool wasAtEnd)
        {
            Line? anchor = FindLineAt(_viewTop);
            double anchorDelta = anchor != null ? _viewTop - anchor.Top : 0;

            double y = 0;
            foreach (Line line in _lines)
            {
                FormatLine(line);
                line.Top = y;
                line.Visual.Offset = new Vector(0, SnapY(y));
                y += line.Height;
            }
            _contentTop = 0;
            _contentBottom = y;
            _viewTop = wasAtEnd || anchor == null ? _contentTop + ScrollableHeight : anchor.Top + Math.Min(anchorDelta, anchor.Height);
            UpdateSelectionVisual();
            _metricsDirty = true;
            ApplyView();
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

        #region 스크롤

        public void ScrollToEnd()
        {
            _viewTop = _contentTop + ScrollableHeight;
            _metricsDirty = true;
            if (_updateDepth == 0)
                ApplyView();
        }

        /// <summary>스크롤 위치를 정한다 (0 = 맨 위).</summary>
        public void ScrollToVerticalOffset(double offset)
        {
            _viewTop = _contentTop + offset;
            _metricsDirty = true;
            if (_updateDepth == 0)
                ApplyView();
        }

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
            ScrollMetricsChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (ScrollableHeight <= 0)
                return;

            double lineHeight = 16;
            if (_lines.Count > 0)
            {
                ChatLineContent last = _lines[^1].Content;
                lineHeight = Math.Max(8, last.FontFamily.LineSpacing * last.FontSize);
            }
            int lines = SystemParameters.WheelScrollLines > 0 ? SystemParameters.WheelScrollLines : 3;
            ScrollToVerticalOffset(VerticalOffset - e.Delta / 120.0 * lines * lineHeight);
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
            Color c = SystemColors.HighlightColor;
            var brush = new SolidColorBrush(Color.FromArgb(0x66, c.R, c.G, c.B));
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

            for (int i = startLine; i <= endLine; i++)
            {
                Line line = _lines[i];
                int length = line.Text.Length;
                int from = i == startLine ? Math.Min(startIndex, length) : 0;
                int to = i == endLine ? Math.Min(endIndex, length) : length;
                if (to <= from)
                    continue;

                Geometry? geometry = line.Formatted.BuildHighlightGeometry(new Point(0, SnapY(line.Top)), from, to - from);
                if (geometry != null)
                    dc.DrawGeometry(SelectionBrush, null, geometry);
            }
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

        private static int HitTestIndex(Line line, Point local)
        {
            int length = line.Text.Length;
            if (length == 0)
                return 0;

            Rect[] bounds = line.CharBounds ??= BuildCharBounds(line);

            // 1) 점이 놓인 글줄(줄바꿈으로 나뉜 줄): 세로로 가장 가까운 글자들의 줄
            double minDy = double.MaxValue;
            for (int i = 0; i < length; i++)
            {
                if (bounds[i].IsEmpty)
                    continue;
                double dy = VerticalDistance(bounds[i], local.Y);
                if (dy < minDy)
                    minDy = dy;
            }
            if (minDy == double.MaxValue)
                return 0;

            // 2) 그 글줄에서 가로로 가장 가까운 글자
            int best = -1;
            double bestDx = double.MaxValue;
            for (int i = 0; i < length; i++)
            {
                Rect r = bounds[i];
                if (r.IsEmpty || VerticalDistance(r, local.Y) > minDy + 0.01)
                    continue;
                double dx = local.X < r.Left ? r.Left - local.X : local.X >= r.Right ? local.X - r.Right : 0;
                if (dx < bestDx)
                {
                    bestDx = dx;
                    best = i;
                }
            }
            if (best < 0)
                return 0;

            Rect hit = bounds[best];
            // 글자의 오른쪽 절반이면 그 글자 뒤
            return local.X >= hit.Left + hit.Width / 2 ? best + 1 : best;
        }

        private static double VerticalDistance(Rect r, double y)
            => y < r.Top ? r.Top - y : y >= r.Bottom ? y - r.Bottom : 0;

        private static Rect[] BuildCharBounds(Line line)
        {
            int length = line.Text.Length;
            var bounds = new Rect[length];
            var origin = new Point(0, 0);
            for (int i = 0; i < length; i++)
            {
                Geometry? geometry = line.Formatted.BuildHighlightGeometry(origin, i, 1);
                bounds[i] = geometry == null ? Rect.Empty : geometry.Bounds;
            }
            return bounds;
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
                _isSelecting = false;
            }
            else
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && _selectionAnchor.HasValue)
                    _selectionActive = position;
                else
                    _selectionAnchor = _selectionActive = position;
                _isSelecting = true;
                CaptureMouse();
            }

            UpdateSelectionVisual();
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_isSelecting || e.LeftButton != MouseButtonState.Pressed)
                return;

            Point point = e.GetPosition(this);
            // 영역 밖으로 끌면 그쪽으로 조금씩 넘긴다
            if (point.Y < 0)
                ScrollToVerticalOffset(VerticalOffset - 16);
            else if (point.Y > ActualHeight)
                ScrollToVerticalOffset(VerticalOffset + 16);

            TextPosition? position = HitTestPosition(point);
            if (!position.HasValue)
                return;
            _selectionActive = position;
            UpdateSelectionVisual();
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_isSelecting)
                return;
            _isSelecting = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            _isSelecting = false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (e.Key == Key.C || e.Key == Key.Insert)
                {
                    CopySelection();
                    e.Handled = true;
                }
                else if (e.Key == Key.A)
                {
                    SelectAll();
                    e.Handled = true;
                }
            }
            else if (e.Key == Key.Escape && HasSelection)
            {
                ClearSelection();
                e.Handled = true;
            }
        }

        #endregion
    }
}
