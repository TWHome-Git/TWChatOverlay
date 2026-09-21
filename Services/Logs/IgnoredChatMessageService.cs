using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 일반 채팅 색(흰색)으로 찍히지만 사람 대화가 아닌 줄(NPC·몬스터 대사, 시스템 안내)을 숨기기 위한 판정.
    ///
    /// 판정은 반드시 "화자"와 "본문"을 나눠서 한다. 예전에는 본문 어디에든 몬스터 이름이 들어가면 숨겼기 때문에
    /// "홍길동 : 신조 가실 분?" 같은 사람 대화까지 조용히 사라졌다(채팅 씹힘의 한 원인).
    /// </summary>
    public static class IgnoredChatMessageService
    {
        /// <summary>
        /// 흰색으로 "이름 : 대사" 형태로 찍히는 NPC·몬스터 화자. 화자 이름이 이와 같을 때만 숨긴다.
        /// 화자가 없는 안내문("흉포한 라이코스이(가) 나타났습니다")에서는 본문 포함 여부로 본다.
        /// </summary>
        private static readonly string[] IgnoredSpeakers =
        {
            "흉포한 라이코스",
            "메달의 사제, 티로로스",
            "서클릿의 사제, 마티아",
            "소매의 사제, 체리아",
            "선봉대장, 로카고스",
            "심연의 제2사도",
            "경보 장치",
            "신조",
            "붉은 프토마",
            "회색 프토마",
            "녹색 프토마",
            "푸른 프토마",
            "크라모르",
            "검의 사제, 셀리니아코스",
            "궤의 사제, 프로에드로스",
            "지팡이의 사제, 고이티아",
            "데스포이나",
            "키시니크",
            "Happy Birthday"
        };

        /// <summary>화자 없이 흰색으로 찍히는 시스템 안내문. 본문에 이 문장이 들어 있으면 숨긴다.</summary>
        private static readonly string[] IgnoredNoticePhrases =
        {
            "금화 주머니를 획득 했습니다.",
            "지역 보상상자를 열었습니다.",
            "이공간 보물상자를 열었습니다.",
            "포탈 전용 상자를 열었습니다."
        };

        // [클럽 보스] only: controlled by ShowClubBoss checkbox in settings.
        private static readonly HashSet<string> ClubIgnoredContains = new(StringComparer.Ordinal)
        {
            "[클럽 보스]"
        };

        /// <summary>형태가 정해진 안내문. 화자 유무와 무관하게 본문 전체에 대해 본다.</summary>
        private static readonly Regex[] NormalIgnoredRegexes =
        {
            new(@"^(?:\[[^\]]+\]\s*)?(SP|MP|Fever|HP)가\s*\d+%\s*회복되었습니다\.?$", RegexOptions.Compiled),
            new(@"^(?:\[[^\]]+\]\s*)?체력이\s*\d+%\s*회복되었습니다\.?$", RegexOptions.Compiled),
            new(@"^3분 후 자동으로 퇴장합니다\.$", RegexOptions.Compiled),
            new(@"남은\s*공격\s*횟수\s*:\s*\d+", RegexOptions.Compiled),
            new(@"절제와\s*균형의\s*중심에서\s*빗나간\s*힘은\s*칼날이\s*되어\s*돌아오지\.?", RegexOptions.Compiled)
        };

        /// <summary>
        /// 일반(흰색) 줄을 숨길지 판정한다.
        /// </summary>
        /// <param name="messageOnly">타임스탬프를 뗀 본문 ("화자 : 내용" 또는 안내문).</param>
        /// <param name="senderId">파서가 뽑은 화자. 사람 채팅이면 닉네임, NPC 대사면 NPC 이름, 안내문이면 null.</param>
        public static bool IsIgnoredNormalMessage(string messageOnly, string? senderId)
        {
            if (string.IsNullOrWhiteSpace(messageOnly))
                return false;

            string message = messageOnly.Trim();

            if (!string.IsNullOrWhiteSpace(senderId))
            {
                // 화자가 있는 줄: 화자 이름으로만 숨긴다. 사람 대화 본문에 몬스터 이름이 있어도 숨기지 않는다.
                string speaker = senderId.Trim();
                foreach (string ignored in IgnoredSpeakers)
                {
                    if (speaker.Equals(ignored, StringComparison.Ordinal) ||
                        speaker.StartsWith(ignored, StringComparison.Ordinal))
                        return true;
                }

                return MatchesNoticeRegex(message);
            }

            // 화자가 없는 줄(안내문): 본문 포함 여부로 판정한다
            foreach (string phrase in IgnoredNoticePhrases)
            {
                if (message.Contains(phrase, StringComparison.Ordinal))
                    return true;
            }

            foreach (string ignored in IgnoredSpeakers)
            {
                if (message.Contains(ignored, StringComparison.Ordinal))
                    return true;
            }

            return MatchesNoticeRegex(message);
        }

        private static bool MatchesNoticeRegex(string message)
        {
            foreach (var regex in NormalIgnoredRegexes)
            {
                if (regex.IsMatch(message))
                    return true;
            }

            return false;
        }

        public static bool IsIgnoredClubMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            foreach (var token in ClubIgnoredContains)
            {
                if (message.Contains(token, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        // Kept for compatibility with existing startup call path.
        public static System.Threading.Tasks.Task EnsureLoadedAsync(bool forceRefresh = false)
            => System.Threading.Tasks.Task.CompletedTask;
    }
}
