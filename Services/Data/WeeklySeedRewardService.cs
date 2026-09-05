using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
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

        // 주간 한도 직전 마지막 클리어는 잔여분만 지급되며 별도 문구로 찍힌다:
        // "SEED 주간 획득 제한으로 1억 3000만 SEED만 획득되었습니다."
        private static readonly Regex PartialSeedRegex = new(
            @"SEED 주간 획득 제한으로\s*(?:(?<eok>\d+)\s*억)?\s*(?:(?<man>\d+)\s*만)?\s*SEED만 획득되었",
            RegexOptions.Compiled);

        private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// 주간 범위의 클리어 보상 시드를 합산 — 주간(일반)과 일간(루비코나) 분리.
        /// 결과는 Logs/Seed/SeedHistory.html 한 파일에 원본 줄과 함께 보관한다.
        /// 지나간 날짜는 보관본을 재사용하므로 게임 로그 파일은 각각 한 번만 스캔되고,
        /// 게임 로그를 삭제해도 보관된 이력은 유지된다.
        /// </summary>
        public static async Task<(long General, long Rubicona)> SumWeeklyClearSeedAsync(
            string logDir, DateTime weekStart, DateTime weekEnd)
        {
            return await Task.Run(() =>
            {
                long general = 0;
                long rubicona = 0;
                DateTime today = DateTime.Today;

                lock (ArchiveLock)
                {
                    var archive = LoadArchive();
                    bool dirty = false;

                    for (DateTime day = weekStart.Date; day <= weekEnd.Date; day = day.AddDays(1))
                    {
                        if (day > today)
                            break;

                        string key = day.ToString("yyyy-MM-dd");
                        if (!(day < today && archive.TryGetValue(key, out List<SeedEntry>? entries)))
                        {
                            // 미보관 날짜(또는 아직 자라는 오늘 파일)는 게임 로그를 스캔한다
                            string path = string.IsNullOrWhiteSpace(logDir)
                                ? string.Empty
                                : Path.Combine(logDir, $"TWChatLog_{day:yyyy_MM_dd}.html");
                            entries = path.Length > 0 && File.Exists(path)
                                ? ScanDayFile(path)
                                : new List<SeedEntry>();

                            archive.TryGetValue(key, out var previous);
                            if (previous is null || previous.Count != entries.Count || SumOf(previous) != SumOf(entries))
                                dirty = true;
                            archive[key] = entries;
                        }

                        foreach (var entry in entries!)
                        {
                            if (entry.Kind == KindDaily)
                                rubicona += entry.Amount;
                            else
                                general += entry.Amount;
                        }
                    }

                    if (dirty)
                        SaveArchive(archive);
                }

                return (general, rubicona);
            }).ConfigureAwait(false);
        }

        private const string KindWeekly = "weekly";   // 주간 버킷
        private const string KindDaily = "daily";     // 일간 버킷 (루비코나 환희·슬픔)
        private const string KindPartial = "partial"; // 주간 한도 직전 부분 지급 (주간 버킷에 합산)

        private sealed record SeedEntry(string Kind, long Amount, string Text);

        /// <summary>하루치 게임 로그에서 시드 획득 줄을 추출·분류한다.</summary>
        private static List<SeedEntry> ScanDayFile(string path)
        {
            var entries = new List<SeedEntry>();
            var seedEvents = new List<(int LineIndex, long Value, string Text)>();
            var markerIndices = new List<int>();

            try
            {
                var encoding = Encoding.GetEncoding(949);
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, encoding, detectEncodingFromByteOrderMarks: true);
                string? line;
                int lineIndex = -1;
                while ((line = reader.ReadLine()) != null)
                {
                    lineIndex++;
                    bool hasReward = line.Contains("보상으로", StringComparison.Ordinal);
                    bool hasPartial = line.Contains("획득 제한으로", StringComparison.Ordinal);
                    if (!hasReward && !hasPartial)
                        continue;

                    string text = HtmlTagRegex.Replace(line, string.Empty);

                    if (hasPartial)
                    {
                        var partial = PartialSeedRegex.Match(text);
                        if (partial.Success)
                        {
                            long clipped = 0;
                            if (partial.Groups["eok"].Success)
                                clipped += long.Parse(partial.Groups["eok"].Value) * Eok;
                            if (partial.Groups["man"].Success)
                                clipped += long.Parse(partial.Groups["man"].Value) * Man;
                            if (clipped > 0)
                                entries.Add(new SeedEntry(KindPartial, clipped, text.Trim()));
                        }
                        continue;
                    }

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
                        seedEvents.Add((lineIndex, value, text.Trim()));
                }

                // 루비코나 몫은 시드 줄 주변(앞 3줄/뒤 8줄)의 "레이티아/설계자 퇴치 보상" 줄로 판별
                // (금액 2억만으로는 최후의 결전과 구분되지 않음)
                int markerCursor = 0;
                foreach (var (index, value, lineText) in seedEvents)
                {
                    while (markerCursor < markerIndices.Count && markerIndices[markerCursor] < index - 3)
                        markerCursor++;
                    bool isRubicona = markerCursor < markerIndices.Count &&
                                      markerIndices[markerCursor] <= index + 8;
                    entries.Add(new SeedEntry(isRubicona ? KindDaily : KindWeekly, value, lineText));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to scan seed rewards from {path}.", ex);
            }

            return entries;
        }

        private static readonly object ArchiveLock = new();
        private static SortedDictionary<string, List<SeedEntry>>? _archive;

        private static string ArchivePath => Path.Combine(LogStoragePaths.SeedDirectory, "SeedHistory.html");

        private static readonly Regex ArchiveEntryRegex = new(
            "<div class=\"seed (?<kind>weekly|daily|partial)\" data-date=\"(?<date>\\d{4}-\\d{2}-\\d{2})\" data-amount=\"(?<amount>\\d+)\">(?<text>.*?)</div>",
            RegexOptions.Compiled);

        private static readonly Regex ArchiveDayRegex = new(
            "class=\"day\" data-day=\"(?<date>\\d{4}-\\d{2}-\\d{2})\"",
            RegexOptions.Compiled);

        private static DateTime ParseDateKey(string key)
            => DateTime.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static DateTime GetWeekStartOf(DateTime date)
            => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

        private static long SumOf(List<SeedEntry> entries)
        {
            long sum = 0;
            foreach (var entry in entries)
                sum += entry.Amount;
            return sum;
        }

        private static SortedDictionary<string, List<SeedEntry>> LoadArchive()
        {
            if (_archive is not null)
                return _archive;

            var result = new SortedDictionary<string, List<SeedEntry>>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(ArchivePath))
                {
                    foreach (string line in File.ReadLines(ArchivePath))
                    {
                        var dayMatch = ArchiveDayRegex.Match(line);
                        if (dayMatch.Success)
                        {
                            string dayKey = dayMatch.Groups["date"].Value;
                            if (!result.ContainsKey(dayKey))
                                result[dayKey] = new List<SeedEntry>();
                            continue;
                        }

                        var entryMatch = ArchiveEntryRegex.Match(line);
                        if (!entryMatch.Success)
                            continue;

                        string key = entryMatch.Groups["date"].Value;
                        if (!result.TryGetValue(key, out var list))
                        {
                            list = new List<SeedEntry>();
                            result[key] = list;
                        }
                        list.Add(new SeedEntry(
                            entryMatch.Groups["kind"].Value,
                            long.Parse(entryMatch.Groups["amount"].Value),
                            WebUtility.HtmlDecode(entryMatch.Groups["text"].Value)));
                    }
                }

                // 구버전 합계 캐시는 아카이브로 대체되었으므로 정리한다
                string legacyCache = Path.Combine(LogStoragePaths.StateDirectory, "seed_daily.json");
                if (File.Exists(legacyCache))
                    File.Delete(legacyCache);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load seed history archive.", ex);
            }

            return _archive = result;
        }

        /// <summary>아카이브를 주별 섹션·합계가 붙은 열람용 HTML로 통째로 다시 쓴다.</summary>
        private static void SaveArchive(SortedDictionary<string, List<SeedEntry>> archive)
        {
            try
            {
                Directory.CreateDirectory(LogStoragePaths.SeedDirectory);

                var sb = new StringBuilder();
                sb.AppendLine("<!doctype html>");
                sb.AppendLine("<html lang=\"ko\">");
                sb.AppendLine("<head>");
                sb.AppendLine("  <meta charset=\"utf-8\" />");
                sb.AppendLine("  <title>시드 획득 내역</title>");
                sb.AppendLine("  <style>");
                sb.AppendLine("    body{background:#111;color:#eee;font-family:'Malgun Gothic',sans-serif;font-size:13px;line-height:1.5;padding:16px;}");
                sb.AppendLine("    h1{font-size:17px;margin:0 0 4px;}");
                sb.AppendLine("    h2{color:#9ad3ff;border-bottom:1px solid rgba(154,211,255,.35);padding-bottom:4px;margin:20px 0 8px;font-size:15px;}");
                sb.AppendLine("    h3{color:#c9d1d9;margin:10px 0 4px;font-size:13px;font-weight:600;}");
                sb.AppendLine("    .seed{margin:1px 0;color:#aab3bb;}");
                sb.AppendLine("    .seed.daily{color:#7ec8ff;}");
                sb.AppendLine("    .seed.partial{color:#ffc266;}");
                sb.AppendLine("    .note{color:#888;margin:0 0 8px;}");
                sb.AppendLine("  </style>");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");
                sb.AppendLine("<h1>시드 획득 내역</h1>");
                sb.AppendLine("<p class=\"note\">TWChatOverlay가 게임 로그에서 수집한 클리어 보상 시드 기록입니다. 파란색은 일간(루비코나), 주황색은 주간 한도 직전 부분 지급입니다. 앱이 다시 읽는 데이터 파일이므로 내용을 직접 수정하지 마세요.</p>");

                foreach (var weekGroup in archive
                             .GroupBy(kv => GetWeekStartOf(ParseDateKey(kv.Key)))
                             .OrderBy(g => g.Key))
                {
                    DateTime ws = weekGroup.Key;
                    DateTime we = ws.AddDays(6);
                    long weekly = 0, daily = 0;
                    foreach (var kv in weekGroup)
                        foreach (var entry in kv.Value)
                        {
                            if (entry.Kind == KindDaily) daily += entry.Amount;
                            else weekly += entry.Amount;
                        }

                    sb.AppendLine($"<h2 data-week=\"{ws:yyyy-MM-dd}\">{ws:M/d(ddd)} ~ {we:M/d(ddd)} — 주간 {FormatSeed(weekly)} · 일간 {FormatSeed(daily)} · 합계 {FormatSeed(weekly + daily)}</h2>");

                    foreach (var kv in weekGroup.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    {
                        long dayWeekly = 0, dayDaily = 0;
                        foreach (var entry in kv.Value)
                        {
                            if (entry.Kind == KindDaily) dayDaily += entry.Amount;
                            else dayWeekly += entry.Amount;
                        }

                        string dayLabel = kv.Value.Count == 0
                            ? "기록 없음"
                            : $"주간 {FormatSeed(dayWeekly)} · 일간 {FormatSeed(dayDaily)}";
                        sb.AppendLine($"<h3 class=\"day\" data-day=\"{kv.Key}\">{ParseDateKey(kv.Key):M/d(ddd)} — {dayLabel}</h3>");

                        foreach (var entry in kv.Value)
                            sb.AppendLine($"<div class=\"seed {entry.Kind}\" data-date=\"{kv.Key}\" data-amount=\"{entry.Amount}\">{WebUtility.HtmlEncode(entry.Text)}</div>");
                    }
                }

                sb.AppendLine("</body>");
                sb.AppendLine("</html>");
                File.WriteAllText(ArchivePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save seed history archive.", ex);
            }
        }

        // 게임의 주간 버킷 한도 이력: 2026-07-20 주부터 66억, 그 이전은 60억 (로그 실측으로 확인)
        private static readonly DateTime WeeklyCap66Since = new(2026, 7, 20);

        /// <summary>
        /// 주간 표시용 보정: 과거 로그에는 일간 컨텐츠(군영 퀘스트류) 시드에 식별 문구가 없어
        /// 주간 몫으로 합산된다. 당시 주간 한도를 넘는 초과분을 일간 몫으로 옮긴다.
        /// </summary>
        public static (long Weekly, long Daily) SplitWeeklyDaily(DateTime weekStart, long general, long rubicona)
        {
            long cap = weekStart >= WeeklyCap66Since ? 66L * Eok : 60L * Eok;
            long overflow = Math.Max(0, general - cap);
            return (general - overflow, rubicona + overflow);
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
