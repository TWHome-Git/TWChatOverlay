using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    public sealed class LogDocumentRenderer
    {
        private static readonly Regex LeadingTimestampRegex = new(
            @"^\s*\[[^\]]+\]\s*",
            RegexOptions.Compiled);

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
            displayText = ApplyEtaDecorations(displayText, log, settings);
            if (!settings.ShowTimestamp)
                displayText = LeadingTimestampRegex.Replace(displayText, string.Empty);

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
                    NotificationService.PlayAlert("Highlight.wav");
            }

            document.Blocks.Add(paragraph);
            if (document.Blocks.Count > _maxBlocks)
                document.Blocks.Remove(document.Blocks.FirstBlock);
        }

        private static string ApplyEtaDecorations(string text, LogParser.ParseResult log, ChatSettings settings)
        {
            string lookupSenderId = log.RawSenderId ?? log.SenderId ?? string.Empty;
            string displaySenderId = log.SenderId ?? log.RawSenderId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text) || lookupSenderId.Length == 0 || displaySenderId.Length == 0)
                return text;
            if (!settings.ShowEtaLevel && !settings.ShowEtaCharacter)
                return text;
            if (!EtaProfileResolver.TryGetProfile(lookupSenderId, out var profile))
                return text;

            string suffix = string.Empty;
            if (settings.ShowEtaLevel)
                suffix += $"[{profile.Level}]";
            if (settings.ShowEtaCharacter && !string.IsNullOrWhiteSpace(profile.CharacterName))
                suffix += $"[{profile.CharacterName}]";
            if (string.IsNullOrEmpty(suffix))
                return text;

            if (log.Category == ChatCategory.Shout)
            {
                return Regex.Replace(
                    text,
                    $@"\[{Regex.Escape(displaySenderId)}\]\s*$",
                    $"[{displaySenderId}{suffix}]");
            }

            if (!TrySplitTimestampAndBody(text, out string body))
                return text;

            int colon = body.IndexOf(':');
            if (colon <= 0) return text;
            string left = body.Substring(0, colon);
            int idx = left.LastIndexOf(displaySenderId, StringComparison.Ordinal);
            if (idx < 0) return text;
            int bodySenderIndex = text.IndexOf(left, StringComparison.Ordinal);
            if (bodySenderIndex < 0) return text;

            int insertIndex = bodySenderIndex + idx + displaySenderId.Length;
            return text.Substring(0, insertIndex) + suffix + text.Substring(insertIndex);
        }

        private static bool TrySplitTimestampAndBody(string text, out string body)
        {
            body = string.Empty;
            int closingBracketIndex = text.IndexOf(']');
            if (closingBracketIndex < 0 || closingBracketIndex + 1 >= text.Length)
                return false;

            body = text[(closingBracketIndex + 1)..].TrimStart();
            return body.Length > 0;
        }
    }
}
