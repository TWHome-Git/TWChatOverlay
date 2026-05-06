using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    public static class MessengerEtaToastService
    {
        private static readonly Dictionary<string, MessengerEtaToastWindow> FileWindows = new(StringComparer.OrdinalIgnoreCase);
        private static MessengerEtaToastWindow? _previewToast;
        private const double ToastWidth = 420;
        private const double DefaultBaseTop = 42;
        private const double Gap = 8;

        public static void ShowForFile(string filePath, IReadOnlyList<string> entries, ChatSettings settings)
        {
            if (string.IsNullOrWhiteSpace(filePath) || entries.Count == 0)
                return;

            if (Application.Current?.Dispatcher == null)
            {
                AppLogger.Warn("Messenger toast skipped because Application.Current.Dispatcher was unavailable.");
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    AppLogger.Info($"Messenger toast render requested. File={filePath}, EntryCount={entries.Count}");
                    if (!FileWindows.TryGetValue(filePath, out MessengerEtaToastWindow? window) || !window.IsLoaded)
                    {
                        window = new MessengerEtaToastWindow(ResolveToastFont(), settings);
                        string key = filePath;
                        window.Closed += (_, _) =>
                        {
                            if (FileWindows.TryGetValue(key, out var current) && ReferenceEquals(current, window))
                                FileWindows.Remove(key);
                            RearrangeWindows(settings);
                        };
                        FileWindows[filePath] = window;
                        AppLogger.Info($"Messenger toast window created. File={filePath}");
                    }

                    window.SetEntries(entries);
                    RearrangeWindows(settings);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Messenger toast render failed inside dispatcher.", ex);
                }
            });
        }

        public static void ShowPositionPreview(ChatSettings settings, bool force = false)
        {
            if (settings == null || (!force && settings.MessengerToastWindowLeft == null && settings.MessengerToastWindowTop == null))
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_previewToast == null || !_previewToast.IsLoaded)
                {
                    _previewToast = new MessengerEtaToastWindow(ResolveToastFont(), settings);
                    _previewToast.Closed += (_, _) => _previewToast = null;
                }

                _previewToast.SetEntries(new[] { "아이디1 [285]", "아이디2 [정보 없음]" });
                _previewToast.SetPreviewMode(true);
                var (left, topBase) = ResolveBasePositionFromSettings(settings);
                _previewToast.ShowAt(left, topBase);
            }));
        }

        public static void ClosePositionPreview(ChatSettings settings)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_previewToast == null)
                    return;

                _previewToast.SaveCurrentPosition();
                _previewToast.Close();
                _previewToast = null;
            }));
        }

        public static void SaveCurrentPosition(ChatSettings settings)
        {
            if (_previewToast?.IsVisible == true)
                _previewToast.SaveCurrentPosition();
            ConfigService.SaveDeferred(settings);
        }


        private static void RearrangeWindows(ChatSettings settings)
        {
            var alive = FileWindows.Values.ToList();
            var (left, baseTop) = ResolveBasePositionFromSettings(settings);
            var area = SystemParameters.WorkArea;
            for (int i = 0; i < alive.Count; i++)
            {
                MessengerEtaToastWindow window = alive[i];
                double top = baseTop + (i * (window.Height + Gap));
                double clampedLeft = Math.Max(area.Left, Math.Min(left, area.Right - window.Width));
                double clampedTop = Math.Max(area.Top, Math.Min(top, area.Bottom - window.Height));
                window.SetPreviewMode(false);
                window.ShowAt(clampedLeft, clampedTop);
                AppLogger.Info($"Messenger toast window shown. Index={i}, Left={clampedLeft:0.##}, Top={clampedTop:0.##}, Width={window.Width:0.##}, Height={window.Height:0.##}");
            }
        }

        private static (double Left, double Top) ResolveBasePositionFromSettings(ChatSettings settings)
        {
            if (settings.MessengerToastWindowLeft.HasValue && settings.MessengerToastWindowTop.HasValue)
                return (settings.MessengerToastWindowLeft.Value, settings.MessengerToastWindowTop.Value);

            var area = SystemParameters.WorkArea;
            return (area.Left + (area.Width - ToastWidth) / 2, DefaultBaseTop);
        }

        private static FontFamily ResolveToastFont()
        {
            try
            {
                MainWindow? main = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                if (main != null && main.CurrentFont != null && !string.IsNullOrWhiteSpace(main.CurrentFont.Source))
                    return main.CurrentFont;
            }
            catch { }

            try
            {
                ChatSettings settings = ConfigService.Load();
                FontFamily configured = FontService.GetFont(settings.FontFamily);
                if (configured != null && !string.IsNullOrWhiteSpace(configured.Source))
                    return configured;
            }
            catch { }

            return new FontFamily("Malgun Gothic");
        }
    }
}
