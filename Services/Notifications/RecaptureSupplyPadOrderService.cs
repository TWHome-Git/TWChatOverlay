using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 보급품 탈환 안에서 경보 장치 해제 문구가 뜨면 발판 색 순서를 알려준다.
    ///
    /// 문구는 경보 장치 해제 성공 6~8초 전에 시스템 메시지로 찍히며, 로그 아홉 달치에서 세 가지만 확인됐다.
    /// 던전 밖에서도 같은 글이 흘러갈 수 있으므로 <see cref="RecaptureSupplyAlertService.IsInRun"/>일 때만 본다.
    /// </summary>
    public static class RecaptureSupplyPadOrderService
    {
        // 설정을 못 읽을 때만 쓴다. 실제 값은 ChatSettings.RecaptureSupplyPadOrderDurationSeconds(기본 10초).
        private const int DefaultDurationSeconds = 10;

        private sealed record PadPattern(Regex Regex, string Phrase, string[] Colors);

        private static readonly PadPattern[] Patterns =
        {
            new(new Regex(@"파란\s*하늘\s*아래\s*개나리\s*한\s*송이와\s*붉은\s*장미", RegexOptions.Compiled),
                "파란 하늘 아래 개나리 한 송이와 붉은 장미", new[] { "파", "노", "빨" }),
            new(new Regex(@"붉은\s*노을이\s*지고\s*칠흑\s*같은\s*어둠이\s*내려앉은\s*바다", RegexOptions.Compiled),
                "붉은 노을이 지고 칠흑 같은 어둠이 내려앉은 바다", new[] { "빨", "검", "파" }),
            new(new Regex(@"하얀\s*종이\s*위에\s*펼쳐져\s*있는\s*푸른\s*바다와\s*달콤한\s*꿀\s*내음", RegexOptions.Compiled),
                "하얀 종이 위에 펼쳐져 있는 푸른 바다와 달콤한 꿀 내음", new[] { "흰", "파", "노" }),
        };

        private static RecaptureSupplyPadOrderWindow? _window;

        public static void Observe(string formattedText)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
                return;
            if (!RecaptureSupplyAlertService.IsInRun)
                return; // 보급품 탈환 진행 중이 아니면 같은 글이 와도 무시한다
            if (TrayAllWindowsService.IsTrayed)
                return; // 트레이 최소화 중에는 알림 창을 띄우지 않는다

            PadPattern? matched = null;
            foreach (PadPattern pattern in Patterns)
            {
                if (pattern.Regex.IsMatch(formattedText))
                {
                    matched = pattern;
                    break;
                }
            }
            if (matched == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                ChatSettings? settings = GetSharedSettings();
                if (settings != null && !settings.ShowRecaptureSupplyPadOrder)
                    return;

                int seconds = settings?.RecaptureSupplyPadOrderDurationSeconds ?? DefaultDurationSeconds;
                EnsureWindow(settings).ShowOrder(matched.Colors, matched.Phrase, seconds);
            }));
        }

        /// <summary>잠금 해제 모드에서 위치를 잡을 수 있게 예시로 띄운다.</summary>
        public static void ShowPositionPreview(ChatSettings settings)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                EnsureWindow(settings).ShowPreview();
            }));
        }

        public static void ClosePositionPreview()
        {
            Close();
        }

        public static void Close()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _window?.Close();
                }
                catch { }
                finally
                {
                    _window = null;
                }
            }));
        }

        private static RecaptureSupplyPadOrderWindow EnsureWindow(ChatSettings? settings)
        {
            if (_window != null && _window.IsLoaded)
                return _window;

            var window = new RecaptureSupplyPadOrderWindow(settings);
            _window = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                    _window = null;
            };

            if (settings?.RecaptureSupplyPadOrderWindowLeft is double left &&
                settings.RecaptureSupplyPadOrderWindowTop is double top)
            {
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Left = left;
                window.Top = top;
            }
            if (settings?.RecaptureSupplyPadOrderWindowWidth is double width && width >= window.MinWidth)
                window.Width = width;
            if (settings?.RecaptureSupplyPadOrderWindowHeight is double height && height >= window.MinHeight)
                window.Height = height;

            return window;
        }

        private static ChatSettings? GetSharedSettings()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow main && main.DataContext is ChatSettings settings)
                        return settings;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to resolve shared settings for recapture supply pad order.", ex);
            }

            return null;
        }
    }
}
