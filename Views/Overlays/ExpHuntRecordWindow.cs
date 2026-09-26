using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 사냥 기록 창. 왼쪽에서 날짜를 고르면 그 날의 판을 막대그래프와 표로 보여준다.
    /// 기록은 <see cref="ExpHuntSessionService"/>가 따로 쌓아 둔 요약 파일에서 읽는다.
    /// </summary>
    public sealed class ExpHuntRecordWindow : OverlayWindowBase
    {
        private static ExpHuntRecordWindow? _instance;

        /// <summary>왼쪽 목록에 둘 날짜 수.</summary>
        private const int MaxDays = 60;
        private const double ChartHeight = 150;

        private readonly ExpHuntSessionService _service;
        private readonly ListBox _dayList;
        private readonly Canvas _chart;
        private readonly StackPanel _rowsPanel;
        private readonly TextBlock _summaryText;

        private List<ExpHuntSession> _all = new();
        private DateTime _selectedDay;

        protected override bool UseToolWindowStyle => true;

        /// <summary>기록만 보는 창이라 잠금 상태에서도 끌어 옮길 수 있게 둔다.</summary>
        protected override bool DragRequiresUnlock => false;

        /// <summary>눌린 곳이 버튼·날짜 목록 안인지 — 누르는 것들은 드래그로 가로채지 않는다.</summary>
        private static bool IsOnButton(DependencyObject source)
        {
            for (DependencyObject? current = source; current != null;
                 current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                     ? VisualTreeHelper.GetParent(current)
                     : LogicalTreeHelper.GetParent(current))
            {
                if (current is System.Windows.Controls.Primitives.ButtonBase or ListBox)
                    return true;
            }
            return false;
        }

        private ExpHuntRecordWindow(ExpHuntSessionService service)
        {
            _service = service;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var title = new TextBlock
            {
                Text = "사냥 기록",
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "OverlayTitleAccentTextBrush");

            _summaryText = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(10, 2, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            _summaryText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");

            // 다른 오버레이 창과 같은 [닫기] (글자는 스타일이 그린다)
            var closeButton = new Button { Style = (Style)FindResource("WindowCloseButtonStyle") };
            closeButton.Click += (_, _) => Close();

            var header = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(closeButton, Dock.Right);
            header.Children.Add(closeButton);
            header.Children.Add(title);
            header.Children.Add(_summaryText);

            // 왼쪽: 날짜 목록 (고른 날이 계속 표시되도록 목록 상자로 둔다)
            _dayList = new ListBox
            {
                Width = 138,
                MaxHeight = 430,
                Margin = new Thickness(0, 0, 12, 0),
                BorderThickness = new Thickness(1),
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_dayList, ScrollBarVisibility.Disabled);
            _dayList.SetResourceReference(BackgroundProperty, "InputFieldBackgroundBrush");
            _dayList.SetResourceReference(BorderBrushProperty, "OverlayStrongBorderBrush");
            _dayList.SetResourceReference(ForegroundProperty, "TextBrush");
            _dayList.SelectionChanged += (_, _) =>
            {
                if (_dayList.SelectedItem is ListBoxItem item && item.Tag is DateTime day)
                {
                    _selectedDay = day;
                    RenderDay();
                }
            };

            // 오른쪽: 막대그래프 + 판 목록
            _chart = new Canvas { Height = ChartHeight, Margin = new Thickness(0, 0, 0, 10) };
            _rowsPanel = new StackPanel();
            var rowScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 250,
                Content = _rowsPanel,
            };

            var right = new StackPanel { Width = 470 };
            right.Children.Add(_chart);
            right.Children.Add(rowScroll);

            var body = new DockPanel();
            DockPanel.SetDock(_dayList, Dock.Left);
            body.Children.Add(_dayList);
            body.Children.Add(right);

            var root = new StackPanel();
            root.Children.Add(header);
            root.Children.Add(body);

            var border = new Border
            {
                BorderThickness = new Thickness(1.4),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 12),
                Child = root,
            };
            border.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
            border.SetResourceReference(Border.BorderBrushProperty, "OverlayAccentBorderBrush");
            Content = border;

            // 창 어디를 잡아도 옮길 수 있다. 날짜 목록·닫기 버튼은 눌려야 하므로 가로채지 않는다
            PreviewMouseLeftButtonDown += (_, e) =>
            {
                if (e.OriginalSource is DependencyObject source && IsOnButton(source))
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

            Reload();
        }

        /// <summary>창이 열리거나 닫힐 때 (메뉴 바 버튼 표시용).</summary>
        public static event Action<bool>? VisibilityChanged;

        /// <summary>메뉴 버튼: 떠 있으면 닫고, 없으면 연다.</summary>
        public static void Toggle(ExpHuntSessionService service)
        {
            if (_instance != null && _instance.IsLoaded)
            {
                try { _instance.Close(); } catch { }
                return;
            }

            _instance = new ExpHuntRecordWindow(service);
            _instance.Show();
            TopmostWindowHelper.BringToTopmost(_instance);
            try { VisibilityChanged?.Invoke(true); } catch { }
        }

        private void OnSessionsChanged()
            => Dispatcher.BeginInvoke(new Action(Reload));

        /// <summary>파일을 다시 읽어 날짜 목록을 만들고, 고른 날(없으면 가장 최근 날)을 그린다.</summary>
        private void Reload()
        {
            _all = _service.Load().OrderBy(static s => s.StartedAt).ToList();

            var days = _all.Select(static s => s.StartedAt.Date).Distinct().OrderByDescending(static d => d).Take(MaxDays).ToList();
            if (_selectedDay == default || !days.Contains(_selectedDay))
                _selectedDay = days.Count > 0 ? days[0] : DateTime.Today;

            _dayList.Items.Clear();
            foreach (DateTime day in days)
            {
                long total = _all.Where(s => s.StartedAt.Date == day).Sum(static s => s.TotalExp);
                var item = new ListBoxItem
                {
                    Content = $"{day:MM.dd(ddd)}  {FormatExpShort(total)}",
                    Tag = day,
                    Padding = new Thickness(6, 3, 6, 3),
                    ToolTip = $"{day:yyyy-MM-dd} 기록 보기",
                };
                _dayList.Items.Add(item);
                if (day == _selectedDay)
                    _dayList.SelectedItem = item;   // 선택이 바뀌면 SelectionChanged가 그려 준다
            }

            if (_dayList.SelectedItem == null)
                RenderDay();
        }

        /// <summary>고른 날의 판을 막대그래프와 표로 그린다.</summary>
        private void RenderDay()
        {
                    var sessions = _all.Where(s => s.StartedAt.Date == _selectedDay).OrderBy(static s => s.StartedAt).ToList();

            long dayTotal = sessions.Sum(static s => s.TotalExp);
            var dayTime = TimeSpan.FromSeconds(sessions.Sum(static s => s.Duration.TotalSeconds));
            _summaryText.Text = sessions.Count > 0
                ? $"{_selectedDay:yyyy.MM.dd} · {sessions.Count}판 · {FormatDuration(dayTime)} · {FormatExp(dayTotal)}"
                : "기록 없음";

            DrawChart(sessions);

            _rowsPanel.Children.Clear();
            _rowsPanel.Children.Add(MakeRow("시간", "획득 경험치", "마리수", isHeader: true));
            if (sessions.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "3분 넘게 사냥하고 100억 넘게 벌면 한 판으로 남습니다.",
                    Margin = new Thickness(2, 10, 2, 4),
                    FontSize = 12,
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
                _rowsPanel.Children.Add(empty);
                return;
            }

            foreach (ExpHuntSession session in sessions)
            {
                _rowsPanel.Children.Add(MakeRow(
                    $"{session.StartedAt:HH:mm}~{session.EndedAt:HH:mm}",
                    FormatExp(session.TotalExp),
                    $"{session.GainCount:N0}마리",
                    isHeader: false));
            }
        }

        /// <summary>판마다 막대 하나. 높이는 그 날 가장 많이 번 판 기준이고, 막대 색은 시간당으로 구분한다.</summary>
        private void DrawChart(List<ExpHuntSession> sessions)
        {
            _chart.Children.Clear();
            if (sessions.Count == 0)
                return;

            const double labelHeight = 28;
            const double topPad = 13;   // 가장 높은 막대 위에도 숫자가 들어가게 남겨 두는 자리
            double plotHeight = ChartHeight - labelHeight - topPad;
            double available = 470;
            double slot = Math.Min(56, available / sessions.Count);
            double barWidth = Math.Max(8, slot - 8);
            long max = sessions.Max(static s => s.TotalExp);
            long best = max;

            var accent = (Brush)FindResource("OverlayAccentTextBrush");
            var rare = (Brush)FindResource("OverlayRareAccentBrush");

            for (int i = 0; i < sessions.Count; i++)
            {
                ExpHuntSession session = sessions[i];
                double height = max > 0 ? Math.Max(2, plotHeight * session.TotalExp / max) : 2;
                double x = i * slot + (slot - barWidth) / 2;

                // 그 날 가장 많이 번 판은 다른 색으로
                bool isBest = session.TotalExp == best;
                var bar = new Border
                {
                    Width = barWidth,
                    Height = height,
                    CornerRadius = new CornerRadius(3, 3, 0, 0),
                    Background = isBest ? rare : accent,
                    Opacity = isBest ? 0.95 : 0.55,
                    ToolTip = $"{session.StartedAt:HH:mm}~{session.EndedAt:HH:mm} · {FormatDuration(session.Duration)}\n" +
                              $"획득 {FormatExp(session.TotalExp)} · {session.GainCount:N0}마리",
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, topPad + plotHeight - height);
                _chart.Children.Add(bar);

                // 막대 위 획득량은 글자가 겹치지 않을 만큼 넓을 때만. 좁으면 그 날 최고 판에만 적는다
                if (slot >= 52 || isBest)
                {
                    var value = new TextBlock
                    {
                        Text = FormatExp(session.TotalExp),
                        FontSize = 9.5,
                        Width = slot,
                        TextAlignment = TextAlignment.Center,
                    };
                    value.SetResourceReference(TextBlock.ForegroundProperty, isBest ? "OverlayRareAccentBrush" : "OverlayHintTextBrush");
                    Canvas.SetLeft(value, i * slot);
                    Canvas.SetTop(value, topPad + plotHeight - height - 12);
                    _chart.Children.Add(value);
                }

                var time = new TextBlock
                {
                    Text = session.StartedAt.ToString("HH:mm"),
                    FontSize = 9.5,
                    Width = slot,
                    TextAlignment = TextAlignment.Center,
                };
                time.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
                Canvas.SetLeft(time, i * slot);
                Canvas.SetTop(time, topPad + plotHeight + 6);
                _chart.Children.Add(time);
            }

            // 바닥선
            var baseline = new Border { Width = Math.Max(120, sessions.Count * slot), Height = 1, Opacity = 0.3 };
            baseline.SetResourceReference(Border.BackgroundProperty, "TextBrush");
            Canvas.SetLeft(baseline, 0);
            Canvas.SetTop(baseline, topPad + plotHeight);
            _chart.Children.Add(baseline);
        }

        private UIElement MakeRow(string time, string total, string count, bool isHeader)
        {
            var grid = new Grid { Margin = new Thickness(0, isHeader ? 0 : 3, 0, isHeader ? 4 : 0) };
            double[] widths = { 140, 150, 110 };
            foreach (double width in widths)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });

            string[] values = { time, total, count };
            for (int i = 0; i < values.Length; i++)
            {
                var cell = new TextBlock
                {
                    Text = values[i],
                    FontSize = isHeader ? 11 : 13,
                    FontWeight = isHeader ? FontWeights.SemiBold : (i == 1 ? FontWeights.SemiBold : FontWeights.Normal),
                    TextAlignment = i == 0 ? TextAlignment.Left : TextAlignment.Right,
                    Margin = new Thickness(0, 0, 10, 0),
                };
                cell.SetResourceReference(TextBlock.ForegroundProperty,
                    isHeader ? "OverlayHintTextBrush"
                    : i == 1 ? "OverlayRareAccentBrush"
                    : "TextBrush");
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }

            if (isHeader)
            {
                var underline = new Border { Height = 1, Opacity = 0.3, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -3) };
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
