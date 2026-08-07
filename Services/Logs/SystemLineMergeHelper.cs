using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 게임이 긴 시스템 메시지를 같은 타임스탬프로 여러 줄에 걸쳐 출력할 때(줄바꿈),
    /// 잘린 조각들을 원래 한 줄로 합친다.
    /// 예) "...추가 획득하였습니" + "다." → "...추가 획득하였습니다."
    /// 오병합을 막기 위해 (첫 줄이 종결부호로 끝나지 않음) + (다음 조각이 짧고 공백 없음) +
    /// (같은 시각·같은 시스템 색상)일 때만 합친다. "클리어 횟수: 3" 같은 카운트 로그는 건드리지 않는다.
    /// </summary>
    internal static class SystemLineMergeHelper
    {
        // 시스템 계열 색상(ff64ff/00ffff/ff6464)만 대상으로 한다.
        // 끝에 </br>/<br>가 붙어 있어도 매칭되도록 허용한다(아카이브 스캔은 물리적 줄을 읽어 </br>가 남아 있음).
        private static readonly Regex SystemLineRegex = new(
            @"^\s*<font[^>]*color=[""']?#?(?:white|ffffff)[""']?[^>]*>\s*(?<time>\[[^<]+?\])\s*</font>\s*<font[^>]*color=[""']?#?(?<color>ff64ff|00ffff|ff6464)[""']?[^>]*>(?<content>.*?)</font>\s*(?:</?br\s*>)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const int MaxContinuationLength = 8;
        private const int MaxMergeSegments = 4;

        public static List<string> MergeWrappedSystemLines(IReadOnlyList<string> lines)
        {
            if (lines.Count <= 1)
                return lines.ToList();

            var merged = new List<string>(lines.Count);
            int i = 0;
            while (i < lines.Count)
            {
                var m = SystemLineRegex.Match(lines[i].Trim());
                if (!m.Success)
                {
                    merged.Add(lines[i].Trim());
                    i++;
                    continue;
                }

                string time = m.Groups["time"].Value;
                string color = m.Groups["color"].Value;
                string timeKey = NormalizeWhitespace(WebUtility.HtmlDecode(time));
                string content = m.Groups["content"].Value;
                string plain = NormalizeWhitespace(WebUtility.HtmlDecode(content));

                int consumed = 0;
                while (!EndsWithTerminator(plain) &&
                       consumed < MaxMergeSegments &&
                       i + 1 + consumed < lines.Count)
                {
                    var next = SystemLineRegex.Match(lines[i + 1 + consumed].Trim());
                    if (!next.Success)
                        break;
                    if (!string.Equals(color, next.Groups["color"].Value, StringComparison.OrdinalIgnoreCase))
                        break;
                    if (!string.Equals(timeKey, NormalizeWhitespace(WebUtility.HtmlDecode(next.Groups["time"].Value)), StringComparison.Ordinal))
                        break;

                    string nextPlain = NormalizeWhitespace(WebUtility.HtmlDecode(next.Groups["content"].Value));
                    // 이어붙일 조각은 짧고 공백이 없어야 한다(별개 메시지 오병합 방지).
                    if (nextPlain.Length == 0 || nextPlain.Length > MaxContinuationLength || nextPlain.Contains(' '))
                        break;

                    content += next.Groups["content"].Value;
                    plain = NormalizeWhitespace(WebUtility.HtmlDecode(content));
                    consumed++;
                }

                if (consumed > 0)
                    merged.Add($@"<font color=""white"">{time}</font><font color=""#{color}"">{content}</font>");
                else
                    merged.Add(lines[i].Trim());

                i += 1 + consumed;
            }

            return merged;
        }

        private static bool EndsWithTerminator(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                return true;

            char last = plain[^1];
            return last is '.' or '!' or '?' or '…' or '"' or ')' or ']' or '」' or '』';
        }

        private static string NormalizeWhitespace(string text)
            => Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
    }
}
