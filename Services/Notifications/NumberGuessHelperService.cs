using System;
using System.Text.RegularExpressions;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 고대 렐릭의 성소 "숫자 맞추기(UP &amp; DOWN)" 미션 도우미 (2026-11-21 사용자 요청).
    /// 시스템 줄만 읽어 정답이 남아 있는 범위와 다음에 넣을 숫자를 던전 알림 창에 띄운다.
    ///
    ///   ★ 미션 목표 : 임의로 정해지는 1 ~ 50 사이의 미션 숫자를 맞추세요!   ← 시작 (범위는 줄에 적힌 대로)
    ///   입력한 숫자 [30] 보다 큽니다.                                      ← 정답이 30보다 크다
    ///   입력한 숫자 [35] 보다 적습니다.                                    ← 정답이 35보다 작다
    ///   입력한 숫자 [8] : 정답입니다.                                      ← 끝
    ///
    /// 뜻은 실제 기록으로 확인했다 (2026-11-20: 25 적습니다 → 15 적습니다 → 5 큽니다 → 10 적습니다 → 8 정답).
    /// 힌트 줄은 누가 넣었든 함께 찍히는 것으로 보이며, 어느 쪽이든 본 줄을 모두 반영하면 범위가 맞는다.
    /// 다음 숫자는 남은 범위의 가운데 — 1~50이면 많아도 6번에 끝난다.
    ///
    /// 정답 줄 없이 끝나는 판도 있다(시간 초과 · 미션 다시 시작). 다음 미션 목표 줄이나 클리어 횟수 줄에서 닫고,
    /// 그것도 못 보면 알림 창의 안전 상한(3분)이 닫는다. 호출하는 쪽에서 실시간 시스템 줄만 넘긴다.
    /// </summary>
    public sealed class NumberGuessHelperService
    {
        public enum LineKind
        {
            None,
            /// <summary>숫자 맞추기 미션 시작.</summary>
            Start,
            /// <summary>힌트를 반영했다.</summary>
            Hint,
            /// <summary>정답을 맞혔다.</summary>
            Correct,
            /// <summary>다른 미션이 시작됐거나 던전이 끝났다.</summary>
            Ended,
        }

        public const int DefaultLow = 1;
        public const int DefaultHigh = 50;

        // 줄 앞의 시각("[17시  4분 42초] ")은 있어도 없어도 된다. 시각 바로 다음에 와야 잡는다 — 채팅에 같은 말이 섞인 줄은 건드리지 않는다
        private const string LineStart = @"^\s*(?:\[[^\]]*\]\s*)?";

        private static readonly Regex StartRegex = new(
            LineStart + @"★\s*미션\s*목표\s*:\s*임의로\s*정해지는\s*(?<low>\d+)\s*~\s*(?<high>\d+)\s*사이의\s*미션\s*숫자를\s*맞추세요",
            RegexOptions.Compiled);

        private static readonly Regex HintRegex = new(
            LineStart + @"입력한\s*숫자\s*\[\s*(?<n>\d+)\s*\]\s*(?:(?<up>보다\s*큽니다)|(?<down>보다\s*(?:적|작)습니다)|(?<ok>:\s*정답입니다))",
            RegexOptions.Compiled);

        private static readonly Regex OtherMissionRegex = new(LineStart + @"★\s*미션\s*목표\s*:", RegexOptions.Compiled);
        private static readonly Regex ClearCountRegex = new(@"렐릭의\s*성소.*클리어\s*횟수", RegexOptions.Compiled);

        /// <summary>이 시간 넘게 소식이 없던 범위는 지난 판의 것으로 보고 버린다 (시작 줄을 못 본 채 힌트부터 들어온 경우).</summary>
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

        private DateTime _lastUpdateUtc = DateTime.MinValue;

        /// <summary>판이 진행 중인지.</summary>
        public bool IsActive { get; private set; }
        /// <summary>정답이 될 수 있는 가장 작은 수.</summary>
        public int Low { get; private set; } = DefaultLow;
        /// <summary>정답이 될 수 있는 가장 큰 수.</summary>
        public int High { get; private set; } = DefaultHigh;
        /// <summary>다음에 넣을 숫자 — 남은 범위의 가운데.</summary>
        public int Suggestion => Low + (High - Low) / 2;

        /// <summary>줄 하나를 반영한다. 화면은 건드리지 않는다.</summary>
        public LineKind Apply(string? text, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(text))
                return LineKind.None;

            if (text.Contains("미션", StringComparison.Ordinal))
            {
                Match start = StartRegex.Match(text);
                if (start.Success &&
                    int.TryParse(start.Groups["low"].Value, out int low) &&
                    int.TryParse(start.Groups["high"].Value, out int high) &&
                    low <= high)
                {
                    Reset(low, high, nowUtc);
                    return LineKind.Start;
                }

                if (IsActive && OtherMissionRegex.IsMatch(text))
                {
                    IsActive = false;
                    return LineKind.Ended;
                }

                return LineKind.None;
            }

            if (text.Contains("입력한", StringComparison.Ordinal))
            {
                Match hint = HintRegex.Match(text);
                if (!hint.Success || !int.TryParse(hint.Groups["n"].Value, out int n))
                    return LineKind.None;

                if (hint.Groups["ok"].Success)
                {
                    IsActive = false;
                    return LineKind.Correct;
                }

                bool answerIsHigher = hint.Groups["up"].Success;
                if (!IsActive || nowUtc - _lastUpdateUtc > StaleAfter)
                    Reset(DefaultLow, DefaultHigh, nowUtc);

                Narrow(n, answerIsHigher);
                if (Low > High)
                {
                    // 앞서 본 힌트와 어긋난다 — 시작 줄을 놓친 새 판이다. 이 힌트만으로 다시 잡는다
                    Reset(DefaultLow, DefaultHigh, nowUtc);
                    Narrow(n, answerIsHigher);
                    if (Low > High)
                        Reset(DefaultLow, DefaultHigh, nowUtc); // 범위 밖 숫자의 힌트는 버린다
                }

                _lastUpdateUtc = nowUtc;
                return LineKind.Hint;
            }

            if (IsActive && text.Contains("클리어", StringComparison.Ordinal) && ClearCountRegex.IsMatch(text))
            {
                IsActive = false;
                return LineKind.Ended;
            }

            return LineKind.None;
        }

        private void Reset(int low, int high, DateTime nowUtc)
        {
            Low = low;
            High = high;
            IsActive = true;
            _lastUpdateUtc = nowUtc;
        }

        private void Narrow(int guess, bool answerIsHigher)
        {
            if (answerIsHigher)
                Low = Math.Max(Low, guess + 1);
            else
                High = Math.Min(High, guess - 1);
        }

        /// <summary>실시간 시스템 줄 하나를 보고 알림 창을 띄우거나 내린다.</summary>
        public void Observe(string formattedText)
        {
            LineKind kind = Apply(formattedText, DateTime.UtcNow);
            if (kind == LineKind.None)
                return;

            if (kind is LineKind.Correct or LineKind.Ended)
            {
                // 설정을 끈 뒤 끝난 경우에도 남지 않게 항상 닫는다
                Views.DungeonAlertWindow.HideAlert(Views.DungeonAlertSource.NumberGuess);
                return;
            }

            ChatSettings? settings = AppServices.TryGet<ChatSettings>();
            if (!(settings?.ShowNumberGuessHelper ?? true))
                return;

            (string title, string body) = Describe();
            Views.DungeonAlertWindow.Show(Views.DungeonAlertSource.NumberGuess, settings, title, body);
        }

        /// <summary>알림 창에 적을 제목과 본문.</summary>
        public (string Title, string Body) Describe()
            => Low == High
                ? ("숫자 맞추기", $"정답 {Low}")
                : ($"숫자 맞추기 ({Low} ~ {High})", $"추천 {Suggestion}");
    }
}
