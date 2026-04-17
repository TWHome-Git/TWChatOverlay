using System.Text.RegularExpressions;

namespace TWChatOverlay.Services.LogAnalysis
{
    public sealed class ExperienceLogAnalyzer
    {
        private static readonly Regex[] ExpRegexes =
        {
            new(@"\uACBD\uD5D8\uCE58(?:\uAC00|\uC744|\uB97C)?\s*(?<exp>[\d,]+)\s*(?:\uC744|\uB97C)?\s*\uD68D\uB4DD(?:\uD558\uC600|\uD588)\uC2B5\uB2C8\uB2E4\.?", RegexOptions.Compiled),
            new(@"(?<exp>[\d,]+)\s*(?:\uC758\s*)?\uACBD\uD5D8\uCE58(?:\uB97C|\uC744)?\s*\uD68D\uB4DD(?:\uD558\uC600|\uD588)\uC2B5\uB2C8\uB2E4\.?", RegexOptions.Compiled),
            new(@"\uACBD\uD5D8\uCE58\s*(?<exp>[\d,]+)\s*(?:\uC774|\uAC00)?\s*\uC9C0\uAE09\uB418\uC5C8\uC2B5\uB2C8\uB2E4\.?", RegexOptions.Compiled),
            new(@"\uACBD\uD5D8\uCE58(?:\uAC00)?\s*(?<exp>[\d,]+)\s*(?:\uC62C\uB790\uC2B5\uB2C8\uB2E4|\uC99D\uAC00\uD558\uC600\uC2B5\uB2C8\uB2E4)\.?", RegexOptions.Compiled)
        };

        public void Analyze(LogLineContext context)
        {
            Match? expMatch = null;
            foreach (var regex in ExpRegexes)
            {
                expMatch = regex.Match(context.ChatContent);
                if (expMatch.Success)
                    break;
            }

            if (expMatch == null || !expMatch.Success)
                return;

            string expText = expMatch.Groups["exp"].Value.Replace(",", string.Empty);
            if (long.TryParse(expText, out long expValue))
            {
                context.Result.GainedExp = expValue;
            }
        }
    }
}
