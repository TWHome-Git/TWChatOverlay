using System;
using System.Collections.Generic;
using TWChatOverlay.Models;
using TWChatOverlay.Services.LogAnalysis;

namespace TWChatOverlay.Services
{
    public sealed class LogAnalysisService
    {
        private readonly ChatSettings _settings;
        private readonly ChatLogAnalyzer _chatLogAnalyzer = new();
        private readonly ItemDropLogAnalyzer _itemDropLogAnalyzer = new();
        private readonly ExperienceLogAnalyzer _experienceLogAnalyzer = new();
        private readonly EtosDirectionLogAnalyzer _etosDirectionLogAnalyzer = new();
        private readonly AlertLogAnalyzer _alertLogAnalyzer = new();

        public LogAnalysisService(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public LogAnalysisResult Analyze(
            string html,
            bool isRealTime,
            DropItemResolver.DropItemFilterSnapshot? itemFilterSnapshot = null)
        {
            if (string.IsNullOrWhiteSpace(html))
                return LogAnalysisResult.Empty(html, isRealTime);

            var context = new LogLineContext(html, _settings);
            _chatLogAnalyzer.Analyze(context);
            if (!context.IsSuccess)
                return LogAnalysisResult.Empty(html, isRealTime);

            bool isSystemLog = context.Result.Category is ChatCategory.System or ChatCategory.System2 or ChatCategory.System3;
            if (isSystemLog)
            {
                _itemDropLogAnalyzer.Analyze(context, itemFilterSnapshot);
            }
            _experienceLogAnalyzer.Analyze(context);
            _etosDirectionLogAnalyzer.Analyze(context);
            _alertLogAnalyzer.Analyze(context);

            var parsed = context.Result;
            parsed.IsClubBossMessage = parsed.Category == ChatCategory.Club &&
                                       IgnoredChatMessageService.IsIgnoredClubMessage(parsed.FormattedText);
            if (parsed.Category is ChatCategory.Normal or ChatCategory.NormalSelf)
            {
                // 숨김 판정의 설정 무관 부분만 여기서 한 번 계산한다 (정규식은 이 한 번만 돈다).
                // ShowBossDialogue/ShowClubBoss 결합은 IsHiddenByChatFilter가 소비 시점마다 현재 설정으로 한다 —
                // 재표시(RequestRefreshLogDisplay)가 토글 변경을 반영해야 하기 때문.
                string messageOnly = GetMessageOnly(parsed.FormattedText);
                parsed.IsBossDialogueLine = IgnoredChatMessageService.MatchesBossDialogue(messageOnly, parsed.SenderId);
                parsed.IsIgnoredNoticeLine = IgnoredChatMessageService.MatchesIgnoredNotice(messageOnly, parsed.SenderId);
            }
            parsed.Brush = ChatBrushResolver.Resolve(_settings, parsed);

            return new LogAnalysisResult(
                RawHtml: html,
                IsRealTime: isRealTime,
                IsSuccess: true,
                Parsed: parsed,
                IsSystemLog: isSystemLog,
                HasExperienceGain: parsed.GainedExp > 0,
                HasTrackedItemDrop: parsed.IsTrackedItemDrop,
                // 버프 줄은 시스템 줄이 보통이지만, 게임이 내 아이디를 화자로 붙여 찍으면 일반 채팅으로 분류된다 — 문구로도 본다
                ShouldRunBuffTracker: isSystemLog || BuffTrackerService.LooksLikeBuffLog(parsed.FormattedText),
                ShouldRunDailyWeeklyContent: isRealTime && isSystemLog,
                ShouldShowItemDropToast: isRealTime && parsed.IsTrackedItemDrop && _settings.ShowItemDropAlert,
                ShouldShowEtosDirection: isRealTime &&
                                         _settings.ShowEtosDirectionAlert &&
                                         !string.IsNullOrWhiteSpace(parsed.EtosImagePath),
                BufferTabs: ResolveBufferTabs(parsed));
        }

        public bool ShouldRenderToTab(LogParser.ParseResult parsed, string currentTabTag)
        {
            if (IsHiddenByChatFilter(parsed))
                return false;

            return parsed.IsHighlight || LogParser.IsMatchTab(parsed, currentTabTag, _settings);
        }

        public IReadOnlyList<string> ResolveBufferTabs(LogParser.ParseResult parsed)
        {
            if (IsHiddenByChatFilter(parsed))
                return Array.Empty<string>();

            var tabs = new List<string>(2);
            if (parsed.Category is ChatCategory.NormalSelf or ChatCategory.Normal)
                tabs.Add("General");

            if (LogParser.IsVisible(parsed, _settings))
                tabs.Add("Basic");

            if (parsed.Category == ChatCategory.Team)
                tabs.Add("Team");
            else if (parsed.Category == ChatCategory.Club)
                tabs.Add("Club");
            else if (parsed.Category == ChatCategory.Shout)
                tabs.Add("Shout");
            else if (parsed.Category is ChatCategory.System or ChatCategory.System2 or ChatCategory.System3)
                tabs.Add("System");

            return tabs;
        }

        /// <summary>
        /// 무시 목록(클럽 보스 공지·보스 대사·안내문)에 걸려 숨길 줄인지.
        /// 분석 시점에 캐시한 매치 결과와 현재 설정만 결합하므로 정규식 없이 싸고, 설정 토글 뒤 재표시에도 그대로 쓸 수 있다.
        /// </summary>
        public bool IsHiddenByChatFilter(LogParser.ParseResult parsed)
        {
            if (parsed.IsClubBossMessage && !_settings.ShowClubBoss)
            {
                return true;
            }

            if ((parsed.Category == ChatCategory.Normal || parsed.Category == ChatCategory.NormalSelf) &&
                (parsed.IsIgnoredNoticeLine || (parsed.IsBossDialogueLine && !_settings.ShowBossDialogue)))
            {
                return true;
            }

            return false;
        }

        private static string GetMessageOnly(string formattedText)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
                return string.Empty;

            int closingBracketIndex = formattedText.IndexOf(']');
            if (closingBracketIndex >= 0 && closingBracketIndex + 1 < formattedText.Length)
                return formattedText[(closingBracketIndex + 1)..].TrimStart();

            return formattedText.Trim();
        }
    }

    public sealed record LogAnalysisResult(
        string RawHtml,
        bool IsRealTime,
        bool IsSuccess,
        LogParser.ParseResult Parsed,
        bool IsSystemLog,
        bool HasExperienceGain,
        bool HasTrackedItemDrop,
        bool ShouldRunBuffTracker,
        bool ShouldRunDailyWeeklyContent,
        bool ShouldShowItemDropToast,
        bool ShouldShowEtosDirection,
        IReadOnlyList<string> BufferTabs)
    {
        public static LogAnalysisResult Empty(string rawHtml, bool isRealTime)
            => new(
                RawHtml: rawHtml,
                IsRealTime: isRealTime,
                IsSuccess: false,
                Parsed: new LogParser.ParseResult(),
                IsSystemLog: false,
                HasExperienceGain: false,
                HasTrackedItemDrop: false,
                ShouldRunBuffTracker: false,
                ShouldRunDailyWeeklyContent: false,
                ShouldShowItemDropToast: false,
                ShouldShowEtosDirection: false,
                BufferTabs: Array.Empty<string>());
    }
}
