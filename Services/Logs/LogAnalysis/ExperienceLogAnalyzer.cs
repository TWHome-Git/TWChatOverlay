using System;
using System.Text.RegularExpressions;

namespace TWChatOverlay.Services.LogAnalysis
{
    public sealed class ExperienceLogAnalyzer
    {
        private const string DetachedForceExpText = "별동대 토벌 보상으로 경험치 1억을 획득했습니다.";
        private const long DetachedForceExpValue = 100_000_000L;

        // "아름다운 음율의 힘으로 경험치가 상승하였습니다. 경험치 상승량 : 8억" —
        // 증가량이 뒤에 한글 단위로 따로 적히는 문구. 숫자가 붙어 있지 않아 아래 ExpRegexes로는 잡히지 않는다.
        private static readonly Regex ExpAmountRegex = new(
            @"경험치\s*(?:상승량|획득량|증가량)\s*[:：]?\s*(?<amount>(?:[\d,]+\s*[조억만]?\s*)+)",
            RegexOptions.Compiled);

        // 대괄호·한글 단위·조사가 섞여 위 정규식들이 못 읽는 획득 문구 (시스템 줄에서만 본다):
        //   "경험치 1000만을 획득 했습니다" · "경험치가 [20억] 올랐습니다" · "경험치가 [289,753,322]만큼 지급되었습니다"
        //   "경험치 [3200000]을 추가로 획득하였습니다" · "경험치 [10억]이(가) 지급 되었습니다"
        private static readonly Regex LooseGainRegex = new(
            @"경험치\s*(?:가|를|을)?\s*\[?\s*(?<amount>[\d,]+\s*[조억만]?(?:\s*[\d,]+\s*[조억만])*)\s*\]?\s*(?:만큼|이\(가\)|을|를|이|가)?\s*(?:추가로\s*)?(?:획득\s*(?:하였|했)습니다|지급\s*(?:되었|됐)습니다|올랐습니다|상승했습니다)",
            RegexOptions.Compiled);

        // 몬스터를 잡아 얻는 경험치 줄 — 앞뒤에 다른 말이 없는 "경험치가 N 올랐습니다." 하나뿐이다 (285일치 로그 기준).
        // 탐험·별동대·훈련소·축제·보물상자 같은 보상은 앞에 이유가 붙거나 숫자가 대괄호에 싸여 있어 여기 걸리지 않는다.
        private static readonly Regex HuntingGainRegex = new(@"^경험치가\s*[\d,]+\s*올랐습니다\.?$", RegexOptions.Compiled);

        /// <summary>
        /// 사냥(몬스터 처치)으로 얻은 경험치 줄인지. 경험치 추적창과 사냥 기록은 이 줄만 센다 —
        /// 보상 경험치가 섞이면 사냥을 멈춘 뒤에 들어온 탐험 보상 하나로 추적창이 새로 시작되거나 판 합계가 부풀기 때문이다.
        /// (누적 경험치 알림처럼 실제로 쌓인 경험치가 필요한 곳은 보상까지 모두 센다)
        /// </summary>
        public static bool IsHuntingGain(string? content)
            => !string.IsNullOrEmpty(content) && HuntingGainRegex.IsMatch(content.Trim());

        // "1조 2000억 3만" 처럼 한글 단위가 섞인 수. 단위가 없으면 그대로 더한다.
        private static readonly Regex KoreanUnitRegex = new(@"(?<num>[\d,]+)\s*(?<unit>[조억만])?", RegexOptions.Compiled);

        private static readonly Regex[] ExpRegexes =
        {
            new(@"\uACBD\uD5D8\uCE58(?:\uAC00|\uC744|\uB97C)?\s*\[?(?<exp>[\d,]+)\]?\s*(?:\uC744|\uB97C)?\s*\uD68D\uB4DD(?:\uD558\uC600|\uD588)\uC2B5\uB2C8\uB2E4\.?", RegexOptions.Compiled),
            new(@"\[?(?<exp>[\d,]+)\]?\s*(?:\uC758\s*)?\uACBD\uD5D8\uCE58(?:\uB97C|\uC744)?\s*\uD68D\uB4DD(?:\uD558\uC600|\uD588)\uC2B5\uB2C8\uB2E4\.?", RegexOptions.Compiled),
            new(@"\uACBD\uD5D8\uCE58\s*\[?(?<exp>[\d,]+)\]?\s*(?:\uC774|\uAC00)?\s*\uC9C0\uAE09\uB418\uC5C8\uC2B5\uB2C8\uB2E4\.?", RegexOptions.Compiled),
            new(@"\uACBD\uD5D8\uCE58(?:\uAC00)?\s*\[?(?<exp>[\d,]+)\]?\s*(?:\uC62C\uB790\uC2B5\uB2C8\uB2E4|\uC0C1\uC2B9\uD588\uC2B5\uB2C8\uB2E4|\uC99D\uAC00\uD558\uC600\uC2B5\uB2C8\uB2E4)\.?", RegexOptions.Compiled),
            new(@"\uACBD\uD5D8\uCE58(?:\uAC00)?\s*\[?(?<exp>[\d,]+)\]?\s*\uAC10\uC18C\uD588\uC2B5\uB2C8\uB2E4\.?", RegexOptions.Compiled),
            new(@"\uACBD\uD5D8\uCE58(?:\uAC00)?\s*\[?(?<exp>[\d,]+)\]?\s*\uC904\uC5C8\uC2B5\uB2C8\uB2E4\.?", RegexOptions.Compiled)
        };

        public void Analyze(LogLineContext context)
        {
            // ChatContent는 ChatLogAnalyzer가 이미 공백을 정규화하고 Trim한 값이다
            long gained = ExtractGain(context.ChatContent, context.IsSystemLog, isWhitespaceNormalized: true);
            if (gained != 0)
            {
                context.Result.GainedExp = gained;
                context.Result.IsHuntingExp = gained > 0 && IsHuntingGain(context.ChatContent);
            }
        }

        /// <summary>
        /// 한 줄에서 경험치 증감을 읽는다. 없으면 0, 감소 문구는 음수.
        /// 지난 로그를 훑어 사냥 기록을 복원할 때도 같은 규칙을 쓰도록 여기로 모아 둔다.
        /// </summary>
        public static long ExtractGain(string? content, bool isSystemLog)
            => ExtractGain(content, isSystemLog, isWhitespaceNormalized: false);

        private static readonly Regex WhitespaceRunRegex = new(@"\s+", RegexOptions.Compiled);

        /// <param name="isWhitespaceNormalized">입력이 이미 공백 연속을 한 칸으로 줄이고 Trim한 값이면 true — 다시 정규화하지 않는다.</param>
        private static long ExtractGain(string? content, bool isSystemLog, bool isWhitespaceNormalized)
        {
            string chatContent = content ?? string.Empty;

            // 모든 경험치 획득/증감 메시지와 특수 보상 문자열(별동대 토벌 보상)은 "경험치"를 포함한다.
            // 포함하지 않는 라인은 어떤 정규식도 매칭될 수 없으므로, 비싼 Regex.Replace + 정규식 매칭을
            // 건너뛴다. 결과는 완전히 동일하며 일반 채팅(대부분)의 처리 비용만 제거한다.
            if (!chatContent.Contains("경험치", StringComparison.Ordinal))
                return 0;

            string normalized = isWhitespaceNormalized
                ? chatContent
                : WhitespaceRunRegex.Replace(chatContent, " ").Trim();
            if (normalized.Contains(DetachedForceExpText, StringComparison.Ordinal))
                return DetachedForceExpValue;

            // 룬 경험치·소울링크(링크 캐릭터) 경험치는 내 캐릭터 경험치가 아니다 (' '는 무시하고 비교)
            if (ContainsIgnoringSpaces(chatContent, "룬경험치") ||
                ContainsIgnoringSpaces(chatContent, "소울링크경험치") ||
                ContainsIgnoringSpaces(chatContent, "링크캐릭터경험치"))
            {
                return 0;
            }

            // 상승량이 따로 적히는 문구는 시스템 줄에서만 센다 (다른 사람이 채팅으로 같은 말을 해도 세지 않게)
            Match amountMatch = isSystemLog ? ExpAmountRegex.Match(normalized) : Match.Empty;
            if (amountMatch.Success && TryParseKoreanAmount(amountMatch.Groups["amount"].Value, out long amount))
                return amount;

            Match? expMatch = null;
            foreach (var regex in ExpRegexes)
            {
                expMatch = regex.Match(chatContent);
                if (expMatch.Success)
                    break;
            }

            if (expMatch == null || !expMatch.Success)
            {
                Match loose = isSystemLog ? LooseGainRegex.Match(normalized) : Match.Empty;
                return loose.Success && TryParseKoreanAmount(loose.Groups["amount"].Value, out long looseAmount) ? looseAmount : 0;
            }

            string expText = expMatch.Groups["exp"].Value.Replace(",", string.Empty);
            if (long.TryParse(expText, out long expValue))
            {
                // 감소 메시지("... 감소했습니다" / "... 줄었습니다")는 음수로 기록한다.
                // (기존에는 "감소"만 검사해 "줄었습니다" 감소가 양수로 잘못 기록되던 버그를 수정)
                if (expMatch.Value.Contains("감소", StringComparison.Ordinal) ||
                    expMatch.Value.Contains("줄었", StringComparison.Ordinal))
                {
                    expValue = -expValue;
                }

                return expValue;
            }

            return 0;
        }

        /// <summary>
        /// <c>text.Replace(" ", "").Contains(needle, Ordinal)</c>과 같은 결과를 새 문자열 없이 낸다.
        /// needle에는 ' '가 없어야 한다.
        /// </summary>
        private static bool ContainsIgnoringSpaces(string text, string needle)
        {
            for (int start = 0; start < text.Length; start++)
            {
                if (text[start] != needle[0])
                    continue;

                int matched = 1;
                for (int i = start + 1; i < text.Length && matched < needle.Length; i++)
                {
                    char c = text[i];
                    if (c == ' ')
                        continue;
                    if (c != needle[matched])
                        break;
                    matched++;
                }

                if (matched == needle.Length)
                    return true;
            }

            return false;
        }

        /// <summary>"8억", "1조 2000억", "35000" 꼴의 수를 숫자로. 하나도 못 읽으면 false.</summary>
        private static bool TryParseKoreanAmount(string text, out long value)
        {
            value = 0;
            bool any = false;
            foreach (Match match in KoreanUnitRegex.Matches(text))
            {
                string digits = match.Groups["num"].Value.Replace(",", string.Empty);
                if (digits.Length == 0 || !long.TryParse(digits, out long number))
                    continue;

                long unit = match.Groups["unit"].Value switch
                {
                    "조" => 1_000_000_000_000L,
                    "억" => 100_000_000L,
                    "만" => 10_000L,
                    _ => 1L,
                };
                value += number * unit;
                any = true;
            }
            return any && value > 0;
        }
    }
}
