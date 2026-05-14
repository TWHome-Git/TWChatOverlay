using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace TWChatOverlay.Services
{
    internal static class ShoutLineMergeHelper
    {
        private static readonly Regex ShoutLineRegex = new(
            @"^\s*<font[^>]*color=[""']?#?(?:white|ffffff)[""']?[^>]*>\s*(?<time>\[[^<]+?\])\s*</font>\s*<font[^>]*color=[""']?#?c896c8[""']?[^>]*>(?<content>.*?)</font>\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static List<string> MergeWrappedShoutLines(IReadOnlyList<string> lines)
        {
            if (lines.Count <= 1)
                return lines.ToList();

            var merged = new List<string>(lines.Count);
            int index = 0;
            while (index < lines.Count)
            {
                string current = lines[index].Trim();
                if (index + 1 < lines.Count &&
                    TryMergeWrappedShout(current, lines[index + 1], out string joined))
                {
                    merged.Add(joined);
                    index += 2;
                    continue;
                }

                merged.Add(current);
                index++;
            }

            return merged;
        }

        private static bool TryMergeWrappedShout(string firstLine, string secondLine, out string mergedLine)
        {
            mergedLine = firstLine;

            var first = ShoutLineRegex.Match(firstLine);
            var second = ShoutLineRegex.Match(secondLine);
            if (!first.Success || !second.Success)
                return false;

            string time1 = NormalizeWhitespace(WebUtility.HtmlDecode(first.Groups["time"].Value));
            string time2 = NormalizeWhitespace(WebUtility.HtmlDecode(second.Groups["time"].Value));
            if (!string.Equals(time1, time2, StringComparison.Ordinal))
                return false;

            string content1 = first.Groups["content"].Value;
            string content2 = second.Groups["content"].Value;
            string plain1 = NormalizeWhitespace(WebUtility.HtmlDecode(content1));
            string plain2 = NormalizeWhitespace(WebUtility.HtmlDecode(content2));

            bool firstLooksLikeShout = plain1.Contains("외치기", StringComparison.OrdinalIgnoreCase);
            bool secondLooksLikeContinuation = !plain2.Contains("외치기", StringComparison.OrdinalIgnoreCase);
            if (!firstLooksLikeShout || !secondLooksLikeContinuation)
                return false;

            mergedLine = $@"<font color=""white"">{first.Groups["time"].Value}</font><font color=""#c896c8"">{content1}{content2}</font>";
            return true;
        }

        private static string NormalizeWhitespace(string text)
            => Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
    }
}
