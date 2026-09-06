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
            /// <summary>외치기 줄의 세부 종류(무료/유료/공지). Category가 Shout일 때만 뜻이 있다.</summary>
            public ShoutKind ShoutKind { get; set; } = ShoutKind.Notice;
            public bool IsSuccess { get; set; } = false;
            public bool IsHighlight { get; set; } = false;
            public bool IsMagicCircleAlert { get; set; } = false;
            public bool IsReflectionPatternAlert { get; set; } = false;
            public bool IsReflectionPatternEndAlert { get; set; } = false;
            public long GainedExp { get; set; } = 0;
            public string? EtosImagePath { get; set; } = null;
            public bool IsTrackedItemDrop { get; set; } = false;
            public string? TrackedItemName { get; set; } = null;
            public ItemDropGrade TrackedItemGrade { get; set; } = ItemDropGrade.Normal;
            public int TrackedItemCount { get; set; } = 1;
            public string? SenderId { get; set; } = null;
            public string? RawSenderId { get; set; } = null;
            public bool HasLeadingBodyWhitespace { get; set; } = false;
            /// <summary>클럽 보스 공지 줄인지 ([클럽 보스] 포함) — 표시 여부·개별 색상에 사용.</summary>
            public bool IsClubBossMessage { get; set; } = false;
        }

        public static ParseResult ParseLine(string html, ChatSettings settings)
            => new LogAnalysisService(settings).Analyze(html, isRealTime: false).Parsed;

        public static List<string> ParseKeywords(string? keywordInput)
            => AlertLogAnalyzer.ParseKeywords(keywordInput);

        public static bool IsMatchTab(ParseResult log, string tabTag, ChatSettings settings)
        {
            return tabTag switch
            {
                "Basic" => IsVisible(log, settings),
                "General" => log.Category is ChatCategory.NormalSelf or ChatCategory.Normal && settings.ShowNormal,
                "Team" => log.Category == ChatCategory.Team,
                "Club" => log.Category == ChatCategory.Club,
                "Shout" => log.Category == ChatCategory.Shout,
                "System" => log.Category is ChatCategory.System or ChatCategory.System2 or ChatCategory.System3,
                "Item" => log.IsTrackedItemDrop,
                "All" => true,
                _ => false
            };
        }

        /// <summary>줄 단위 표시 여부. 외치기는 무료/유료/공지 종류별 설정을 따른다.</summary>
        public static bool IsVisible(ParseResult log, ChatSettings settings)
        {
            if (log.Category == ChatCategory.Shout)
            {
                return log.ShoutKind switch
                {
                    ShoutKind.Free => settings.ShowFreeShout,
                    ShoutKind.Paid => settings.ShowPaidShout,
                    _ => settings.ShowNoticeShout,
                };
            }

            return IsVisible(log.Category, settings);
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
