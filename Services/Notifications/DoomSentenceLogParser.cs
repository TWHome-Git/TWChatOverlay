using System.Text.RegularExpressions;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 설계자의 잔재 "운명 선고" 패턴의 채팅 로그 줄을 읽는다.
    ///
    /// 로그에 찍히는 것 (2026-11-12 테스트 서버 로그, 시스템 색 ff64ff):
    ///   ▶ 운명 선고 2회차 : 발판 6개 (제한 시간 16초)          ← 회차 시작. 같은 순간 게임 화면에 밟을 순서가 3초쯤 뜬다
    ///   ▶ 기쁨(←) 발판을 밟았습니다. (1 / 6)                  ← 맞게 밟을 때마다
    ///   ▶ 파훼 성공! 무적이 풀린 설계자의 잔재가 6초간 그로기에 빠집니다.   ← 다 밟음. 마지막 발판은 "밟았습니다 (n / n)" 줄 없이 이 줄이 온다
    ///   ▶ 제한 시간이 지나 운명 선고가 발동되었습니다. …        ← 실패 (시간 초과)
    ///   ▶ 틀린 발판을 밟아 운명 선고가 발동되었습니다. …        ← 실패 (틀린 발판)
    /// 밟을 순서(① 슬픔(↑) ② 즐거움(↓) …)는 화면 공지로만 뜨고 로그에는 남지 않는다 — 그래서 화면을 찍는다.
    /// </summary>
    public static class DoomSentenceLogParser
    {
        public enum LineKind
        {
            None,
            /// <summary>회차 시작: Round(회차) · Total(발판 수) · Seconds(제한 시간).</summary>
            RoundStart,
            /// <summary>발판 하나를 맞게 밟음: Done / Total.</summary>
            Step,
            /// <summary>실패로 선고가 발동됨 (그 회차는 끝).</summary>
            Triggered,
            /// <summary>발판을 모두 밟아 무적이 풀림 (그 회차는 성공으로 끝).</summary>
            Completed,
        }

        public readonly record struct Line(LineKind Kind, int Round, int Done, int Total, int Seconds);

        private static readonly Regex StartRegex = new(
            @"운명\s*선고\s*(?<round>\d+)\s*회차\s*:\s*발판\s*(?<total>\d+)\s*개\s*\(\s*제한\s*시간\s*(?<seconds>\d+)\s*초",
            RegexOptions.Compiled);

        private static readonly Regex StepRegex = new(
            @"발판을\s*밟았습니다\.?\s*\(\s*(?<done>\d+)\s*/\s*(?<total>\d+)\s*\)",
            RegexOptions.Compiled);

        private static readonly Regex TriggeredRegex = new(
            @"운명\s*선고가\s*발동되었습니다",
            RegexOptions.Compiled);

        // 다른 격리실의 "파훼 성공!"(가면 · 은신)과 구분하려고 "무적이 풀린"까지 본다
        private static readonly Regex CompletedRegex = new(
            @"파훼\s*성공.*무적이\s*풀린",
            RegexOptions.Compiled);

        public static Line Parse(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return default;
            // 대부분의 줄은 여기서 걸러진다
            if (!text.Contains("운명", System.StringComparison.Ordinal) && !text.Contains("발판을", System.StringComparison.Ordinal) &&
                !text.Contains("파훼", System.StringComparison.Ordinal))
                return default;

            Match start = StartRegex.Match(text);
            if (start.Success &&
                int.TryParse(start.Groups["round"].Value, out int round) &&
                int.TryParse(start.Groups["total"].Value, out int total) &&
                int.TryParse(start.Groups["seconds"].Value, out int seconds))
            {
                return new Line(LineKind.RoundStart, round, 0, total, seconds);
            }

            if (TriggeredRegex.IsMatch(text))
                return new Line(LineKind.Triggered, 0, 0, 0, 0);

            if (CompletedRegex.IsMatch(text))
                return new Line(LineKind.Completed, 0, 0, 0, 0);

            Match step = StepRegex.Match(text);
            if (step.Success &&
                int.TryParse(step.Groups["done"].Value, out int done) &&
                int.TryParse(step.Groups["total"].Value, out int stepTotal))
            {
                return new Line(LineKind.Step, 0, done, stepTotal, 0);
            }

            return default;
        }
    }
}
