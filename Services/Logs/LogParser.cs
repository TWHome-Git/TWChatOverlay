using System.Collections.Generic;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.Services.LogAnalysis;

namespace TWChatOverlay
{
    /// <summary>
    /// Backward-compatible facade for log analysis helpers.
    /// </summary>
    public static class LogParser
    {
        public class ParseResult
        {
            public string FormattedText { get; set; } = "";
            public SolidColorBrush Brush { get; set; } = Brushes.White;
            public ChatCategory Category { get; set; } = ChatCategory.Unknown;
            public bool IsSuccess { get; set; } = false;
            public bool IsHighlight { get; set; } = false;
            public bool IsMagicCircleAlert { get; set; } = false;
            public long GainedExp { get; set; } = 0;
            public string? EtosImagePath { get; set; } = null;
            public bool IsTrackedItemDrop { get; set; } = false;
            public string? TrackedItemName { get; set; } = null;
            public ItemDropGrade TrackedItemGrade { get; set; } = ItemDropGrade.Normal;
            public int TrackedItemCount { get; set; } = 1;
            public string? SenderId { get; set; } = null;
        }

        public static ParseResult ParseLine(string html, ChatSettings settings)
            => new LogAnalysisService(settings).Analyze(html, isRealTime: false).Parsed;

        public static List<string> ParseKeywords(string? keywordInput)
            => AlertLogAnalyzer.ParseKeywords(keywordInput);

        public static bool IsMatchTab(ParseResult log, string tabTag, ChatSettings settings)
        {
            return tabTag switch
            {
                "Basic" => IsVisible(log.Category, settings),
                "Team" => log.Category == ChatCategory.Team,
                "Club" => log.Category == ChatCategory.Club,
                "Shout" => log.Category == ChatCategory.Shout,
                "System" => log.Category is ChatCategory.System or ChatCategory.System2 or ChatCategory.System3,
                "Item" => log.IsTrackedItemDrop,
                "All" => true,
                _ => false
            };
        }

        public static bool IsVisible(ChatCategory category, ChatSettings settings)
        {
            return category switch
            {
                ChatCategory.NormalSelf or ChatCategory.Normal => settings.ShowNormal,
                ChatCategory.Shout => settings.ShowShout,
                ChatCategory.Club => settings.ShowClub,
                ChatCategory.Team => settings.ShowTeam,
                ChatCategory.System or ChatCategory.System2 or ChatCategory.System3 => settings.ShowSystem,
                _ => true
            };
        }
    }
}
