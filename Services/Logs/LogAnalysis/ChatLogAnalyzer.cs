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

        // 외치기 꼬리표: "… From [이름]"은 무료, "… Click [이름]"은 유료. 둘 다 없으면 시스템 공지형(룬 마스터 달성·강화 성공 등)
        private static readonly Regex FreeShoutTailRegex = new(@"\bFrom\s*\[[^\[\]]+\]\s*$", RegexOptions.Compiled);
        private static readonly Regex PaidShoutTailRegex = new(@"\bClick\s*\[[^\[\]]+\]\s*$", RegexOptions.Compiled);

        private static readonly Regex WhitespaceRunRegex = new(@"\s+", RegexOptions.Compiled);

        // 카테고리 판별용 브러시 — 줄마다 새로 만들지 않도록 Frozen 정적 인스턴스 재사용
        private static readonly SolidColorBrush NormalSelfBrush = CreateFrozen(200, 255, 200);
        private static readonly SolidColorBrush ShoutBrush = CreateFrozen(200, 150, 200);
        private static readonly SolidColorBrush ClubBrush = CreateFrozen(148, 221, 250);
        private static readonly SolidColorBrush TeamBrush = CreateFrozen(247, 183, 60);
        private static readonly SolidColorBrush SystemBrush = CreateFrozen(255, 100, 255);
        private static readonly SolidColorBrush System3Brush = CreateFrozen(255, 100, 100);

        private static SolidColorBrush CreateFrozen(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        public void Analyze(LogLineContext context)
        {
            var fontMatches = FontTagRegex.Matches(context.RawHtml);
            if (fontMatches.Count < 2)
                return;

            string timeRaw = fontMatches[0].Groups["content"].Value.Trim();
            string chatColor = fontMatches[1].Groups["color"].Value.ToLowerInvariant();
            string rawContent = fontMatches[1].Groups["content"].Value;
            string decodedContent = WebUtility.HtmlDecode(rawContent)
                .Replace("&nbsp", " ");
            string chatContent = WhitespaceRunRegex.Replace(decodedContent, " ").Trim();

            if (chatColor == "white")
                chatColor = "ffffff";

            var (category, brush) = GetCategoryByColor(chatColor);
            context.Result.Category = category;
            context.Result.Brush = brush;
            if (category == ChatCategory.Shout)
                context.Result.ShoutKind = ResolveShoutKind(chatContent);
            context.Result.SenderId = ExtractSender(chatContent, category, trim: true);
            context.Result.RawSenderId = ExtractSender(decodedContent, category, trim: false);
            context.Result.HasLeadingBodyWhitespace = !string.IsNullOrEmpty(decodedContent) && char.IsWhiteSpace(decodedContent[0]);

            context.ChatContent = chatContent;
            context.MessageOnly = ExtractMessageOnly(chatContent);
            context.Result.FormattedText = $"{timeRaw} {chatContent}";
            context.IsSuccess = true;
        }

        private static ShoutKind ResolveShoutKind(string chatContent)
        {
            if (FreeShoutTailRegex.IsMatch(chatContent)) return ShoutKind.Free;
            if (PaidShoutTailRegex.IsMatch(chatContent)) return ShoutKind.Paid;
            return ShoutKind.Notice;
        }

        private static string ExtractMessageOnly(string chatContent)
        {
            int colonIndex = chatContent.IndexOf(':');
            return colonIndex < 0 ? chatContent : chatContent.Substring(colonIndex + 1).Trim();
        }


        /// <summary>
        /// 화자 아이디. 외치기는 꼬리 "[아이디]", 그 외는 ':' 앞의 마지막 ']' 뒤 부분. 시스템 줄은 없다.
        /// trim=true는 공백 정규화된 본문용(SenderId), false는 원문 공백을 남긴 RawSenderId용 —
        /// 둘의 차이는 Trim 유무뿐이며, 아이디가 공백만이면 둘 다 null로 본다.
        /// </summary>
        private static string? ExtractSender(string chatContent, ChatCategory category, bool trim)
        {
            if (string.IsNullOrWhiteSpace(chatContent))
                return null;

            if (category == ChatCategory.Shout)
            {
                var shoutMatch = ShoutTrailingUserIdRegex.Match(chatContent);
                if (!shoutMatch.Success)
                    return null;

                string shoutUserId = shoutMatch.Groups["userId"].Value;
                if (trim)
                    shoutUserId = shoutUserId.Trim();
                return string.IsNullOrWhiteSpace(shoutUserId) ? null : shoutUserId;
            }

            if (category is ChatCategory.System or ChatCategory.System2 or ChatCategory.System3)
                return null;

            int colonIndex = chatContent.IndexOf(':');
            if (colonIndex <= 0)
                return null;

            string leftPart = chatContent.Substring(0, colonIndex).TrimEnd();
            int nameStart = leftPart.LastIndexOf(']');
            nameStart = nameStart >= 0 ? nameStart + 1 : 0;

            string userId = leftPart.Substring(nameStart);
            if (trim)
                userId = userId.Trim();
            return string.IsNullOrWhiteSpace(userId) ? null : userId;
        }

        private static (ChatCategory category, SolidColorBrush brush) GetCategoryByColor(string colorCode)
        {
            return colorCode switch
            {
                "c8ffc8" => (ChatCategory.NormalSelf, NormalSelfBrush),
                "ffffff" => (ChatCategory.Normal, Brushes.White),
                "c896c8" => (ChatCategory.Shout, ShoutBrush),
                "94ddfa" => (ChatCategory.Club, ClubBrush),
                "f7b73c" => (ChatCategory.Team, TeamBrush),
                "ff64ff" => (ChatCategory.System, SystemBrush),
                // 공지·던전 상황 안내 색. 분류표에 없어 Unknown이 되면 설정과 무관하게 기본 탭에만 강제로 남는다
                "64ff80" => (ChatCategory.System, SystemBrush),
                "00ffff" => (ChatCategory.System2, Brushes.Cyan),
                "ff6464" => (ChatCategory.System3, System3Brush),
                _ => (ChatCategory.Unknown, Brushes.White)
            };
        }
    }
}
