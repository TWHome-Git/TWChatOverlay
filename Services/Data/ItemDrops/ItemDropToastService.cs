using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    public enum ItemDropGrade
    {
        Normal,
        Rare,
        Special
    }

    public static class ItemDropToastService
    {
        private static readonly List<ItemDropToastWindow> ActiveToasts = new();
        private const double ToastWidth = 420;
        private const double DefaultBaseTop = 42;
        private const double Gap = 6;
        private const double ToastHeight = 56;

        public static void Show(string itemName, ItemDropGrade grade = ItemDropGrade.Normal, bool withSound = true)
        {
            if (string.IsNullOrWhiteSpace(itemName))
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var toast = new ItemDropToastWindow(itemName, grade, ResolveToastFont());
                toast.Closed += (_, _) =>
                {
                    ActiveToasts.Remove(toast);
                    RearrangeToasts();
                };

                ActiveToasts.Add(toast);

                if (withSound)
                {
                    string soundFile = grade == ItemDropGrade.Normal
                        ? "drop_low.mp3"
                        : "drop.mp3";
                    NotificationService.PlayAlert(soundFile);
                }

                var (left, topBase) = ResolveBasePosition();
                double top = topBase + ((ToastHeight + Gap) * (ActiveToasts.Count - 1));
                toast.ShowAnimated(left, top);
            }));
        }

        private static void RearrangeToasts()
        {
            for (int i = 0; i < ActiveToasts.Count; i++)
            {
                var toast = ActiveToasts[i];
                if (!toast.IsVisible)
                    continue;

                var (_, topBase) = ResolveBasePosition();
                double targetTop = topBase + ((ToastHeight + Gap) * i);
                toast.MoveTo(targetTop);
            }
        }

        private static (double Left, double Top) ResolveBasePosition()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow mainWindow && mainWindow.DataContext is ChatSettings settings)
                    {
                        if (settings.ItemDropWindowLeft.HasValue && settings.ItemDropWindowTop.HasValue)
                            return (settings.ItemDropWindowLeft.Value, settings.ItemDropWindowTop.Value);
                    }
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
