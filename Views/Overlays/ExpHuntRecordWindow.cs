using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 사냥 기록 창. 던전 타이머 기록 추이(자세히) 창과 같은 말씨 — 민트 제목, 알약 칩, 수치 타일, 카드에 담은 그래프와 표, 힌트 각주.
    /// 고른 날의 판을 하루 시간축 위에 막대로(가로 = 사냥한 시간대, 세로 = 번 경험치) 그리고, 아래 표에 판별 수치를 적는다.
    /// 기록은 <see cref="ExpHuntSessionService"/>가 쌓아 둔 요약 파일에서 읽는다.
    ///
    /// 그리기 원칙 (기록 추이 창과 같다):
    ///  - 계열은 하나뿐이라 색도 민트 하나. 그 날 최고 판만 진하게, 나머지는 같은 색을 옅게. 글자는 글자 색 토큰만 쓴다.
    ///  - 숫자를 모든 막대에 붙이지 않는다 — 최고 판에만 직접 표기하고, 나머지는 마우스를 올리면 말풍선으로.
    ///  - 격자·축은 가는 실선으로 물러나 있고, 표는 줄무늬와 막대로 크기를 눈으로 비교하게 한다.
    /// </summary>
    public sealed class ExpHuntRecordWindow : OverlayWindowBase
    {
        private static ExpHuntRecordWindow? _instance;
        private static readonly Color Mint = OverlayChrome.Mint;
        private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");

        /// <summary>날짜 목록에 둘 날 수.</summary>
        private const int MaxDays = 90;
        private const double ContentWidth = 720;
        private const double ChartWidth = ContentWidth - 28;   // 카드 안쪽 폭
        private const double ChartHeight = 236;
        private const double PlotLeft = 56, PlotRight = 14, PlotTop = 24, PlotBottom = 26;
        private const double MinFont = 11, MaxFont = 26;

        private readonly ExpHuntSessionService _service;
        private readonly ChatSettings? _settings;

        private readonly TextBlock _fontSizeText;
        private readonly Button _dateButton;
        private readonly Button _prevButton;
        private readonly Button _nextButton;
        private readonly Popup _datePopup;
        private readonly ListBox _dayList;
        private readonly UniformGrid _tiles;
        private readonly Canvas _chart;
        private readonly StackPanel _rowsPanel;
        private readonly ScrollViewer _rowScroll;
        private readonly Border _tooltip;
        private readonly TextBlock _tooltipTitle;
        private readonly TextBlock _tooltipValue;
        private readonly TextBlock _tooltipDetail;

        private List<ExpHuntSession> _all = new();
        private List<DateTime> _days = new();
        private DateTime _selectedDay;
        private double _fontSize = 15;

        protected override bool UseToolWindowStyle => true;
        /// <summary>기록만 보는 창이라 잠금 상태에서도 끌어 옮길 수 있게 둔다.</summary>
        protected override bool DragRequiresUnlock => false;
        protected override bool PersistBoundsOnChange => false; // 드래그 끝·닫힘에 저장
        protected override ChatSettings? ResolveSettings() => _settings ?? base.ResolveSettings();

        protected override bool PersistBounds(ChatSettings settings)
        {
            settings.ExpHuntRecordWindowLeft = Left;
            settings.ExpHuntRecordWindowTop = Top;
            return true;
        }

        protected override (double? Left, double? Top, double? Width, double? Height)? StoredBounds(ChatSettings settings)
            => (settings.ExpHuntRecordWindowLeft, settings.ExpHuntRecordWindowTop, null, null);

        private ExpHuntRecordWindow(ExpHuntSessionService service, ChatSettings? settings)
        {
            _service = service;
            _settings = settings;
            _fontSize = settings?.ExpHuntRecordFontSize ?? 15;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
            Title = "사냥 기록";

            // ── 제목 줄: 민트 제목 · 글자 크기 칩 · 닫기 ──
            var titleText = OverlayChrome.MakeTitle("사냥 기록");

            var fontLabel = new TextBlock { Text = "글자", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            fontLabel.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
            var smaller = MakeChipButton("A−", "글자 작게", () => ApplyFontSize(_fontSize - 1));
            var bigger = MakeChipButton("A+", "글자 크게", () => ApplyFontSize(_fontSize + 1));
            smaller.Margin = new Thickness(0, 0, 4, 0);
            bigger.Margin = new Thickness(0, 0, 6, 0);
            _fontSizeText = new TextBlock { FontSize = 11, Width = 30, VerticalAlignment = VerticalAlignment.Center };
            _fontSizeText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayMutedTextBrush");

            var closeButton = OverlayChrome.MakeCloseButton(() => Close(), new Thickness(10, 0, 0, 0));
            closeButton.VerticalAlignment = VerticalAlignment.Center;

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(fontLabel);
            right.Children.Add(smaller);
            right.Children.Add(bigger);
            right.Children.Add(_fontSizeText);
            right.Children.Add(closeButton);

            var header = OverlayChrome.MakeHeader(titleText, new Thickness(0, 0, 0, 10), right);

            // ── 날짜 줄: ◀ [날짜 ▾] ▶ — 날짜를 누르면 기록이 있는 날 목록 ──
            _prevButton = MakeChipButton("◀", "이전 기록 날짜", () => MoveDay(-1));
            _nextButton = MakeChipButton("▶", "다음 기록 날짜", () => MoveDay(+1));
            _prevButton.Padding = new Thickness(9, 0, 9, 1);
            _nextButton.Padding = new Thickness(9, 0, 9, 1);
            _dateButton = MakeChipButton("-", "날짜 고르기", () => _datePopup!.IsOpen = !_datePopup.IsOpen);
            _dateButton.Tag = "Selected";
            _dateButton.MinWidth = 170;
            _dateButton.Margin = new Thickness(6, 0, 6, 0);

            _dayList = new ListBox { Width = 250, MaxHeight = 360, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(_dayList, ScrollBarVisibility.Disabled);
            _dayList.SetResourceReference(ForegroundProperty, "TextBrush");

            var popupBorder = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(6), Child = _dayList, Margin = new Thickness(0, 4, 0, 0) };
            popupBorder.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
            popupBorder.SetResourceReference(Border.BorderBrushProperty, "OverlayWindowBorderBrush");
            _datePopup = new Popup { PlacementTarget = _dateButton, Placement = PlacementMode.Bottom, StaysOpen = false, Child = popupBorder, AllowsTransparency = true };

            _dayList.SelectionChanged += (_, _) =>
            {
                if (_dayList.SelectedItem is ListBoxItem item && item.Tag is DateTime day && day != _selectedDay)
                {
                    _selectedDay = day;
                    _datePopup.IsOpen = false;
                    RenderDay();
                }
            };

            var dateBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            dateBar.Children.Add(_prevButton);
            dateBar.Children.Add(_dateButton);
            dateBar.Children.Add(_nextButton);
            dateBar.Children.Add(_datePopup);

            // ── 수치 타일 ──
            _tiles = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 0, -4, 8) };

            // ── 그래프 카드 ──
            _chart = new Canvas { Width = ChartWidth, Height = ChartHeight, ClipToBounds = true, Background = Brushes.Transparent };
            _chart.MouseLeave += (_, _) => HideTooltip();
            _tooltipTitle = new TextBlock { FontSize = 11 };
            _tooltipTitle.SetResourceReference(TextBlock.ForegroundProperty, "OverlayMutedTextBrush");
            _tooltipValue = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 1, 0, 1) };
            _tooltipValue.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            _tooltipDetail = new TextBlock { FontSize = 11 };
            _tooltipDetail.SetResourceReference(TextBlock.ForegroundProperty, "OverlayMutedTextBrush");
            var tooltipStack = new StackPanel();
            tooltipStack.Children.Add(_tooltipTitle);
            tooltipStack.Children.Add(_tooltipValue);
            tooltipStack.Children.Add(_tooltipDetail);
            _tooltip = new Border
            {
                Child = tooltipStack,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(9, 6, 9, 7),
                BorderThickness = new Thickness(1),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            _tooltip.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
            _tooltip.SetResourceReference(Border.BorderBrushProperty, "OverlayWindowBorderBrush");

            // ── 표 카드 ──
            _rowsPanel = new StackPanel();
            _rowScroll = new ScrollViewer
            {
                Width = ChartWidth,
                MaxHeight = 236,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _rowsPanel,
            };

            var footnote = new TextBlock { FontSize = 11, Margin = new Thickness(4, 8, 0, 0) };
            footnote.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
            var info = new System.Windows.Documents.Run("ⓘ");
            info.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "OverlayTitleAccentTextBrush");
            footnote.Inlines.Add(info);
            footnote.Inlines.Add(new System.Windows.Documents.Run(" 3분 넘게 사냥하고 100억 넘게 벌면 한 판으로 남습니다 · 채팅 로그 기준"));

            var body = new StackPanel { Width = ContentWidth };
            body.Children.Add(header);
            body.Children.Add(dateBar);
            body.Children.Add(_tiles);
            body.Children.Add(MakeCard(_chart, new Thickness(14, 10, 14, 8)));
            var tableCard = MakeCard(_rowScroll, new Thickness(14, 8, 14, 8));
            tableCard.Margin = new Thickness(0, 8, 0, 0);
            body.Children.Add(tableCard);
            body.Children.Add(footnote);

            Content = OverlayChrome.MakeRootBorder(body, new Thickness(18, 14, 18, 14));

            // 저장된 자리가 있으면 거기서 열고(Manual), 없으면 화면 가운데(CenterScreen)
            ApplyStoredBounds(ResolveSettings());

            // 창 어디를 잡아도 옮길 수 있다. 버튼·목록·스크롤은 눌려야 하므로 가로채지 않는다
            PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (OverlayChrome.IsOnInteractiveControl(e.OriginalSource as DependencyObject))
                    return;
                TryBeginDrag(e, markHandled: true);
            };

            _service.SessionsChanged += OnSessionsChanged;
            Closed += (_, _) =>
            {
                _service.SessionsChanged -= OnSessionsChanged;
                if (ReferenceEquals(_instance, this))
                    _instance = null;
                try { VisibilityChanged?.Invoke(false); } catch { }
            };

            _fontSizeText.Text = $"{(int)Math.Round(_fontSize)}px";
            Reload();
        }

        // ===== 조각 만들기 =====

        private Button MakeChipButton(string text, string tip, Action onClick)
        {
            var button = new Button { Content = text, ToolTip = tip, Style = (Style)FindResource("OverlayChipButtonStyle") };
            button.Click += (_, _) => onClick();
            return button;
        }

        private static Border MakeCard(UIElement child, Thickness padding) => OverlayChrome.MakeCard(child, padding);

        /// <summary>수치 타일 (공용 크롬, 값 글자 20).</summary>
        private static FrameworkElement MakeTile(string label, string value, string sub, bool accent)
            => OverlayChrome.MakeTile(label, value, sub, accent);

        /// <summary>창이 열리거나 닫힐 때 (메뉴 바 버튼 표시용).</summary>
        public static event Action<bool>? VisibilityChanged;

        /// <summary>메뉴 버튼: 떠 있으면 닫고, 없으면 연다.</summary>
        public static void Toggle(ExpHuntSessionService service, ChatSettings? settings = null)
        {
            if (_instance != null && _instance.IsLoaded)
            {
                try { _instance.Close(); } catch { }
                return;
            }

            _instance = new ExpHuntRecordWindow(service, settings);
            _instance.Show();
            TopmostWindowHelper.BringToTopmost(_instance);
            try { VisibilityChanged?.Invoke(true); } catch { }
        }

        private void OnSessionsChanged()
            => Dispatcher.BeginInvoke(new Action(Reload));

        /// <summary>표·그래프 글자 크기를 바꾸고 설정에 남긴다.</summary>
        private void ApplyFontSize(double size)
        {
            _fontSize = Math.Max(MinFont, Math.Min(MaxFont, size));
            _fontSizeText.Text = $"{(int)Math.Round(_fontSize)}px";
            if (_settings != null)
            {
                _settings.ExpHuntRecordFontSize = _fontSize;
                try { ConfigService.SaveDeferred(_settings); } catch { }
            }
            RenderDay();
        }

        // ===== 데이터 =====

        /// <summary>파일을 다시 읽어 날짜 목록을 만들고, 고른 날(없으면 가장 최근 날)을 그린다.</summary>
        private void Reload()
        {
            _all = _service.Load().OrderBy(static s => s.StartedAt).ToList();
            _days = _all.Select(static s => s.StartedAt.Date).Distinct().OrderByDescending(static d => d).Take(MaxDays).ToList();
            if (_selectedDay == default || !_days.Contains(_selectedDay))
                _selectedDay = _days.Count > 0 ? _days[0] : DateTime.Today;

            _dayList.Items.Clear();
            foreach (DateTime day in _days)
            {
                var runs = _all.Where(s => s.StartedAt.Date == day).ToList();
                _dayList.Items.Add(MakeDayItem(day, runs.Count, runs.Sum(static s => s.TotalExp)));
            }
            SelectDayItem();
            RenderDay();
        }

        /// <summary>날짜 목록 한 줄: 날짜 · 판 수 · 합계. 고른 날은 민트 글자.</summary>
        private ListBoxItem MakeDayItem(DateTime day, int runs, long total)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dateText = new TextBlock { Text = day.ToString("M.d (ddd)", Korean), FontSize = 12.5 };
            var sumText = new TextBlock { Text = $"{runs}판 · {FormatExpShort(total)}", FontSize = 11.5, Margin = new Thickness(12, 0, 0, 0) };
            sumText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayMutedTextBrush");
            Grid.SetColumn(sumText, 1);
            grid.Children.Add(dateText);
            grid.Children.Add(sumText);
            var item = new ListBoxItem { Content = grid, Tag = day, Padding = new Thickness(10, 5, 10, 5), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
            item.SetResourceReference(ForegroundProperty, "TextBrush");
            return item;
        }

        private void SelectDayItem()
        {
            foreach (object? entry in _dayList.Items)
            {
                if (entry is ListBoxItem item && item.Tag is DateTime day && day == _selectedDay)
                {
                    _dayList.SelectedItem = item;
                    break;
                }
            }
        }

        /// <summary>앞뒤 날짜로 옮긴다 (목록은 최근이 위라 ◀가 과거).</summary>
        private void MoveDay(int direction)
        {
            int index = _days.IndexOf(_selectedDay);
            if (index < 0)
                return;
            int next = index + (direction < 0 ? 1 : -1);
            if (next < 0 || next >= _days.Count)
                return;
            _selectedDay = _days[next];
            SelectDayItem();
            RenderDay();
        }

        // ===== 그리기 =====

        private void RenderDay()
        {
            var sessions = _all.Where(s => s.StartedAt.Date == _selectedDay).OrderBy(static s => s.StartedAt).ToList();

            _dateButton.Content = _selectedDay.ToString("yyyy.M.d (ddd)", Korean) + "  ▾";
            int index = _days.IndexOf(_selectedDay);
            _prevButton.IsEnabled = index >= 0 && index < _days.Count - 1;
            _nextButton.IsEnabled = index > 0;

            RenderTiles(sessions);
            DrawChart(sessions);
            RenderTable(sessions);
        }

        private void RenderTiles(List<ExpHuntSession> sessions)
        {
            _tiles.Children.Clear();
            if (sessions.Count == 0)
            {
                _tiles.Children.Add(MakeTile("판", "0판", "기록 없음", accent: false));
                _tiles.Children.Add(MakeTile("총 경험치", "-", string.Empty, accent: false));
                _tiles.Children.Add(MakeTile("평균 시간당", "-", string.Empty, accent: false));
                _tiles.Children.Add(MakeTile("최고 판", "-", string.Empty, accent: false));
                return;
            }

            long dayTotal = sessions.Sum(static s => s.TotalExp);
            double seconds = sessions.Sum(static s => s.Duration.TotalSeconds);
            long perHour = seconds > 0 ? (long)(dayTotal / seconds * 3600) : 0;
            ExpHuntSession best = sessions.MaxBy(static s => s.TotalExp)!;
            _tiles.Children.Add(MakeTile("판", $"{sessions.Count}판", $"사냥 {FormatDuration(TimeSpan.FromSeconds(seconds))}", accent: false));
            _tiles.Children.Add(MakeTile("총 경험치", FormatExp(dayTotal), "하루 합계", accent: false));
            _tiles.Children.Add(MakeTile("평균 시간당", FormatExp(perHour), "사냥 시간 기준", accent: false));
            _tiles.Children.Add(MakeTile("최고 판", FormatExp(best.TotalExp), $"{best.StartedAt:HH:mm} ~ {best.EndedAt:HH:mm}", accent: true));
        }

        /// <summary>
        /// 하루 시간축 위의 막대: 가로 자리·폭 = 사냥한 시간대, 세로 = 번 경험치. 축은 그 날 기록을 시 단위로 감싸는 범위.
        /// 최고 판만 진한 민트 + 값 표기, 나머지는 옅은 민트. 막대에 마우스를 올리면 말풍선.
        /// </summary>
        private void DrawChart(List<ExpHuntSession> sessions)
        {
            _chart.Children.Clear();
            HideTooltip();

            double plotW = ChartWidth - PlotLeft - PlotRight;
            double plotH = ChartHeight - PlotTop - PlotBottom;
            double baselineY = PlotTop + plotH;
            var gridBrush = (Brush)FindResource("OverlayCardBorderBrush");
            var hintBrush = (Brush)FindResource("OverlayHintTextBrush");
            var textBrush = (Brush)FindResource("TextBrush");
            double labelSize = Math.Max(10, Math.Min(13, _fontSize - 3));

            if (sessions.Count == 0)
            {
                var empty = new TextBlock { Text = "이 날은 기록이 없습니다", FontSize = 12, Foreground = hintBrush };
                Canvas.SetLeft(empty, PlotLeft);
                Canvas.SetTop(empty, PlotTop + plotH / 2 - 8);
                _chart.Children.Add(empty);
                return;
            }

            // 가로축: 첫 판 시작 시 ~ 마지막 판 끝 시, 최소 6시간
            DateTime day = _selectedDay;
            double startHour = Math.Floor((sessions[0].StartedAt - day).TotalHours);
            double endHour = Math.Ceiling((sessions[^1].EndedAt - day).TotalHours);
            if (endHour - startHour < 6) { double mid = (startHour + endHour) / 2; startHour = Math.Max(0, Math.Floor(mid - 3)); endHour = startHour + 6; }
            endHour = Math.Min(24, Math.Max(endHour, startHour + 6));
            startHour = Math.Max(0, endHour - Math.Max(6, endHour - startHour));
            double hours = endHour - startHour;
            double X(DateTime at) => PlotLeft + ((at - day).TotalHours - startHour) / hours * plotW;

            // 세로축: 0 ~ 최고 판을 보기 좋은 눈금으로
            long max = sessions.Max(static s => s.TotalExp);
            double step = NiceStep(max / 4.5);
            double yMax = Math.Ceiling(max / step) * step;
            if (yMax <= 0) yMax = step;
            double Y(double value) => PlotTop + plotH - value / yMax * plotH;

            for (double v = step; v <= yMax + step / 2; v += step)
            {
                double y = Y(v);
                _chart.Children.Add(new Line { X1 = PlotLeft, X2 = PlotLeft + plotW, Y1 = y, Y2 = y, Stroke = gridBrush, StrokeThickness = 1, Opacity = 0.7, SnapsToDevicePixels = true });
                var label = new TextBlock { Text = FormatExpShort((long)v), FontSize = 10.5, Foreground = hintBrush, Width = PlotLeft - 8, TextAlignment = TextAlignment.Right };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, y - 8);
                _chart.Children.Add(label);
            }

            int tick = hours <= 8 ? 1 : hours <= 14 ? 2 : 3;
            for (double h = startHour; h <= endHour + 0.001; h += tick)
            {
                double x = PlotLeft + (h - startHour) / hours * plotW;
                _chart.Children.Add(new Line { X1 = x, X2 = x, Y1 = baselineY, Y2 = baselineY + 4, Stroke = gridBrush, StrokeThickness = 1, SnapsToDevicePixels = true });
                var label = new TextBlock { Text = $"{(int)h % 24:00}:00", FontSize = 10.5, Foreground = hintBrush, Width = 44, TextAlignment = TextAlignment.Center };
                Canvas.SetLeft(label, x - 22);
                Canvas.SetTop(label, baselineY + 7);
                _chart.Children.Add(label);
            }
            _chart.Children.Add(new Line { X1 = PlotLeft, X2 = PlotLeft + plotW, Y1 = baselineY, Y2 = baselineY, Stroke = gridBrush, StrokeThickness = 1, SnapsToDevicePixels = true });

            var mint = new SolidColorBrush(Mint);
            var mintSoft = new SolidColorBrush(Color.FromArgb(0xA6, Mint.R, Mint.G, Mint.B));
            foreach (ExpHuntSession session in sessions)
            {
                bool isBest = session.TotalExp == max;
                double x1 = X(session.StartedAt), x2 = X(session.EndedAt);
                double width = Math.Max(8, x2 - x1);
                double top = Y(session.TotalExp);
                var bar = new Border
                {
                    Width = width,
                    Height = Math.Max(2, baselineY - top),
                    CornerRadius = new CornerRadius(3, 3, 0, 0),
                    Background = isBest ? mint : mintSoft,
                    Cursor = Cursors.Hand,
                    Tag = session,
                };
                Canvas.SetLeft(bar, x1);
                Canvas.SetTop(bar, top);
                bar.MouseEnter += (s, _) => { ((Border)s).Background = mint; ShowTooltip(session, x1 + width / 2, top); };
                bar.MouseLeave += (s, _) => { ((Border)s).Background = isBest ? mint : mintSoft; HideTooltip(); };
                _chart.Children.Add(bar);

                if (isBest)
                {
                    var label = new TextBlock { Text = FormatExp(session.TotalExp), FontSize = labelSize, FontWeight = FontWeights.SemiBold, Foreground = textBrush };
                    label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double lx = Math.Max(PlotLeft, Math.Min(PlotLeft + plotW - label.DesiredSize.Width, x1 + width / 2 - label.DesiredSize.Width / 2));
                    Canvas.SetLeft(label, lx);
                    Canvas.SetTop(label, Math.Max(2, top - label.DesiredSize.Height - 3));
                    _chart.Children.Add(label);
                }
            }

            _chart.Children.Add(_tooltip);
        }

        private void ShowTooltip(ExpHuntSession session, double anchorX, double anchorY)
        {
            _tooltipTitle.Text = $"{session.StartedAt:HH:mm} ~ {session.EndedAt:HH:mm} · {FormatDuration(session.Duration)}";
            _tooltipValue.Text = FormatExp(session.TotalExp);
            _tooltipDetail.Text = $"시간당 {FormatExp(session.ExpPerHour)} · {session.GainCount:N0}마리";
            _tooltip.Visibility = Visibility.Visible;
            _tooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = _tooltip.DesiredSize.Width, h = _tooltip.DesiredSize.Height;
            double left = Math.Max(2, Math.Min(ChartWidth - w - 2, anchorX - w / 2));
            double top = anchorY - h - 10;
            if (top < 2) top = Math.Min(ChartHeight - h - 2, anchorY + 12);
            Canvas.SetLeft(_tooltip, left);
            Canvas.SetTop(_tooltip, top);
        }

        private void HideTooltip() => _tooltip.Visibility = Visibility.Collapsed;

        /// <summary>표: 머리글 줄 아래 가는 선, 줄무늬, 획득 경험치 칸에 비교 막대, 최고 판에 알약.</summary>
        private void RenderTable(List<ExpHuntSession> sessions)
        {
            _rowsPanel.Children.Clear();
            double headerSize = Math.Max(10, _fontSize - 3);
            double[] widths = { 176, 70, 196, 124, 98 };
            string[] headers = { "시간", "길이", "획득 경험치", "시간당", "마리수" };

            var head = new Grid { Margin = new Thickness(8, 4, 8, 4) };
            foreach (double w in widths)
                head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = new TextBlock
                {
                    Text = headers[c],
                    FontSize = headerSize,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = c <= 1 ? TextAlignment.Left : TextAlignment.Right,
                    Margin = c == 2 ? new Thickness(0, 0, 70, 0) : new Thickness(0), // 획득 경험치 머리글은 값 자리에 맞춘다 (막대 왼쪽)
                };
                cell.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
                Grid.SetColumn(cell, c);
                head.Children.Add(cell);
            }
            _rowsPanel.Children.Add(head);
            var rule = new Border { Height = 1, Margin = new Thickness(4, 0, 4, 2) };
            rule.SetResourceReference(Border.BackgroundProperty, "OverlayCardBorderBrush");
            _rowsPanel.Children.Add(rule);

            if (sessions.Count == 0)
            {
                var empty = new TextBlock { Text = "이 날은 남은 판이 없습니다", FontSize = Math.Max(11, _fontSize - 2), Margin = new Thickness(8, 10, 8, 8) };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
                _rowsPanel.Children.Add(empty);
                return;
            }

            long max = sessions.Max(static s => s.TotalExp);
            var mintSoft = new SolidColorBrush(Color.FromArgb(0x66, Mint.R, Mint.G, Mint.B));
            var mintFill = new SolidColorBrush(Mint);
            for (int i = 0; i < sessions.Count; i++)
            {
                ExpHuntSession s = sessions[i];
                bool isBest = s.TotalExp == max;
                var grid = new Grid();
                foreach (double w in widths)
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });

                var timePanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var timeText = new TextBlock { Text = $"{s.StartedAt:HH:mm} ~ {s.EndedAt:HH:mm}", FontSize = _fontSize, VerticalAlignment = VerticalAlignment.Center };
                timeText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                timePanel.Children.Add(timeText);
                if (isBest)
                {
                    // 최고 판 알약은 숫자 열을 밀지 않게 시간 옆에 둔다
                    var pillText = new TextBlock { Text = "최고", FontSize = Math.Max(9, _fontSize - 5), FontWeight = FontWeights.SemiBold, Foreground = mintFill };
                    var pill = new Border { Child = pillText, CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(7, 0, 0, 0), BorderThickness = new Thickness(1), BorderBrush = mintSoft, Background = new SolidColorBrush(Color.FromArgb(0x22, Mint.R, Mint.G, Mint.B)), VerticalAlignment = VerticalAlignment.Center };
                    timePanel.Children.Add(pill);
                }
                Grid.SetColumn(timePanel, 0);
                grid.Children.Add(timePanel);
                grid.Children.Add(Cell(FormatDuration(s.Duration), 1, TextAlignment.Left, "OverlayMutedTextBrush", FontWeights.Normal));

                // 획득 경험치: [값][막대] — 값은 고정 폭 오른쪽 정렬, 막대는 그 날 최고 대비
                var expPanel = new Grid();
                expPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                expPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                var expText = new TextBlock { Text = FormatExp(s.TotalExp), FontSize = _fontSize, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                expText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 };
                track.SetResourceReference(Border.BackgroundProperty, "OverlayCardBorderBrush");
                var fill = new Border { Height = 6, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Background = isBest ? mintFill : mintSoft, Width = Math.Max(4, 60.0 * s.TotalExp / Math.Max(1, max)), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(track, 1);
                Grid.SetColumn(fill, 1);
                expPanel.Children.Add(expText);
                expPanel.Children.Add(track);
                expPanel.Children.Add(fill);
                Grid.SetColumn(expPanel, 2);
                grid.Children.Add(expPanel);

                grid.Children.Add(Cell(FormatExp(s.ExpPerHour), 3, TextAlignment.Right, "TextBrush", FontWeights.Normal));

                grid.Children.Add(Cell($"{s.GainCount:N0}마리", 4, TextAlignment.Right, "OverlayMutedTextBrush", FontWeights.Normal));

                var row = new Border { Child = grid, CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 5, 8, 6), Background = Brushes.Transparent };
                if (i % 2 == 1)
                    row.SetResourceReference(Border.BackgroundProperty, "OverlayTabHoverBrush");
                _rowsPanel.Children.Add(row);
            }

            TextBlock Cell(string text, int column, TextAlignment align, string brushKey, FontWeight weight)
            {
                var cell = new TextBlock { Text = text, FontSize = _fontSize, FontWeight = weight, TextAlignment = align, VerticalAlignment = VerticalAlignment.Center };
                cell.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
                Grid.SetColumn(cell, column);
                return cell;
            }
        }

        // ===== 숫자 =====

        /// <summary>축 눈금 간격: 1·2·5 × 10ⁿ 중 raw 이상인 가장 작은 값.</summary>
        private static double NiceStep(double raw)
        {
            if (raw <= 0) return 1;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            foreach (double m in new[] { 1, 2, 5, 10 })
                if (m * magnitude >= raw) return m * magnitude;
            return 10 * magnitude;
        }

        /// <summary>"1시간 15분" · "45분" 꼴.</summary>
        private static string FormatDuration(TimeSpan span)
        {
            int minutes = span > TimeSpan.Zero ? (int)span.TotalMinutes : 0;
            if (minutes < 60)
                return $"{minutes}분";
            return minutes % 60 == 0 ? $"{minutes / 60}시간" : $"{minutes / 60}시간 {minutes % 60}분";
        }

        /// <summary>좁은 자리(축·날짜 목록)용 짧은 표기 — "9.2조", "1,725억".</summary>
        private static string FormatExpShort(long value)
        {
            if (value >= 1_000_000_000_000)
                return $"{value / 1_000_000_000_000d:F1}조";
            if (value >= 100_000_000)
                return $"{value / 100_000_000d:N0}억";
            return $"{value / 10_000d:N0}만";
        }

        /// <summary>경험치 표기 — 경험치 추적창과 같은 단위(조·억·만).</summary>
        private static string FormatExp(long value)
        {
            if (value >= 1_000_000_000_000)
            {
                long jo = value / 1_000_000_000_000;
                double eok = (value % 1_000_000_000_000) / 100_000_000.0;
                return $"{jo}조 {Math.Floor(eok * 10) / 10.0:F1}억";
            }
            if (value >= 100_000_000)
                return $"{Math.Floor(value / 100_000_000.0 * 10) / 10.0:F1}억";
            if (value >= 10_000)
                return $"{value / 10_000d:N1}만";
            return value.ToString("N0");
        }
    }
}
