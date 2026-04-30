using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 채팅 로그 결과를 WPF 문서 요소로 렌더링합니다.
    /// </summary>
    public sealed class LogDocumentRenderer
    {
        private readonly int _maxBlocks;

        public LogDocumentRenderer(int maxBlocks = 200)
        {
            _maxBlocks = maxBlocks > 0 ? maxBlocks : 200;
        }

        public void AddLog(
            FlowDocument document,
            LogParser.ParseResult log,
            ChatSettings settings,
            FontFamily fontFamily,
            double fontSize,
            bool isRealTime,
            bool canPlayAlertSound)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            bool isBlacklisted = BlacklistService.TryGetReason(log.SenderId, out string blacklistReason);
            Brush foreground = isBlacklisted ? BlacklistService.HighlightBrush : log.Brush;
            string displayText = isBlacklisted ? $"{log.FormattedText} [ {blacklistReason} ]" : log.FormattedText;

            Paragraph paragraph = new(new Run(displayText))
            {
                Foreground = foreground,
                FontSize = fontSize,
                FontFamily = fontFamily,
                Margin = new Thickness(0, 0, 0, 1),
                LineHeight = 1
            };

            if (isBlacklisted)
            {
                paragraph.Background = BlacklistService.HighlightBackgroundBrush;
                paragraph.FontWeight = FontWeights.Bold;
            }

            if (log.IsHighlight)
            {
                if (settings.UseAlertColor && !isBlacklisted)
                {
                    paragraph.Background = new SolidColorBrush(Color.FromArgb(120, 255, 140, 0));
                    paragraph.FontWeight = FontWeights.Bold;
                }

                if (isRealTime && settings.UseAlertSound && canPlayAlertSound)
                {
                    NotificationService.PlayAlert("Highlight.wav");
                }
            }

            document.Blocks.Add(paragraph);

            if (document.Blocks.Count > _maxBlocks)
            {
                document.Blocks.Remove(document.Blocks.FirstBlock);
            }
        }
    }
}
