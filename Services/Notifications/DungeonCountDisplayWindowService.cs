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
        private const double DisplayHeight = 76;
        private const double TopOffset = 124;
        private const double Gap = 8;
        private const string PositionPreviewKey = "__dungeon_count_position_preview";
        private static readonly List<DungeonCountDisplayWindow> ActiveWindows = new();
        private static readonly Dictionary<string, DungeonCountDisplayWindow> ActiveWindowsByKey = new(StringComparer.Ordinal);

        public static void Show(string dungeonName, int currentCount, int maxCount, int durationSeconds, ChatSettings settings)
        {
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
                    existing.UpdateDisplay(message, durationSeconds);
                    return;
                }

                var window = new DungeonCountDisplayWindow(message, ResolveFont(), durationSeconds, settings);
                window.Closed += (_, _) =>
                {
                    ActiveWindows.Remove(window);
                    ActiveWindowsByKey.Remove(dungeonName);
                    Rearrange();
                };

                ActiveWindows.Add(window);
                ActiveWindowsByKey[dungeonName] = window;

                var (left, topBase) = ResolveBasePosition(settings);
                double top = topBase + ((DisplayHeight + Gap) * (ActiveWindows.Count - 1));
                window.ShowDisplay(left, top);
            }));
        }

        public static void ShowPositionPreview(ChatSettings settings)
        {
            if (settings == null || !settings.ShowDungeonCountDisplayWindow)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                string message = "던전 카운터 위치";
                if (ActiveWindowsByKey.TryGetValue(PositionPreviewKey, out DungeonCountDisplayWindow? existing) &&
                    existing.IsLoaded)
                {
                    existing.SetSettings(settings);
                    existing.UpdateDisplay(message, durationSeconds: 0);
                    return;
                }

                var window = new DungeonCountDisplayWindow(message, ResolveFont(), durationSeconds: 0, settings);
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
                if (!settings.ShowDungeonCountDisplayWindow)
                    return;

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
        {
            if (settings.DungeonCountDisplayWindowLeft.HasValue &&
                settings.DungeonCountDisplayWindowTop.HasValue)
            {
                return (settings.DungeonCountDisplayWindowLeft.Value, settings.DungeonCountDisplayWindowTop.Value);
            }

            Rect area = SystemParameters.WorkArea;
            return (area.Left + ((area.Width - DisplayWidth) / 2.0), area.Top + TopOffset);
        }

        private static FontFamily ResolveFont()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow mainWindow)
                        return mainWindow.CurrentFont;
                }
            }
            catch { }

            try
            {
                ChatSettings settings = ConfigService.Load();
                return FontService.GetFont(settings.FontFamily);
            }
            catch { }

            return new FontFamily("Malgun Gothic");
        }
    }
}
