using System;
using System.Collections.Generic;
using System.Text;

namespace TWChatOverlay.Services
{
    /// <summary>운명 선고의 발판. 게임 공지에 감정 이름과 방향이 같이 적혀 나온다.</summary>
    public enum DoomPad
    {
        /// <summary>기쁨(←)</summary>
        Joy,
        /// <summary>즐거움(↓)</summary>
        Pleasure,
        /// <summary>분노(→)</summary>
        Anger,
        /// <summary>슬픔(↑)</summary>
        Sorrow,
    }

    /// <summary>
    /// 글자 인식 결과(줄 → 낱말)에서 발판 순서를 뽑는다.
    ///
    /// 공지는 이렇게 생겼다 (2026-11-12):
    ///   운명 선고! 12초 안에 순서대로 밟으십시오.
    ///   모두 밟기 전까지 설계자의 잔재는 피해를 받지 않습니다.
    ///   ① 즐거움(↓)   ② 슬픔(↑)   ③ 분노(→)   ④ 즐거움(↓)        ← 발판 6·8개면 다음 줄에 이어진다
    /// 인식기는 동그라미 숫자와 화살표를 엉뚱하게 읽는다 ("0 즐거움(1) 0 슬픔(T) 0 분노(놔) 즐거움(1)").
    /// 나올 수 있는 말이 네 가지뿐이라, 한글만 추려 네 낱말 중 가장 가까운 것(자모 단위 편집 거리)으로 맞춘다 —
    /// "술품"처럼 한두 자모가 틀려도 슬픔으로 읽힌다. 방향은 낱말에서 정해지므로 화살표는 읽을 필요가 없다.
    /// 안내 두 줄은 거기에만 나오는 말(선고·순서·밟·설계·잔재·피해…)로 알아보고 건너뛴다.
    /// </summary>
    public static class DoomSentenceOrderParser
    {
        private static readonly (DoomPad Pad, string Word, string Jamo)[] Words =
        {
            (DoomPad.Joy, "기쁨", ToJamo("기쁨")),
            (DoomPad.Pleasure, "즐거움", ToJamo("즐거움")),
            (DoomPad.Anger, "분노", ToJamo("분노")),
            (DoomPad.Sorrow, "슬픔", ToJamo("슬픔")),
        };

        // 안내 줄에만 나오는 말. 하나라도 있으면 그 줄은 순서 줄이 아니다
        private static readonly string[] HeaderMarkers = { "선고", "순서", "밟", "설계", "잔재", "피해", "받지", "않습" };

        // 자모가 이만큼 맞아야 그 낱말로 본다 (술품 ↔ 슬픔 = 0.67, 엉뚱한 말은 0.5 아래)
        private const double MinSimilarity = 0.6;

        public static string NameOf(DoomPad pad) => pad switch
        {
            DoomPad.Joy => "기쁨",
            DoomPad.Pleasure => "즐거움",
            DoomPad.Anger => "분노",
            _ => "슬픔",
        };

        public static string ArrowOf(DoomPad pad) => pad switch
        {
            DoomPad.Joy => "←",
            DoomPad.Pleasure => "↓",
            DoomPad.Anger => "→",
            _ => "↑",
        };

        /// <summary>인식된 줄들(위에서 아래로, 줄마다 낱말은 왼쪽에서 오른쪽으로)에서 발판 순서를 뽑는다.</summary>
        public static List<DoomPad> Parse(IEnumerable<IReadOnlyList<string>> lines)
        {
            var order = new List<DoomPad>();
            foreach (IReadOnlyList<string> words in lines)
            {
                if (IsHeaderLine(words))
                    continue;

                foreach (string word in words)
                {
                    if (TryMatchWord(word, out DoomPad pad))
                        order.Add(pad);
                }
            }
            return order;
        }

        private static bool IsHeaderLine(IReadOnlyList<string> words)
        {
            foreach (string word in words)
            {
                foreach (string marker in HeaderMarkers)
                {
                    if (word.Contains(marker, StringComparison.Ordinal))
                        return true;
                }
            }
            return false;
        }

        /// <summary>낱말 하나("즐거움(1)", "술-품(", "0")가 네 발판 이름 중 하나인지.</summary>
        public static bool TryMatchWord(string? word, out DoomPad pad)
        {
            pad = default;
            if (string.IsNullOrEmpty(word))
                return false;

            // 먼저 낱말 안의 한글을 다 이어서 본다 ("술-품" → "술품"). 안 맞으면 끊긴 덩어리마다 본다 ("즐거움(1)결" → "즐거움", "결")
            var all = new StringBuilder();
            var runs = new List<string>();
            var run = new StringBuilder();
            foreach (char c in word)
            {
                if (c >= '가' && c <= '힣')
                {
                    all.Append(c);
                    run.Append(c);
                }
                else if (run.Length > 0)
                {
                    runs.Add(run.ToString());
                    run.Clear();
                }
            }
            if (run.Length > 0)
                runs.Add(run.ToString());

            if (all.Length == 0)
                return false;
            if (TryMatchHangul(all.ToString(), out pad))
                return true;
            if (runs.Count > 1)
            {
                foreach (string part in runs)
                {
                    if (TryMatchHangul(part, out pad))
                        return true;
                }
            }
            return false;
        }

        private static bool TryMatchHangul(string hangul, out DoomPad pad)
        {
            pad = default;
            if (hangul.Length < 2 || hangul.Length > 5)
                return false;

            string jamo = ToJamo(hangul);
            double best = 0;
            foreach (var (candidate, _, candidateJamo) in Words)
            {
                int distance = EditDistance(jamo, candidateJamo);
                double similarity = 1.0 - (double)distance / Math.Max(jamo.Length, candidateJamo.Length);
                if (similarity > best)
                {
                    best = similarity;
                    pad = candidate;
                }
            }
            return best >= MinSimilarity;
        }

        /// <summary>한글 음절을 초성·중성·종성 자모로 푼다 (비교용이라 글자 값만 서로 다르면 된다).</summary>
        private static string ToJamo(string text)
        {
            var sb = new StringBuilder(text.Length * 3);
            foreach (char c in text)
            {
                if (c < '가' || c > '힣')
                {
                    sb.Append(c);
                    continue;
                }
                int index = c - 0xAC00;
                sb.Append((char)(0x1100 + index / 588));
                sb.Append((char)(0x1161 + index % 588 / 28));
                int final = index % 28;
                if (final != 0)
                    sb.Append((char)(0x11A7 + final));
            }
            return sb.ToString();
        }

        private static int EditDistance(string a, string b)
        {
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
                previous[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
                }
                (previous, current) = (current, previous);
            }
            return previous[b.Length];
        }
    }
}
