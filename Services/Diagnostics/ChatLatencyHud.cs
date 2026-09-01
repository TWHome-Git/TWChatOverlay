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

        /// <summary>실시간 줄이 UI에 추가될 때 호출. 타임스탬프가 없거나 비정상 값이면 무시.</summary>
        public static void Report(string? formattedText)
        {
            if (string.IsNullOrEmpty(formattedText))
                return;

            Match match = TimeRegex.Match(formattedText);
            if (!match.Success)
                return;

            DateTime now = DateTime.Now;
            var logTime = new DateTime(now.Year, now.Month, now.Day,
                int.Parse(match.Groups["h"].Value),
                int.Parse(match.Groups["m"].Value),
                int.Parse(match.Groups["s"].Value));

            // 자정 직후: 로그 시각이 미래로 보이면 전날로 본다
            if (logTime > now.AddSeconds(5))
                logTime = logTime.AddDays(-1);

            double seconds = (now - logTime).TotalSeconds;
            if (seconds < 0 || seconds > 30)
                return; // 백필/시계 이상은 제외

            lock (Sync)
            {
                _last = seconds;
                Samples.Enqueue((now, seconds));
                while (Samples.Count > 0 && (now - Samples.Peek().At).TotalSeconds > 60)
                    Samples.Dequeue();
            }

            // UI 갱신은 250ms 스로틀
            if ((now - _lastUiUpdate).TotalMilliseconds < 250)
                return;
            _lastUiUpdate = now;

            Application.Current?.Dispatcher.BeginInvoke(new Action(UpdateWindow));
        }

        private static void UpdateWindow()
        {
            try
            {
                double last, avg = 0, max = 0;
                int count;
                lock (Sync)
                {
                    last = _last;
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

                _window.SetText($"표시 지연(로그 시각 대비)  최근 {last:F2}s · 1분 평균 {avg:F2}s · 최대 {max:F2}s · n={count}");
                if (!_window.IsVisible)
                    _window.Show();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Chat latency HUD update failed.", ex);
            }
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

                root.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ButtonState == MouseButtonState.Pressed)
                    {
                        try { DragMove(); } catch { }
                    }
                };
            }

            public void SetText(string text) => _text.Text = text;
        }
    }
}
