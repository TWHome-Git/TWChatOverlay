using System.Text.RegularExpressions;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 놀라움의 격리실(설계자의 잔재) 가면 기믹의 채팅 로그 줄을 읽는다. 보스가 감정 가면을 쓰면 상성인 가면을 부숴야 풀린다
    /// (분노 ↔ 즐거움, 기쁨 ↔ 슬픔). 어느 가면을 부술지는 로그 줄에 그대로 적혀 나온다 (2026-11-12 테스트 서버 로그, 시스템 색 ff64ff):
    ///
    ///   ▶ 슬픔의 가면으로 플레이어의 회복 효율이 50% 감소합니다. (HP 물약 포함)      ← 보스가 쓴 가면
    ///   ▶ 슬픔의 가면은 기쁨 가면을 파괴하면 해제됩니다.                               ← 부술 가면 = 기쁨
    ///   ▶ 기쁨의 가면으로 설계자의 잔재가 매초 최대 HP의 1%를 회복합니다.
    ///   ▶ 기쁨의 가면은 슬픔 가면을 파괴하면 해제됩니다.                               ← 슬픔
    ///   ▶ 분노의 가면으로 설계자의 잔재 공격력이 30% 증가합니다.
    ///   ▶ 분노의 가면은 즐거움 가면을 파괴하면 해제됩니다.                             ← 즐거움
    ///   ▶ 즐거움의 감정이 20초 뒤 폭발합니다.                                          ← 즐거움은 "가면으로" 줄 없이 폭발 예고가 온다
    ///   ▶ 분노의 가면을 파괴하거나, 안전지대로 대피하십시오.                           ← 분노
    ///   ▶ 파훼 성공! 설계자의 잔재의 슬픔 상태가 해제되고 3초간 그로기에 빠집니다.     ← 끝
    ///
    /// 방 안내 줄("[파훼] 상성 가면을 파괴하면…", "[오답] 잘못된 가면을 파괴하면…")은 감정 이름이 "가면을 파괴" 바로 앞에 없어 걸리지 않는다.
    /// </summary>
    public static class MaskGimmickLogParser
    {
        public enum LineKind
        {
            None,
            /// <summary>보스가 그 감정의 가면을 썼다 (Pad).</summary>
            Wearing,
            /// <summary>즐거움의 감정이 Seconds 초 뒤 폭발한다 (보스가 즐거움 상태).</summary>
            Explosion,
            /// <summary>부숴야 할 가면 (Pad).</summary>
            Target,
            /// <summary>상태가 풀렸다 · 방이 끝났다.</summary>
            Cleared,
        }

        public readonly record struct Line(LineKind Kind, DoomPad Pad, int Seconds);

        private const string Emotions = "기쁨|슬픔|분노|즐거움";

        private static readonly Regex TargetRegex = new(
            $@"(?<pad>{Emotions})(?:의)?\s*가면을\s*파괴하", RegexOptions.Compiled);

        private static readonly Regex WearingRegex = new(
            $@"(?<pad>{Emotions})의\s*가면으로", RegexOptions.Compiled);

        private static readonly Regex ExplosionRegex = new(
            @"즐거움의\s*감정이\s*(?<seconds>\d+)\s*초\s*뒤\s*폭발", RegexOptions.Compiled);

        private static readonly Regex ClearedRegex = new(
            @"파훼\s*성공|상태가\s*해제되고|감정의\s*잔재가\s*진정되었습니다", RegexOptions.Compiled);

        public static Line Parse(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return default;
            // 대부분의 줄은 여기서 걸러진다
            if (!text.Contains("가면", System.StringComparison.Ordinal) &&
                !text.Contains("감정", System.StringComparison.Ordinal) &&
                !text.Contains("파훼", System.StringComparison.Ordinal))
                return default;

            Match target = TargetRegex.Match(text);
            if (target.Success && TryPad(target.Groups["pad"].Value, out DoomPad targetPad))
                return new Line(LineKind.Target, targetPad, 0);

            Match explosion = ExplosionRegex.Match(text);
            if (explosion.Success && int.TryParse(explosion.Groups["seconds"].Value, out int seconds))
                return new Line(LineKind.Explosion, DoomPad.Pleasure, seconds);

            Match wearing = WearingRegex.Match(text);
            if (wearing.Success && TryPad(wearing.Groups["pad"].Value, out DoomPad wearingPad))
                return new Line(LineKind.Wearing, wearingPad, 0);

            if (ClearedRegex.IsMatch(text))
                return new Line(LineKind.Cleared, default, 0);

            return default;
        }

        private static bool TryPad(string name, out DoomPad pad)
        {
            switch (name)
            {
                case "기쁨": pad = DoomPad.Joy; return true;
                case "슬픔": pad = DoomPad.Sorrow; return true;
                case "분노": pad = DoomPad.Anger; return true;
                case "즐거움": pad = DoomPad.Pleasure; return true;
                default: pad = default; return false;
            }
        }
    }
}
