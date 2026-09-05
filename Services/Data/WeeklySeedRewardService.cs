using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 주간 클리어 보상 시드 집계.
    /// 실측: 게임 로그의 "보상으로 N SEED를 획득했습니다" 줄을 주간 범위로 직접 합산.
    /// 예상: 일일/주간 컨텐츠 체크리스트에서 켜져 있는 항목의 주간 최대 보상을
    ///       그룹 한도 → 주간 66억(루비코나 제외) / 루비코나 28억 한도 순으로 적용해 계산.
    /// </summary>
    public static class WeeklySeedRewardService
    {
        private const long Eok = 100_000_000L;
        private const long Man = 10_000L;

        private const long WeeklyBucketCap = 66L * Eok;          // 루비코나 제외 주간 공통 한도
        private const long RubiconaBucketCap = 28L * Eok;        // 루비코나(환희·슬픔) 별도 한도

        private sealed record Entry(string ItemName, long WeeklySeed);
        private sealed record Group(long Cap, Entry[] Entries);

        // 항목명은 일일/주간 컨텐츠 체크리스트(DungeonItemConfigs 키)와 일치해야 한다.
        private static readonly Group[] WeeklyGroups =
        {
            // 이클립스 지역 — 아페티리아(택1)는 별도 처리 후 이 그룹 한도에 합산
            new(5325L * Man * 100, new Entry[]
            {
                new("로카고스", 245L * Man * 100),
                new("에토스", 245L * Man * 100),
                new("체리아", 245L * Man * 100),
                new("마티아", 245L * Man * 100),
                new("라이코스", 245L * Man * 100),
                new("티로로스", 245L * Man * 100),
                new("이클립스 토벌전", 840L * Man * 100),
                new("보급품 탈환", 210L * Man * 100),
                new("훈련소", 245L * Man * 100),
                new("최후의 결전", 20L * Eok),
            }),
            // 이클립스 코어 마스터
            new(8L * Eok, new Entry[]
            {
                new("로카고스 코어 마스터", 280L * Man * 100),
                new("에토스 코어 마스터", 280L * Man * 100),
                new("체리아 코어 마스터", 280L * Man * 100),
                new("마티아 코어 마스터", 280L * Man * 100),
                new("라이코스 코어 마스터", 280L * Man * 100),
                new("티로로스 코어 마스터", 280L * Man * 100),
            }),
            // 어비스 코어 마스터
            new(7L * Eok, new Entry[]
            {
                new("심층Ⅰ 코어 마스터", 245L * Man * 100),
                new("심층Ⅱ 코어 마스터", 245L * Man * 100),
                new("심층Ⅲ 코어 마스터", 245L * Man * 100),
            }),
            // 머큐리얼 코어 마스터
            new(6L * Eok, new Entry[]
            {
                new("샐리온 코어 마스터 던전", 210L * Man * 100),
                new("샐레아나 코어 마스터 던전", 210L * Man * 100),
                new("실라이론 코어 마스터 던전", 210L * Man * 100),
                new("실반 코어 마스터 던전", 210L * Man * 100),
                new("루미너스 코어 마스터 던전", 210L * Man * 100),
            }),
            // 머큐리얼 주간
            new(525L * Man * 100, new Entry[]
            {
                new("샐리온", 105L * Man * 100),
                new("샐레아나", 105L * Man * 100),
                new("실라이론", 105L * Man * 100),
                new("실반", 105L * Man * 100),
                new("루미너스", 105L * Man * 100),
            }),
            // 어비스 지옥
            new(735L * Man * 100, new Entry[]
            {
                new("어비스 - 심층Ⅰ", 245L * Man * 100),
                new("어비스 - 심층Ⅱ", 245L * Man * 100),
                new("어비스 - 심층Ⅲ", 245L * Man * 100),
            }),
            // 그룹 한도가 개별 합과 같은 단독 항목들
            new(long.MaxValue, new Entry[]
            {
                new("차원의 틈", 210L * Man * 100),      // 지하요새의 망령
                new("신조의 둥지 어려움", 735L * Man * 100),
                new("오를리 방어전 지옥", 210L * Man * 100),
                new("카타콤 지옥", 50L * Man * 100),
            }),
        };

        // 아페티리아 일반/어려움(EX)은 택1 — 둘 다 켜져 있으면 큰 쪽만 반영 (이클립스 그룹 한도에 포함)
        private static readonly Entry ApetiriaNormal = new("아페티리아", 735L * Man * 100);
        private static readonly Entry ApetiriaEx = new("아페티리아 EX", 840L * Man * 100);

        // 루비코나(환희·슬픔) — 각 보스·난이도 하루 2억 × 7일
        private static readonly Entry[] RubiconaEntries =
        {
            new("추종하는 환희(일반)", 14L * Eok),
            new("응시하는 슬픔(일반)", 14L * Eok),
            new("추종하는 환희(어려움)", 14L * Eok),
            new("응시하는 슬픔(어려움)", 14L * Eok),
        };

        /// <summary>체크리스트에서 켜진 항목 기준 주간 시드 한도 — 일반(루비코나 제외)과 루비코나 분리.</summary>
        public static (long General, long Rubicona) ComputeWeeklySeedCaps(ChatSettings settings)
        {
            long weekly = 0;
            bool eclipseFirst = true;
            foreach (var group in WeeklyGroups)
            {
                long sum = 0;
                foreach (var entry in group.Entries)
                {
                    if (IsItemEnabled(settings, entry.ItemName))
                        sum += entry.WeeklySeed;
                }

                if (eclipseFirst)
                {
                    eclipseFirst = false;
                    long apetiria = 0;
                    if (IsItemEnabled(settings, ApetiriaEx.ItemName))
                        apetiria = ApetiriaEx.WeeklySeed;
                    else if (IsItemEnabled(settings, ApetiriaNormal.ItemName))
                        apetiria = ApetiriaNormal.WeeklySeed;
                    sum += apetiria;
                }

                weekly += Math.Min(sum, group.Cap);
            }

            weekly = Math.Min(weekly, WeeklyBucketCap);

            long rubicona = 0;
            foreach (var entry in RubiconaEntries)
            {
                if (IsItemEnabled(settings, entry.ItemName))
                    rubicona += entry.WeeklySeed;
            }
            rubicona = Math.Min(rubicona, RubiconaBucketCap);

            return (weekly, rubicona);
        }

        private static bool IsItemEnabled(ChatSettings settings, string itemName)
        {
            // 체크리스트를 한 번도 만지지 않은 항목은 기본 활성으로 취급
            return !settings.DungeonItemConfigs.TryGetValue(itemName, out var config) || config.IsEnabled;
        }

        // 줄에 "보상으로"가 있는지는 호출부에서 먼저 거르고, 금액은 "SEED를 획득했" 직전 값을 읽는다.
        // (보급품 탈환은 "보상으로 경험의 정수 N개와 3000만 Seed를 획득했습니다"처럼 중간에 다른 보상이 낀다)
        private static readonly Regex SeedRewardRegex = new(
            @"(?:(?<eok>\d+)\s*억)?\s*(?:(?<man>\d+)\s*만)?\s*(?:SEED|Seed)를 획득했",
            RegexOptions.Compiled);

        private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// 주간 범위의 게임 로그에서 클리어 보상 시드 획득 줄을 직접 합산 — 일반과 루비코나 분리.
        /// 루비코나 몫은 시드 줄 주변(앞 3줄/뒤 8줄)의 "레이티아/설계자 퇴치 보상" 줄로 판별한다
        /// (금액 2억만으로는 최후의 결전과 구분되지 않음).
        /// </summary>
        public static async Task<(long General, long Rubicona)> SumWeeklyClearSeedAsync(
            string logDir, DateTime weekStart, DateTime weekEnd)
        {
            return await Task.Run(() =>
            {
                long general = 0;
                long rubicona = 0;
                if (string.IsNullOrWhiteSpace(logDir) || !Directory.Exists(logDir))
                    return (general, rubicona);

                var encoding = Encoding.GetEncoding(949);
                var seedEvents = new List<(int LineIndex, long Value)>();
                var markerIndices = new List<int>();

                for (DateTime day = weekStart.Date; day <= weekEnd.Date; day = day.AddDays(1))
                {
                    string path = Path.Combine(logDir, $"TWChatLog_{day:yyyy_MM_dd}.html");
                    if (!File.Exists(path))
                        continue;

                    try
                    {
                        seedEvents.Clear();
                        markerIndices.Clear();

                        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete);
                        using var reader = new StreamReader(fs, encoding, detectEncodingFromByteOrderMarks: true);
                        string? line;
                        int lineIndex = -1;
                        while ((line = reader.ReadLine()) != null)
                        {
                            lineIndex++;
                            if (!line.Contains("보상으로", StringComparison.Ordinal))
                                continue;

                            string text = HtmlTagRegex.Replace(line, string.Empty);

                            // 보급품 탈환은 한 판에 "콘텐츠 클리어 보상으로 3000만 SEED"와
                            // "보급품 탈환 성공 보상으로 … 3000만 Seed" 두 줄이 찍힌다(실수령은 3000만 1회).
                            // 중복 합산을 막기 위해 내용 중복인 성공 보상 줄은 제외한다.
                            if (text.Contains("보급품 탈환 성공 보상으로", StringComparison.Ordinal))
                                continue;

                            if (text.Contains("퇴치 보상으로", StringComparison.Ordinal) &&
                                (text.Contains("레이티아", StringComparison.Ordinal) ||
                                 text.Contains("설계자", StringComparison.Ordinal)))
                            {
                                markerIndices.Add(lineIndex);
                                continue;
                            }

                            if (!text.Contains("를 획득했", StringComparison.Ordinal))
                                continue;

                            var match = SeedRewardRegex.Match(text);
                            if (!match.Success)
                                continue;

                            long value = 0;
                            if (match.Groups["eok"].Success)
                                value += long.Parse(match.Groups["eok"].Value) * Eok;
                            if (match.Groups["man"].Success)
                                value += long.Parse(match.Groups["man"].Value) * Man;
                            if (value > 0)
                                seedEvents.Add((lineIndex, value));
                        }

                        int markerCursor = 0;
                        foreach (var (index, value) in seedEvents)
                        {
                            while (markerCursor < markerIndices.Count && markerIndices[markerCursor] < index - 3)
                                markerCursor++;
                            bool isRubicona = markerCursor < markerIndices.Count &&
                                              markerIndices[markerCursor] <= index + 8;
                            if (isRubicona)
                                rubicona += value;
                            else
                                general += value;
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Failed to scan seed rewards from {path}.", ex);
                    }
                }

                return (general, rubicona);
            }).ConfigureAwait(false);
        }

        /// <summary>시드 금액을 "93.15억" / "8500만" 형태로 표기.</summary>
        public static string FormatSeed(long seed)
        {
            if (seed >= Eok)
            {
                double eok = seed / (double)Eok;
                return eok % 1 == 0 ? $"{eok:0}억" : $"{eok:0.##}억";
            }
            if (seed >= Man)
                return $"{seed / Man}만";
            return seed.ToString();
        }
    }
}
