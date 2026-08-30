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
        private const double DisplayWidth = 360;
        private const double DisplayHeight = 72;
        private const double TopOffset = 124;
        private const double Gap = 8;
        private const string PositionPreviewKey = "__dungeon_count_position_preview";
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

                // 실제 알림창이 뜨면 미리보기는 중복이므로 먼저 치운다
                RemovePositionPreview();

                var window = new DungeonCountDisplayWindow(message, ResolveFont(), durationSeconds, settings);
                window.Closed += (_, _) =>
                {
                    ActiveWindows.Remove(window);
                    ActiveWindowsByKey.Remove(dungeonName);
                    Rearrange();

                    // 잠금 해제 중에 실제 알림이 사라지면 옮길 대상이 없어지므로 미리보기를 되살린다
                    if (UiLockService.IsUnlocked)
                        ShowPositionPreview(settings, force: true);
                };

                ActiveWindows.Add(window);
                ActiveWindowsByKey[dungeonName] = window;

                if (fontSize.HasValue)
                    window.SetFontSize(fontSize.Value);

                var (left, topBase) = ResolveBasePosition(settings);
                double top = topBase + ((DisplayHeight + Gap) * (ActiveWindows.Count - 1));
                window.ShowDisplay(left, top);
            }));
        }

        public static void ShowPositionPreview(ChatSettings settings, bool force = false)
        {
            if (settings == null || (!force && !settings.ShowDungeonCountDisplayWindow))
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 실제 알림창이 떠 있으면 그 창을 직접 옮기면 되므로 미리보기를 띄우지 않는다
                if (HasVisibleLiveWindow())
                {
                    RemovePositionPreview();
                    return;
                }

                string message = "던전 카운트 알림창";
                if (ActiveWindowsByKey.TryGetValue(PositionPreviewKey, out DungeonCountDisplayWindow? existing) &&
                    existing.IsLoaded)
                {
                    existing.SetSettings(settings);
                    existing.UpdateDisplay(message, durationSeconds: 0);
                    existing.SetPreviewMode(true);
                    return;
                }

                var window = new DungeonCountDisplayWindow(message, ResolveFont(), durationSeconds: 0, settings);
                window.SetPreviewMode(true);
                window.Closed += (_, _) =>
                {
                    ActiveWindows.Remove(window);
                    ActiveWindowsByKey.Remove(PositionPreviewKey);
                    Rearrange();
                };

                ActiveWindows.Add(window);
                ActiveWindowsByKey[PositionPreviewKey] = window;

                var (left, topBase) = ResolveBasePosition(settings);
                double top = topBase + ((DisplayHeight + Gap) * (ActiveWindows.Count - 1));
                window.ShowDisplay(left, top);
            }));
        }

        public static void ClosePositionPreview(ChatSettings settings)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (settings != null)
                {
                    foreach (DungeonCountDisplayWindow activeWindow in ActiveWindows)
                    {
                        if (activeWindow.IsLoaded)
                            activeWindow.SetSettings(settings);
                    }
                }

                if (!ActiveWindowsByKey.TryGetValue(PositionPreviewKey, out DungeonCountDisplayWindow? window))
                    return;

                if (window.IsVisible)
                    window.Close();

                ActiveWindowsByKey.Remove(PositionPreviewKey);
                ActiveWindows.Remove(window);
                Rearrange();
            }));
        }

        public static void SaveCurrentPosition(ChatSettings settings)
        {
            if (settings == null)
                return;

            Application.Current.Dispatcher.Invoke(() =>
            {
                if (ActiveWindowsByKey.TryGetValue(PositionPreviewKey, out DungeonCountDisplayWindow? preview) &&
                    preview.IsVisible)
                {
                    settings.DungeonCountDisplayWindowLeft = preview.Left;
                    settings.DungeonCountDisplayWindowTop = preview.Top;
                    return;
                }

                foreach (DungeonCountDisplayWindow window in ActiveWindows)
                {
                    if (!window.IsVisible)
                        continue;

                    settings.DungeonCountDisplayWindowLeft = window.Left;
                    settings.DungeonCountDisplayWindowTop = window.Top;
                    break;
                }
            });

            ConfigService.Save(settings);
        }

        /// <summary>미리보기가 아닌 실제 던전 카운트 알림창이 화면에 떠 있는지.</summary>
        private static bool HasVisibleLiveWindow()
        {
            ActiveWindowsByKey.TryGetValue(PositionPreviewKey, out DungeonCountDisplayWindow? preview);

            foreach (DungeonCountDisplayWindow window in ActiveWindows)
            {
                if (!ReferenceEquals(window, preview) && window.IsVisible)
                    return true;
            }

            return false;
        }

        /// <summary>자리 잡기용 미리보기 창을 닫고 목록에서 제거한다.</summary>
        private static void RemovePositionPreview()
        {
            if (!ActiveWindowsByKey.TryGetValue(PositionPreviewKey, out DungeonCountDisplayWindow? preview))
                return;

            ActiveWindowsByKey.Remove(PositionPreviewKey);
            ActiveWindows.Remove(preview);

            if (preview.IsVisible)
                preview.Close();

            Rearrange();
        }

        private static void Rearrange()
        {
            Rect area = SystemParameters.WorkArea;
            double topBase = area.Top + TopOffset;

            for (int i = 0; i < ActiveWindows.Count; i++)
            {
                DungeonCountDisplayWindow window = ActiveWindows[i];
                if (!window.IsVisible)
                    continue;

                window.MoveTo(topBase + ((DisplayHeight + Gap) * i));
            }
        }

        private static (double Left, double Top) ResolveBasePosition(ChatSettings settings)
            => ToastPresentationHelper.ResolveBasePosition(
                settings.DungeonCountDisplayWindowLeft,
                settings.DungeonCountDisplayWindowTop,
                DisplayWidth,
                SystemParameters.WorkArea.Top + TopOffset);

        private static FontFamily ResolveFont() => ToastPresentationHelper.ResolveToastFont();
    }
}
