using System;
using System.Collections.Generic;
using System.Linq;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services.LogAnalysis
{
    public sealed class AlertLogAnalyzer
    {
        private const string MagicCircleKeyword = "발 밑에 마법진이 나타났다!";

        public void Analyze(LogLineContext context)
        {
            var settings = context.Settings;
            if (settings == null || (!settings.UseAlertColor && !settings.UseAlertSound && !settings.UseMagicCircleAlert))
                return;

            if (settings.UseMagicCircleAlert &&
                (context.Result.Category == ChatCategory.Normal || context.Result.Category == ChatCategory.NormalSelf) &&
                context.MessageOnly.Contains(MagicCircleKeyword, StringComparison.Ordinal))
            {
                context.Result.IsHighlight = true;
                context.Result.IsMagicCircleAlert = true;
                return;
            }

            if (context.Result.Category == ChatCategory.System ||
                context.Result.Category == ChatCategory.System2 ||
                context.Result.Category == ChatCategory.System3 ||
                context.Result.Category == ChatCategory.Shout)
            {
                return;
            }

            if (context.MessageOnly.Contains("[클럽 보스]", StringComparison.Ordinal))
                return;

            var keywords = ParseKeywords(settings.KeywordInput);
            if (keywords.Count == 0)
                return;

            string cleanContent = context.MessageOnly.Replace("@", string.Empty);
            foreach (var keyword in keywords)
            {
                string cleanKeyword = keyword.Replace("@", string.Empty).Trim();
                if (!string.IsNullOrEmpty(cleanKeyword) && cleanContent.Contains(cleanKeyword))
                {
                    context.Result.IsHighlight = true;
                    break;
                }
            }
        }

        public static List<string> ParseKeywords(string? keywordInput)
        {
            if (string.IsNullOrWhiteSpace(keywordInput))
                return new List<string>();

            return keywordInput.Split(new[] { ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(static keyword => keyword.StartsWith("@"))
                .ToList();
        }
    }
}
