using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 사냥 기록 창. 외치기 로그 창과 같은 틀(폰트 조절 · 가운데 제목 · ◀ 날짜 ▶)을 쓰고,
    /// 고른 날의 판을 막대그래프와 표로 보여준다. 기록은 <see cref="ExpHuntSessionService"/>가 쌓아 둔 요약 파일에서 읽는다.
    /// </summary>
    public sealed class ExpHuntRecordWindow : OverlayWindowBase
    {
        private static ExpHuntRecordWindow? _instance;

        /// <summary>날짜 목록에 둘 날 수.</summary>
        private const int MaxDays = 90;
        private const double ContentWidth = 760;
        private const double ChartHeight = 264;

        private readonly ExpHuntSessionService _service;
        private readonly ChatSettings? _settings;

        private readonly TextBlock _summaryText;
        private readonly TextBlock _fontSizeText;
        private readonly Slider _fontSlider;
        private readonly Button _dateButton;
        private readonly Button _prevButton;
        private readonly Button _nextButton;
        private readonly Popup _datePopup;
        private readonly ListBox _dayList;
        private readonly Canvas _chart;
        private readonly StackPanel _rowsPanel;
        private readonly ScrollViewer _rowScroll;

        private List<ExpHuntSession> _all = new();
        private List<DateTime> _days = new();
        private DateTime _selectedDay;
        private double _fontSize = 15;

        protected override bool UseToolWindowStyle => true;
        /// <summary>기록만 보는 창이라 잠금 상태에서도 끌어 옮길 수 있게 둔다.</summary>
        protected override bool DragRequiresUnlock => false;
        protected override ChatSettings? ResolveSettings() => _settings;

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
            SizeToContent = SizeToContent.Height;
            Width = ContentWidth + 24;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // ── 제목 줄: 폰트 조절 · 가운데 제목 · 닫기 (외치기 로그 창과 같은 구성)
            var title = new TextBlock
            {
                Text = "사냥 기록",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "OverlayTitleAccentTextBrush");

            var fontLabel = new TextBlock { Text = "폰트", FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            fontLabel.SetResourceReference(TextBlock.ForegroundProperty, "OverlayInfoTextBrush");

            _fontSlider = new Slider
            {
                Width = 90,
                Minimum = 11,
                Maximum = 26,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
                Value = _fontSize,
            };
            _fontSlider.ValueChanged += (_, e) => ApplyFontSize(e.NewValue);

            _fontSizeText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Width = 34, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) };
            _fontSizeText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayInfoTextBrush");

            var fontBox = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            fontBox.Children.Add(fontLabel);
            fontBox.Children.Add(_fontSlider);
            fontBox.Children.Add(_fontSizeText);

            var closeButton = new Button { HorizontalAlignment = HorizontalAlignment.Right, Style = (Style)FindResource("WindowCloseButtonStyle") };
            closeButton.Click += (_, _) => Close();

            var titleGrid = new Grid();
            titleGrid.Children.Add(title);
            titleGrid.Children.Add(fontBox);
            titleGrid.Children.Add(closeButton);

            var titleBar = new Border { BorderThickness = new Thickness(0, 0, 0, 1), CornerRadius = new CornerRadius(4, 4, 0, 0), Padding = new Thickness(10), Child = titleGrid, Cursor = Cursors.SizeAll };
            titleBar.SetResourceReference(Border.BackgroundProperty, "OverlaySurfaceBackgroundBrush");
            titleBar.SetResourceReference(Border.BorderBrushProperty, "OverlayStrongBorderBrush");

            // ── 날짜 줄: ◀ 날짜 ▶ (날짜를 누르면 기록이 있는 날 목록이 열린다) + 그 날 요약
            _prevButton = MakeNavButton("◀", "이전 기록 날짜");
            _nextButton = MakeNavButton("▶", "다음 기록 날짜");
            _prevButton.Click += (_, _) => MoveDay(-1);
            _nextButton.Click += (_, _) => MoveDay(+1);

            _dateButton = new Button
            {
                Content = "-",
                Width = 226,
                Height = 30,
                Padding = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(6, 0, 6, 0),
                Style = (Style)FindResource("SecondaryButtonStyle"),
                ToolTip = "날짜 고르기",
            };
            _dateButton.Click += (_, _) => _datePopup.IsOpen = !_datePopup.IsOpen;

            _dayList = new ListBox { Width = 226, MaxHeight = 380, BorderThickness = new Thickness(0) };
            ScrollViewer.SetHorizontalScrollBarVisibility(_dayList, ScrollBarVisibility.Disabled);
            _dayList.SetResourceReference(BackgroundProperty, "InputFieldBackgroundBrush");
            _dayList.SetResourceReference(ForegroundProperty, "TextBrush");
            _dayList.SelectionChanged += (_, _) =>
            {
                if (_dayList.SelectedItem is ListBoxItem item && item.Tag is DateTime day && day != _selectedDay)
                {
                    _selectedDay = day;
                    _datePopup.IsOpen = false;
                    RenderDay();
                }
            };

            var popupBorder = new Border { BorderThickness = new Thickness(1), Padding = new Thickness(2), Child = _dayList };
            popupBorder.SetResourceReference(Border.BackgroundProperty, "OverlaySurfaceBackgroundBrush");
            popupBorder.SetResourceReference(Border.BorderBrushProperty, "OverlayCardBorderBrush");
            _datePopup = new Popup { PlacementTarget = _dateButton, Placement = PlacementMode.Bottom, StaysOpen = false, Child = popupBorder };

            _summaryText = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            _summaryText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var dateBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            dateBar.Children.Add(_prevButton);
            dateBar.Children.Add(_dateButton);
            dateBar.Children.Add(_nextButton);
            dateBar.Children.Add(_summaryText);
            dateBar.Children.Add(_datePopup);

            // ── 그래프와 표 (표는 판이 많으면 스크롤)
            _chart = new Canvas { Height = ChartHeight, Margin = new Thickness(0, 0, 0, 12) };
            _rowsPanel = new StackPanel();
            _rowScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Height = 216,
                Content = _rowsPanel,
            };

            var bodyStack = new StackPanel { Margin = new Thickness(10) };
            bodyStack.Children.Add(dateBar);
            bodyStack.Children.Add(_chart);
            bodyStack.Children.Add(_rowScroll);

            var bodyBorder = new Border { Child = bodyStack };
            bodyBorder.SetResourceReference(Border.BackgroundProperty, "OverlayShellBackgroundBrush");

            var root = new DockPanel();
            DockPanel.SetDock(titleBar, Dock.Top);
            root.Children.Add(titleBar);
            root.Children.Add(bodyBorder);

            var border = new Border { BorderThickness = new Thickness(1.4), CornerRadius = new CornerRadius(6), Child = root };
            border.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "OverlayAccentBorderBrush");
            Content = border;

            // 창 어디를 잡아도 옮길 수 있다. 버튼·슬라이더·목록은 눌려야 하므로 가로채지 않는다
            PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && IsOnControl(source))
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

            ApplyFontSize(_fontSize);
            Reload();
        }

        private Button MakeNavButton(string glyph, string tip) => new()
        {
            Content = glyph,
            Width = 34,
            Height = 30,
            Padding = new Thickness(0),
            FontSize = 11,
            Style = (Style)FindResource("SecondaryButtonStyle"),
            ToolTip = tip,
        };

        /// <summary>눌러 쓰는 것(버튼·슬라이더·목록) 위인지 — 그 위에서는 창을 끌지 않는다.</summary>
        private static bool IsOnControl(DependencyObject source)
        {
            for (DependencyObject? current = source; current != null;
                 current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                     ? VisualTreeHelper.GetParent(current)
                     : LogicalTreeHelper.GetParent(current))
            {
                if (current is ButtonBase or ListBox or Slider or ScrollBar)
                    return true;
            }
            return false;
        }

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
            _fontSize = Math.Max(11, Math.Min(26, size));
            _fontSizeText.Text = $"{(int)Math.Round(_fontSize)}px";
            if (_settings != null)
            {
                _settings.ExpHuntRecordFontSize = _fontSize;
                try { ConfigService.SaveDeferred(_settings); } catch { }
            }
            RenderDay();
        }

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
                long total = _all.Where(s => s.StartedAt.Date == day).Sum(static s => s.TotalExp);
                var item = new ListBoxItem
                {
                    Content = $"{day:yyyy-MM-dd(ddd)}    {FormatExpShort(total)}",
                    Tag = day,
                    Padding = new Thickness(8, 4, 8, 4),
                    FontSize = 13,
                };
                _dayList.Items.Add(item);
                if (day == _selectedDay)
                    _dayList.SelectedItem = item;
            }

            RenderDay();
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
            foreach (object? entry in _dayList.Items)
            {
                if (entry is ListBoxItem item && item.Tag is DateTime day && day == _selectedDay)
                {
                    _dayList.SelectedItem = item;
                    break;
                }
            }
            RenderDay();
        }

        /// <summary>고른 날의 판을 막대그래프와 표로 그린다.</summary>
        private void RenderDay()
        {
            var sessions = _all.Where(s => s.StartedAt.Date == _selectedDay).OrderBy(static s => s.StartedAt).ToList();

            _dateButton.Content = _selectedDay.ToString("yyyy-MM-dd");
            int index = _days.IndexOf(_selectedDay);
            _prevButton.IsEnabled = index >= 0 && index < _days.Count - 1;
            _nextButton.IsEnabled = index > 0;

            long dayTotal = sessions.Sum(static s => s.TotalExp);
            var dayTime = TimeSpan.FromSeconds(sessions.Sum(static s => s.Duration.TotalSeconds));
            _summaryText.Text = sessions.Count > 0
                ? $"{sessions.Count}판 · {FormatDuration(dayTime)} · {FormatExp(dayTotal)}"
                : "기록 없음";

            DrawChart(sessions);

            _rowsPanel.Children.Clear();
            _rowsPanel.Children.Add(MakeRow("시간", "획득 경험치", "시간당", "마리수", isHeader: true));
            if (sessions.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "3분 넘게 사냥하고 100억 넘게 벌면 한 판으로 남습니다.",
                    Margin = new Thickness(2, 12, 2, 4),
                    FontSize = Math.Max(11, _fontSize - 2),
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "OverlayInfoTextBrush");
                _rowsPanel.Children.Add(empty);
                return;
            }

            foreach (ExpHuntSession session in sessions)
            {
                _rowsPanel.Children.Add(MakeRow(
                    $"{session.StartedAt:HH:mm}~{session.EndedAt:HH:mm}",
                    FormatExp(session.TotalExp),
                    FormatExp(session.ExpPerHour),
                    $"{session.GainCount:N0}마리",
                    isHeader: false));
            }
        }

        /// <summary>판마다 막대 하나. 높이는 그 날 가장 많이 번 판 기준.</summary>
        private void DrawChart(List<ExpHuntSession> sessions)
        {
            _chart.Children.Clear();
            if (sessions.Count == 0)
                return;

            double labelSize = Math.Max(10, _fontSize - 2);
            double labelHeight = labelSize + 14;
            double topPad = labelSize + 6;   // 가장 높은 막대 위에도 숫자가 들어가게
            double plotHeight = ChartHeight - labelHeight - topPad;
            double slot = Math.Min(110, ContentWidth / Math.Max(1, sessions.Count));
            double barWidth = Math.Max(10, slot - 18);
            long max = sessions.Max(static s => s.TotalExp);

            var accent = (Brush)FindResource("OverlayAccentTextBrush");
            var rare = (Brush)FindResource("OverlayRareAccentBrush");

            for (int i = 0; i < sessions.Count; i++)
            {
                ExpHuntSession session = sessions[i];
                double height = max > 0 ? Math.Max(3, plotHeight * session.TotalExp / max) : 3;
                double x = i * slot + (slot - barWidth) / 2;
                bool isBest = session.TotalExp == max;

                var bar = new Border
                {
                    Width = barWidth,
                    Height = height,
                    CornerRadius = new CornerRadius(3, 3, 0, 0),
                    Background = isBest ? rare : accent,
                    Opacity = isBest ? 0.95 : 0.6,
                    ToolTip = $"{session.StartedAt:HH:mm}~{session.EndedAt:HH:mm} · {FormatDuration(session.Duration)}\n" +
                              $"획득 {FormatExp(session.TotalExp)} · 시간당 {FormatExp(session.ExpPerHour)} · {session.GainCount:N0}마리",
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, topPad + plotHeight - height);
                _chart.Children.Add(bar);

                if (slot >= labelSize * 4.5 || isBest)
                {
                    var value = new TextBlock
                    {
                        Text = FormatExp(session.TotalExp),
                        FontSize = labelSize,
                        FontWeight = FontWeights.SemiBold,
                        Width = slot,
                        TextAlignment = TextAlignment.Center,
                        Foreground = isBest ? rare : accent,
                    };
                    Canvas.SetLeft(value, i * slot);
                    Canvas.SetTop(value, topPad + plotHeight - height - labelSize - 4);
                    _chart.Children.Add(value);
                }

                var time = new TextBlock
                {
                    Text = session.StartedAt.ToString("HH:mm"),
                    FontSize = labelSize,
                    Width = slot,
                    TextAlignment = TextAlignment.Center,
                };
                time.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                Canvas.SetLeft(time, i * slot);
                Canvas.SetTop(time, topPad + plotHeight + 8);
                _chart.Children.Add(time);
            }

            var baseline = new Border { Width = Math.Max(160, sessions.Count * slot), Height = 1, Opacity = 0.45 };
            baseline.SetResourceReference(Border.BackgroundProperty, "TextBrush");
            Canvas.SetLeft(baseline, 0);
            Canvas.SetTop(baseline, topPad + plotHeight);
            _chart.Children.Add(baseline);
        }

        private UIElement MakeRow(string time, string total, string perHour, string count, bool isHeader)
        {
            var grid = new Grid { Margin = new Thickness(0, isHeader ? 0 : 5, 0, isHeader ? 6 : 0) };
            double[] widths = { 150, 196, 196, 148 };   // 시간 칸을 줄여 값들이 너무 멀어지지 않게
            foreach (double width in widths)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

            string[] values = { time, total, perHour, count };
            for (int i = 0; i < values.Length; i++)
            {
                var cell = new TextBlock
                {
                    Text = values[i],
                    FontSize = isHeader ? Math.Max(11, _fontSize - 2) : _fontSize,
                    FontWeight = isHeader ? FontWeights.SemiBold : (i is 1 or 2 ? FontWeights.SemiBold : FontWeights.Normal),
                    TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Right,
                    Margin = new Thickness(0, 0, 16, 0),
                };
                // 흐린 회색은 어두운 배경에서 잘 안 읽혀, 본문은 기본 글자색·머리글은 밝은 안내색을 쓴다
                cell.SetResourceReference(TextBlock.ForegroundProperty,
                    isHeader ? "OverlayInfoTextBrush"
                    : i == 1 ? "OverlayRareAccentBrush"
                    : i == 2 ? "OverlayAccentTextBrush"
                    : "TextBrush");
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }

            if (isHeader)
            {
                var underline = new Border { Height = 1, Opacity = 0.4, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -4) };
                underline.SetResourceReference(Border.BackgroundProperty, "TextBrush");
                Grid.SetColumnSpan(underline, widths.Length);
                grid.Children.Add(underline);
            }

            return grid;
        }

        /// <summary>"1시간 15분" · "45분" 꼴.</summary>
        private static string FormatDuration(TimeSpan span)
        {
            int minutes = span > TimeSpan.Zero ? (int)span.TotalMinutes : 0;
            if (minutes < 60)
                return $"{minutes}분";
            return minutes % 60 == 0 ? $"{minutes / 60}시간" : $"{minutes / 60}시간 {minutes % 60}분";
        }

        /// <summary>좁은 자리(날짜 목록)용 짧은 표기 — "9.2조", "1725억".</summary>
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
