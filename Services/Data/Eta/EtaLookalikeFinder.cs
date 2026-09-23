using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TWChatOverlay.Services
{
    /// <summary>1:1 대화 상대와 닮은 랭킹 아이디 하나. Kind는 얼마나 닮았는지 — 헷갈리는 글자만 다른지(Confusable), 한 글자 차이인지(OneEdit).</summary>
    public readonly record struct EtaLookalike(string UserId, int Level, EtaLookalikeKind Kind);

    public enum EtaLookalikeKind
    {
        /// <summary>l/I/1, O/0, ㄷ/ㄸ처럼 모양이 헷갈리는 글자만 다르다 — 사칭 가능성이 가장 높다.</summary>
        Confusable,
        /// <summary>글자 하나가 더 붙거나 빠지거나 바뀌었다 (드드해 ↔ 드드해1).</summary>
        OneEdit,
    }

    /// <summary>
    /// 에타 랭킹에서 주어진 아이디와 닮은 아이디를 찾는다 (1:1 대화 상대 사칭 대비).
    /// 1) 헷갈리는 글자를 같은 글자로 정규화해 같아지는 아이디 (YulLin ↔ YuILin, 드드해 ↔ 뜨뜨해)
    /// 2) 원문 기준 편집 거리 1 (드드해 ↔ 드드해1)
    /// 랭킹은 서버당 수천 건이라 매번 전부 비교해도 1:1 대화 한 번에 수 ms면 끝난다.
    /// </summary>
    public static class EtaLookalikeFinder
    {
        public static IReadOnlyList<EtaLookalike> Find(string userId, int max = 3)
        {
            if (string.IsNullOrEmpty(userId))
                return Array.Empty<EtaLookalike>();

            IReadOnlyList<EtaProfileResolver.EtaRankingEntry> rankings;
            try { rankings = EtaProfileResolver.GetRankings(); }
            catch { return Array.Empty<EtaLookalike>(); }

            string normalized = Normalize(userId);
            var found = new List<EtaLookalike>();
            foreach (var entry in rankings)
            {
                string other = entry.UserId;
                if (string.IsNullOrEmpty(other) || string.Equals(other, userId, StringComparison.Ordinal))
                    continue;
                if (found.Any(f => string.Equals(f.UserId, other, StringComparison.Ordinal)))
                    continue; // 여러 캐릭터로 랭킹에 있는 아이디는 한 번만

                if (Normalize(other) == normalized)
                    found.Add(new EtaLookalike(other, entry.Level, EtaLookalikeKind.Confusable));
                else if (Math.Abs(other.Length - userId.Length) <= 1 && EditDistanceAtMostOne(other, userId))
                    found.Add(new EtaLookalike(other, entry.Level, EtaLookalikeKind.OneEdit));
            }

            return found
                .OrderBy(f => f.Kind)
                .ThenByDescending(f => f.Level)
                .Take(max)
                .ToList();
        }

        /// <summary>
        /// 모양이 헷갈리는 글자를 하나로 합친다: 대소문자 무시, l/I/1/| → l, O/0 → o,
        /// 한글은 자모로 풀고 된소리(ㄸ→ㄷ 등)·ㅐ/ㅔ·ㅒ/ㅖ를 합친다.
        /// </summary>
        public static string Normalize(string id)
        {
            var sb = new StringBuilder(id.Length * 3);
            foreach (char raw in id)
            {
                char c = char.ToLowerInvariant(raw);
                if (c >= '가' && c <= '힣')
                {
                    int code = c - '가';
                    int lead = code / 588, vowel = code % 588 / 28, tail = code % 28;
                    lead = lead switch { 1 => 0, 4 => 3, 8 => 7, 10 => 9, 13 => 12, _ => lead }; // ㄲ ㄸ ㅃ ㅆ ㅉ → 예사소리
                    vowel = vowel switch { 5 => 1, 7 => 3, _ => vowel };                        // ㅔ→ㅐ, ㅖ→ㅒ
                    sb.Append((char)('A' + lead)).Append((char)('a' + vowel)).Append((char)('0' + tail)).Append('/');
                    continue;
                }
                sb.Append(c switch
                {
                    'i' or 'l' or '1' or '|' => 'l',
                    'o' or '0' => 'o',
                    _ => c,
                });
            }
            return sb.ToString();
        }

        /// <summary>편집 거리(삽입·삭제·치환) 1 이하인지. 길이 차이는 호출 쪽에서 1 이하로 걸러 둔다.</summary>
        private static bool EditDistanceAtMostOne(string a, string b)
        {
            if (a.Length == b.Length)
            {
                int diff = 0;
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i] && ++diff > 1) return false;
                return diff == 1;
            }
            // 길이 1 차이: 긴 쪽에서 글자 하나를 빼면 같아지는지
            string longer = a.Length > b.Length ? a : b, shorter = a.Length > b.Length ? b : a;
            int li = 0, si = 0;
            bool skipped = false;
            while (li < longer.Length && si < shorter.Length)
            {
                if (longer[li] == shorter[si]) { li++; si++; continue; }
                if (skipped) return false;
                skipped = true;
                li++;
            }
            return true;
        }
    }
}
