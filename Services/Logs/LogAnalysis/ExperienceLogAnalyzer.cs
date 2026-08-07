using System;
using System.Text.RegularExpressions;

namespace TWChatOverlay.Services.LogAnalysis
{
    public sealed class ExperienceLogAnalyzer
    {
        private const string DetachedForceExpText = "별동대 토벌 보상으로 경험치 1억을 획득했습니다.";
        private const long DetachedForceExpValue = 100_000_000L;

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
            string chatContent = context.ChatContent ?? string.Empty;

            // 모든 경험치 획득/증감 메시지와 특수 보상 문자열(별동대 토벌 보상)은 "경험치"를 포함한다.
            // 포함하지 않는 라인은 어떤 정규식도 매칭될 수 없으므로, 비싼 Regex.Replace + 정규식 매칭을
            // 건너뛴다. 결과는 완전히 동일하며 일반 채팅(대부분)의 처리 비용만 제거한다.
            if (!chatContent.Contains("경험치", StringComparison.Ordinal))
                return;

            string normalized = Regex.Replace(chatContent, @"\s+", " ").Trim();
            if (normalized.Contains(DetachedForceExpText, StringComparison.Ordinal))
            {
                context.Result.GainedExp = DetachedForceExpValue;
                return;
            }

            if (chatContent.Contains("룬 경험치", StringComparison.Ordinal) ||
                chatContent.Replace(" ", string.Empty).Contains("룬경험치", StringComparison.Ordinal))
            {
                return;
            }

            Match? expMatch = null;
            foreach (var regex in ExpRegexes)
            {
                expMatch = regex.Match(chatContent);
                if (expMatch.Success)
                    break;
            }

            if (expMatch == null || !expMatch.Success)
                return;

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

                context.Result.GainedExp = expValue;
            }
        }
    }
}
