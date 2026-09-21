using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 던전 타이머 기록 추이 창. 타이머 창의 [자세히]로 연다. 열려 있는 동안 타이머 창에서 다른 던전을 고르면 따라 바뀐다.
    /// 세로축 = 클리어 시간, 가로축 = 첫 기록부터 지금까지의 기간. 점 하나가 한 주(월~일)의 평균이다.
    ///
    /// 구성: 제목줄(그래프|표 전환) → 선택 칩(던전·난이도·구간) → 수치 타일 4개 → 카드 안의 그래프 또는 표.
    ///
    /// 그리는 규칙:
    ///  - 한 번에 한 계열만 보여 준다 → 범례가 없고 색은 민트 하나. 면 채움은 같은 색의 옅은 물결(위 18% → 아래 0%).
    ///    기준 칩으로 '주 평균'과 '주 최고' 중 무엇을 그릴지 바꾼다 (두 선을 겹치지 않는다).
    ///  - 선 2px + 점(표면색 테두리). 격자·축·평균 기준선은 가는 실선으로 물러나 있다.
    ///  - 숫자를 점마다 붙이지 않는다. 가장 빠른 주와 최근 주만 직접 표기하고, 나머지는 마우스를 올리면 십자선+말풍선으로 읽는다.
    ///  - 말풍선이 유일한 통로가 되지 않게 [표]에서 같은 값을 주 단위 목록으로 본다. 표의 평균 칸 막대는 그 주 평균의 크기다.
    ///  - 글자는 글자 색만 쓴다. 계열 색은 선·점·막대·선택 표시에만 쓴다.
    /// </summary>
    public sealed class DungeonTimerDetailWindow : OverlayWindowBase
    {
        private static DungeonTimerDetailWindow? _instance;

        private const double ContentWidth = 720;
        private const double ChartWidth = 692;   // 카드 안쪽 (좌우 14 여백)
        private const double ChartHeight = 290;
        private const double PlotLeft = 52, PlotRight = 20, PlotTop = 24, PlotBottom = 30;
        private const string WholeRunChoice = "전체";

        private static readonly Color Mint = Color.FromRgb(0x0C, 0xD2, 0x9D);
        private static readonly CultureInfo Korean = CultureInfo.GetCultureInfo("ko-KR");

        /// <summary>한 주(월요일 0시 ~ 일요일)의 집계.</summary>
        private sealed record WeekPoint(DateTime WeekStart, double Average, int Count, double Best)
        {
            public DateTime WeekEnd => WeekStart.AddDays(6);
            /// <summary>가로 위치는 주의 한가운데 (눈금은 월요일에 선다).</summary>
            public DateTime Middle => WeekStart.AddDays(3.5);
        }

        private readonly ContentTimerService _service;
        private IReadOnlyList<ContentTimerService.DungeonDefinition> _members;
        private string _groupKey;

        private ContentTimerService.DungeonDefinition _def;
        private string _difficulty = string.Empty;
        private string _segment = WholeRunChoice;
        private bool _showTable;
        /// <summary>그래프 기준: false = 그 주의 평균, true = 그 주의 최고(가장 빠른 판).</summary>
        private bool _useBest;

        private const string MetricAverage = "평균", MetricBest = "최고";

        /// <summary>그래프가 그리는 값 (고른 기준에 따라 주 평균 또는 주 최고).</summary>
        private double Value(WeekPoint week) => _useBest ? week.Best : week.Average;

        private IReadOnlyList<DungeonRunRecord> _allRecords = Array.Empty<DungeonRunRecord>();
        private List<(DateTime At, double Seconds)> _runs = new();
        private List<WeekPoint> _weeks = new();
        private int _loadVersion;

        private readonly TextBlock _titleText;
        private readonly StackPanel _viewSwitch;
        private readonly StackPanel _filterPanel;
        private readonly UniformGrid _tiles;
        private readonly TextBlock _noteText;
        private readonly Canvas _chart;
        private readonly Border _chartCard;
        private readonly Border _tableCard;
        private readonly StackPanel _tableRows;
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

        /// <summary>자세히 창이 열려 있으면 타이머 창에서 고른 던전으로 내용을 바꾼다. 닫혀 있으면 아무것도 하지 않는다.</summary>
        public static void SyncGroupIfOpen(string groupKey)
        {
            try
            {
                if (_instance?.IsLoaded == true && !string.IsNullOrEmpty(groupKey))
                    _instance.SetGroup(groupKey);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to sync dungeon timer detail window.", ex);
            }
        }

        private DungeonTimerDetailWindow(string groupKey)
        {
            _service = AppServices.Get<ContentTimerService>();
            _groupKey = groupKey;
            _members = _service.GetGroupMembers(groupKey);
            if (_members.Count == 0)
            {
                _groupKey = _service.Groups[0].Key;
                _members = _service.GetGroupMembers(_groupKey);
            }
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

            // ── 제목줄 ──
            _titleText = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            _titleText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayTitleAccentTextBrush");

            var closeButton = new Button
            {
                Content = "닫기",
                Height = 24,
                MinWidth = 44,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 11,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            closeButton.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            closeButton.Click += (_, _) => { try { Close(); } catch { } };

            _viewSwitch = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent };
            DockPanel.SetDock(closeButton, Dock.Right);
            DockPanel.SetDock(_viewSwitch, Dock.Right);
            header.Children.Add(closeButton);
            header.Children.Add(_viewSwitch);
            header.Children.Add(_titleText);
            // 제목 줄을 잡고 옮긴다 (잠금과 무관). 그래프 영역은 마우스 올림에 쓰므로 제외.
            header.MouseLeftButtonDown += (_, e) => TryBeginDrag(e);

            // ── 선택 칩: 아래의 타일·그래프·표가 모두 이 선택을 따른다 ──
            _filterPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

            // ── 수치 타일 ──
            _tiles = new UniformGrid { Columns = 4, Margin = new Thickness(-4, 0, -4, 8) };

            _noteText = new TextBlock { FontSize = 11, Margin = new Thickness(2, 0, 0, 8), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            _noteText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");

            // ── 그래프 카드 ──
            _chart = new Canvas { Width = ChartWidth, Height = ChartHeight, ClipToBounds = true, Background = Brushes.Transparent };
            _chart.MouseMove += (_, e) => ShowHoverAt(e.GetPosition(_chart).X);
            _chart.MouseLeave += (_, _) => HideHover();
            _chartCard = MakeCard(_chart, new Thickness(14, 10, 14, 8));

            // ── 표 카드 ──
            _tableRows = new StackPanel();
            var tableScroll = new ScrollViewer
            {
                Width = ChartWidth,
                Height = ChartHeight,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = _tableRows,
            };
            _tableCard = MakeCard(tableScroll, new Thickness(14, 10, 14, 8));

            var body = new StackPanel { Width = ContentWidth };
            body.Children.Add(header);
            body.Children.Add(_filterPanel);
            body.Children.Add(_tiles);
            body.Children.Add(_noteText);
            body.Children.Add(_chartCard);
            body.Children.Add(_tableCard);

            var root = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, 14, 18, 16),
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

        /// <summary>타이머 창에서 다른 던전 묶음을 골랐을 때: 그 묶음의 첫 던전으로 바꾸고 다시 읽는다. 보기(그래프/표)는 유지.</summary>
        private void SetGroup(string groupKey)
        {
            if (groupKey == _groupKey)
                return;

            var members = _service.GetGroupMembers(groupKey);
            if (members.Count == 0)
                return;

            _groupKey = groupKey;
            _members = members;
            _def = members[0];
            _difficulty = string.Empty;
            _segment = WholeRunChoice;
            _ = ReloadAsync();
        }

        // ===== 데이터 =====

        private async Task ReloadAsync()
        {
            int version = ++_loadVersion;
            var def = _def;
            IReadOnlyList<DungeonRunRecord> records;
            try
            {
                records = await Task.Run(() => _service.GetArchive(def)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load dungeon timer archive.", ex);
                records = Array.Empty<DungeonRunRecord>();
            }

            // 파일 읽기는 작업 스레드에서 끝났다 — 화면 갱신은 호출한 쪽이 어디든 UI 스레드에서 한다
            await Dispatcher.InvokeAsync(() =>
            {
                if (version != _loadVersion)
                    return; // 그 사이 다른 던전을 골랐다

                _allRecords = records;
                NormalizeSelection();
                BuildFilters();
                RebuildWeeks();
                RenderAll();
            });
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

        // ===== 선택 칩 =====

        private void BuildFilters()
        {
            _filterPanel.Children.Clear();

            if (_members.Count > 1)
            {
                _filterPanel.Children.Add(BuildChipRow("던전", _members.Select(m => m.Name).ToList(), _def.Name, name =>
                {
                    _def = _members.First(m => m.Name == name);
                    _ = ReloadAsync();
                }));
            }

            var difficulties = Difficulties();
            if (difficulties.Count > 1)
                _filterPanel.Children.Add(BuildChipRow("난이도", difficulties, _difficulty, value => { _difficulty = value; OnSelectionChanged(); }));

            var segments = SegmentChoices();
            if (segments.Count > 1)
                _filterPanel.Children.Add(BuildChipRow("구간", segments, _segment, value => { _segment = value; OnSelectionChanged(); }));

            _filterPanel.Children.Add(BuildChipRow("기준", new[] { MetricAverage, MetricBest }, _useBest ? MetricBest : MetricAverage, value =>
            {
                _useBest = value == MetricBest;
                BuildFilters();
                RenderAll();
            }));
        }

        private void OnSelectionChanged()
        {
            BuildFilters();
            RebuildWeeks();
            RenderAll();
        }

        private FrameworkElement BuildChipRow(string label, IReadOnlyList<string> options, string selected, Action<string> onSelect)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };
            var caption = new TextBlock { Text = label, FontSize = 11, Width = 46, VerticalAlignment = VerticalAlignment.Center };
            caption.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
            row.Children.Add(caption);

            foreach (string option in options)
            {
                string captured = option;
                row.Children.Add(MakeChip(string.IsNullOrEmpty(option) ? "기본" : option, option == selected, () => onSelect(captured), new Thickness(0, 0, 5, 3)));
            }
            return row;
        }

        /// <summary>
        /// 알약 모양 선택 칩. 선택된 칩은 민트 물결 배경 + 민트 테두리로, 나머지는 테두리만.
        /// 글자는 어느 쪽이든 글자 색(선택=본문, 비선택=흐림)이다.
        /// </summary>
        private Border MakeChip(string text, bool selected, Action onClick, Thickness margin)
        {
            var label = new TextBlock { Text = text, FontSize = 11.5, FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal };
            label.SetResourceReference(TextBlock.ForegroundProperty, selected ? "TextBrush" : "OverlayMutedTextBrush");

            var chip = new Border
            {
                Child = label,
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(11, 3, 11, 4),
                Margin = margin,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true,
            };
            if (selected)
            {
                chip.SetResourceReference(Border.BackgroundProperty, "OverlayTabSelectedBrush");
                chip.SetResourceReference(Border.BorderBrushProperty, "OverlayTabSelectedBorderBrush");
            }
            else
            {
                chip.Background = Brushes.Transparent;
                chip.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
                chip.MouseEnter += (_, _) => chip.SetResourceReference(Border.BackgroundProperty, "OverlayTabHoverBrush");
                chip.MouseLeave += (_, _) => chip.Background = Brushes.Transparent;
            }
            chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; onClick(); };
            // 칩 위에서 눌러도 창이 끌리지 않게
            chip.MouseLeftButtonDown += (_, e) => e.Handled = true;
            return chip;
        }

        // ===== 그리기 =====

        private void ApplyViewMode()
        {
            _viewSwitch.Children.Clear();
            _viewSwitch.Children.Add(MakeChip("그래프", !_showTable, () => { _showTable = false; ApplyViewMode(); }, new Thickness(0, 0, 4, 0)));
            _viewSwitch.Children.Add(MakeChip("표", _showTable, () => { _showTable = true; ApplyViewMode(); }, new Thickness(0)));

            _chartCard.Visibility = _showTable ? Visibility.Collapsed : Visibility.Visible;
            _tableCard.Visibility = _showTable ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RenderAll()
        {
            string what = _segment == WholeRunChoice ? string.Empty : $" · {_segment}";
            string diff = string.IsNullOrEmpty(_difficulty) ? string.Empty : $" ({_difficulty})";
            _titleText.Text = $"{_def.Name}{diff}{what} 주간 {(_useBest ? "최고 기록" : "평균")} 추이";

            RenderTiles();
            RenderChart();
            RenderTable();
        }

        private void RenderTiles()
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

            _tiles.Children.Clear();
            if (_runs.Count == 0)
            {
                _tiles.Children.Add(MakeTile("기록", "0판", "아직 기록이 없습니다", accent: false));
                _tiles.Children.Add(MakeTile("Best", "-", string.Empty, accent: false));
                _tiles.Children.Add(MakeTile("전체 평균", "-", string.Empty, accent: false));
                _tiles.Children.Add(MakeTile("최근 주 평균", "-", string.Empty, accent: false));
                return;
            }

            var best = _runs.MinBy(r => r.Seconds);
            WeekPoint latest = _weeks[^1];
            _tiles.Children.Add(MakeTile("기록", $"{_runs.Count:N0}판", $"{_weeks.Count:N0}주 · {_runs[0].At:yyyy.M.d}부터", accent: false));
            _tiles.Children.Add(MakeTile("Best", FormatDuration(best.Seconds), best.At.ToString("yyyy.M.d (ddd)", Korean), accent: true));
            _tiles.Children.Add(MakeTile("전체 평균", FormatDuration(_runs.Average(r => r.Seconds)), "모든 판", accent: false));
            _tiles.Children.Add(MakeTile("최근 주 평균", FormatDuration(latest.Average), $"{latest.WeekStart:M.d} ~ {latest.WeekEnd:M.d} · {latest.Count}판", accent: false));
        }

        /// <summary>수치 타일: 작은 이름 · 큰 값 · 작은 보조 설명. 강조 타일은 왼쪽에 민트 띠를 둔다 (글자 색은 그대로).</summary>
        private FrameworkElement MakeTile(string label, string value, string sub, bool accent)
        {
            var labelText = new TextBlock { Text = label, FontSize = 11 };
            labelText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
            var valueText = new TextBlock { Text = value, FontSize = 21, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 1, 0, 1) };
            valueText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var subText = new TextBlock { Text = sub, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
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
                tile.BorderBrush = new SolidColorBrush(Mint);
            else
                tile.SetResourceReference(Border.BorderBrushProperty, "OverlayCardBorderBrush");
            return tile;
        }

        private Border MakeCard(UIElement child, Thickness padding)
        {
            var card = new Border
            {
                Child = child,
                CornerRadius = new CornerRadius(10),
                Padding = padding,
                BorderThickness = new Thickness(1),
            };
            card.SetResourceReference(Border.BackgroundProperty, "OverlayCardBackgroundBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "OverlayCardBorderBrush");
            return card;
        }

        private void RenderChart()
        {
            _chart.Children.Clear();
            _crosshair = null; _hoverDot = null; _tooltip = null;

            Brush gridBrush = ResourceBrush("ControlBorderBrush", Color.FromRgb(0x2E, 0x38, 0x33));
            Brush hintBrush = ResourceBrush("OverlayHintTextBrush", Color.FromRgb(0x77, 0x80, 0x7B));
            Brush mutedBrush = ResourceBrush("OverlayMutedTextBrush", Color.FromRgb(0x8C, 0x91, 0x97));
            Brush textBrush = ResourceBrush("TextBrush", Colors.White);
            Color surface = (ResourceBrush("OverlayCardBackgroundBrush", Color.FromRgb(0x16, 0x1D, 0x1A)) as SolidColorBrush)?.Color
                            ?? Color.FromRgb(0x16, 0x1D, 0x1A);
            var surfaceBrush = new SolidColorBrush(Color.FromRgb(surface.R, surface.G, surface.B));
            var seriesBrush = new SolidColorBrush(Mint);

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
            double dataMin = _weeks.Min(Value), dataMax = _weeks.Max(Value);
            double step = NiceStep(Math.Max(dataMax - dataMin, 1) * 1.15);
            _yMin = Math.Max(0, Math.Floor(dataMin / step) * step);
            _yMax = Math.Ceiling(dataMax / step) * step;
            if (_yMax <= _yMin) _yMax = _yMin + step;
            if ((dataMax - _yMin) > (_yMax - _yMin) * 0.92) _yMax += step;                    // 위쪽 직접 표기 여유
            if ((dataMin - _yMin) < (_yMax - _yMin) * 0.10 && _yMin >= step) _yMin -= step;   // 아래쪽 직접 표기 여유

            double baselineY = PlotTop + plotH;

            // 격자(가로) + 세로축 눈금
            for (double v = _yMin; v <= _yMax + 0.001; v += step)
            {
                double y = Y(v, plotH);
                _chart.Children.Add(new Line { X1 = PlotLeft, X2 = PlotLeft + plotW, Y1 = y, Y2 = y, Stroke = gridBrush, StrokeThickness = 1, SnapsToDevicePixels = true, Opacity = 0.55 });
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
                _chart.Children.Add(new Line { X1 = x, X2 = x, Y1 = baselineY, Y2 = baselineY + 4, Stroke = gridBrush, StrokeThickness = 1, SnapsToDevicePixels = true });
                var label = new TextBlock { Text = t.ToString(dateFormat, CultureInfo.InvariantCulture), FontSize = 10.5, Foreground = hintBrush, Width = 60, TextAlignment = TextAlignment.Center };
                Canvas.SetLeft(label, Math.Min(Math.Max(x - 30, 0), ChartWidth - 60));
                Canvas.SetTop(label, baselineY + 7);
                _chart.Children.Add(label);
            }
            _chart.Children.Add(new Line { X1 = PlotLeft, X2 = PlotLeft + plotW, Y1 = baselineY, Y2 = baselineY, Stroke = gridBrush, StrokeThickness = 1, SnapsToDevicePixels = true });

            var points = _weeks.Select(w => new Point(X(w.Middle, plotW), Y(Value(w), plotH))).ToList();

            // 면 채움: 같은 색의 옅은 물결 (위 18% → 아래 0%). 선 아래 공간에 무게를 준다.
            if (points.Count > 1)
            {
                var area = new Polygon
                {
                    Fill = new LinearGradientBrush(Color.FromArgb(0x2E, Mint.R, Mint.G, Mint.B), Color.FromArgb(0x00, Mint.R, Mint.G, Mint.B), 90),
                    IsHitTestVisible = false,
                };
                foreach (Point p in points) area.Points.Add(p);
                area.Points.Add(new Point(points[^1].X, baselineY));
                area.Points.Add(new Point(points[0].X, baselineY));
                _chart.Children.Add(area);
            }

            // 기준선 (가는 실선, 물러난 색) — 평균 기준이면 전체 평균, 최고 기준이면 역대 Best. 왼쪽 끝에 이름표
            double overall = _useBest ? _runs.Min(r => r.Seconds) : _runs.Average(r => r.Seconds);
            string overallName = _useBest ? "Best" : "전체 평균";
            if (overall >= _yMin && overall <= _yMax)
            {
                double y = Y(overall, plotH);
                _chart.Children.Add(new Line { X1 = PlotLeft, X2 = PlotLeft + plotW, Y1 = y, Y2 = y, Stroke = mutedBrush, StrokeThickness = 1, Opacity = 0.75, SnapsToDevicePixels = true });
                var tag = new Border
                {
                    Background = surfaceBrush,
                    Padding = new Thickness(4, 0, 4, 1),
                    CornerRadius = new CornerRadius(3),
                    Child = new TextBlock { Text = $"{overallName} {FormatDuration(overall)}", FontSize = 10.5, Foreground = mutedBrush },
                };
                tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                // 기본은 왼쪽 끝. 첫 점이 기준선 가까이에 있어 겹치면 오른쪽 끝으로 옮긴다 (마지막 점도 가까우면 그대로 왼쪽)
                bool firstNear = Math.Abs(points[0].Y - y) < 16 && points[0].X < PlotLeft + tag.DesiredSize.Width + 10;
                bool lastNear = Math.Abs(points[^1].Y - y) < 16;
                Canvas.SetLeft(tag, firstNear && !lastNear ? PlotLeft + plotW - tag.DesiredSize.Width : PlotLeft);
                Canvas.SetTop(tag, y - tag.DesiredSize.Height / 2);
                _chart.Children.Add(tag);
            }

            // 계열: 주 평균을 잇는 선 2px + 주마다 점
            var line = new Polyline
            {
                Stroke = seriesBrush,
                StrokeThickness = 2,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                IsHitTestVisible = false,
            };
            foreach (Point p in points) line.Points.Add(p);
            _chart.Children.Add(line);

            // 최근 주 점은 옅은 후광으로 눈에 띄게
            Point last = points[^1];
            var halo = new Ellipse { Width = 22, Height = 22, Fill = new SolidColorBrush(Color.FromArgb(0x33, Mint.R, Mint.G, Mint.B)), IsHitTestVisible = false };
            Canvas.SetLeft(halo, last.X - 11);
            Canvas.SetTop(halo, last.Y - 11);
            _chart.Children.Add(halo);

            foreach (Point p in points)
                _chart.Children.Add(MakeDot(p.X, p.Y, 4.5, seriesBrush, surfaceBrush));

            // 직접 표기는 둘만: (고른 기준으로) 가장 빨랐던 주와 최근 주
            WeekPoint fastest = _weeks.MinBy(Value)!;
            WeekPoint latest = _weeks[^1];
            if (ReferenceEquals(fastest, latest))
            {
                AddDirectLabel(latest, $"최근 주 · 가장 빠름 {FormatDuration(Value(latest))}", below: true, plotW, plotH, textBrush, surfaceBrush);
            }
            else
            {
                AddDirectLabel(fastest, $"가장 빠른 주 {FormatDuration(Value(fastest))}", below: true, plotW, plotH, textBrush, surfaceBrush);
                AddDirectLabel(latest, $"최근 주 {FormatDuration(Value(latest))}", below: false, plotW, plotH, textBrush, surfaceBrush);
            }

            // 마우스 올림 요소
            _crosshair = new Line { Y1 = PlotTop, Y2 = baselineY, Stroke = mutedBrush, StrokeThickness = 1, Opacity = 0.7, Visibility = Visibility.Collapsed, IsHitTestVisible = false, SnapsToDevicePixels = true };
            _chart.Children.Add(_crosshair);
            _hoverDot = MakeDot(0, 0, 6.5, seriesBrush, surfaceBrush);
            _hoverDot.Visibility = Visibility.Collapsed;
            _hoverDot.IsHitTestVisible = false;
            _chart.Children.Add(_hoverDot);

            _tooltipValue = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = textBrush };
            _tooltipDetail = new TextBlock { FontSize = 11, Foreground = mutedBrush, Margin = new Thickness(0, 2, 0, 0), LineHeight = 15 };
            var tipStack = new StackPanel();
            tipStack.Children.Add(_tooltipValue);
            tipStack.Children.Add(_tooltipDetail);
            _tooltip = new Border
            {
                Child = tipStack,
                Padding = new Thickness(10, 6, 10, 7),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                BorderBrush = gridBrush,
                Background = ResourceBrush("OverlaySurfaceAltBackgroundBrush", Color.FromRgb(0x1A, 0x23, 0x20)),
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
            };
            _chart.Children.Add(_tooltip);
        }

        private void AddDirectLabel(WeekPoint week, string text, bool below, double plotW, double plotH, Brush textBrush, Brush surfaceBrush)
        {
            double x = X(week.Middle, plotW), y = Y(Value(week), plotH);

            // 선 위에 놓여도 읽히도록 표면색 판을 깐다 (테두리 없이 — 판은 여백 역할만 한다)
            var label = new Border
            {
                Background = surfaceBrush,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 2),
                Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = textBrush },
                IsHitTestVisible = false,
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double w = label.DesiredSize.Width, h = label.DesiredSize.Height;
            double left = Math.Min(Math.Max(x - w / 2, PlotLeft + 2), ChartWidth - PlotRight - w + 14);
            double top = below ? y + 12 : y - h - 12;
            if (top < 2) top = y + 12;
            if (top + h > PlotTop + plotH - 2) top = y - h - 12;
            Canvas.SetLeft(label, left);
            Canvas.SetTop(label, top);
            _chart.Children.Add(label);
        }

        private static Ellipse MakeDot(double x, double y, double radius, Brush fill, Brush ring)
        {
            var dot = new Ellipse { Width = radius * 2, Height = radius * 2, Fill = fill, Stroke = ring, StrokeThickness = 2, IsHitTestVisible = false };
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
            double x = X(week.Middle, plotW), y = Y(Value(week), plotH);

            _crosshair.X1 = _crosshair.X2 = x;
            _crosshair.Visibility = Visibility.Visible;
            Canvas.SetLeft(_hoverDot, x - _hoverDot.Width / 2);
            Canvas.SetTop(_hoverDot, y - _hoverDot.Height / 2);
            _hoverDot.Visibility = Visibility.Visible;

            // 말풍선: 값이 먼저(굵게), 설명이 뒤
            // 고른 기준의 값이 앞에(굵게), 다른 쪽 값은 설명 줄에
            _tooltipValue!.Text = _useBest ? $"최고 {FormatDuration(week.Best)}" : $"평균 {FormatDuration(week.Average)}";
            string other = _useBest ? $"평균 {FormatDuration(week.Average)}" : $"최고 {FormatDuration(week.Best)}";
            _tooltipDetail!.Text = $"{FormatWeek(week)}\n{week.Count}판 · {other}";
            _tooltip.Visibility = Visibility.Visible;
            _tooltip.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double tw = _tooltip.DesiredSize.Width, th = _tooltip.DesiredSize.Height;
            double left = x + 14;
            if (left + tw > ChartWidth - 2) left = x - 14 - tw;
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

        private const double ColWeek = 236, ColCount = 70, ColAverage = 250, ColBest = 100;

        private void RenderTable()
        {
            _tableRows.Children.Clear();

            // 머리글 + 구분선
            _tableRows.Children.Add(BuildTableHeader());
            var rule = new Border { Height = 1, Margin = new Thickness(0, 5, 0, 3) };
            rule.SetResourceReference(Border.BackgroundProperty, "ControlBorderBrush");
            _tableRows.Children.Add(rule);

            if (_weeks.Count == 0)
            {
                var empty = new TextBlock { Text = "표시할 기록이 없습니다", FontSize = 12, Margin = new Thickness(8, 10, 0, 0) };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
                _tableRows.Children.Add(empty);
                return;
            }

            double maxAverage = _weeks.Max(w => w.Average);
            WeekPoint fastest = _weeks.MinBy(w => w.Average)!;
            double allTimeBest = _runs.Min(r => r.Seconds);

            int index = 0;
            for (int i = _weeks.Count - 1; i >= 0; i--, index++)
                _tableRows.Children.Add(BuildTableRow(_weeks[i], index % 2 == 1, maxAverage, ReferenceEquals(_weeks[i], fastest), Math.Abs(_weeks[i].Best - allTimeBest) < 0.0001));
        }

        private FrameworkElement BuildTableHeader()
        {
            var grid = NewTableGrid();
            grid.Margin = new Thickness(8, 2, 8, 0);
            string[] headers = { "주 (월 ~ 일)", "판 수", "평균", "최고" };
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = new TextBlock
                {
                    Text = headers[c],
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = c == 0 || c == 2 ? TextAlignment.Left : TextAlignment.Right,
                    Margin = c == 2 ? new Thickness(18, 0, 0, 0) : new Thickness(0), // 평균 칸의 막대 시작 위치
                };
                cell.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
            return grid;
        }

        private static Grid NewTableGrid()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColWeek) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColCount) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColAverage) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ColBest) });
            return grid;
        }

        /// <summary>
        /// 한 주 한 줄. 짝수 줄은 옅은 띠, 평균 칸은 [막대 + 값], 가장 빠른 주에는 알약 표시, 역대 Best가 나온 주의 최고 칸에는 민트 점.
        /// </summary>
        private FrameworkElement BuildTableRow(WeekPoint week, bool striped, double maxAverage, bool isFastest, bool hasAllTimeBest)
        {
            var grid = NewTableGrid();

            // 주
            var weekPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var weekText = new TextBlock { Text = FormatWeek(week), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            weekText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            weekPanel.Children.Add(weekText);
            if (isFastest)
            {
                var pillText = new TextBlock { Text = "가장 빠름", FontSize = 10 };
                pillText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                var pill = new Border { Child = pillText, CornerRadius = new CornerRadius(8), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(7, 0, 0, 0), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
                pill.SetResourceReference(Border.BackgroundProperty, "OverlayTabSelectedBrush");
                pill.SetResourceReference(Border.BorderBrushProperty, "OverlayTabSelectedBorderBrush");
                weekPanel.Children.Add(pill);
            }
            Grid.SetColumn(weekPanel, 0);
            grid.Children.Add(weekPanel);

            // 판 수
            var countText = new TextBlock { Text = $"{week.Count}판", FontSize = 12, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            countText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayLabelTextBrush");
            Grid.SetColumn(countText, 1);
            grid.Children.Add(countText);

            // 평균: 막대(그 주 평균의 크기) + 값
            var averagePanel = new Grid { Margin = new Thickness(18, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            averagePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            averagePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(54) });
            double trackWidth = ColAverage - 18 - 54;
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6 };
            track.SetResourceReference(Border.BackgroundProperty, "ControlBorderBrush");
            var fill = new Border
            {
                Height = 6,
                CornerRadius = new CornerRadius(3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Width = Math.Max(6, trackWidth * (maxAverage > 0 ? week.Average / maxAverage : 0)),
                Background = new SolidColorBrush(Mint),
                Opacity = isFastest ? 1.0 : 0.7,
            };
            averagePanel.Children.Add(track);
            averagePanel.Children.Add(fill);
            var averageText = new TextBlock { Text = FormatDuration(week.Average), FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            averageText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetColumn(averageText, 1);
            averagePanel.Children.Add(averageText);
            Grid.SetColumn(averagePanel, 2);
            grid.Children.Add(averagePanel);

            // 최고 (역대 Best가 이 주에 나왔으면 민트 점을 앞에 둔다 — 글자 색은 그대로)
            var bestPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            if (hasAllTimeBest)
                bestPanel.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Mint), Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "역대 Best가 나온 주" });
            var bestText = new TextBlock { Text = FormatDuration(week.Best), FontSize = 12, FontWeight = hasAllTimeBest ? FontWeights.SemiBold : FontWeights.Normal };
            bestText.SetResourceReference(TextBlock.ForegroundProperty, hasAllTimeBest ? "TextBrush" : "OverlayLabelTextBrush");
            bestPanel.Children.Add(bestText);
            Grid.SetColumn(bestPanel, 3);
            grid.Children.Add(bestPanel);

            var row = new Border { Child = grid, CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 5, 8, 6), Background = Brushes.Transparent };
            if (striped)
            {
                row.SetResourceReference(Border.BackgroundProperty, "OverlaySurfaceAltBackgroundBrush");
                row.MouseLeave += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "OverlaySurfaceAltBackgroundBrush");
            }
            else
            {
                row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            }
            row.MouseEnter += (_, _) => row.SetResourceReference(Border.BackgroundProperty, "OverlayTabHoverBrush");
            return row;
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
