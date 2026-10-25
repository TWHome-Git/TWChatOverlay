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
            // 직전 반복에서 lines[index]에 이미 돌린 매치 결과. 정규식이 앞뒤 공백(^\s* … \s*$)을 허용하므로
            // Trim 전 줄에 돌린 결과와 Trim 후 결과의 성공 여부·그룹 값이 같다.
            Match? carried = null;
            while (index < lines.Count)
            {
                string current = lines[index].Trim();
                Match first = carried ?? MatchShoutLine(current);
                carried = null;
                if (index + 1 < lines.Count)
                {
                    Match second = MatchShoutLine(lines[index + 1]);
                    if (TryMergeWrappedShout(first, second, out string joined))
                    {
                        merged.Add(joined);
                        index += 2;
                        continue;
                    }

                    carried = second;
                }

                merged.Add(current);
                index++;
            }

            return merged;
        }

        // 외치기 색(c896c8)이 없는 줄은 정규식이 맞을 수 없다 (IgnoreCase라 대소문자 무시로 찾는다)
        private static Match MatchShoutLine(string line)
            => line.Contains("c896c8", StringComparison.OrdinalIgnoreCase) ? ShoutLineRegex.Match(line) : Match.Empty;

        private static bool TryMergeWrappedShout(Match first, Match second, out string mergedLine)
        {
            mergedLine = string.Empty;

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

        private static readonly Regex WhitespaceRunRegex = new(@"\s+", RegexOptions.Compiled);

        private static string NormalizeWhitespace(string text)
            => WhitespaceRunRegex.Replace(text ?? string.Empty, " ").Trim();
    }
}
