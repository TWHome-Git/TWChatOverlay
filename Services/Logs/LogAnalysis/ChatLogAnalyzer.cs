using System;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services.LogAnalysis
{
    public sealed class ChatLogAnalyzer
    {
        private static readonly Regex FontTagRegex = new(
            @"<font[^>]*color=[""']?#?(?<color>[a-fA-F0-9]+|white)[""']?[^>]*>(?<content>.*?)</font>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ShoutTrailingUserIdRegex = new(
            @"\[(?<userId>[^\[\]]+)\]\s*$",
            RegexOptions.Compiled);

        private static readonly Regex WhitespaceRunRegex = new(@"\s+", RegexOptions.Compiled);

        // 카테고리 판별용 브러시 — 줄마다 새로 만들지 않도록 Frozen 정적 인스턴스 재사용
        private static readonly SolidColorBrush NormalSelfBrush = CreateFrozen(200, 255, 200);
        private static readonly SolidColorBrush ShoutBrush = CreateFrozen(200, 150, 200);
        private static readonly SolidColorBrush ClubBrush = CreateFrozen(148, 221, 250);
        private static readonly SolidColorBrush TeamBrush = CreateFrozen(247, 183, 60);
        private static readonly SolidColorBrush SystemBrush = CreateFrozen(255, 100, 255);
        private static readonly SolidColorBrush System3Brush = CreateFrozen(255, 100, 100);

        private static SolidColorBrush CreateFrozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public void Analyze(LogLineContext context)
        {
            var fontMatches = FontTagRegex.Matches(context.RawHtml);
            if (fontMatches.Count < 2)
                return;

            string timeRaw = fontMatches[0].Groups["content"].Value.Trim();
            string chatColor = fontMatches[1].Groups["color"].Value.ToLowerInvariant();
            string rawContent = fontMatches[1].Groups["content"].Value;
            string decodedContent = WebUtility.HtmlDecode(rawContent)
                .Replace("&nbsp", " ");
            string chatContent = WhitespaceRunRegex.Replace(decodedContent, " ").Trim();

            if (chatColor == "white")
                chatColor = "ffffff";

            var (category, brush) = GetCategoryByColor(chatColor);
            context.Result.Category = category;
            context.Result.Brush = brush;
            context.Result.SenderId = ExtractSenderId(chatContent, category);
            context.Result.RawSenderId = ExtractRawSenderId(decodedContent, category);
            context.Result.HasLeadingBodyWhitespace = !string.IsNullOrEmpty(decodedContent) && char.IsWhiteSpace(decodedContent[0]);

            context.ChatContent = chatContent;
            context.MessageOnly = ExtractMessageOnly(chatContent);
            context.Result.FormattedText = $"{timeRaw} {chatContent}";
            context.IsSuccess = true;
        }

        private static string ExtractMessageOnly(string chatContent)
        {
            int colonIndex = chatContent.IndexOf(':');
            return colonIndex < 0 ? chatContent : chatContent.Substring(colonIndex + 1).Trim();
        }


        private static string? ExtractSenderId(string chatContent, ChatCategory category)
        {
            if (string.IsNullOrWhiteSpace(chatContent))
                return null;

            if (category == ChatCategory.Shout)
            {
                var shoutMatch = ShoutTrailingUserIdRegex.Match(chatContent);
                if (shoutMatch.Success)
                {
                    string shoutUserId = shoutMatch.Groups["userId"].Value.Trim();
                    return string.IsNullOrWhiteSpace(shoutUserId) ? null : shoutUserId;
                }

                return null;
            }

            if (category is ChatCategory.System or ChatCategory.System2 or ChatCategory.System3)
                return null;

            int colonIndex = chatContent.IndexOf(':');
            if (colonIndex <= 0)
                return null;

            string leftPart = chatContent.Substring(0, colonIndex).TrimEnd();
            int nameStart = leftPart.LastIndexOf(']');
            nameStart = nameStart >= 0 ? nameStart + 1 : 0;

            string userId = leftPart.Substring(nameStart).Trim();
            return string.IsNullOrWhiteSpace(userId) ? null : userId;
        }

        private static string? ExtractRawSenderId(string chatContent, ChatCategory category)
        {
            if (string.IsNullOrEmpty(chatContent))
                return null;

            if (category == ChatCategory.Shout)
            {
                var shoutMatch = ShoutTrailingUserIdRegex.Match(chatContent);
                if (shoutMatch.Success)
                {
                    string shoutUserId = shoutMatch.Groups["userId"].Value;
                    return shoutUserId.Length == 0 ? null : shoutUserId;
                }

                return null;
            }

            if (category is ChatCategory.System or ChatCategory.System2 or ChatCategory.System3)
                return null;

            int colonIndex = chatContent.IndexOf(':');
            if (colonIndex <= 0)
                return null;

            string leftPart = chatContent.Substring(0, colonIndex).TrimEnd();
            int nameStart = leftPart.LastIndexOf(']');
            nameStart = nameStart >= 0 ? nameStart + 1 : 0;

            string userId = leftPart.Substring(nameStart);
            return string.IsNullOrWhiteSpace(userId) ? null : userId;
        }

        private static bool IsIgnoredNormalMessage(string rawContent)
        {
            string message = WhitespaceRunRegex.Replace(rawContent, " ").Trim();
            return IgnoredChatMessageService.IsIgnoredNormalMessage(message);
        }

        private static (ChatCategory category, SolidColorBrush brush) GetCategoryByColor(string colorCode)
        {
            return colorCode switch
            {
                "c8ffc8" => (ChatCategory.NormalSelf, NormalSelfBrush),
                "ffffff" => (ChatCategory.Normal, Brushes.White),
                "c896c8" => (ChatCategory.Shout, ShoutBrush),
                "94ddfa" => (ChatCategory.Club, ClubBrush),
                "f7b73c" => (ChatCategory.Team, TeamBrush),
                "ff64ff" => (ChatCategory.System, SystemBrush),
                "00ffff" => (ChatCategory.System2, Brushes.Cyan),
                "ff6464" => (ChatCategory.System3, System3Brush),
                _ => (ChatCategory.Unknown, Brushes.White)
            };
        }
    }
}
