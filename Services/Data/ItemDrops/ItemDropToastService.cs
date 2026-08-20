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
                double top = topBase + ((ToastHeight + Gap) * (ActiveToasts.Count - 1 + HelperOffset()));
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
                double targetTop = topBase + ((ToastHeight + Gap) * (i + HelperOffset()));
                toast.MoveTo(targetTop);
            }
        }

        /// <summary>위치 조정용 도우미 창이 보이는 동안에는 실제 토스트를 한 칸 아래로 밀어 겹치지 않게 한다.</summary>
        private static int HelperOffset()
            => TWChatOverlay.Views.ItemDropHelperWindow.Instance?.IsVisible == true ? 1 : 0;

        private static (double Left, double Top) ResolveBasePosition()
        {
            ChatSettings? settings = ToastPresentationHelper.FindSharedSettings();
            return ToastPresentationHelper.ResolveBasePosition(
                settings?.ItemDropWindowLeft, settings?.ItemDropWindowTop, ToastWidth, DefaultBaseTop);
        }

        private static FontFamily ResolveToastFont() => ToastPresentationHelper.ResolveToastFont();
    }
}
