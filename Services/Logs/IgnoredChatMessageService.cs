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
            "Happy Birthday",
            // 회랑·이공간
            "설계자",
            "환희의 레이티아",
            "레이티아",
            "회랑의 거목, 에테르",
            "양면의 군주, 야누아르",
            "슬픔의 무희, 오페리아",
            "회랑의 파수꾼, 가고일",
            "회랑의 잔재, 루이나스",
            "운명의 심판자, 노아",
            // 그 밖의 몬스터·장치
            "CS-87H",
            "하수인",
            "지원부대 부대장",
            "선봉대 부대장",
            "끈질긴 먼지 개미",
            "봉인 결계",
            "근원의 핵",
            "유마 올름"
        };

        /// <summary>
        /// 보스·몬스터가 하는 대사 (화자 → 그 몬스터가 실제로 하는 말).
        ///
        /// 화자 이름만으로 판정하지 않는 이유: 같은 이름을 쓰는 실제 유저가 있다.
        /// "스페르첸드"는 필드 보스 이름이면서 같은 아이디의 유저도 채팅을 한다 —
        /// 이름으로 막으면 그 유저가 말할 때도 에타 레벨이 사라진다.
        /// 그래서 그 몬스터가 실제로 하는 대사일 때만 몬스터 줄로 본다.
        /// </summary>
        private static readonly Dictionary<string, string[]> MonsterDialogues = new(StringComparer.Ordinal)
        {
            ["스페르첸드"] = new[] { "어둠의 힘이 너희를 베고 지나가리라!" },
        };

        /// <summary>이 줄이 보스·몬스터 대사인지. 맞으면 에타 레벨·캐릭터·아이디 태그를 붙이지 않는다.</summary>
        public static bool IsMonsterDialogue(string? senderId, string? text)
        {
            if (string.IsNullOrWhiteSpace(senderId) || string.IsNullOrWhiteSpace(text))
                return false;

            if (!MonsterDialogues.TryGetValue(senderId.Trim(), out string[]? dialogues))
                return false;

            foreach (string dialogue in dialogues)
            {
                if (text.Contains(dialogue, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

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

        /// <summary>
        /// 형태가 정해진 안내문. 화자 유무와 무관하게 본문 전체에 대해 본다.
        /// 여기는 일반(흰색·내 채팅색) 줄만 지나간다 — 공지색(#64ff80) 안내는
        /// <see cref="NoisyNoticeFilter"/>가 맡는다 (자동 퇴장, 오를리 남은 공격 등).
        /// </summary>
        private static readonly Regex[] NormalIgnoredRegexes =
        {
            new(@"^(?:\[[^\]]+\]\s*)?(SP|MP|Fever|HP)가\s*\d+%\s*회복되었습니다\.?$", RegexOptions.Compiled),
            new(@"^(?:\[[^\]]+\]\s*)?체력이\s*\d+%\s*회복되었습니다\.?$", RegexOptions.Compiled),
            // 상자를 이미 먹었거나 꽝일 때 — "열었습니다"와 짝을 이루는 문구들
            new(@"(?:보상|상자)를?\s*이미\s*획득\s*하였습니다\.?$", RegexOptions.Compiled),
            new(@"보상을\s*획득하지\s*못했습니다\.?$", RegexOptions.Compiled),
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
                // 이름이 정확히 같을 때만 본다 — "설계자"로 시작한다는 이유로 "설계자123" 같은
                // 유저 아이디까지 숨기면 사람 말이 조용히 사라진다.
                string speaker = senderId.Trim();
                foreach (string ignored in IgnoredSpeakers)
                {
                    if (speaker.Equals(ignored, StringComparison.Ordinal))
                        return true;
                }

                // 게임이 내 아이디를 화자로 붙여 찍는 안내문도 있다 ("드드해 : 금화 주머니를 획득 했습니다.").
                // 문구가 통째로 일치할 때만 보므로 사람이 쓴 대화가 걸릴 일은 거의 없다.
                return MatchesNoticePhrase(message) || MatchesNoticeRegex(message);
            }

            // 화자가 없는 줄(안내문): 본문 포함 여부로 판정한다
            if (MatchesNoticePhrase(message))
                return true;

            foreach (string ignored in IgnoredSpeakers)
            {
                if (message.Contains(ignored, StringComparison.Ordinal))
                    return true;
            }

            return MatchesNoticeRegex(message);
        }

        private static bool MatchesNoticePhrase(string message)
        {
            foreach (string phrase in IgnoredNoticePhrases)
            {
                if (message.Contains(phrase, StringComparison.Ordinal))
                    return true;
            }

            return false;
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
