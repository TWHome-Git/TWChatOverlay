using System;
using System.Net;
using System.Text.RegularExpressions;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 채팅을 덮기만 하는 반복 안내 줄을 가려낸다.
    ///
    /// 모두 시각 바로 뒤에서 시작하는 줄만 잡는다 — 화자(:)가 앞에 붙는 일반 채팅·외치기·길드 채팅에
    /// 같은 말이 섞인 줄("외치기 : 3분 후 자동으로 퇴장합니다 ㅋㅋ")은 건드리지 않는다.
    ///
    /// 지금 가리는 것은 한 줄로 끝나는 안내뿐이다. 레이스·점검 공지처럼 게임이 두 줄로 쪼개 보내는
    /// 안내를 더하려면 꼬리 줄(같은 시각으로 오는 뒷부분)까지 함께 가려야 한다.
    /// </summary>
    public static class NoisyNoticeFilter
    {
        private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhiteSpaceRegex = new(@"\s+", RegexOptions.Compiled);

        /// <summary>줄 맨 앞의 시각. 이 뒤에 바로 안내문이 와야 한다.</summary>
        private const string Head = @"^(?:\[[^\]]*\]\s*)?";

        private static readonly Regex[] Patterns =
        {
            // 오를리 방어전: 한 대 때릴 때마다 찍힌다 (따로 창으로 보여 준다)
            new(Head + @"남은\s*공격\s*횟수\s*:\s*\d+", RegexOptions.Compiled),
            // 던전 클리어 후 자동 퇴장 ("3분 후", "10초 후", "잠시 후")
            new(Head + @"[^:]{0,16}자동으로\s*퇴장합니다", RegexOptions.Compiled),
        };

        /// <summary>정규식을 돌리기 전에 값싸게 거르는 낱말. 채팅 줄마다 지나가는 자리라 먼저 본다.</summary>
        private static readonly string[] Markers = { "공격 횟수", "퇴장" };

        /// <summary>이 줄이 채팅에서 가릴 반복 안내인지.</summary>
        public static bool IsNoise(string? html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return false;

            bool maybe = false;
            foreach (string marker in Markers)
            {
                if (html.Contains(marker, StringComparison.Ordinal)) { maybe = true; break; }
            }
            if (!maybe)
                return false;

            string text = Normalize(html);
            foreach (Regex pattern in Patterns)
            {
                if (pattern.IsMatch(text))
                    return true;
            }

            return false;
        }

        private static string Normalize(string text)
        {
            string decoded = WebUtility.HtmlDecode(text);
            decoded = HtmlTagRegex.Replace(decoded, " ");
            return WhiteSpaceRegex.Replace(decoded, " ").Trim();
        }
    }
}
