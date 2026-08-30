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
            if (TrayAllWindowsService.IsTrayed)
                return; // 트레이 최소화 중에는 알림 창을 띄우지 않는다

            if (string.IsNullOrWhiteSpace(itemName))
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var toast = new ItemDropToastWindow(itemName, grade, ResolveToastFont());
                toast.Closed += (_, _) => ActiveToasts.Remove(toast);

                ActiveToasts.Add(toast);

                if (withSound)
                {
                    string soundFile = grade == ItemDropGrade.Normal
                        ? "drop_low.mp3"
                        : "drop.mp3";
                    NotificationService.PlayAlert(soundFile);
                }

                // 통합 알림 스택: 앵커 위치에서 다른 알림들 아래로 배치
                var (left, top) = ToastStackService.Attach(toast);
                toast.ShowAnimated(left, top);
            }));
        }

        private static FontFamily ResolveToastFont() => ToastPresentationHelper.ResolveToastFont();
    }
}
