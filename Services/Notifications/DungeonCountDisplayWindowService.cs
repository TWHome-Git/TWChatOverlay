using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    public sealed class DungeonCountDisplayWindowService
    {
        private readonly List<DungeonCountDisplayWindow> ActiveWindows = new();
        private readonly Dictionary<string, DungeonCountDisplayWindow> ActiveWindowsByKey = new(StringComparer.Ordinal);

        /// <summary>설정 슬라이더 변경을 열려 있는 알림 창(미리보기 포함)에 즉시 반영한다.</summary>
        public void ApplyFontSize(double size)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var window in ActiveWindows)
                {
                    try { window.SetFontSize(size); } catch { }
                }
            }));
        }

        public void Show(string dungeonName, int currentCount, int maxCount, int durationSeconds, ChatSettings settings, double? fontSize = null)
            => ShowMessage(dungeonName, $"{dungeonName} {currentCount}/{maxCount}", durationSeconds, settings, fontSize);

        /// <summary>N/최대 형식이 아닌 자유 문구용 (예: 심연의 보물창고 금화 주머니 카운트). iconUri는 메시지 왼쪽 아이콘.</summary>
        public void ShowMessage(string dungeonName, string message, int durationSeconds, ChatSettings settings, double? fontSize = null, string? iconUri = null)
        {
            if (AppServices.Get<TrayAllWindowsService>().IsTrayed)
                return; // 트레이 최소화 중에는 알림 창을 띄우지 않는다

            if (string.IsNullOrWhiteSpace(dungeonName))
                return;
            if (settings == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (ActiveWindowsByKey.TryGetValue(dungeonName, out DungeonCountDisplayWindow? existing) &&
                    existing.IsLoaded)
                {
                    existing.SetSettings(settings);
                    if (fontSize.HasValue)
                        existing.SetFontSize(fontSize.Value);
                    existing.SetIcon(iconUri);
                    existing.UpdateDisplay(message, durationSeconds);
                    return;
                }

                var window = new DungeonCountDisplayWindow(message, ResolveFont(), durationSeconds, settings);
                window.SetIcon(iconUri);
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
                var (left, top) = AppServices.Get<ToastStackService>().Attach(window);
                window.ShowDisplay(left, top);
            }));
        }

        /// <summary>통합 알림 스택 앵커 미리보기로 위임.</summary>
        public void ShowPositionPreview(ChatSettings settings, bool force = false)
        {
            if (settings == null || (!force && !settings.ShowDungeonCountDisplayWindow))
                return;

            AppServices.Get<ToastStackService>().ShowPositionPreview(settings);
        }

        public void ClosePositionPreview(ChatSettings settings)
            => AppServices.Get<ToastStackService>().ClosePositionPreview();

        public void SaveCurrentPosition(ChatSettings settings)
            => AppServices.Get<ToastStackService>().SaveCurrentPosition(settings);

        private FontFamily ResolveFont() => ToastPresentationHelper.ResolveToastFont();
    }
}
