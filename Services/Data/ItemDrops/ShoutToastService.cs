using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    public static class ShoutToastService
    {
        private static readonly List<ShoutToastWindow> ActiveToasts = new();
        private static ShoutToastWindow? _previewToast;
        private const double ToastWidth = 420;
        private const double DefaultBaseTop = 124;
        private const double Gap = 6;
        private const double ToastHeight = 72;

        public static void Show(string formattedText, ChatSettings settings)
        {
            if (string.IsNullOrWhiteSpace(formattedText) || settings == null || !settings.ShowShoutToastPopup)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var toast = new ShoutToastWindow(formattedText, ResolveToastFont(), settings);
                toast.Closed += (_, _) =>
                {
                    ActiveToasts.Remove(toast);
                    RearrangeToasts();
                };

                ActiveToasts.Add(toast);

                var (left, topBase) = ResolveBasePosition(settings);
                int previewOffset = _previewToast?.IsVisible == true ? 1 : 0;
                double top = topBase + ((ToastHeight + Gap) * (ActiveToasts.Count - 1 + previewOffset));
                toast.ShowAnimated(left, top, settings.ShoutToastDurationSeconds);
            }));
        }

        public static void ShowPositionPreview(ChatSettings settings, bool force = false)
        {
            if (settings == null || (!force && settings.ShoutToastWindowLeft == null && settings.ShoutToastWindowTop == null))
            {
                return;
            }

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_previewToast == null || !_previewToast.IsLoaded)
                {
                    _previewToast = new ShoutToastWindow("외치기 토스트 위치", ResolveToastFont(), settings);
                    _previewToast.Closed += (_, _) =>
                    {
                        _previewToast = null;
                        RearrangeToasts();
                    };
                }
                else
                {
                    _previewToast.SetSettings(settings);
                    _previewToast.SetMessage("외치기 토스트 위치");
                }

                _previewToast.SetPreviewMode(true);

                var (left, topBase) = ResolveBasePosition(settings);
                _previewToast.ShowPreview(left, topBase);
                RearrangeToasts();
            }));
        }

        public static void ClosePositionPreview(ChatSettings settings)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_previewToast == null)
                    return;

                try
                {
                    if (_previewToast.IsVisible)
                    {
                        _previewToast.SaveCurrentPosition();
                        _previewToast.Close();
                    }
                }
                catch { }
                finally
                {
                    _previewToast = null;
                    RearrangeToasts();
                }
            }));
        }

        public static void SaveCurrentPosition(ChatSettings settings)
        {
            if (settings == null)
                return;

            void Save()
            {
                if (_previewToast?.IsVisible == true)
                {
                    _previewToast.SaveCurrentPosition();
                }
                else if (settings.ShoutToastWindowLeft.HasValue && settings.ShoutToastWindowTop.HasValue)
                {
                    ConfigService.Save(settings);
                }
            }

            if (Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                Save();
            }
            else
            {
                Application.Current?.Dispatcher?.Invoke(new Action(Save));
            }
        }

        public static void NotifyPreviewPositionChanged()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(RearrangeToasts));
        }

        private static void RearrangeToasts()
        {
            var basePosition = ResolveBasePositionFromSharedSettings();
            double topBase = basePosition.Top;
            int previewOffset = _previewToast?.IsVisible == true ? 1 : 0;

            for (int i = 0; i < ActiveToasts.Count; i++)
            {
                var toast = ActiveToasts[i];
                if (!toast.IsVisible)
                    continue;

                double targetTop = topBase + ((ToastHeight + Gap) * (i + previewOffset));
                toast.MoveTo(targetTop);
            }

            if (_previewToast?.IsVisible == true)
            {
                _previewToast.MoveTo(topBase);
            }
        }

        private static (double Left, double Top) ResolveBasePosition(ChatSettings settings)
        {
            if (settings.ShoutToastWindowLeft.HasValue && settings.ShoutToastWindowTop.HasValue)
                return (settings.ShoutToastWindowLeft.Value, settings.ShoutToastWindowTop.Value);

            var area = SystemParameters.WorkArea;
            return (area.Left + (area.Width - ToastWidth) / 2, DefaultBaseTop);
        }

        private static (double Left, double Top) ResolveBasePositionFromSharedSettings()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow mainWindow && mainWindow.DataContext is ChatSettings settings)
                        return ResolveBasePosition(settings);
                }
            }
            catch { }

            var area = SystemParameters.WorkArea;
            return (area.Left + (area.Width - ToastWidth) / 2, DefaultBaseTop);
        }

        private static FontFamily ResolveToastFont()
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
                var settings = ConfigService.Load();
                return FontService.GetFont(settings.FontFamily);
            }
            catch { }

            return new FontFamily("Malgun Gothic");
        }
    }
}
