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
                context.Result.IsReflectionPatternAlert = true;

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

            string[] keywords = GetCleanKeywords(settings.KeywordInput);
            if (keywords.Length == 0)
                return;

            string cleanContent = context.MessageOnly.Replace("@", string.Empty);
            foreach (var cleanKeyword in keywords)
            {
                if (cleanContent.Contains(cleanKeyword))
                {
                    context.Result.IsHighlight = true;
                    break;
                }
            }
        }

        // 키워드 입력은 설정 화면에서만 바뀌므로 마지막으로 파싱한 원본 문자열과 정제된 키워드('@' 제거·Trim, 빈 값 제외)를 함께 둔다.
        // PropertyChanged가 뜨지 않는 경로(역직렬화·섹션 직접 대입)도 있어 원본 문자열 비교로 갱신을 판단한다.
        private sealed record KeywordCache(string? Source, string[] Keywords);

        private static volatile KeywordCache _keywordCache = new(null, Array.Empty<string>());

        private static string[] GetCleanKeywords(string? keywordInput)
        {
            KeywordCache cache = _keywordCache;
            if (ReferenceEquals(cache.Source, keywordInput) || string.Equals(cache.Source, keywordInput, StringComparison.Ordinal))
                return cache.Keywords;

            var clean = new List<string>();
            foreach (var keyword in ParseKeywords(keywordInput))
            {
                string cleanKeyword = keyword.Replace("@", string.Empty).Trim();
                if (!string.IsNullOrEmpty(cleanKeyword))
                    clean.Add(cleanKeyword);
            }

            cache = new KeywordCache(keywordInput, clean.ToArray());
            _keywordCache = cache;
            return cache.Keywords;
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
    }
}
