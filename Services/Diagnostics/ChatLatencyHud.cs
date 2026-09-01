using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 디버그 빌드 전용: 채팅 줄이 화면에 붙는 순간의 앱 파이프라인 지연(파일 읽기 → 표시)을
    /// ms 단위로 측정해 메인 채팅창 상단 HUD로 보여준다. 클릭하면 최근 5분 그래프(100ms 기준선)를 접고 편다.
    /// 로그 타임스탬프 대비 값은 초 단위 양자화 + 게임 기록 지연이 섞여 참고용으로만 표시한다.
    /// </summary>
    public static class ChatLatencyHud
    {
        private static readonly Regex TimeRegex = new(
            @"\[\s*(?<h>\d{1,2})시\s*(?<m>\d{1,2})분\s*(?<s>\d{1,2})초\s*\]",
            RegexOptions.Compiled);

        private const double GraphWindowSeconds = 300; // 그래프 표시 범위: 최근 5분
        private const double StatsWindowSeconds = 60;  // 텍스트 통계 범위: 최근 1분

        private static readonly Queue<(DateTime At, double Seconds, double UiSeconds)> Samples = new();
        private static readonly object Sync = new();
        private static double _last;
        private static double _lastUi;
        private static double _lastLogStamp = -1;
        private static DateTime _lastUiUpdate = DateTime.MinValue;
        private static HudWindow? _window;

        /// <summary>
        /// 실시간 줄이 UI에 추가될 때 호출.
        /// 핵심 지표는 앱 지연(파일에서 읽은 순간 → 화면 표시, ms 단위 정밀).
        /// </summary>
        public static void Report(string? formattedText, DateTime readAtUtc, DateTime analyzedAtUtc = default)
        {
            DateTime nowUtc = DateTime.UtcNow;

            // 앱 파이프라인 지연 (읽기 → 표시), UI 구간(분석 완료 → 표시) 분해 포함
            if (readAtUtc != default)
            {
                double appSeconds = (nowUtc - readAtUtc).TotalSeconds;
                double uiSeconds = analyzedAtUtc != default ? (nowUtc - analyzedAtUtc).TotalSeconds : 0;
                if (appSeconds >= 0 && appSeconds <= 30)
                {
                    lock (Sync)
                    {
                        _last = appSeconds;
                        _lastUi = Math.Max(0, uiSeconds);
                        Samples.Enqueue((nowUtc, appSeconds, Math.Max(0, uiSeconds)));
                        while (Samples.Count > 0 && (nowUtc - Samples.Peek().At).TotalSeconds > GraphWindowSeconds)
                            Samples.Dequeue();
                    }
                }
            }

            // 참고용: 로그 타임스탬프 대비 (초 단위라 0~1초 오차 내재)
            if (!string.IsNullOrEmpty(formattedText))
            {
                Match match = TimeRegex.Match(formattedText);
                if (match.Success)
                {
                    DateTime now = DateTime.Now;
                    var logTime = new DateTime(now.Year, now.Month, now.Day,
                        int.Parse(match.Groups["h"].Value),
                        int.Parse(match.Groups["m"].Value),
                        int.Parse(match.Groups["s"].Value));
                    if (logTime > now.AddSeconds(5))
                        logTime = logTime.AddDays(-1);

                    double stamp = (now - logTime).TotalSeconds;
                    if (stamp >= 0 && stamp <= 30)
                        _lastLogStamp = stamp;
                }
            }

            // UI 갱신은 250ms 스로틀
            if ((nowUtc - _lastUiUpdate).TotalMilliseconds < 250)
                return;
            _lastUiUpdate = nowUtc;

            Application.Current?.Dispatcher.BeginInvoke(new Action(UpdateWindow));
        }

        private static void UpdateWindow()
        {
            try
            {
                DateTime nowUtc = DateTime.UtcNow;
                double last, lastUi, lastStamp, avg = 0, max = 0, uiAvg = 0, uiMax = 0;
                int count = 0;
                List<(DateTime At, double Seconds, double UiSeconds)> snapshot;
                lock (Sync)
                {
                    last = _last;
                    lastUi = _lastUi;
                    lastStamp = _lastLogStamp;
                    snapshot = Samples.ToList();
                }

                foreach (var (at, s, ui) in snapshot)
                {
                    if ((nowUtc - at).TotalSeconds > StatsWindowSeconds)
                        continue;
                    count++;
                    avg += s;
                    uiAvg += ui;
                    if (s > max) max = s;
                    if (ui > uiMax) uiMax = ui;
                }
                if (count > 0) { avg /= count; uiAvg /= count; }

                if (_window == null || !_window.IsLoaded)
                    _window = new HudWindow();

                string stampText = lastStamp >= 0 ? $"{lastStamp:F1}s" : "-";
                _window.SetText(
                    $"앱 지연(읽기→표시)  최근 {last * 1000:F0}ms (읽기·분석 {(last - lastUi) * 1000:F0} + UI {lastUi * 1000:F0}) · 1분 평균 {avg * 1000:F0}ms · 최대 {max * 1000:F0}ms\n" +
                    $"UI 구간 1분 평균 {uiAvg * 1000:F0}ms · 최대 {uiMax * 1000:F0}ms · n={count}   |   로그 시각 대비 {stampText} (참고용)");
                _window.DrawGraph(snapshot, nowUtc);
                if (!_window.IsVisible)
                    _window.Show();

                PositionAtMainWindowTop(_window);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Chat latency HUD update failed.", ex);
            }
        }

        /// <summary>메인 채팅창 맨 윗줄에 도킹한다 (위 공간이 없으면 창 안쪽 상단).</summary>
        private static void PositionAtMainWindowTop(HudWindow hud)
        {
            try
            {
                var main = MainWindowHost.Current as Window;
                if (main == null || !main.IsVisible)
                    return;

                hud.UpdateLayout();
                double hudHeight = hud.ActualHeight > 0 ? hud.ActualHeight : hud.Height;

                hud.Left = main.Left + 6;
                double above = main.Top - hudHeight - 2;
                hud.Top = above >= SystemParameters.VirtualScreenTop ? above : main.Top + 4;
            }
            catch { }
        }

        private sealed class HudWindow : Window
        {
            private const double GraphWidth = 420;
            private const double GraphHeight = 64;

            private readonly TextBlock _text;
            private readonly Canvas _graph;
            private readonly Border _graphHost;
            private readonly Polyline _line;
            private readonly Line _guide100;
            private readonly TextBlock _guideLabel;
            private readonly TextBlock _scaleLabel;

            public HudWindow()
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                SizeToContent = SizeToContent.WidthAndHeight;
                Left = 8;
                Top = 8;

                _text = new TextBlock
                {
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0xF0, 0xC8)),
                    FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                };

                // ── 최근 5분 지연 그래프 ──
                _graph = new Canvas
                {
                    Width = GraphWidth,
                    Height = GraphHeight,
                    Background = new SolidColorBrush(Color.FromArgb(0x66, 0x0A, 0x0F, 0x0D)),
                    ClipToBounds = true,
                };

                _guide100 = new Line
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0x5A, 0x5A)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 3, 3 },
                    X1 = 0,
                    X2 = GraphWidth,
                };
                _guideLabel = new TextBlock
                {
                    Text = "100ms",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0x5A, 0x5A)),
                };
                _scaleLabel = new TextBlock
                {
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xA0, 0x9E, 0xF0, 0xC8)),
                };
                _line = new Polyline
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(0x9E, 0xF0, 0xC8)),
                    StrokeThickness = 1.4,
                    StrokeLineJoin = PenLineJoin.Round,
                };

                _graph.Children.Add(_guide100);
                _graph.Children.Add(_line);
                _graph.Children.Add(_guideLabel);
                _graph.Children.Add(_scaleLabel);

                _graphHost = new Border
                {
                    Margin = new Thickness(0, 5, 0, 0),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, 0x2E, 0x6E, 0x5A)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Child = _graph,
                };

                var stack = new StackPanel();
                stack.Children.Add(_text);
                stack.Children.Add(_graphHost);

                var root = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x10, 0x16, 0x14)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x2E, 0x6E, 0x5A)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 6),
                    Child = stack,
                };
                Content = root;

                // 클릭으로 그래프 접기/펼치기
                root.MouseLeftButtonDown += (_, _) =>
                {
                    _graphHost.Visibility = _graphHost.Visibility == Visibility.Visible
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                };
            }

            public void SetText(string text) => _text.Text = text;

            /// <summary>최근 5분 표본을 폴리라인으로 그린다. 세로축은 관측 최대(최소 120ms)에 맞춰 스케일.</summary>
            public void DrawGraph(IReadOnlyList<(DateTime At, double Seconds, double UiSeconds)> samples, DateTime nowUtc)
            {
                if (_graphHost.Visibility != Visibility.Visible)
                    return;

                double maxSeconds = 0.12; // 최소 눈금 120ms — 100ms 기준선이 항상 보이게
                foreach (var (_, s, _) in samples)
                {
                    if (s > maxSeconds) maxSeconds = s;
                }
                maxSeconds *= 1.1;

                var points = new PointCollection();
                foreach (var (at, s, _) in samples)
                {
                    double age = (nowUtc - at).TotalSeconds;
                    if (age < 0 || age > GraphWindowSeconds)
                        continue;

                    double x = GraphWidth * (1 - age / GraphWindowSeconds);
                    double y = GraphHeight * (1 - Math.Min(s, maxSeconds) / maxSeconds);
                    points.Add(new Point(x, y));
                }
                _line.Points = points;

                double guideY = GraphHeight * (1 - 0.1 / maxSeconds);
                _guide100.Y1 = guideY;
                _guide100.Y2 = guideY;
                Canvas.SetLeft(_guideLabel, 2);
                Canvas.SetTop(_guideLabel, Math.Max(0, guideY - 12));

                _scaleLabel.Text = $"↑{maxSeconds * 1000:F0}ms · ←5분";
                Canvas.SetLeft(_scaleLabel, GraphWidth - 78);
                Canvas.SetTop(_scaleLabel, 1);
            }
        }
    }
}
