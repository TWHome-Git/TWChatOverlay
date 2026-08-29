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
        SenderId,
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

            // 1.5) 종류 말머리: [일반]/[팀]/[클럽]/[시스템] (외치기는 원문에 이미 '외치기 :'가 있어 생략)
            if (settings.ShowCategoryPrefix)
            {
                string? prefix = log.Category switch
                {
                    ChatCategory.Normal or ChatCategory.NormalSelf => "[일반] ",
                    ChatCategory.Team => "[팀] ",
                    ChatCategory.Club => "[클럽] ",
                    ChatCategory.System or ChatCategory.System2 or ChatCategory.System3 => "[시스템] ",
                    _ => null,
                };
                if (prefix != null)
                    segments.Add(new ChatSegment(prefix, ChatSegmentKind.Body));
            }

            // 2) 아이디 구간 + 아이디 뒤 장식 조각. 아이디를 못 찾으면 통짜 본문으로.
            var decorations = BuildDecorations(log, settings);
            if (!TryFindSenderRange(rest, log, out int senderStart, out int senderEnd))
            {
                segments.Add(new ChatSegment(rest, ChatSegmentKind.Body));
                return segments;
            }

            segments.Add(new ChatSegment(rest[..senderStart], ChatSegmentKind.Body));
            segments.Add(new ChatSegment(rest[senderStart..senderEnd], ChatSegmentKind.SenderId));
            segments.AddRange(decorations);
            if (senderEnd < rest.Length)
                segments.Add(new ChatSegment(rest[senderEnd..], ChatSegmentKind.Body));
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
                    ChatSegmentKind.SenderId when !settings.SenderIdColorSync => ChatBrushResolver.ToBrush(settings.SenderIdColor),
                    ChatSegmentKind.Timestamp when !settings.TimestampColorSync => ChatBrushResolver.ToBrush(settings.TimestampColor),
                    ChatSegmentKind.EtaLevel when settings.EtaLevelRangeColors => EtaLevelRangeBrush(segment.Text, settings),
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

        /// <summary>"[N]" 형태의 레벨 텍스트에서 숫자를 읽어 구간(1~20/21~40/41~60/61~80/81~) 색을 고른다.</summary>
        private static Brush? EtaLevelRangeBrush(string text, ChatSettings settings)
        {
            int level = 0;
            foreach (char c in text)
            {
                if (char.IsDigit(c))
                    level = level * 10 + (c - '0');
                else if (level > 0)
                    break;
            }

            string hex = level switch
            {
                <= 20 => settings.EtaLevelRange1Color,
                <= 40 => settings.EtaLevelRange2Color,
                <= 60 => settings.EtaLevelRange3Color,
                <= 80 => settings.EtaLevelRange4Color,
                _ => settings.EtaLevelRange5Color,
            };
            return ChatBrushResolver.ToBrush(hex);
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
                    // 레벨 0은 에타 정보 없음 → 표기하지 않는다
                    if (settings.ShowEtaLevel && profile.Level > 0)
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

        /// <summary>아이디가 차지하는 구간. 외치기는 끝의 [보낸이] 괄호 안, 그 외는 콜론 앞 아이디. 장식은 구간 끝에 붙는다.</summary>
        private static bool TryFindSenderRange(string body, LogParser.ParseResult log, out int start, out int end)
        {
            start = end = -1;

            if (log.Category == ChatCategory.Shout)
            {
                Match m = ShoutTrailingSenderRegex.Match(body);
                if (!m.Success) return false;
                start = m.Index + 1;                      // "[" 바로 뒤
                end = start + m.Groups["id"].Length;      // 닫는 "]" 바로 앞
                return true;
            }

            string displaySenderId = log.SenderId ?? log.RawSenderId ?? string.Empty;
            if (displaySenderId.Length == 0) return false;
            int colon = body.IndexOf(':');
            if (colon <= 0) return false;
            string left = body[..colon];
            int idx = left.LastIndexOf(displaySenderId, StringComparison.Ordinal);
            if (idx < 0) return false;
            start = idx;
            end = idx + displaySenderId.Length;
            return true;
        }
    }
}
