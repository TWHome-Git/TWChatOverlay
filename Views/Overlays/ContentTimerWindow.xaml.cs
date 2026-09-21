using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 던전 타이머 창. 서비스가 만든 <see cref="TimerView"/>를 그리기만 한다.
    /// 열은 지난주 평균 · 직전 판 · 최근 판. 최근 판 열은 강조하고 직전 판 대비 차이를 작게 붙인다.
    /// 표의 틀(행 수·열 너비)은 가장 큰 던전 기준으로 고정해 두어, &lt; &gt; 로 던전을 넘겨도 창 크기가 변하지 않는다.
    /// 진행 중에는 아무것도 보여주지 않고, 클리어되는 순간 결과를 지정한 시간 동안 남긴 뒤 저절로 닫힌다.
    /// 창 크기는 '타이머 글자 크기' 설정에 맞춰 잡히고, 잠금 해제 모드에서 끌어서 옮길 수 있다.
    /// </summary>
    public partial class ContentTimerWindow : OverlayWindowBase
    {
        private static readonly string[] ColumnHeaders = { "지난주", "직전 판", "최근 판" };
        private const int CurrentColumn = ContentTimerService.CurrentColumn;
        private const int PreviousColumn = CurrentColumn - 1;
        private const string TotalRowName = "합계";
        private const double DimOpacity = 0.6;
        /// <summary>글자 크기 설정값이 이 값일 때 아래 기준 크기들이 그대로 쓰인다.</summary>
        private const double BaseFontSize = 16.0;
        // 값 열 너비(글자 크기 16 기준)와 행 이름 열의 여유. 열 너비를 고정해 어느 던전을 보든 창 너비가 같게 한다.
        // 행 이름 열은 모든 던전의 행 이름 중 가장 긴 것에 맞춘다.
        // 값 열 너비 — 부제 날짜 "09.14 22:05"가 들어갈 만큼 (머리글 "지난주"도 같은 폭에 들어간다)
        private static readonly double[] ValueColumnWidths = { 72, 72, 72 };
        private const double LabelColumnPadding = 8;
        // 최근 판 값 옆 차이("+7", "-33")를 놓는 고정 폭 열. 차이 글자 수에 따라 시간이 흔들리지 않게 따로 둔다.
        private const double DeltaColumnWidth = 34;
        private const double DeltaBaseFontSize = 11.5;
        // 작은 모드 열 너비: 지난주 평균 열은 숨기고(0) 값 열을 조금 더 좁힌다. 행 이름 열은 전체 모드와 같이 모든 던전 공통 폭이다
        private static readonly double[] CompactValueColumnWidths = { 0, 72, 72 };
        private const double CompactDeltaColumnWidth = 32;
        private double _labelColumnBaseWidth;

        /// <summary>표 장식(띠·줄무늬·밑줄)의 기준 여백(글자 크기 16 기준). 글자 크기가 바뀌면 같이 배율을 적용한다.</summary>
        private sealed record DecorStyle(double Left, double Top, double Right, double Bottom);

        private static readonly SolidColorBrush FasterBrush = Frozen(124, 227, 139);
        private static readonly SolidColorBrush SlowerBrush = Frozen(255, 123, 123);

        private static SolidColorBrush Frozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        /// <summary>방금 끝난 구간 강조: 켜면 초록, 끄면 기본 글자색으로 되돌린다.</summary>
        private static void ApplyHighlight(TextBlock block, bool on)
        {
            if (on)
                block.Foreground = FasterBrush;
            else
                block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        }

        /// <summary>표 글자 하나의 기준 크기(글자 크기 설정 16 기준). Tag에 넣어 두고 크기가 바뀌면 제자리에서 다시 계산한다.</summary>
        private sealed record TextStyle(double FontSize, double Left, double Top, double Bottom);

        private readonly DispatcherTimer _lifetimeTimer;
        private readonly ChatSettings? _settings;

        private double _fontScale = 1.0;
        private bool _tableBuilt;
        private string _tableKey = string.Empty;         // 행 수·합계 유무가 같으면 표를 다시 만들지 않는다
        private TextBlock[] _columnSubHeaders = Array.Empty<TextBlock>();
        private TextBlock[] _rowLabels = Array.Empty<TextBlock>();
        private TextBlock[,] _cells = new TextBlock[0, 0];  // [행][열], 마지막 행은 합계 자리
        private TextBlock[] _deltas = Array.Empty<TextBlock>();  // 행별 최근 판 차이 칸
        private TextBlock? _previousHeader;                      // 눌러서 직전 판 ↔ Best를 바꾸는 머리글
        private Border? _totalSeparator;
        private int _totalRowIndex = -1;
        private bool _isPreviewMode;
        private bool _isClosing;
        /// <summary>작은 모드: 실시간 클리어 결과용. 던전 목록·화살표·지난주 평균 열을 숨긴다. 직접 열면(메뉴·미리보기) 전체 모드.</summary>
        private bool _compact;
        private const int HiddenColumnInCompact = 0; // 값 열 중 지난주 평균

        public ContentTimerWindow(ChatSettings? settings)
        {
            _settings = settings;
            InitializeComponent();

            _lifetimeTimer = new DispatcherTimer();
            _lifetimeTimer.Tick += (_, _) =>
            {
                _lifetimeTimer.Stop();
                StartCloseAnimation();
            };

            // 잠금 해제 모드에서는 창 어디를 잡아도 선택+드래그 가능
            PreviewMouseLeftButtonDown += (_, e) => TryBeginDrag(e, markHandled: true);
            LocationChanged += (_, _) => PersistPositionDeferred();

            BuildGroupList();

            if (_settings != null)
            {
                ApplyFontSize(_settings.ContentTimerFontSize);
                _settings.PropertyChanged += Settings_PropertyChanged;
            }
        }

        // ===== 왼쪽 던전 목록 =====

        private readonly System.Collections.Generic.Dictionary<string, Button> _groupButtons = new(StringComparer.Ordinal);
        private string _selectedGroupKey = string.Empty;

        /// <summary>왼쪽 목록을 묶음 순서대로 만든다. 클릭하면 그 던전 기록으로 바로 간다.</summary>
        private void BuildGroupList()
        {
            GroupList.Children.Clear();
            _groupButtons.Clear();
            foreach (var (key, name) in AppServices.Get<ContentTimerService>().Groups)
            {
                var button = new Button
                {
                    Content = name,
                    Tag = key,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 0, 0, 3),
                    Style = (Style)FindResource("WindowSmallButtonStyle"),
                    ToolTip = name + " 기록 보기",
                };
                button.Click += (_, _) => AppServices.Get<ContentTimerService>().ShowGroup(key);
                _groupButtons[key] = button;
                GroupList.Children.Add(button);
            }
        }

        /// <summary>목록에서 현재 보고 있는 던전을 강조한다 (민트 글자 + 굵게). 버튼 템플릿이 테두리를 고정하므로 글자로 표시한다.</summary>
        private void HighlightGroup(string groupKey)
        {
            _selectedGroupKey = groupKey;
            foreach (var pair in _groupButtons)
            {
                bool selected = pair.Key == groupKey;
                pair.Value.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
                pair.Value.SetResourceReference(Control.ForegroundProperty, selected ? "OverlayAccentBorderBrush" : "ButtonTextBrush");
            }
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_settings == null)
                return;
            // 빈 이름 = 설정 전체 교체(프로필 불러오기)
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ChatSettings.ContentTimerFontSize))
                Dispatcher.BeginInvoke(new Action(() => ApplyFontSize(_settings.ContentTimerFontSize)));
        }

        /// <summary>디버그용: 창을 계속 띄워 둔다. 결과 유지 시간이 지나도 닫지 않고 대기 상태로 돌아간다.</summary>
        public bool KeepOpen { get; set; }

        // ===== 글자 크기 =====

        /// <summary>
        /// 설정의 글자 크기를 적용한다. 표를 다시 만들지 않고 글자 크기와 여백, 열 최소 너비만 제자리에서 바꾼다 —
        /// 창 크기가 내용에 맞춰 잡히므로 표를 비웠다 채우면 창이 줄었다 커지며 흔들린다.
        /// </summary>
        public void ApplyFontSize(double size)
        {
            double scale = Math.Max(10.0, Math.Min(40.0, size)) / BaseFontSize;
            if (Math.Abs(scale - _fontScale) < 0.001)
                return;

            _fontScale = scale;
            TitleText.FontSize = Scaled(15);
            foreach (Button item in _groupButtons.Values)
            {
                item.FontSize = Scaled(12);
                item.Height = Scaled(24);
            }

            foreach (UIElement child in TableGrid.Children)
            {
                if (child is Border decor && decor.Tag is DecorStyle decorStyle)
                {
                    ApplyDecorStyle(decor, decorStyle);
                    continue;
                }
                if (child is not TextBlock block || block.Tag is not TextStyle style)
                    continue;
                ApplyStyle(block, style);
                foreach (Inline inline in block.Inlines)
                {
                    if (inline is Run run && run.Tag is double baseRunSize)
                        run.FontSize = Scaled(baseRunSize);
                }
            }
            ApplyColumnWidths();
        }

        private void ApplyStyle(TextBlock block, TextStyle style)
        {
            block.FontSize = Scaled(style.FontSize);
            block.Margin = new Thickness(Scaled(style.Left), Scaled(style.Top), 0, Scaled(style.Bottom));
        }

        private void ApplyDecorStyle(Border decor, DecorStyle style)
            => decor.Margin = new Thickness(Scaled(style.Left), Scaled(style.Top), Scaled(style.Right), Scaled(style.Bottom));

        /// <summary>열 너비를 고정한다: 행 이름 열은 모든 던전의 가장 긴 행 이름, 값 열은 정해진 너비. 던전을 넘겨도 창 너비가 같다.</summary>
        private void ApplyColumnWidths()
        {
            if (TableGrid.ColumnDefinitions.Count == 0)
                return;
            if (_labelColumnBaseWidth <= 0)
                _labelColumnBaseWidth = MeasureLongestRowName();
            int last = TableGrid.ColumnDefinitions.Count - 1;
            double labelWidth = _labelColumnBaseWidth;
            double[] valueWidths = _compact ? CompactValueColumnWidths : ValueColumnWidths;
            double deltaWidth = _compact ? CompactDeltaColumnWidth : DeltaColumnWidth;
            TableGrid.ColumnDefinitions[0].Width = new GridLength(Scaled(labelWidth + LabelColumnPadding));
            for (int c = 1; c < last; c++)
            {
                double width = valueWidths[Math.Min(c - 1, valueWidths.Length - 1)];
                TableGrid.ColumnDefinitions[c].Width = new GridLength(width <= 0 ? 0 : Scaled(width));
            }
            TableGrid.ColumnDefinitions[last].Width = new GridLength(Scaled(deltaWidth));
        }

        /// <summary>
        /// 전체 모드 / 작은 모드를 적용한다. 작은 모드는 왼쪽 던전 목록과 &lt; &gt; 버튼을 접고 지난주 평균 열을 숨겨,
        /// 클리어 순간 화면 한쪽에 작게 떠서 직전 판과 최근 판만 보여준다. 표는 다시 만들지 않고 숨기기만 한다.
        /// </summary>
        private void ApplyLayoutMode()
        {
            Visibility full = _compact ? Visibility.Collapsed : Visibility.Visible;
            GroupListPanel.Visibility = full;
            PrevGroupButton.Visibility = full;
            NextGroupButton.Visibility = full;
            ModeButton.Content = _compact ? "크게" : "작게";

            int hiddenGridColumn = HiddenColumnInCompact + 1;
            foreach (UIElement child in TableGrid.Children)
            {
                if (child is not TextBlock block)
                    continue; // 장식(띠·줄무늬·밑줄)은 열을 가로지르므로 그대로 둔다
                if (Grid.GetColumn(block) == hiddenGridColumn && Grid.GetColumnSpan(block) == 1)
                    block.Visibility = full;
            }
            ApplyColumnWidths();
        }

        /// <summary>모든 던전의 행 이름 중 가장 넓은 것의 너비(글자 크기 16 기준).</summary>
        private double MeasureLongestRowName()
            => MeasureWidest(AppServices.Get<ContentTimerService>().AllRowNames().Append(TotalRowName));

        /// <summary>주어진 행 이름들 중 가장 넓은 것의 너비(글자 크기 16 기준).</summary>
        private double MeasureWidest(System.Collections.Generic.IEnumerable<string> names)
        {
            var typeface = new Typeface(FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal); // 행 이름은 보통 굵기
            double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            double widest = 0;
            foreach (string name in names)
            {
                var text = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    typeface, BaseFontSize, Brushes.White, pixelsPerDip);
                widest = Math.Max(widest, text.WidthIncludingTrailingWhitespace);
            }
            return Math.Ceiling(widest);
        }

        private double Scaled(double baseSize) => Math.Round(baseSize * _fontScale, 1);

        // ===== 표시 =====

        /// <summary>클리어. 결과를 보여주고 N초 뒤 닫는다.</summary>
        public void ShowResult(TimerView view, int holdSeconds)
        {
            _isPreviewMode = false;
            _compact = true; // 실시간 결과는 작은 모드: 던전 목록·화살표·지난주 평균 열 없이 직전 판과 최근 판만
            _isClosing = false;
            Render(view);
            Reveal();

            _lifetimeTimer.Stop();
            _lifetimeTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, Math.Min(300, holdSeconds)));
            _lifetimeTimer.Start();
        }

        /// <summary>잠금 해제 모드에서 위치를 잡을 수 있게 띄운다. 마지막에 쓰던 모드 그대로 띄워야 자리를 제대로 잡는다. 저절로 닫히지 않는다.</summary>
        public void ShowPreview(TimerView view, bool compact = false)
        {
            _isPreviewMode = true;
            _compact = compact;
            _lifetimeTimer.Stop();
            Render(view);
            Reveal();
        }

        /// <summary>표시 모드(작게/크게·미리보기·자동 닫힘)는 그대로 두고 내용만 다시 그린다. 직전 판 ↔ Best 전환용.</summary>
        public void RefreshView(TimerView view) => Render(view);

        /// <summary>기록 보기(메뉴 버튼·화살표·디버그 대기). 완료 기록만 보여주고 저절로 닫히지 않는다.</summary>
        public void ShowIdle(TimerView view, bool compact = false)
        {
            _isPreviewMode = false;
            _compact = compact;
            _isClosing = false;
            _lifetimeTimer.Stop();
            Render(view);
            Reveal();
        }

        /// <summary>표 내용을 그린다. 행 수가 바뀌면 표를 다시 만든다(세로는 내용대로). 가로는 열 최소 너비로 고정된다.</summary>
        private void Render(TimerView view)
        {
            EnsureTable(view);
            ApplyLayoutMode();

            TitleText.Text = view.Title;
            HighlightGroup(view.GroupKey);
            ApplyPreviousHeader(view.PreviousIsBest);

            for (int c = 0; c < ColumnHeaders.Length; c++)
            {
                TextBlock sub = _columnSubHeaders[c];
                sub.Visibility = view.ShowColumnTimes ? Visibility.Visible : Visibility.Collapsed;
                sub.Text = view.ColumnSub[c] ?? "-";
            }

            for (int r = 0; r < view.Rows.Count; r++)
            {
                TimerRow row = view.Rows[r];
                _rowLabels[r].Text = row.Name;
                // 방금 끝난 구간은 행 이름과 최근 판 값을 초록색으로
                ApplyHighlight(_rowLabels[r], row.Highlight);
                ApplyHighlight(_cells[r, CurrentColumn], row.Highlight);
                for (int c = 0; c < ColumnHeaders.Length; c++)
                {
                    double? sec = row.Seconds[c];
                    // 상한 값("30초 이하")은 정확한 시간이 아니므로 차이를 붙이지 않는다
                    bool cappedPair = row.Capped[c] || row.Capped[PreviousColumn];
                    double? delta = c == CurrentColumn && !cappedPair && sec.HasValue && row.Seconds[PreviousColumn].HasValue
                        ? sec - row.Seconds[PreviousColumn]
                        : null;
                    SetCell(r, c, sec, delta, row.Capped[c], row.Times[c]);
                }
            }

            if (_totalRowIndex >= 0)
            {
                for (int c = 0; c < ColumnHeaders.Length; c++)
                {
                    double? total = view.Totals[c];
                    bool cappedPair = view.TotalsCapped[c] || view.TotalsCapped[PreviousColumn];
                    double? delta = c == CurrentColumn && !cappedPair && total.HasValue && view.Totals[PreviousColumn].HasValue
                        ? total - view.Totals[PreviousColumn]
                        : null;
                    SetCell(_totalRowIndex, c, total, delta, view.TotalsCapped[c]);
                }
            }
        }

        /// <summary>가운데 열 머리글: 평소 "직전 판", 최고 기록을 보는 중이면 "Best"로 또렷하게 적는다.</summary>
        private void ApplyPreviousHeader(bool isBest)
        {
            if (_previousHeader == null)
                return;
            _previousHeader.Text = isBest ? "Best" : ColumnHeaders[PreviousColumn];
            _previousHeader.Opacity = isBest ? 1.0 : DimOpacity;
            _previousHeader.SetResourceReference(TextBlock.ForegroundProperty, isBest ? "OverlayTitleAccentTextBrush" : "TextBrush");
        }

        /// <summary>가운데 열 머리글 클릭: 직전 판 ↔ 최고 기록. 잠금 해제(위치 조정) 중에는 창을 끌어 옮기는 중이라 넘긴다.</summary>
        private void PreviousHeader_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (UiLockService.IsUnlocked || _isPreviewMode)
                return;
            AppServices.Get<ContentTimerService>().TogglePreviousColumn();
            e.Handled = true;
        }

        /// <summary>
        /// 값 칸. 초는 "4:20" 꼴, 상한 값은 "30초 이하". 최근 판 열이면 옆의 고정 폭 차이 칸에 빠르면 초록 "-7", 느리면 빨강 "+7"을 적는다
        /// (시간은 시간끼리, 차이는 차이끼리 줄이 맞도록 한 칸에 이어 붙이지 않는다).
        /// </summary>
        private void SetCell(int row, int col, double? seconds, double? deltaSeconds, bool capped = false, string? time = null)
        {
            TextBlock cell = _cells[row, col];
            string text = !seconds.HasValue ? "-"
                : capped ? $"{(int)Math.Round(seconds.Value)}s↓"
                : FormatClock(seconds.Value);
            cell.Inlines.Clear();
            cell.Inlines.Add(new Run(text));
            if (seconds.HasValue && !string.IsNullOrEmpty(time))
            {
                // 묶음 모드: 행마다 판이 다르므로 값 아래에 그 판의 시각을 작게 붙인다
                const double timeBaseFontSize = 10.5;
                cell.Inlines.Add(new LineBreak());
                var timeRun = new Run(time) { FontSize = Scaled(timeBaseFontSize), Tag = timeBaseFontSize, FontWeight = FontWeights.Normal };
                timeRun.SetResourceReference(TextElement.ForegroundProperty, "OverlaySubtleTextBrush");
                cell.Inlines.Add(timeRun);
            }

            if (col != CurrentColumn || row >= _deltas.Length)
                return;
            TextBlock deltaBlock = _deltas[row];
            int delta = deltaSeconds.HasValue ? (int)Math.Round(deltaSeconds.Value) : 0;
            if (delta == 0)
            {
                deltaBlock.Text = string.Empty;
                return;
            }
            deltaBlock.Text = $"{(delta > 0 ? "+" : "-")}{Math.Abs(delta)}";
            deltaBlock.Foreground = delta > 0 ? SlowerBrush : FasterBrush;
        }

        /// <summary>"4:20" 꼴. 1시간 넘으면 "1:04:20".</summary>
        private static string FormatClock(double seconds)
        {
            if (seconds < 0) seconds = 0;
            int total = (int)Math.Round(seconds);
            int h = total / 3600, m = total % 3600 / 60, s = total % 60;
            return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m}:{s:00}";
        }

        /// <summary>
        /// 행 수·합계 유무가 바뀌었을 때만 표를 다시 만든다. 세로는 내용대로 잡히고, 가로는 열 최소 너비로 고정된다.
        /// </summary>
        private void EnsureTable(TimerView view)
        {
            int valueRows = view.Rows.Count;
            int allRows = valueRows + (view.ShowTotal ? 1 : 0);
            string key = $"{valueRows}|{view.ShowTotal}";
            if (_tableBuilt && key == _tableKey)
                return;
            _tableBuilt = true;
            _tableKey = key;

            TableGrid.Children.Clear();
            TableGrid.RowDefinitions.Clear();
            TableGrid.ColumnDefinitions.Clear();

            int colCount = ColumnHeaders.Length + 2;       // 행 이름 + 값 열들 + 최근 판 차이 칸
            int deltaColumn = CurrentColumn + 2;           // 최근 판 값 열 바로 오른쪽
            const int HeaderRow = 0, SubHeaderRow = 1, FirstValueRow = 2;
            _totalRowIndex = view.ShowTotal ? valueRows : -1;

            int gridRows = FirstValueRow + allRows;
            for (int r = 0; r < gridRows; r++)
                TableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int c = 0; c < colCount; c++)
                TableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ApplyColumnWidths();

            // 장식은 글자보다 먼저 넣어 뒤에 깔리게 한다: 최근 판 열 전체에 민트 띠, 값 행 하나 걸러 옅은 줄무늬, 머리글 아래 밑줄
            TableGrid.Children.Add(MakeDecor(0, CurrentColumn + 1, gridRows, 2, "OverlayAccentBorderBrush", 0.12,
                new DecorStyle(8, -2, -8, -2), radius: 6));
            for (int r = 1; r < valueRows; r += 2)
                TableGrid.Children.Add(MakeDecor(FirstValueRow + r, 0, 1, colCount, "TextBrush", 0.06,
                    new DecorStyle(-6, 0, -8, 0), radius: 4));
            Border underline = MakeDecor(SubHeaderRow, 0, 1, colCount, "TextBrush", 0.25, new DecorStyle(-6, 0, -8, 0), radius: 0);
            underline.Height = 1;
            underline.VerticalAlignment = VerticalAlignment.Bottom;
            TableGrid.Children.Add(underline);

            // 머리글: 열 이름 + 그 아래 부제(지난주 판 수, 클리어 시각)
            _columnSubHeaders = new TextBlock[ColumnHeaders.Length];
            for (int c = 0; c < ColumnHeaders.Length; c++)
            {
                bool isCurrent = c == CurrentColumn;
                var header = MakeText(ColumnHeaders[c], HeaderRow, c + 1, new TextStyle(15, 8, 1, 1), bold: true,
                    opacity: isCurrent ? 1.0 : DimOpacity);
                if (isCurrent)
                {
                    header.SetResourceReference(TextBlock.ForegroundProperty, "OverlayAccentBorderBrush");
                    Grid.SetColumnSpan(header, 2); // 최근 판 머리글은 값 열 + 차이 칸에 걸친다
                }
                if (c == PreviousColumn)
                {
                    // 누르면 직전 판 ↔ 최고 기록. 글자만으로는 눌러지는지 모르므로 손 모양 커서와 설명을 붙인다
                    header.Background = Brushes.Transparent; // 글자 사이 빈 곳도 눌리게
                    header.Cursor = Cursors.Hand;
                    header.ToolTip = "눌러서 최고 기록(Best) ↔ 직전 판";
                    header.MouseLeftButtonUp += PreviousHeader_MouseLeftButtonUp;
                    _previousHeader = header;
                }
                TableGrid.Children.Add(header);

                var sub = MakeText("-", SubHeaderRow, c + 1, new TextStyle(11, 8, 0, 4), bold: false, opacity: 0.5);
                if (isCurrent)
                    Grid.SetColumnSpan(sub, 2);
                _columnSubHeaders[c] = sub;
                TableGrid.Children.Add(sub);
            }

            // 합계 행 위 구분선
            _totalSeparator = null;
            if (view.ShowTotal)
            {
                _totalSeparator = new Border
                {
                    Height = 1,
                    Opacity = 0.35,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 0, 0),
                };
                _totalSeparator.SetResourceReference(Border.BackgroundProperty, "TextBrush");
                Grid.SetRow(_totalSeparator, FirstValueRow + _totalRowIndex);
                Grid.SetColumn(_totalSeparator, 0);
                Grid.SetColumnSpan(_totalSeparator, colCount);
                TableGrid.Children.Add(_totalSeparator);
            }

            _rowLabels = new TextBlock[allRows];
            _cells = new TextBlock[allRows, ColumnHeaders.Length];
            _deltas = new TextBlock[allRows];
            for (int r = 0; r < allRows; r++)
            {
                bool isTotal = r == _totalRowIndex;
                int gridRow = FirstValueRow + r;
                double topMargin = isTotal ? 6 : 3;

                // 차이 칸: 왼쪽 정렬이라 부호가 세로로 나란히 선다
                var deltaBlock = MakeText(string.Empty, gridRow, deltaColumn, new TextStyle(DeltaBaseFontSize, 6, topMargin, 3),
                    bold: false, opacity: 1.0, alignLeft: true);
                _deltas[r] = deltaBlock;
                TableGrid.Children.Add(deltaBlock);

                var label = MakeText(isTotal ? TotalRowName : "-", gridRow, 0,
                    new TextStyle(16, 0, topMargin, 3), bold: isTotal, opacity: 1.0, alignLeft: true);
                _rowLabels[r] = label;
                TableGrid.Children.Add(label);

                for (int c = 0; c < ColumnHeaders.Length; c++)
                {
                    bool isCurrent = c == CurrentColumn;
                    var cell = MakeText("-", gridRow, c + 1, new TextStyle(isCurrent ? 18 : 16, 8, topMargin, 3),
                        bold: isTotal || isCurrent, opacity: isCurrent ? 1.0 : DimOpacity);
                    _cells[r, c] = cell;
                    TableGrid.Children.Add(cell);
                }
            }
        }

        /// <summary>표 장식 하나(배경 띠·줄무늬·밑줄). 기준 여백은 Tag에 남겨 두고 현재 글자 크기 배율로 적용한다.</summary>
        private Border MakeDecor(int row, int col, int rowSpan, int colSpan, string brushKey, double opacity, DecorStyle style, double radius)
        {
            var border = new Border
            {
                Opacity = opacity,
                CornerRadius = new CornerRadius(radius),
                Tag = style,
                IsHitTestVisible = false,
            };
            border.SetResourceReference(Border.BackgroundProperty, brushKey);
            ApplyDecorStyle(border, style);
            Grid.SetRow(border, row);
            Grid.SetColumn(border, col);
            Grid.SetRowSpan(border, rowSpan);
            Grid.SetColumnSpan(border, colSpan);
            return border;
        }

        /// <summary>표 글자 하나. 기준 크기·여백은 Tag에 남겨 두고 현재 글자 크기 배율로 적용한다.</summary>
        private TextBlock MakeText(string text, int row, int col, TextStyle style, bool bold, double opacity, bool alignLeft = false)
        {
            var block = new TextBlock
            {
                Text = text,
                Tag = style,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
                Opacity = opacity,
                HorizontalAlignment = alignLeft ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                TextAlignment = alignLeft ? TextAlignment.Left : TextAlignment.Right, // 두 줄 칸(값 + 시각)도 오른쪽 끝을 맞춘다
                VerticalAlignment = VerticalAlignment.Center,
            };
            ApplyStyle(block, style);
            block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            Grid.SetRow(block, row);
            Grid.SetColumn(block, col);
            return block;
        }

        private void Reveal()
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            if (!IsVisible)
                Show();
            TopmostWindowHelper.BringToTopmost(this);
        }

        private void StartCloseAnimation()
        {
            if (_isPreviewMode || _isClosing)
                return;

            if (KeepOpen)
            {
                AppServices.Get<ContentTimerService>().ShowIdle(); // 디버그: 닫는 대신 대기 상태로
                return;
            }

            _isClosing = true;
            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }

        // ===== 이동 · 저장 =====

        // 위치 저장은 이 창의 PersistPositionDeferred가 맡는다 (모드·크기 저장과 함께)
        protected override bool PersistBoundsOnChange => false;
        protected override void OnDragCompleted() => PersistPositionDeferred();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => TryBeginDrag(e);

        /// <summary>작게/크게 버튼: 작은 모드 ↔ 전체 모드. 바꾼 모드는 설정에 남겨 다음에 메뉴 버튼으로 열 때 그 모드로 연다.</summary>
        private void ModeButton_Click(object sender, RoutedEventArgs e)
        {
            _compact = !_compact;
            ApplyLayoutMode();
            if (_settings != null)
            {
                _settings.ContentTimerCompact = _compact;
                try { ConfigService.SaveDeferred(_settings); } catch { }
            }
        }

        /// <summary>&lt; &gt; 버튼: 던전 묶음을 앞뒤로 넘긴다. 결과 자동 닫힘은 멈춘다.</summary>
        private void PrevGroup_Click(object sender, RoutedEventArgs e) => AppServices.Get<ContentTimerService>().ShowNextGroup(-1);
        private void NextGroup_Click(object sender, RoutedEventArgs e) => AppServices.Get<ContentTimerService>().ShowNextGroup(+1);

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            KeepOpen = false; // X는 디버그 상시 표시보다 우선한다
            _lifetimeTimer.Stop();
            Close(); // 기록만 보는 창이므로 뒤에서 진행 중인 판은 건드리지 않는다
        }

        protected override void OnClosed(EventArgs e)
        {
            _lifetimeTimer.Stop();
            if (_settings != null)
                _settings.PropertyChanged -= Settings_PropertyChanged;
            PersistPosition(saveImmediately: true);
            base.OnClosed(e);
        }

        private void PersistPositionDeferred()
        {
            if (!IsLoaded || !IsVisible)
                return;
            PersistPosition(saveImmediately: false);
        }

        private void PersistPosition(bool saveImmediately)
        {
            if (_settings == null)
                return;

            try
            {
                _settings.ContentTimerWindowLeft = Left;
                _settings.ContentTimerWindowTop = Top;
                if (saveImmediately)
                    ConfigService.Save(_settings);
                else
                    ConfigService.SaveDeferred(_settings);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save dungeon timer window position.", ex);
            }
        }
    }
}
