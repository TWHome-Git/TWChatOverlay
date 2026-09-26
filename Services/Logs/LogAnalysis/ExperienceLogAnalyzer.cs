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
            long gained = ExtractGain(context.ChatContent, context.IsSystemLog);
            if (gained != 0)
                context.Result.GainedExp = gained;
        }

        /// <summary>
        /// 한 줄에서 경험치 증감을 읽는다. 없으면 0, 감소 문구는 음수.
        /// 지난 로그를 훑어 사냥 기록을 복원할 때도 같은 규칙을 쓰도록 여기로 모아 둔다.
        /// </summary>
        public static long ExtractGain(string? content, bool isSystemLog)
        {
            string chatContent = content ?? string.Empty;

            // 모든 경험치 획득/증감 메시지와 특수 보상 문자열(별동대 토벌 보상)은 "경험치"를 포함한다.
            // 포함하지 않는 라인은 어떤 정규식도 매칭될 수 없으므로, 비싼 Regex.Replace + 정규식 매칭을
            // 건너뛴다. 결과는 완전히 동일하며 일반 채팅(대부분)의 처리 비용만 제거한다.
            if (!chatContent.Contains("경험치", StringComparison.Ordinal))
                return 0;

            string normalized = Regex.Replace(chatContent, @"\s+", " ").Trim();
            if (normalized.Contains(DetachedForceExpText, StringComparison.Ordinal))
                return DetachedForceExpValue;

            if (chatContent.Contains("룬 경험치", StringComparison.Ordinal) ||
                chatContent.Replace(" ", string.Empty).Contains("룬경험치", StringComparison.Ordinal))
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
                return 0;

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
