using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    public enum ChatSegmentKind
    {
        Body,
        Timestamp,
        EtaLevel,
        Character,
        IdTag,
    }

    public readonly record struct ChatSegment(string Text, ChatSegmentKind Kind);

    /// <summary>
    /// 채팅 한 줄을 색을 달리 칠할 수 있는 조각(타임스탬프 / 본문 / 에타레벨 / 캐릭터 / 태그)으로 나눈다.
    /// 메인 채팅창과 서브 채팅창이 같은 규칙으로 그리도록 여기 한 곳에 둔다.
    /// 표기 순서: [시각] 아이디[에타레벨][캐릭터][태그] : 내용
    /// </summary>
    public static class ChatLineComposer
    {
        private static readonly Regex LeadingTimestampRegex = new(@"^\s*\[[^\]]+\]\s*", RegexOptions.Compiled);
        private static readonly Regex ShoutTrailingSenderRegex = new(@"\[(?<id>[^\[\]]+)\]\s*$", RegexOptions.Compiled);

        public static List<ChatSegment> Compose(string text, LogParser.ParseResult log, ChatSettings settings)
        {
            var segments = new List<ChatSegment>(6);
            if (string.IsNullOrEmpty(text))
                return segments;

            // 1) 타임스탬프 분리 (끄면 버린다)
            string rest = text;
            Match ts = LeadingTimestampRegex.Match(text);
            if (ts.Success)
            {
                if (settings.ShowTimestamp)
                    segments.Add(new ChatSegment(ts.Value, ChatSegmentKind.Timestamp));
                rest = text[ts.Length..];
            }

            // 2) 아이디 뒤 장식 조각
            var decorations = BuildDecorations(log, settings);
            if (decorations.Count == 0)
            {
                segments.Add(new ChatSegment(rest, ChatSegmentKind.Body));
                return segments;
            }

            int insertIndex = FindInsertIndex(rest, log);
            if (insertIndex < 0)
            {
                segments.Add(new ChatSegment(rest, ChatSegmentKind.Body));
                return segments;
            }

            segments.Add(new ChatSegment(rest[..insertIndex], ChatSegmentKind.Body));
            segments.AddRange(decorations);
            if (insertIndex < rest.Length)
                segments.Add(new ChatSegment(rest[insertIndex..], ChatSegmentKind.Body));
            return segments;
        }

        /// <summary>조각들을 Run으로 만들어 문단에 넣는다. 색상 동기화가 켜져 있으면 전부 기본 색.</summary>
        public static void AppendRuns(Paragraph paragraph, IReadOnlyList<ChatSegment> segments, Brush baseBrush, ChatSettings settings)
        {
            foreach (var segment in segments)
            {
                if (segment.Text.Length == 0)
                    continue;

                var run = new Run(segment.Text);
                // 항목별 색상 동기화: 꺼진 항목만 개별 색을 칠하고, 나머지는 줄 색을 따른다
                Brush? brush = segment.Kind switch
                {
                    ChatSegmentKind.Timestamp when !settings.TimestampColorSync => ChatBrushResolver.ToBrush(settings.TimestampColor),
                    ChatSegmentKind.EtaLevel when !settings.EtaLevelColorSync => ChatBrushResolver.ToBrush(settings.EtaLevelColor),
                    ChatSegmentKind.Character when !settings.EtaCharacterColorSync => ChatBrushResolver.ToBrush(settings.EtaCharacterColor),
                    ChatSegmentKind.IdTag when !settings.IdTagColorSync => ChatBrushResolver.ToBrush(settings.IdTagColor),
                    _ => null,
                };
                if (brush != null)
                    run.Foreground = brush;

                paragraph.Inlines.Add(run);
            }

            // 기본 색은 문단 단위로 — 지정하지 않은 조각은 이 색을 물려받는다
            paragraph.Foreground = baseBrush;
        }

        private static List<ChatSegment> BuildDecorations(LogParser.ParseResult log, ChatSettings settings)
        {
            var result = new List<ChatSegment>(3);
            string lookupSenderId = log.RawSenderId ?? log.SenderId ?? string.Empty;
            string displaySenderId = log.SenderId ?? log.RawSenderId ?? string.Empty;
            if (lookupSenderId.Length == 0 || displaySenderId.Length == 0)
                return result;
            if (!settings.ShowEtaLevel && !settings.ShowEtaCharacter && !settings.ShowIdTag)
                return result;

            if (settings.ShowEtaLevel || settings.ShowEtaCharacter)
            {
                if (EtaProfileResolver.TryGetProfile(lookupSenderId, out var profile)
                    || EtaProfileResolver.TryGetProfile(lookupSenderId.Trim(), out profile))
                {
                    if (settings.ShowEtaLevel)
                        result.Add(new ChatSegment($"[{profile.Level}]", ChatSegmentKind.EtaLevel));
                    if (settings.ShowEtaCharacter && !string.IsNullOrWhiteSpace(profile.CharacterName))
                        result.Add(new ChatSegment($"[{profile.CharacterName}]", ChatSegmentKind.Character));
                }
            }

            if (settings.ShowIdTag
                && (IdTagService.TryGetTag(lookupSenderId, out string idTag)
                    || IdTagService.TryGetTag(displaySenderId, out idTag)))
            {
                result.Add(new ChatSegment($"[{idTag}]", ChatSegmentKind.IdTag));
            }

            return result;
        }

        /// <summary>장식을 끼워 넣을 위치. 외치기는 끝의 [보낸이] 닫는 괄호 앞, 그 외는 아이디 바로 뒤.</summary>
        private static int FindInsertIndex(string body, LogParser.ParseResult log)
        {
            if (log.Category == ChatCategory.Shout)
            {
                Match m = ShoutTrailingSenderRegex.Match(body);
                if (!m.Success) return -1;
                // "[" + id 뒤, 즉 닫는 "]" 바로 앞
                return m.Index + 1 + m.Groups["id"].Length;
            }

            string displaySenderId = log.SenderId ?? log.RawSenderId ?? string.Empty;
            int colon = body.IndexOf(':');
            if (colon <= 0) return -1;
            string left = body[..colon];
            int idx = left.LastIndexOf(displaySenderId, StringComparison.Ordinal);
            if (idx < 0) return -1;
            return idx + displaySenderId.Length;
        }
    }
}
