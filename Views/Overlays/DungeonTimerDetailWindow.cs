using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 던전 타이머 기록 추이 창. 타이머 창의 [자세히]로 연다.
    /// 세로축 = 클리어 시간, 가로축 = 첫 기록부터 지금까지의 기간. 점 하나가 한 주(월~일)의 평균이다.
    ///
    /// 그리는 규칙:
    ///  - 한 번에 한 계열만 보여 준다 (던전·난이도·구간을 위 선택 줄에서 고른다) → 범례가 없고 색은 민트 하나.
    ///  - 선 2px + 점(표면색 테두리). 격자·축은 가는 실선으로 물러나 있다.
    ///  - 숫자를 점마다 붙이지 않는다. 가장 빨랐던 주와 최근 주만 직접 표기하고, 나머지는 마우스를 올리면 십자선+말풍선으로 읽는다.
    ///  - 말풍선이 유일한 통로가 되지 않게 [표]로 바꿔 같은 값을 주 단위 목록으로 볼 수 있다.
    ///  - 글자는 글자 색(TextBrush/Hint)만 쓴다. 계열 색은 선·점에만 쓴다.
    /// </summary>
    public sealed class DungeonTimerDetailWindow : OverlayWindowBase
    {
        private static DungeonTimerDetailWindow? _instance;

        private const double ChartWidth = 700;
        private const double ChartHeight = 300;
        private const double PlotLeft = 56, PlotRight = 18, PlotTop = 22, PlotBottom = 30;
        private const string WholeRunChoice = "전체";

        private static readonly Color SeriesColor = Color.FromRgb(0x0C, 0xD2, 0x9D);
        private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");

        /// <summary>한 주(월요일 0시 ~ 일요일)의 집계.</summary>
        private sealed record WeekPoint(DateTime WeekStart, double Average, int Count, double Best)
        {
            public DateTime WeekEnd => WeekStart.AddDays(6);
            /// <summary>가로 위치는 주의 한가운데 (눈금은 월요일에 선다).</summary>
            public DateTime Middle => WeekStart.AddDays(3.5);
        }

        private readonly ContentTimerService _service;
        private readonly IReadOnlyList<ContentTimerService.DungeonDefinition> _members;

        private ContentTimerService.DungeonDefinition _def;
        private string _difficulty = string.Empty;
        private string _segment = WholeRunChoice;
        private bool _showTable;

        private IReadOnlyList<DungeonRunRecord> _allRecords = Array.Empty<DungeonRunRecord>();
        private List<(DateTime At, double Seconds)> _runs = new();
        private List<WeekPoint> _weeks = new();
        private int _loadVersion;

        private readonly TextBlock _titleText;
        private readonly StackPanel _filterPanel;
        private readonly TextBlock _summaryText;
        private readonly TextBlock _noteText;
        private readonly Canvas _chart;
        private readonly Border _chartHost;
        private readonly ScrollViewer _tableHost;
        private readonly StackPanel _tableRows;
        private readonly Button _viewToggle;
        private readonly DispatcherTimer _reloadTimer;

        // 마우스 올림 표시 요소 (다시 그릴 때마다 새로 만든다)
        private Line? _crosshair;
        private Ellipse? _hoverDot;
        private Border? _tooltip;
        private TextBlock? _tooltipValue;
        private TextBlock? _tooltipDetail;
        private double _xMinTicks, _xMaxTicks, _yMin, _yMax;

        public static void ShowFor(string groupKey)
        {
            try
            {
                if (_instance?.IsLoaded == true)
                {
                    try { _instance.Close(); } catch { }
                }
                _instance = new DungeonTimerDetailWindow(groupKey);
                _instance.Show();
                _instance.BringToFront();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to show dungeon timer detail window.", ex);
            }
        }

        private DungeonTimerDetailWindow(string groupKey)
        {
            _service = AppServices.Get<ContentTimerService>();
            _members = _service.GetGroupMembers(groupKey);
            if (_members.Count == 0)
                _members = _service.GetGroupMembers(_service.Groups[0].Key);
            _def = _members[0];

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Title = "던전 타이머 기록 추이";

            _titleText = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold };
            _titleText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayTitleAccentTextBrush");

            Button HeaderButton(string? content, string? toolTip)
            {
                var button = new Button
                {
                    Content = content,
                    Height = 22,
                    MinWidth = 40,
                    Padding = new Thickness(8, 0, 8, 0),
                    FontSize = 11,
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Top,
                    ToolTip = toolTip,
                };
                button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
                return button;
            }

            var closeButton = HeaderButton("닫기", null);
            closeButton.Click += (_, _) => { try { Close(); } catch { } };

            _viewToggle = HeaderButton(null, "그래프 / 표 전환");
            _viewToggle.Margin = new Thickness(0, 0, 6, 0);
            _viewToggle.Click += (_, _) => { _showTable = !_showTable; ApplyViewMode(); };

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8), Background = Brushes.Transparent };
            DockPanel.SetDock(closeButton, Dock.Right);
            DockPanel.SetDock(_viewToggle, Dock.Right);
            header.Children.Add(closeButton);
            header.Children.Add(_viewToggle);
            header.Children.Add(_titleText);
            // 제목 줄을 잡고 옮긴다 (잠금과 무관). 그래프 영역은 마우스 올림에 쓰므로 제외.
            header.MouseLeftButtonDown += (_, e) => TryBeginDrag(e);

            // 선택 줄: 그래프 위 한 곳. 아래의 요약·그래프·표가 모두 이 선택을 따른다.
            _filterPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };

            _summaryText = new TextBlock { FontSize = 12, Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap };
            _summaryText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            _noteText = new TextBlock { FontSize = 11, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            _noteText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");

            _chart = new Canvas { Width = ChartWidth, Height = ChartHeight, ClipToBounds = true, Background = Brushes.Transparent };
            _chart.MouseMove += (_, e) => ShowHoverAt(e.GetPosition(_chart).X);
            _chart.MouseLeave += (_, _) => HideHover();
            _chartHost = new Border { Child = _chart };

            _tableRows = new StackPanel();
            _tableHost = new ScrollViewer
            {
                Width = ChartWidth,
                Height = ChartHeight,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _tableRows,
                Visibility = Visibility.Collapsed,
            };

            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(_filterPanel);
            body.Children.Add(_summaryText);
            body.Children.Add(_noteText);
            body.Children.Add(_chartHost);
            body.Children.Add(_tableHost);

            var root = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 12, 16, 14),
                Child = body,
            };
            root.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
            root.SetResourceReference(Border.BorderBrushProperty, "OverlayWindowBorderBrush");
            Content = root;

            // 기록이 더해질 때(실시간 클리어·과거 로그 추출) 몰아서 한 번만 다시 읽는다
            _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); _ = ReloadAsync(); };
            _service.ArchiveChanged += OnArchiveChanged;
            Closed += (_, _) =>
            {
                _service.ArchiveChanged -= OnArchiveChanged;
                _reloadTimer.Stop();
                if (ReferenceEquals(_instance, this))
                    _instance = null;
            };

            ApplyViewMode();
            Loaded += (_, _) => _ = ReloadAsync();
        }

        protected override bool KeepBelowSettingsHost => false;
        protected override bool UseToolWindowStyle => true;
        protected override bool DragRequiresUnlock => false;
        protected override bool PersistBoundsOnChange => false;

        private void OnArchiveChanged()
            => Dispatcher.BeginInvoke(new Action(() => { _reloadTimer.Stop(); _reloadTimer.Start(); }));

        // ===== 데이터 =====

        private async Task ReloadAsync()
        {
            int version = ++_loadVersion;
            var def = _def;
            IReadOnlyList<DungeonRunRecord> records;
            try
            {
                records = await Task.Run(() => _service.GetArchive(def));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load dungeon timer archive.", ex);
                records = Array.Empty<DungeonRunRecord>();
            }

            if (version != _loadVersion)
                return; // 그 사이 다른 던전을 골랐다

            _allRecords = records;
            NormalizeSelection();
            BuildFilters();
            RebuildWeeks();
            RenderAll();
        }

        /// <summary>고른 난이도·구간이 이 던전에 없으면 있는 값으로 되돌린다.</summary>
        private void NormalizeSelection()
        {
            var difficulties = Difficulties();
            if (!difficulties.Contains(_difficulty))
                _difficulty = _allRecords.Count > 0 ? (_allRecords[^1].Difficulty ?? string.Empty) : (difficulties.FirstOrDefault() ?? string.Empty);

            var segments = SegmentChoices();
            if (!segments.Contains(_segment))
                _segment = segments[0];
        }

        private List<string> Difficulties()
            => _allRecords.Select(r => r.Difficulty ?? string.Empty).Distinct().OrderBy(d => d, StringComparer.CurrentCulture).ToList();

        /// <summary>볼 수 있는 값: 판 전체, 그리고 구간이 여럿인 던전은 구간별. 합계가 의미 없는 던전(ShowTotal=false)은 전체를 빼고 구간만.</summary>
        private List<string> SegmentChoices()
        {
            var names = _def.Segments.Where(s => !s.WholeRun).Select(s => s.Name).ToList();
            var choices = new List<string>();
            if (_def.ShowTotal || names.Count == 0)
                choices.Add(WholeRunChoice);
            if (names.Count > 1 || !_def.ShowTotal)
                choices.AddRange(names);
            if (choices.Count == 0)
                choices.Add(WholeRunChoice);
            return choices;
        }

        /// <summary>월요일 0시 기준의 주 시작.</summary>
        private static DateTime WeekStartOf(DateTime at)
            => at.Date.AddDays(-(((int)at.DayOfWeek + 6) % 7));

        /// <summary>고른 난이도·구간의 판들을 모으고, 월~일 단위 평균으로 묶는다.</summary>
        private void RebuildWeeks()
        {
            _runs = new List<(DateTime, double)>();
            foreach (DungeonRunRecord record in _allRecords)
            {
                if ((record.Difficulty ?? string.Empty) != _difficulty)
                    continue;
                if (record.Capped)
                    continue; // "N초 이하"는 실제 시간을 모르므로 평균에 넣지 않는다

                double seconds;
                if (_segment == WholeRunChoice)
                    seconds = record.TotalSeconds;
                else if (!record.Segments.TryGetValue(_segment, out seconds))
                    continue;

                if (seconds > 0)
                    _runs.Add((record.EndedAt, seconds));
            }

            _weeks = _runs
                .GroupBy(r => WeekStartOf(r.At))
                .OrderBy(g => g.Key)
                .Select(g => new WeekPoint(g.Key, g.Average(r => r.Seconds), g.Count(), g.Min(r => r.Seconds)))
                .ToList();
        }

        // ===== 선택 줄 =====

        private void BuildFilters()
        {
            _filterPanel.Children.Clear();

            if (_members.Count > 1)
            {
                _filterPanel.Children.Add(BuildChoiceRow("던전", _members.Select(m => m.Name).ToList(), _def.Name, name =>
                {
                    _def = _members.First(m => m.Name == name);
                    _ = ReloadAsync();
                }));
            }

            var difficulties = Difficulties();
            if (difficulties.Count > 1)
                _filterPanel.Children.Add(BuildChoiceRow("난이도", difficulties, _difficulty, value => { _difficulty = value; OnSelectionChanged(); }));

            var segments = SegmentChoices();
            if (segments.Count > 1)
                _filterPanel.Children.Add(BuildChoiceRow("구간", segments, _segment, value => { _segment = value; OnSelectionChanged(); }));
        }

        private void OnSelectionChanged()
        {
            BuildFilters();
            RebuildWeeks();
            RenderAll();
        }

        private static FrameworkElement BuildChoiceRow(string label, IReadOnlyList<string> options, string selected, Action<string> onSelect)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            var caption = new TextBlock { Text = label, FontSize = 11, Width = 44, VerticalAlignment = VerticalAlignment.Center };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
            row.Children.Add(caption);

            foreach (string option in options)
            {
                bool isSelected = option == selected;
                var button = new Button
                {
                    Content = string.IsNullOrEmpty(option) ? "기본" : option,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 4, 2),
                    Padding = new Thickness(8, 1, 8, 1),
                    Cursor = Cursors.Hand,
                    FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                    Opacity = isSelected ? 1.0 : 0.65,
                };
                button.SetResourceReference(StyleProperty, "WindowSmallButtonStyle");
                string captured = option;
                button.Click += (_, _) => onSelect(captured);
                row.Children.Add(button);
            }
            return row;
        }

        // ===== 그리기 =====

        private void ApplyViewMode()
        {
            _viewToggle.Content = _showTable ? "그래프" : "표";
            _chartHost.Visibility = _showTable ? Visibility.Collapsed : Visibility.Visible;
            _tableHost.Visibility = _showTable ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RenderAll()
        {
            string what = _segment == WholeRunChoice ? string.Empty : $" · {_segment}";
            string diff = string.IsNullOrEmpty(_difficulty) ? string.Empty : $" ({_difficulty})";
            _titleText.Text = $"{_def.Name}{diff}{what} 주간 평균 추이";

            RenderSummary();
            RenderChart();
            RenderTable();
        }

        private void RenderSummary()
        {
            if (_service.IsArchiveScanRunning)
            {
                _noteText.Text = "과거 채팅 로그에서 기록을 뽑는 중입니다. 끝나면 자동으로 갱신됩니다. (처음 한 번만 실행됩니다)";
                _noteText.Visibility = Visibility.Visible;
            }
            else
            {
                _noteText.Visibility = Visibility.Collapsed;
            }

            if (_runs.Count == 0)
            {
                _summaryText.Text = "아직 기록이 없습니다.";
                return;
            }

            var best = _runs.MinBy(r => r.Seconds);
            _summaryText.Text =
                $"기록 {_runs.Count:N0}판 · {_weeks.Count:N0}주     Best {FormatDuration(best.Seconds)} ({best.At:yyyy.M.d})     전체 평균 {FormatDuration(_runs.Average(r => r.Seconds))}";
        }

        private void RenderChart()
        {
            _chart.Children.Clear();
            _crosshair = null; _hoverDot = null; _tooltip = null;

            Brush gridBrush = ResourceBrush("ControlBorderBrush", Color.FromRgb(0x2A, 0x33, 0x2E));
            Brush hintBrush = ResourceBrush("OverlayHintTextBrush", Color.FromRgb(0x8C, 0x91, 0x97));
            Brush textBrush = ResourceBrush("TextBrush", Colors.White);
            Color surface = (ResourceBrush("OverlayWindowBackgroundBrush", Color.FromRgb(0x11, 0x11, 0x11)) as SolidColorBrush)?.Color
                            ?? Color.FromRgb(0x11, 0x11, 0x11);
            var surfaceBrush = new SolidColorBrush(Color.FromRgb(surface.R, surface.G, surface.B));
            var seriesBrush = new SolidColorBrush(SeriesColor);

            double plotW = ChartWidth - PlotLeft - PlotRight;
            double plotH = ChartHeight - PlotTop - PlotBottom;

            if (_weeks.Count == 0)
            {
                var empty = new TextBlock { Text = "표시할 기록이 없습니다", FontSize = 13, Foreground = hintBrush };
                Canvas.SetLeft(empty, PlotLeft + plotW / 2 - 70);
                Canvas.SetTop(empty, PlotTop + plotH / 2 - 10);
                _chart.Children.Add(empty);
                return;
            }

            // 가로축: 첫 기록이 있는 주 ~ 이번 주 끝
            DateTime xStart = _weeks[0].WeekStart;
            DateTime xEnd = WeekStartOf(DateTime.Now).AddDays(7);
            if (xEnd <= xStart) xEnd = xStart.AddDays(7);
            _xMinTicks = xStart.Ticks;
            _xMaxTicks = xEnd.Ticks;

            // 세로축: 값 범위를 깔끔한 눈금으로 감싼다 (선 그래프는 위치로 읽으므로 0에서 시작하지 않아도 된다)
            double dataMin = _weeks.Min(w => w.Average), dataMax = _weeks.Max(w => w.Average);
            double step = NiceStep(Math.Max(dataMax - dataMin, 1) * 1.15);
            _yMin = Math.Max(0, Math.Floor(dataMin / step) * step);
            _yMax = Math.Ceiling(dataMax / step) * step;
            if (_yMax <= _yMin) _yMax = _yMin + step;
            if ((dataMax - _yMin) > (_yMax - _yMin) * 0.92) _yMax += step;          // 위쪽 직접 표기 여유
            if ((dataMin - _yMin) < (_yMax - _yMin) * 0.10 && _yMin >= step) _yMin -= step; // 아래쪽 직접 표기 여유

            // 격자(가로) + 세로축 눈금
            for (double v = _yMin; v <= _yMax + 0.001; v += step)
            {
                double y = Y(v, plotH);
                _chart.Children.Add(new Line { X1 = PlotLeft, X2 = PlotLeft + plotW, Y1 = y, Y2 = y, Stroke = gridBrush, StrokeThickness = 1, SnapsToDevicePixels = true, Opacity = 0.7 });
                var label = new TextBlock { Text = FormatDuration(v), FontSize = 10.5, Foreground = hintBrush, Width = PlotLeft - 8, TextAlignment = TextAlignment.Right };
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, y - 7);
                _chart.Children.Add(label);
            }

            // 가로축 눈금: 월요일마다. 주가 많으면 간격을 넓힌다.
            double totalWeeks = (xEnd - xStart).TotalDays / 7;
            int weekStep = new[] { 1, 2, 4, 8, 13, 26, 52 }.First(s => totalWeeks / s <= 8 || s == 52);
            string dateFormat = totalWeeks > 48 ? "yy.M.d" : "M.d";
            for (DateTime t = xStart; t < xEnd; t = t.AddDays(7 * weekStep))
            {
                double x = X(t, plotW);
                _chart.Children.Add(new Line { X1 = x, X2 = x, Y1 = PlotTop + plotH, Y2 = PlotTop + plotH + 4, Stroke = gridBrush, StrokeThickness = 1, SnapsToDevicePixels = true });
                var label = new TextBlock { Text = t.ToString(dateFormat, CultureInfo.InvariantCulture), FontSize = 10.5, Foreground = hintBrush, Width = 60, TextAlignment = TextAlignment.Center };
                Canvas.SetLeft(label, Math.Min(Math.Max(x - 30, 0), ChartWidth - 60));
                Canvas.SetTop(label, PlotTop + plotH + 7);
                _chart.Children.Add(label);
            }
            _chart.Children.Add(new Line { X1 = PlotLeft, X2 = PlotLeft + plotW, Y1 = PlotTop + plotH, Y2 = PlotTop + plotH, Stroke = gridBrush, StrokeThickness = 1, SnapsToDevicePixels = true });

            // 계열: 주 평균을 잇는 선 2px + 주마다 점
            var line = new Polyline
            {
                Stroke = seriesBrush,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            };
            foreach (WeekPoint w in _weeks)
                line.Points.Add(new Point(X(w.Middle, plotW), Y(w.Average, plotH)));
            _chart.Children.Add(line);

            foreach (WeekPoint w in _weeks)
                _chart.Children.Add(MakeDot(X(w.Middle, plotW), Y(w.Average, plotH), 4.5, seriesBrush, surfaceBrush));

            // 직접 표기는 둘만: 평균이 가장 빨랐던 주와 최근 주
            WeekPoint fastest = _weeks.MinBy(w => w.Average)!;
            WeekPoint latest = _weeks[^1];
            if (ReferenceEquals(fastest, latest))
            {
                AddDirectLabel(latest, $"최근 주 · 가장 빠름 {FormatDuration(latest.Average)}", below: true, plotW, plotH, textBrush, surfaceBrush);
            }
            else
            {
                AddDirectLabel(fastest, $"가장 빠른 주 {FormatDuration(fastest.Average)}", below: true, plotW, plotH, textBrush, surfaceBrush);
                AddDirectLabel(latest, $"최근 주 {FormatDuration(latest.Average)}", below: false, plotW, plotH, textBrush, surfaceBrush);
            }

            // 마우스 올림 요소
            _crosshair = new Line { Y1 = PlotTop, Y2 = PlotTop + plotH, Stroke = hintBrush, StrokeThickness = 1, Opacity = 0.6, Visibility = Visibility.Collapsed, IsHitTestVisible = false, SnapsToDevicePixels = true };
            _chart.Children.Add(_crosshair);
            _hoverDot = MakeDot(0, 0, 6, seriesBrush, surfaceBrush);
            _hoverDot.Visibility = Visibility.Collapsed;
            _hoverDot.IsHitTestVisible = false;
            _chart.Children.Add(_hoverDot);

            _tooltipValue = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = textBrush };
            _tooltipDetail = new TextBlock { FontSize = 11, Foreground = hintBrush, Margin = new Thickness(0, 1, 0, 0) };
            var tipStack = new StackPanel();
            tipStack.Children.Add(_tooltipValue);
            tipStack.Children.Add(_tooltipDetail);
            _tooltip = new Border
            {
                Child = tipStack,
                Padding = new Thickness(8, 5, 8, 6),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                BorderBrush = gridBrush,
                Background = surfaceBrush,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            _chart.Children.Add(_tooltip);
        }

        private void AddDirectLabel(WeekPoint week, string text, bool below, double plotW, double plotH, Brush textBrush, Brush surfaceBrush)
        {
            double x = X(week.Middle, plotW), y = Y(week.Average, plotH);

            // 선 위에 놓여도 읽히도록 표면색 판을 깐다 (테두리 없이 — 판은 여백 역할만 한다)
            var label = new Border
            {
                Background = surfaceBrush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = textBrush },
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = label.DesiredSize.Width, h = label.DesiredSize.Height;
            double left = Math.Min(Math.Max(x - w / 2, PlotLeft + 2), ChartWidth - PlotRight - w);
            double top = below ? y + 9 : y - h - 9;
            if (top < 2) top = y + 9;
            if (top + h > PlotTop + plotH - 2) top = y - h - 9;
            Canvas.SetLeft(label, left);
            Canvas.SetTop(label, top);
            _chart.Children.Add(label);
        }

        private static Ellipse MakeDot(double x, double y, double radius, Brush fill, Brush ring)
        {
            var dot = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, Stroke = ring, StrokeThickness = 2 };
            Canvas.SetLeft(dot, x - radius);
            Canvas.SetTop(dot, y - radius);
            return dot;
        }

        private double X(DateTime at, double plotW)
            => PlotLeft + (at.Ticks - _xMinTicks) / Math.Max(1, _xMaxTicks - _xMinTicks) * plotW;

        private double Y(double seconds, double plotH)
            => PlotTop + (1 - (seconds - _yMin) / Math.Max(0.001, _yMax - _yMin)) * plotH;

        /// <summary>눈금이 6개를 넘지 않는 가장 작은 '깔끔한' 간격(초).</summary>
        private static double NiceStep(double range)
        {
            double[] steps = { 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200 };
            foreach (double s in steps)
            {
                if (range / s <= 6)
                    return s;
            }
            return 14400;
        }

        // ===== 마우스 올림: 가장 가까운 주로 십자선이 붙는다 (점을 정확히 겨눌 필요가 없다) =====

        private void ShowHoverAt(double pointerX)
        {
            if (_weeks.Count == 0 || _crosshair == null || _hoverDot == null || _tooltip == null)
                return;

            double plotW = ChartWidth - PlotLeft - PlotRight;
            double plotH = ChartHeight - PlotTop - PlotBottom;
            if (pointerX < PlotLeft - 10 || pointerX > ChartWidth - PlotRight + 10)
            {
                HideHover();
                return;
            }

            WeekPoint week = _weeks.MinBy(w => Math.Abs(X(w.Middle, plotW) - pointerX))!;
            double x = X(week.Middle, plotW), y = Y(week.Average, plotH);

            _crosshair.X1 = _crosshair.X2 = x;
            _crosshair.Visibility = Visibility.Visible;
            Canvas.SetLeft(_hoverDot, x - _hoverDot.Width / 2);
            Canvas.SetTop(_hoverDot, y - _hoverDot.Height / 2);
            _hoverDot.Visibility = Visibility.Visible;

            // 말풍선: 값이 먼저(굵게), 설명이 뒤
            _tooltipValue!.Text = $"평균 {FormatDuration(week.Average)}";
            _tooltipDetail!.Text = $"{FormatWeek(week)}\n{week.Count}판 · 최고 {FormatDuration(week.Best)}";
            _tooltip.Visibility = Visibility.Visible;
            _tooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double tw = _tooltip.DesiredSize.Width, th = _tooltip.DesiredSize.Height;
            double left = x + 12;
            if (left + tw > ChartWidth - 2) left = x - 12 - tw;
            double top = Math.Min(Math.Max(y - th / 2, 2), ChartHeight - th - 2);
            Canvas.SetLeft(_tooltip, left);
            Canvas.SetTop(_tooltip, top);
        }

        private void HideHover()
        {
            if (_crosshair != null) _crosshair.Visibility = Visibility.Collapsed;
            if (_hoverDot != null) _hoverDot.Visibility = Visibility.Collapsed;
            if (_tooltip != null) _tooltip.Visibility = Visibility.Collapsed;
        }

        // ===== 표 보기 (그래프와 같은 값을 주 단위 목록으로, 최근 주부터) =====

        private void RenderTable()
        {
            _tableRows.Children.Clear();
            Brush textBrush = ResourceBrush("TextBrush", Colors.White);
            Brush hintBrush = ResourceBrush("OverlayHintTextBrush", Color.FromRgb(0x8C, 0x91, 0x97));

            var headerRow = BuildTableRow("주 (월 ~ 일)", "판 수", "평균", "최고", hintBrush, FontWeights.SemiBold);
            headerRow.Margin = new Thickness(0, 6, 0, 4);
            _tableRows.Children.Add(headerRow);

            for (int i = _weeks.Count - 1; i >= 0; i--)
            {
                WeekPoint w = _weeks[i];
                _tableRows.Children.Add(BuildTableRow(FormatWeek(w), $"{w.Count}판", FormatDuration(w.Average), FormatDuration(w.Best), textBrush, FontWeights.Normal));
            }
        }

        private static FrameworkElement BuildTableRow(string week, string count, string average, string best, Brush brush, FontWeight weight)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            string[] cells = { week, count, average, best };
            for (int c = 0; c < cells.Length; c++)
            {
                var cell = new TextBlock { Text = cells[c], FontSize = 12, Foreground = brush, FontWeight = weight, TextAlignment = c == 0 ? TextAlignment.Left : TextAlignment.Right };
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
            return grid;
        }

        // ===== 도우미 =====

        private Brush ResourceBrush(string key, Color fallback)
            => TryFindResource(key) as Brush ?? new SolidColorBrush(fallback);

        /// <summary>"2026.9.14(월) ~ 9.20(일)"</summary>
        private static string FormatWeek(WeekPoint week)
            => $"{week.WeekStart.ToString("yyyy.M.d(ddd)", Korean)} ~ {week.WeekEnd.ToString("M.d(ddd)", Korean)}";

        /// <summary>초 → "m:ss" (한 시간 이상이면 "h:mm:ss").</summary>
        private static string FormatDuration(double seconds)
        {
            int total = (int)Math.Round(Math.Max(0, seconds));
            int h = total / 3600, m = (total % 3600) / 60, s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }
    }
}
