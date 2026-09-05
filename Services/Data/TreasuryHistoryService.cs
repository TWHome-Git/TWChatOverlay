using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 심연의 보물창고 이력 아카이브 — 게임 로그에서 입장/금화 주머니 획득 줄을 추출해
    /// Logs/Treasury/TreasuryHistory.html 한 파일에 보관한다 (시드 아카이브와 같은 방식).
    /// 지나간 날짜는 한 번만 스캔하고 오늘만 매번 다시 읽으므로, 실시간 감지가 꺼져 있던
    /// (앱 미실행 포함) 동안의 판도 로그가 남아있는 한 복원된다.
    /// </summary>
    public static class TreasuryHistoryService
    {
        private const int MaxRuns = 7;

        private static readonly Regex EntryRegex = new(
            @"심연의\s*보물창고\s*입장\s*횟수:\s*\[?\s*(?<run>\d+)\s*회",
            RegexOptions.Compiled);

        private static readonly Regex LineTimeRegex = new(
            @"\[\s*(?<h>\d+)시\s*(?<m>\d+)분\s*(?<s>\d+)초\]",
            RegexOptions.Compiled);

        private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);

        // 입장 후 이 시간 안의 금화 주머니 획득만 보물창고 몫으로 센다 (다른 컨텐츠 획득과 구분)
        private static readonly TimeSpan SessionDuration = TimeSpan.FromSeconds(150);

        // Kind: "entry" (amount = 회차) / "gold" (금화 1개, amount = 회차)
        private sealed record TreasuryEvent(string Kind, int Run, string Text);

        private static readonly object ArchiveLock = new();
        private static SortedDictionary<string, List<TreasuryEvent>>? _archive;

        private static string ArchivePath => Path.Combine(LogStoragePaths.TreasuryDirectory, "TreasuryHistory.html");

        private static readonly Regex ArchiveEntryRegex = new(
            "<div class=\"tw (?<kind>entry|gold)\" data-date=\"(?<date>\\d{4}-\\d{2}-\\d{2})\" data-run=\"(?<run>\\d+)\">(?<text>.*?)</div>",
            RegexOptions.Compiled);

        private static readonly Regex ArchiveDayRegex = new(
            "class=\"day\" data-day=\"(?<date>\\d{4}-\\d{2}-\\d{2})\"",
            RegexOptions.Compiled);

        /// <summary>
        /// 주간(월~일) 보물창고 회차별 금화 개수를 로그 아카이브 기준으로 집계한다.
        /// 반환: 회차별 개수 배열(시작된 회차까지), 마지막으로 시작한 회차.
        /// </summary>
        public static async Task<(int[] Counts, int LastRun)> GetWeekAsync(string logDir, DateTime weekStart)
        {
            return await Task.Run(() =>
            {
                DateTime today = DateTime.Today;
                DateTime weekEnd = weekStart.Date.AddDays(6);
                var counts = new List<int>();
                int lastRun = 0;

                lock (ArchiveLock)
                {
                    var archive = LoadArchive();
                    bool dirty = false;

                    for (DateTime day = weekStart.Date; day <= weekEnd && day <= today; day = day.AddDays(1))
                    {
                        string key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                        if (!(day < today && archive.TryGetValue(key, out List<TreasuryEvent>? events)))
                        {
                            string path = string.IsNullOrWhiteSpace(logDir)
                                ? string.Empty
                                : Path.Combine(logDir, $"TWChatLog_{day:yyyy_MM_dd}.html");
                            events = path.Length > 0 && File.Exists(path)
                                ? ScanDayFile(path)
                                : new List<TreasuryEvent>();

                            archive.TryGetValue(key, out var previous);
                            if (previous is null || previous.Count != events.Count)
                                dirty = true;
                            archive[key] = events;
                        }

                        foreach (var ev in events!)
                        {
                            int run = Math.Clamp(ev.Run, 1, MaxRuns);
                            while (counts.Count < run)
                                counts.Add(0);
                            if (ev.Kind == "entry")
                            {
                                counts[run - 1] = 0; // 회차 새로 시작
                                lastRun = run;
                            }
                            else
                            {
                                counts[run - 1]++;
                            }
                        }
                    }

                    if (dirty)
                        SaveArchive(archive);
                }

                return (counts.ToArray(), lastRun);
            }).ConfigureAwait(false);
        }

        /// <summary>하루치 게임 로그에서 보물창고 입장/금화 획득 이벤트를 추출한다.</summary>
        private static List<TreasuryEvent> ScanDayFile(string path)
        {
            var events = new List<TreasuryEvent>();
            try
            {
                var encoding = Encoding.GetEncoding(949);
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, encoding, detectEncodingFromByteOrderMarks: true);

                int currentRun = 0;
                TimeSpan sessionEnd = TimeSpan.MinValue;

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    bool hasEntry = line.Contains("보물창고", StringComparison.Ordinal);
                    bool hasGold = line.Contains("금화 주머니", StringComparison.Ordinal);
                    if (!hasEntry && !hasGold)
                        continue;

                    string text = HtmlTagRegex.Replace(line, string.Empty).Trim();

                    if (hasEntry)
                    {
                        Match entry = EntryRegex.Match(text);
                        if (entry.Success && int.TryParse(entry.Groups["run"].Value, out int run))
                        {
                            currentRun = Math.Clamp(run, 1, MaxRuns);
                            TimeSpan? time = ParseLineTime(text);
                            sessionEnd = time.HasValue ? time.Value + SessionDuration : TimeSpan.MinValue;
                            events.Add(new TreasuryEvent("entry", currentRun, text));
                            continue;
                        }
                    }

                    if (hasGold && text.Contains("금화 주머니를 획득", StringComparison.Ordinal))
                    {
                        if (currentRun <= 0)
                            continue;
                        TimeSpan? time = ParseLineTime(text);
                        if (!time.HasValue || time.Value > sessionEnd)
                            continue; // 세션 밖 획득(다른 컨텐츠)은 무시
                        events.Add(new TreasuryEvent("gold", currentRun, text));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to scan treasury events from {path}.", ex);
            }

            return events;
        }

        private static TimeSpan? ParseLineTime(string text)
        {
            Match m = LineTimeRegex.Match(text);
            if (!m.Success)
                return null;
            return new TimeSpan(
                int.Parse(m.Groups["h"].Value),
                int.Parse(m.Groups["m"].Value),
                int.Parse(m.Groups["s"].Value));
        }

        private static SortedDictionary<string, List<TreasuryEvent>> LoadArchive()
        {
            if (_archive is not null)
                return _archive;

            var result = new SortedDictionary<string, List<TreasuryEvent>>(StringComparer.Ordinal);
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
                                result[dayKey] = new List<TreasuryEvent>();
                            continue;
                        }

                        var entryMatch = ArchiveEntryRegex.Match(line);
                        if (!entryMatch.Success)
                            continue;

                        string key = entryMatch.Groups["date"].Value;
                        if (!result.TryGetValue(key, out var list))
                        {
                            list = new List<TreasuryEvent>();
                            result[key] = list;
                        }
                        list.Add(new TreasuryEvent(
                            entryMatch.Groups["kind"].Value,
                            int.Parse(entryMatch.Groups["run"].Value),
                            WebUtility.HtmlDecode(entryMatch.Groups["text"].Value)));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load treasury history archive.", ex);
            }

            return _archive = result;
        }

        /// <summary>아카이브를 주별 섹션·합계가 붙은 열람용 HTML로 통째로 다시 쓴다.</summary>
        private static void SaveArchive(SortedDictionary<string, List<TreasuryEvent>> archive)
        {
            try
            {
                Directory.CreateDirectory(LogStoragePaths.TreasuryDirectory);

                var sb = new StringBuilder();
                sb.AppendLine("<!doctype html>");
                sb.AppendLine("<html lang=\"ko\">");
                sb.AppendLine("<head>");
                sb.AppendLine("  <meta charset=\"utf-8\" />");
                sb.AppendLine("  <title>보물창고 획득 내역</title>");
                sb.AppendLine("  <style>");
                sb.AppendLine("    body{background:#111;color:#eee;font-family:'Malgun Gothic',sans-serif;font-size:13px;line-height:1.5;padding:16px;}");
                sb.AppendLine("    h1{font-size:17px;margin:0 0 4px;}");
                sb.AppendLine("    h2{color:#9ad3ff;border-bottom:1px solid rgba(154,211,255,.35);padding-bottom:4px;margin:20px 0 8px;font-size:15px;}");
                sb.AppendLine("    h3{color:#c9d1d9;margin:10px 0 4px;font-size:13px;font-weight:600;}");
                sb.AppendLine("    .tw{margin:1px 0;color:#aab3bb;}");
                sb.AppendLine("    .tw.entry{color:#9ad3ff;}");
                sb.AppendLine("    .tw.gold{color:#ffd84a;}");
                sb.AppendLine("    .note{color:#888;margin:0 0 8px;}");
                sb.AppendLine("  </style>");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");
                sb.AppendLine("<h1>보물창고 획득 내역</h1>");
                sb.AppendLine("<p class=\"note\">TWChatOverlay가 게임 로그에서 수집한 심연의 보물창고 기록입니다. 파란색은 입장, 금색은 금화 주머니 획득입니다. 앱이 다시 읽는 데이터 파일이므로 내용을 직접 수정하지 마세요.</p>");

                foreach (var weekGroup in archive
                             .GroupBy(kv => GetWeekStartOf(ParseDateKey(kv.Key)))
                             .OrderBy(g => g.Key))
                {
                    DateTime ws = weekGroup.Key;
                    DateTime we = ws.AddDays(6);
                    int golds = weekGroup.Sum(kv => kv.Value.Count(ev => ev.Kind == "gold"));
                    int runs = weekGroup.Sum(kv => kv.Value.Count(ev => ev.Kind == "entry"));
                    sb.AppendLine($"<h2 data-week=\"{ws:yyyy-MM-dd}\">{ws:M/d(ddd)} ~ {we:M/d(ddd)} — 입장 {runs}회 · 금화 주머니 {golds}개</h2>");

                    foreach (var kv in weekGroup.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    {
                        int dayGolds = kv.Value.Count(ev => ev.Kind == "gold");
                        string dayLabel = kv.Value.Count == 0 ? "기록 없음" : $"금화 주머니 {dayGolds}개";
                        sb.AppendLine($"<h3 class=\"day\" data-day=\"{kv.Key}\">{ParseDateKey(kv.Key):M/d(ddd)} — {dayLabel}</h3>");

                        foreach (var ev in kv.Value)
                            sb.AppendLine($"<div class=\"tw {ev.Kind}\" data-date=\"{kv.Key}\" data-run=\"{ev.Run}\">{WebUtility.HtmlEncode(ev.Text)}</div>");
                    }
                }

                sb.AppendLine("</body>");
                sb.AppendLine("</html>");
                File.WriteAllText(ArchivePath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save treasury history archive.", ex);
            }
        }

        private static DateTime ParseDateKey(string key)
            => DateTime.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static DateTime GetWeekStartOf(DateTime date)
            => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    }
}
