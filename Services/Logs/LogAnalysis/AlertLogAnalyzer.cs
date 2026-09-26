using System;
using System.Collections.Generic;
using System.Linq;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services.LogAnalysis
{
    public sealed class AlertLogAnalyzer
    {
        private const string MagicCircleKeyword = "몬스터가 남아있으면 다음 웨이브로 넘어가지 않습니다.";
        private const string ReflectionPatternEndAlertSender = "심연의 제2사도";
        private const string ReflectionPatternEndAlertMessage = "절제와 균형의 중심에서 빗나간 힘은 칼날이 되어 돌아오지.";
        private static readonly (string Sender, string Message)[] ReflectionPatternAlertMessages =
        {
            (ReflectionPatternEndAlertSender, ReflectionPatternEndAlertMessage)
        };

        public void Analyze(LogLineContext context)
        {
            // 방전은 시스템 줄이라 아래 카테고리 걸러내기 전에 먼저 본다.
            // "방전되었습니다 : 상태이상 [방전]" — 보낸이 자리가 문구라 본문만 볼 수도, 줄 전체를 볼 수도 있다.
            if (IsSystemMessage(context, DischargeKeyword))
                context.Result.IsDischargeAlert = true;
            else if (IsSystemMessage(context, DischargeClearedKeyword) || IsSystemMessage(context, DischargeClearedSpacedKeyword))
                context.Result.IsDischargeCleared = true;

            var settings = context.Settings;
            if (settings == null || (!settings.UseAlertColor &&
                                     !settings.UseAlertSound &&
                                     !settings.UseMagicCircleAlert &&
                                     !settings.EnableReflectionPatternAlert))
                return;

            if (settings.EnableReflectionPatternAlert && IsReflectionPatternAlertMessage(context))
            {
                context.Result.IsReflectionPatternAlert = true;
                if (IsReflectionPatternEndAlertMessage(context))
                    context.Result.IsReflectionPatternEndAlert = true;
            }

            if (settings.UseMagicCircleAlert &&
                (context.Result.Category == ChatCategory.System ||
                 context.Result.Category == ChatCategory.System2 ||
                 context.Result.Category == ChatCategory.System3) &&
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

        private const string DischargeKeyword = "상태이상 [방전]";
        private const string DischargeClearedKeyword = "방전상태가 풀렸습니다";
        private const string DischargeClearedSpacedKeyword = "방전 상태가 풀렸습니다";

        /// <summary>시스템 줄에 그 문구가 있는지. 다른 사람이 채팅으로 같은 말을 적어도 세지 않게 시스템 줄만 본다.</summary>
        private static bool IsSystemMessage(LogLineContext context, string keyword)
        {
            if (!context.IsSystemLog)
                return false;

            return context.MessageOnly.Contains(keyword, StringComparison.Ordinal) ||
                   context.ChatContent.Contains(keyword, StringComparison.Ordinal);
        }

        private static bool IsReflectionPatternAlertMessage(LogLineContext context)
        {
            if (context.Result.Category != ChatCategory.Normal &&
                context.Result.Category != ChatCategory.NormalSelf)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(context.Result.SenderId) || string.IsNullOrWhiteSpace(context.MessageOnly))
                return false;

            string sender = context.Result.SenderId.Trim();
            string message = context.MessageOnly.Trim();

            foreach (var pattern in ReflectionPatternAlertMessages)
            {
                if (sender.Equals(pattern.Sender, StringComparison.Ordinal) &&
                    message.Contains(pattern.Message, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsReflectionPatternEndAlertMessage(LogLineContext context)
        {
            if (context.Result.Category != ChatCategory.Normal &&
                context.Result.Category != ChatCategory.NormalSelf)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(context.Result.SenderId) || string.IsNullOrWhiteSpace(context.MessageOnly))
                return false;

            return context.Result.SenderId.Trim().Equals(ReflectionPatternEndAlertSender, StringComparison.Ordinal) &&
                   context.MessageOnly.Trim().Contains(ReflectionPatternEndAlertMessage, StringComparison.Ordinal);
        }
    }
}
