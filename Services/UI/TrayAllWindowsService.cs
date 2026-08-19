using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 앱의 모든 창을 한 번에 트레이로 숨기고 다시 복원합니다.
    /// 숨길 당시 보이던 창만 기억해 두었다가 복원하므로, 원래 닫혀 있던 창은 다시 뜨지 않습니다.
    /// </summary>
    public static class TrayAllWindowsService
    {
        private static readonly List<WeakReference<Window>> _hiddenWindows = new();
        private static readonly object _lock = new();

        public static bool IsTrayed { get; private set; }

        public static event Action<bool>? TrayStateChanged;

        public static void Toggle()
        {
            if (IsTrayed) RestoreAll();
            else HideAll();
        }

        public static void HideAll()
        {
            var app = Application.Current;
            if (app == null) return;

            app.Dispatcher.Invoke(() =>
            {
                lock (_lock)
                {
                    _hiddenWindows.Clear();
                    foreach (Window w in app.Windows.Cast<Window>().ToList())
                    {
                        try
                        {
                            if (!w.IsVisible) continue;
                            _hiddenWindows.Add(new WeakReference<Window>(w));
                            w.Hide();
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Failed to hide window '{w.GetType().Name}' for tray.", ex);
                        }
                    }
                    IsTrayed = true;
                }
                AppLogger.Info($"All windows hidden to tray ({_hiddenWindows.Count}).");
                TrayStateChanged?.Invoke(true);
            });
        }

        public static void RestoreAll()
        {
            var app = Application.Current;
            if (app == null) return;

            app.Dispatcher.Invoke(() =>
            {
                lock (_lock)
                {
                    foreach (var reference in _hiddenWindows)
                    {
                        if (!reference.TryGetTarget(out var w)) continue;
                        try
                        {
                            w.Show();
                            if (w.WindowState == WindowState.Minimized)
                                w.WindowState = WindowState.Normal;
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Failed to restore window '{w.GetType().Name}' from tray.", ex);
                        }
                    }
                    _hiddenWindows.Clear();
                    IsTrayed = false;
                }
                AppLogger.Info("All windows restored from tray.");
                TrayStateChanged?.Invoke(false);
            });
        }
    }
}
