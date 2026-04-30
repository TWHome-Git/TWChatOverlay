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

        public void Analyze(LogLineContext context)
        {
            var fontMatches = FontTagRegex.Matches(context.RawHtml);
            if (fontMatches.Count < 2)
                return;

            string timeRaw = fontMatches[0].Groups["content"].Value.Trim();
            string chatColor = fontMatches[1].Groups["color"].Value.ToLowerInvariant();
            string rawContent = fontMatches[1].Groups["content"].Value;
            string chatContent = WebUtility.HtmlDecode(rawContent)
                .Replace("&nbsp", " ");
            chatContent = Regex.Replace(chatContent, @"\s+", " ").Trim();

            if (chatColor == "white")
                chatColor = "ffffff";

            var (category, brush) = GetCategoryByColor(chatColor);
            context.Result.Category = category;
            context.Result.Brush = brush;
            context.Result.SenderId = ExtractSenderId(chatContent, category);

            if (category == ChatCategory.Shout)
                chatContent = ApplyEtaProfileByShoutTrailingUserId(chatContent);
            else if (category is not ChatCategory.System and not ChatCategory.System2 and not ChatCategory.System3)
                chatContent = ApplyEtaProfile(chatContent);

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

        private static string ApplyEtaProfile(string chatContent)
        {
            int colonIndex = chatContent.IndexOf(":", StringComparison.Ordinal);
            if (colonIndex <= 0)
                return chatContent;

            string leftPart = chatContent.Substring(0, colonIndex).TrimEnd();
            string rightPart = chatContent.Substring(colonIndex + 1);

            int nameStart = leftPart.LastIndexOf(']');
            nameStart = nameStart >= 0 ? nameStart + 1 : 0;

            string prefix = leftPart.Substring(0, nameStart);
            string userId = leftPart.Substring(nameStart).Trim();

            if (string.IsNullOrWhiteSpace(userId) || !EtaProfileResolver.TryGetProfile(userId, out var profile))
                return chatContent;

            string decoratedUserId = $"{userId}[{profile.Level}]";
            return $"{prefix}{decoratedUserId}:{rightPart}";
        }

        private static string ApplyEtaProfileByShoutTrailingUserId(string chatContent)
        {
            var match = ShoutTrailingUserIdRegex.Match(chatContent);
            if (!match.Success)
                return chatContent;

            string userId = match.Groups["userId"].Value.Trim();
            if (string.IsNullOrWhiteSpace(userId) || !EtaProfileResolver.TryGetProfile(userId, out var profile))
                return chatContent;

            string etaLevel = $"[{profile.Level}]";
            return chatContent.Insert(match.Index + match.Length, $" {etaLevel}");
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

        private static (ChatCategory category, SolidColorBrush brush) GetCategoryByColor(string colorCode)
        {
            return colorCode switch
            {
                "c8ffc8" => (ChatCategory.NormalSelf, new SolidColorBrush(Color.FromRgb(200, 255, 200))),
                "ffffff" => (ChatCategory.Normal, Brushes.White),
                "c896c8" => (ChatCategory.Shout, new SolidColorBrush(Color.FromRgb(200, 150, 200))),
                "94ddfa" => (ChatCategory.Club, new SolidColorBrush(Color.FromRgb(148, 221, 250))),
                "f7b73c" => (ChatCategory.Team, new SolidColorBrush(Color.FromRgb(247, 183, 60))),
                "ff64ff" => (ChatCategory.System, new SolidColorBrush(Color.FromRgb(255, 100, 255))),
                "00ffff" => (ChatCategory.System2, Brushes.Cyan),
                "ff6464" => (ChatCategory.System3, new SolidColorBrush(Color.FromRgb(255, 100, 100))),
                _ => (ChatCategory.Unknown, Brushes.White)
            };
        }
    }
}
