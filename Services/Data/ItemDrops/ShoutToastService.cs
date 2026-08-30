using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    public static class ShoutToastService
    {
        private static readonly List<ShoutToastWindow> ActiveToasts = new();
        private static ShoutToastWindow? _previewToast; // 프리웜 전용 (표시는 통합 스택 미리보기 사용)

        public static void Show(string formattedText, ChatSettings settings)
        {
            if (TrayAllWindowsService.IsTrayed)
                return; // 트레이 최소화 중에는 알림 창을 띄우지 않는다

            if (string.IsNullOrWhiteSpace(formattedText) || settings == null || !settings.ShowShoutToastPopup)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var toast = new ShoutToastWindow(formattedText, ResolveToastFont(), settings);
                toast.Closed += (_, _) => ActiveToasts.Remove(toast);

                ActiveToasts.Add(toast);

                // 통합 알림 스택: 앵커 위치에서 다른 알림들 아래로 배치
                var (left, top) = ToastStackService.Attach(toast);
                toast.ShowAnimated(left, top, settings.ShoutToastDurationSeconds);
            }));
        }

        public static void Show(LogParser.ParseResult parseResult, ChatSettings settings)
        {
            if (parseResult == null || settings == null || !settings.ShowShoutToastPopup)
                return;

            Show(BuildMessageWithEta(parseResult), settings);
        }

        /// <summary>통합 알림 스택 앵커 미리보기로 위임.</summary>
        public static void ShowPositionPreview(ChatSettings settings, bool force = false)
            => ToastStackService.ShowPositionPreview(settings);

        /// <summary>설정 슬라이더 변경을 열려 있는 토스트(미리보기 포함)에 즉시 반영한다.</summary>
        public static void ApplyFontSize(double size)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                try { _previewToast?.SetFontSize(size); } catch { }
                foreach (var toast in ActiveToasts)
                {
                    try { toast.SetFontSize(size); } catch { }
                }
            }));
        }

        public static void ClosePositionPreview(ChatSettings settings)
            => ToastStackService.ClosePositionPreview();

        public static void SaveCurrentPosition(ChatSettings settings)
            => ToastStackService.SaveCurrentPosition(settings);

        public static void NotifyPreviewPositionChanged()
            => Application.Current.Dispatcher.BeginInvoke(new Action(ToastStackService.Reflow));

        public static ShoutToastWindow? GetOrCreatePreviewWindow(ChatSettings settings)
        {
            if (settings == null)
                return null;

            if (_previewToast == null || !_previewToast.IsLoaded)
            {
                _previewToast = new ShoutToastWindow("외치기 알림창", ResolveToastFont(), settings);
                _previewToast.Closed += (_, _) => { _previewToast = null; };
            }
            else
            {
                _previewToast.SetSettings(settings);
            }

            return _previewToast;
        }

        private static FontFamily ResolveToastFont() => ToastPresentationHelper.ResolveToastFont();

        private static string BuildMessageWithEta(LogParser.ParseResult parseResult)
        {
            string message = parseResult.FormattedText ?? string.Empty;
            string lookupSenderId = parseResult.RawSenderId ?? parseResult.SenderId ?? string.Empty;
            string displaySenderId = parseResult.SenderId ?? parseResult.RawSenderId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(message) || lookupSenderId.Length == 0 || displaySenderId.Length == 0)
                return message;
            if (!EtaProfileResolver.TryGetProfile(lookupSenderId, out var profile))
                return message;

            string suffix = $"[{profile.Level}]";

            if (Regex.IsMatch(message, $@"\[{Regex.Escape(displaySenderId)}\]\s*$"))
            {
                return Regex.Replace(
                    message,
                    $@"\[{Regex.Escape(displaySenderId)}\]\s*$",
                    $"[{displaySenderId}{suffix}]");
            }

            int closingBracketIndex = message.IndexOf(']');
            if (closingBracketIndex < 0 || closingBracketIndex + 1 >= message.Length)
                return message;

            string body = message[(closingBracketIndex + 1)..].TrimStart();
            int colon = body.IndexOf(':');
            if (colon <= 0)
                return message;

            string left = body.Substring(0, colon);
            int idx = left.LastIndexOf(displaySenderId, StringComparison.Ordinal);
            if (idx < 0)
                return message;

            int bodySenderIndex = message.IndexOf(left, StringComparison.Ordinal);
            if (bodySenderIndex < 0)
                return message;

            int insertIndex = bodySenderIndex + idx + displaySenderId.Length;
            return message.Substring(0, insertIndex) + suffix + message.Substring(insertIndex);
        }
    }
}
