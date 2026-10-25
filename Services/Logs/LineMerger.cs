using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 게임이 긴 외치기·시스템 메시지를 같은 시각으로 여러 줄에 걸쳐 출력할 때(줄바꿈) 조각을 원래 한 줄로 합친다.
    /// 실시간 경로(LogService)와 과거 로그 스캔(RawChatLogReader)이 같은 인스턴스 로직을 쓰므로 두 경로의 결과가 같다.
    ///
    /// 규칙
    ///  - 외치기(c896c8): 같은 시각의 인접한 두 줄 중 앞 줄에 "외치기"가 있고 뒷 줄에는 없으면 하나로 (두 줄까지만).
    ///  - 시스템(ff64ff/00ffff/ff6464): 앞 줄이 종결부호로 끝나지 않고, 같은 시각·같은 색의 다음 조각이 짧고(8자 이하) 공백이 없으면
    ///    이어붙인다. 최대 <see cref="MaxMergeSegments"/>조각까지(원래 줄 + 4조각). "클리어 횟수: 3" 같은 카운트 줄은 건드리지 않는다.
    ///
    /// 스트리밍으로 동작한다: <see cref="Push"/>로 줄을 넣으면 확정된 줄만 sink로 내보내고, 끝에 <see cref="Flush"/>로 남은 줄을 내보낸다.
    /// 전체 목록을 한 번에 처리하는 <see cref="MergeAll"/>도 같은 로직을 쓴다.
    /// </summary>
    public sealed class LineMerger
    {
        private static readonly Regex ShoutLineRegex = new(
            @"^\s*<font[^>]*color=[""']?#?(?:white|ffffff)[""']?[^>]*>\s*(?<time>\[[^<]+?\])\s*</font>\s*<font[^>]*color=[""']?#?c896c8[""']?[^>]*>(?<content>.*?)</font>\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // 끝에 </br>/<br>가 붙어 있어도 매칭되도록 허용한다 (물리적 줄을 그대로 넘기는 호출자 대비).
        private static readonly Regex SystemLineRegex = new(
            @"^\s*<font[^>]*color=[""']?#?(?:white|ffffff)[""']?[^>]*>\s*(?<time>\[[^<]+?\])\s*</font>\s*<font[^>]*color=[""']?#?(?<color>ff64ff|00ffff|ff6464)[""']?[^>]*>(?<content>.*?)</font>\s*(?:</?br\s*>)?\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex WhitespaceRunRegex = new(@"\s+", RegexOptions.Compiled);

        private const int MaxContinuationLength = 8;
        /// <summary>원래 줄 뒤에 이어붙일 수 있는 시스템 조각 수 상한.</summary>
        public const int MaxMergeSegments = 4;

        private readonly Action<string> _sink;

        // 1단계(외치기): 다음 줄과 합칠지 보려고 들고 있는 줄
        private string? _shoutPending;
        private Match? _shoutPendingMatch;

        // 2단계(시스템): 이어붙이는 중인 머리 줄
        private string? _systemHead;
        private string _systemTime = string.Empty;
        private string _systemTimeKey = string.Empty;
        private string _systemColor = string.Empty;
        private string _systemContent = string.Empty;
        private string _systemPlain = string.Empty;
        private int _systemConsumed;

        public LineMerger(Action<string> sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <summary>전체 줄 목록을 한 번에 병합한다 (실시간 경로의 청크 처리용).</summary>
        public static List<string> MergeAll(IReadOnlyList<string> lines)
        {
            var result = new List<string>(lines.Count);
            if (lines.Count == 0)
                return result;

            var merger = new LineMerger(result.Add);
            foreach (string line in lines)
                merger.Push(line);
            merger.Flush();
            return result;
        }

        /// <summary>줄 하나를 넣는다. 확정된 줄(Trim 적용)만 sink로 나간다. 빈 줄은 무시한다.</summary>
        public void Push(string? rawLine)
        {
            if (string.IsNullOrWhiteSpace(rawLine))
                return;

            string current = rawLine.Trim();
            Match currentMatch = MatchShoutLine(current);

            if (_shoutPending == null)
            {
                _shoutPending = current;
                _shoutPendingMatch = currentMatch;
                return;
            }

            if (TryMergeWrappedShout(_shoutPendingMatch!, currentMatch, out string joined))
            {
                // 합친 결과는 다음 줄과 다시 합치지 않는다 (목록 병합과 같은 규칙: 두 줄까지만)
                PushToSystemStage(joined);
                _shoutPending = null;
                _shoutPendingMatch = null;
                return;
            }

            PushToSystemStage(_shoutPending);
            _shoutPending = current;
            _shoutPendingMatch = currentMatch;
        }

        /// <summary>들고 있는 줄을 모두 내보낸다. 파일 끝·청크 끝에서 부른다.</summary>
        public void Flush()
        {
            if (_shoutPending != null)
            {
                PushToSystemStage(_shoutPending);
                _shoutPending = null;
                _shoutPendingMatch = null;
            }
            EmitSystemHead();
        }

        // ── 2단계: 시스템 줄바꿈 ──

        private void PushToSystemStage(string line)
        {
            Match m = MatchSystemLine(line);

            if (_systemHead != null)
            {
                if (!EndsWithTerminator(_systemPlain) &&
                    _systemConsumed < MaxMergeSegments &&
                    m.Success &&
                    string.Equals(_systemColor, m.Groups["color"].Value, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_systemTimeKey, NormalizeWhitespace(WebUtility.HtmlDecode(m.Groups["time"].Value)), StringComparison.Ordinal))
                {
                    string nextPlain = NormalizeWhitespace(WebUtility.HtmlDecode(m.Groups["content"].Value));
                    // 이어붙일 조각은 짧고 공백이 없어야 한다(별개 메시지 오병합 방지).
                    if (nextPlain.Length > 0 && nextPlain.Length <= MaxContinuationLength && !nextPlain.Contains(' '))
                    {
                        _systemContent += m.Groups["content"].Value;
                        _systemPlain = NormalizeWhitespace(WebUtility.HtmlDecode(_systemContent));
                        _systemConsumed++;
                        return;
                    }
                }

                EmitSystemHead();
            }

            if (!m.Success)
            {
                _sink(line);
                return;
            }

            _systemHead = line;
            _systemTime = m.Groups["time"].Value;
            _systemColor = m.Groups["color"].Value;
            _systemTimeKey = NormalizeWhitespace(WebUtility.HtmlDecode(_systemTime));
            _systemContent = m.Groups["content"].Value;
            _systemPlain = NormalizeWhitespace(WebUtility.HtmlDecode(_systemContent));
            _systemConsumed = 0;
        }

        private void EmitSystemHead()
        {
            if (_systemHead == null)
                return;

            string head = _systemHead;
            _systemHead = null;
            if (_systemConsumed > 0)
                _sink($@"<font color=""white"">{_systemTime}</font><font color=""#{_systemColor}"">{_systemContent}</font>");
            else
                _sink(head);
        }

        private static bool EndsWithTerminator(string plain)
        {
            if (string.IsNullOrEmpty(plain))
                return true;

            char last = plain[^1];
            return last is '.' or '!' or '?' or '…' or '"' or ')' or ']' or '」' or '』';
        }

        // 시스템 색 3종(ff64ff/00ffff/ff6464) 중 하나도 없는 줄은 정규식이 맞을 수 없다 (IgnoreCase라 대소문자 무시로 찾는다)
        private static Match MatchSystemLine(string line)
            => line.Contains("ff64ff", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("00ffff", StringComparison.OrdinalIgnoreCase) ||
               line.Contains("ff6464", StringComparison.OrdinalIgnoreCase)
                ? SystemLineRegex.Match(line)
                : Match.Empty;

        // ── 1단계: 외치기 줄바꿈 ──

        // 외치기 색(c896c8)이 없는 줄은 정규식이 맞을 수 없다
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

        private static string NormalizeWhitespace(string text)
            => WhitespaceRunRegex.Replace(text ?? string.Empty, " ").Trim();
    }
}
