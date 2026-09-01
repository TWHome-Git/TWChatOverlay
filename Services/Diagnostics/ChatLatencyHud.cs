using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 디버그 빌드 전용: 채팅 줄이 화면에 붙는 순간, 로그 타임스탬프 대비 표시 지연을 측정해
    /// 작은 HUD 창으로 보여준다 (최근 / 최근 1분 평균 / 최근 1분 최대).
    /// 게임이 로그에 찍는 시각은 '파일에 쓴 시각'이므로 이 값은 파이프라인 지연의 상한이 아니라
    /// 로그 기록 시점 이후의 앱 지연이다 — 게임 자체의 기록 지연은 포함되지 않는다.
    /// </summary>
    public static class ChatLatencyHud
    {
        private static readonly Regex TimeRegex = new(
            @"\[\s*(?<h>\d{1,2})시\s*(?<m>\d{1,2})분\s*(?<s>\d{1,2})초\s*\]",
            RegexOptions.Compiled);

        private static readonly Queue<(DateTime At, double Seconds)> Samples = new();
        private static readonly object Sync = new();
        private static double _last;
        private static DateTime _lastUiUpdate = DateTime.MinValue;
        private static HudWindow? _window;

        private static double _lastLogStamp = -1;

        /// <summary>
        /// 실시간 줄이 UI에 추가될 때 호출.
        /// 핵심 지표는 앱 지연(파일에서 읽은 순간 → 화면 표시, ms 단위 정밀) —
        /// 로그 타임스탬프 대비 값은 초 단위 양자화 + 게임 기록 지연이 섞여 참고용으로만 보여준다.
        /// </summary>
        public static void Report(string? formattedText, DateTime readAtUtc)
        {
            DateTime nowUtc = DateTime.UtcNow;

            // 앱 파이프라인 지연 (읽기 → 표시)
            if (readAtUtc != default)
            {
                double appSeconds = (nowUtc - readAtUtc).TotalSeconds;
                if (appSeconds >= 0 && appSeconds <= 30)
                {
                    lock (Sync)
                    {
                        _last = appSeconds;
                        Samples.Enqueue((nowUtc, appSeconds));
                        while (Samples.Count > 0 && (nowUtc - Samples.Peek().At).TotalSeconds > 60)
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
                double last, avg = 0, max = 0, lastStamp;
                int count;
                lock (Sync)
                {
                    last = _last;
                    lastStamp = _lastLogStamp;
                    count = Samples.Count;
                    foreach (var (_, s) in Samples)
                    {
                        avg += s;
                        if (s > max) max = s;
                    }
                    if (count > 0) avg /= count;
                }

                if (_window == null || !_window.IsLoaded)
                    _window = new HudWindow();

                string stampText = lastStamp >= 0 ? $"{lastStamp:F1}s" : "-";
                _window.SetText(
                    $"앱 지연(읽기→표시)  최근 {last * 1000:F0}ms · 1분 평균 {avg * 1000:F0}ms · 최대 {max * 1000:F0}ms · n={count}\n" +
                    $"로그 시각 대비 {stampText}  (초 단위 양자화 + 게임 기록 지연 포함 — 참고용)");
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
            private readonly TextBlock _text;

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

                var root = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x10, 0x16, 0x14)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(0xFF, 0x2E, 0x6E, 0x5A)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 4, 8, 5),
                    Child = _text,
                };
                Content = root;
            }

            public void SetText(string text) => _text.Text = text;
        }
    }
}
