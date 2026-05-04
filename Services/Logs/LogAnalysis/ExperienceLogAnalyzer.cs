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
                if (expMatch.Value.Contains("감소", StringComparison.Ordinal))
                {
                    expValue = -expValue;
                }

                context.Result.GainedExp = expValue;
            }
        }
    }
}
