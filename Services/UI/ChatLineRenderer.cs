using System;
using System.Collections.Generic;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 채팅 한 줄을 표시 영역(<see cref="ChatLineDisplay"/>)에 그릴 내용으로 만들어 넣는다. 메인 채팅창과 서브 채팅창이 같이 쓴다.
    /// 줄 색·배경·굵기 규칙과 강조 알림음이 여기 있다. 글꼴과 남길 줄 수는 표시 영역이 정한다.
    /// </summary>
    public static class ChatLineRenderer
    {
        public static void AddLog(
            ChatLineDisplay display,
            LogParser.ParseResult log,
            ChatSettings settings,
            bool isRealTime,
            bool canPlayAlertSound,
            bool useMagicCircleAlert = false)
        {
            if (display == null) throw new ArgumentNullException(nameof(display));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            bool isBlacklisted = AppServices.Get<BlacklistService>().TryGetReason(log.SenderId, out string blacklistReason);
            Brush foreground = isBlacklisted ? BlacklistService.HighlightBrush : log.Brush;
            string displayText = isBlacklisted ? $"{log.FormattedText} [ {blacklistReason} ]" : log.FormattedText;

            // 타임스탬프/에타레벨/캐릭터를 조각으로 나눠 색을 따로 칠할 수 있게 (ChatLineComposer)
            List<ChatSegment> segments = ChatLineComposer.Compose(displayText, log, settings);
            var parts = new List<(string Text, Brush? Brush)>(segments.Count);
            foreach (ChatSegment segment in segments)
            {
                if (segment.Text.Length == 0)
                    continue;
                parts.Add((segment.Text, ChatLineComposer.ResolveSegmentBrush(segment, settings)));
            }

            Brush? background = null;
            bool bold = false;

            if (isBlacklisted)
            {
                background = BlacklistService.HighlightBackgroundBrush;
                bold = true;
            }

            if (log.IsHighlight)
            {
                // 마법진 보라 배경을 먼저 칠하고, 하이라이트 색이 켜져 있으면 주황 배경이 덮어쓴다
                if (useMagicCircleAlert && log.IsMagicCircleAlert)
                {
                    background = ChatBrushResolver.MagicCircleBackgroundBrush;
                    bold = true;
                }

                if (settings.UseAlertColor && !isBlacklisted)
                {
                    background = ChatBrushResolver.HighlightBackgroundBrush;
                    bold = true;
                }

                if (isRealTime && canPlayAlertSound)
                {
                    if (useMagicCircleAlert && log.IsMagicCircleAlert && settings.UseMagicCircleAlert)
                        AppServices.Get<NotificationService>().PlayAlert("Wave.mp3", AlertChannel.MagicCircle);
                    else if (settings.UseAlertSound)
                        AppServices.Get<NotificationService>().PlayAlert("Highlight.mp3", AlertChannel.Highlight);
                }
            }

            display.AddLine(new ChatLineContent(parts, foreground)
            {
                Background = background,
                Bold = bold,
            });
        }
    }
}
