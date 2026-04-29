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

            _itemDropLogAnalyzer.Analyze(context, itemFilterSnapshot);
            _experienceLogAnalyzer.Analyze(context);
            _etosDirectionLogAnalyzer.Analyze(context);
            _alertLogAnalyzer.Analyze(context);

            var parsed = context.Result;
            parsed.Brush = ChatBrushResolver.Resolve(_settings, parsed.Category);

            bool isSystemLog = parsed.Category is ChatCategory.System or ChatCategory.System2 or ChatCategory.System3;
            bool isRareTrackedItem = parsed.IsTrackedItemDrop &&
                                     (parsed.TrackedItemGrade == ItemDropGrade.Rare ||
                                      parsed.TrackedItemGrade == ItemDropGrade.Special);

            return new LogAnalysisResult(
                RawHtml: html,
                IsRealTime: isRealTime,
                IsSuccess: true,
                Parsed: parsed,
                IsSystemLog: isSystemLog,
                HasExperienceGain: parsed.GainedExp > 0,
                HasTrackedItemDrop: parsed.IsTrackedItemDrop,
                IsRareTrackedItemDrop: isRareTrackedItem,
                ShouldRunBuffTracker: isSystemLog,
                ShouldRunDailyWeeklyContent: isRealTime && isSystemLog,
                ShouldShowItemDropToast: isRealTime && parsed.IsTrackedItemDrop && _settings.ShowItemDropAlert,
                ShouldShowEtosDirection: isRealTime &&
                                         _settings.ShowEtosDirectionAlert &&
                                         !string.IsNullOrWhiteSpace(parsed.EtosImagePath),
                BufferTabs: ResolveBufferTabs(parsed));
        }

        public bool ShouldRenderToTab(LogParser.ParseResult parsed, string currentTabTag)
            => parsed.IsHighlight || LogParser.IsMatchTab(parsed, currentTabTag, _settings);

        public IReadOnlyList<string> ResolveBufferTabs(LogParser.ParseResult parsed)
        {
            var tabs = new List<string>(2);
            if (parsed.Category is ChatCategory.NormalSelf or ChatCategory.Normal)
                tabs.Add("General");

            if (LogParser.IsVisible(parsed.Category, _settings))
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
    }

    public sealed record LogAnalysisResult(
        string RawHtml,
        bool IsRealTime,
        bool IsSuccess,
        LogParser.ParseResult Parsed,
        bool IsSystemLog,
        bool HasExperienceGain,
        bool HasTrackedItemDrop,
        bool IsRareTrackedItemDrop,
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
                IsRareTrackedItemDrop: false,
                ShouldRunBuffTracker: false,
                ShouldRunDailyWeeklyContent: false,
                ShouldShowItemDropToast: false,
                ShouldShowEtosDirection: false,
                BufferTabs: Array.Empty<string>());
    }
}
