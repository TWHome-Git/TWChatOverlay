using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    public static class DungeonCountDisplayWindowService
    {
        private static readonly List<DungeonCountDisplayWindow> ActiveWindows = new();
        private static readonly Dictionary<string, DungeonCountDisplayWindow> ActiveWindowsByKey = new(StringComparer.Ordinal);

        /// <summary>설정 슬라이더 변경을 열려 있는 알림 창(미리보기 포함)에 즉시 반영한다.</summary>
        public static void ApplyFontSize(double size)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var window in ActiveWindows)
                {
                    try { window.SetFontSize(size); } catch { }
                }
            }));
        }

        public static void Show(string dungeonName, int currentCount, int maxCount, int durationSeconds, ChatSettings settings, double? fontSize = null)
        {
            if (TrayAllWindowsService.IsTrayed)
                return; // 트레이 최소화 중에는 알림 창을 띄우지 않는다

            if (string.IsNullOrWhiteSpace(dungeonName))
                return;
            if (settings == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                string message = $"{dungeonName} {currentCount}/{maxCount}";
                if (ActiveWindowsByKey.TryGetValue(dungeonName, out DungeonCountDisplayWindow? existing) &&
                    existing.IsLoaded)
                {
                    existing.SetSettings(settings);
                    if (fontSize.HasValue)
                        existing.SetFontSize(fontSize.Value);
                    existing.UpdateDisplay(message, durationSeconds);
                    return;
                }

                var window = new DungeonCountDisplayWindow(message, ResolveFont(), durationSeconds, settings);
                window.Closed += (_, _) =>
                {
                    ActiveWindows.Remove(window);
                    ActiveWindowsByKey.Remove(dungeonName);
                };

                ActiveWindows.Add(window);
                ActiveWindowsByKey[dungeonName] = window;

                if (fontSize.HasValue)
                    window.SetFontSize(fontSize.Value);

                // 통합 알림 스택: 앵커 위치에서 다른 알림들 아래로 배치
                var (left, top) = ToastStackService.Attach(window);
                window.ShowDisplay(left, top);
            }));
        }

        /// <summary>통합 알림 스택 앵커 미리보기로 위임.</summary>
        public static void ShowPositionPreview(ChatSettings settings, bool force = false)
        {
            if (settings == null || (!force && !settings.ShowDungeonCountDisplayWindow))
                return;

            ToastStackService.ShowPositionPreview(settings);
        }

        public static void ClosePositionPreview(ChatSettings settings)
            => ToastStackService.ClosePositionPreview();

        public static void SaveCurrentPosition(ChatSettings settings)
            => ToastStackService.SaveCurrentPosition(settings);

        private static FontFamily ResolveFont() => ToastPresentationHelper.ResolveToastFont();
    }
}
