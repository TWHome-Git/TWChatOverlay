using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 주간 클리어 보상 시드 집계.
    /// 실측: 게임 로그의 "보상으로 N SEED를 획득했습니다" 줄을 실시간 파이프라인에서 바로 기록하고(ObserveLiveLine),
    ///       앱이 꺼져 있던 동안 쌓인 줄은 시작·날짜 전환 때 한 번 보충 스캔한다(CatchUpAsync).
    ///       기록은 Logs/Seed/SeedHistory.html 한 파일에 원본 줄과 함께 보관하고, 통계 창은 이 보관본만 읽는다.
    /// 예상: 일일/주간 컨텐츠 체크리스트에서 켜져 있는 항목의 주간 최대 보상을
    ///       그룹 한도 → 주간 66억(루비코나 제외) / 루비코나 28억 한도 순으로 적용해 계산.
    /// </summary>
    public sealed class WeeklySeedRewardService
    {
        private const long Eok = 100_000_000L;
        private const long Man = 10_000L;

        private const long WeeklyBucketCap = 66L * Eok;          // 루비코나 제외 주간 공통 한도
        private const long RubiconaBucketCap = 28L * Eok;        // 루비코나(환희·슬픔) 별도 한도

        private sealed record Entry(string ItemName, long WeeklySeed);
        private sealed record Group(long Cap, Entry[] Entries);

        // 항목명은 일일/주간 컨텐츠 체크리스트(DungeonItemConfigs 키)와 일치해야 한다.
        private readonly Group[] WeeklyGroups =
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

        // 아페티리아는 체크리스트 항목 하나("아페티리아")가 일반/어려움을 같이 다루는데 주간 시드가 다르다
        // (일반 3500만×3×7 = 7.35억, 어려움 4000만×3×7 = 8.4억). 난이도는 로그 마커로 판별한다
        // (GetApetiriaHard). 아페티리아 EX는 시드를 주지 않는다.
        private const string ApetiriaItemName = "아페티리아";
        private const long ApetiriaNormalWeeklySeed = 735L * Man * 100;
        private const long ApetiriaHardWeeklySeed = 840L * Man * 100;

        // 루비코나(환희·슬픔) — 각 보스·난이도 하루 2억 × 7일
        private readonly Entry[] RubiconaEntries =
        {
            new("추종하는 환희(일반)", 14L * Eok),
            new("응시하는 슬픔(일반)", 14L * Eok),
            new("추종하는 환희(어려움)", 14L * Eok),
            new("응시하는 슬픔(어려움)", 14L * Eok),
        };

        /// <summary>
        /// 체크리스트에서 켜진 항목 기준 주간 시드 한도 — 일반(루비코나 제외)과 루비코나 분리.
        /// <paramref name="apetiriaHard"/>는 이번 주 아페티리아 난이도(GetApetiriaHard, 기본 어려움).
        /// </summary>
        public (long General, long Rubicona) ComputeWeeklySeedCaps(ChatSettings settings, bool apetiriaHard = true)
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
                    if (IsItemEnabled(settings, ApetiriaItemName))
                        sum += apetiriaHard ? ApetiriaHardWeeklySeed : ApetiriaNormalWeeklySeed;
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

        private bool IsItemEnabled(ChatSettings settings, string itemName)
        {
            // 체크리스트를 한 번도 만지지 않은 항목은 기본 활성으로 취급
            return !settings.DungeonItemConfigs.TryGetValue(itemName, out var config) || config.IsEnabled;
        }

        // ───────────────────────── 로그 줄 분류 ─────────────────────────

        // 줄에 "보상으로"가 있는지는 먼저 거르고, 금액은 "SEED를 획득했" 직전 값을 읽는다.
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

        private const string KindWeekly = "weekly";   // 주간 버킷
        private const string KindDaily = "daily";     // 일간 버킷 (루비코나 환희·슬픔)
        private const string KindPartial = "partial"; // 주간 한도 직전 부분 지급 (주간 버킷에 합산)
        private const string KindMarker = "marker";   // 금액 없는 판별용 줄 (아페티리아 난이도)

        private sealed record SeedEntry(string Kind, long Amount, string Text);

        // 난이도 판별 마커: 어려움은 "[아페티리아 어려움 보상 상자] 아이템을 1개 획득하였습니다."가 클리어마다 찍힌다.
        // 일반은 고유 문구가 없으므로 "아페티리아 클리어 횟수:" 줄만 있고 그날 어려움 상자가 없으면 일반으로 본다.
        // ("아페티리아(EX) 클리어 횟수:"는 다른 컨텐츠라 제외)
        private const string ApetiriaHardMarker = "[아페티리아 어려움 보상 상자]";
        private const string ApetiriaClearMarker = "아페티리아 클리어 횟수:";

        private static long ParseAmount(Match match)
        {
            long value = 0;
            if (match.Groups["eok"].Success)
                value += long.Parse(match.Groups["eok"].Value) * Eok;
            if (match.Groups["man"].Success)
                value += long.Parse(match.Groups["man"].Value) * Man;
            return value;
        }

        /// <summary>
        /// 하루치 로그 줄을 순서대로 받아 시드 항목으로 분류한다. 파일 스캔과 실시간 경로가 같은 규칙을 쓴다.
        /// 루비코나 몫은 시드 줄 주변(앞 3줄/뒤 8줄)의 "레이티아/설계자 퇴치 보상" 줄로 판별하므로
        /// (금액 2억만으로는 최후의 결전과 구분되지 않음) 시드 줄은 뒤 8줄이 지날 때까지 대기시킨다.
        /// </summary>
        private sealed class DayCollector
        {
            private const int MarkerLookBehind = 3;  // 시드 줄 앞 몇 줄까지의 마커를 인정하는가
            private const int MarkerLookAhead = 8;   // 시드 줄 뒤 몇 줄까지의 마커를 인정하는가

            private readonly Action<SeedEntry> _sink;
            private readonly List<(int Index, long Value, string Text)> _pending = new();
            private int _lineIndex = -1;
            private int _lastRubiconaMarkerIndex = int.MinValue;
            private string? _lastApetiriaMarker;

            public DayCollector(Action<SeedEntry> sink)
            {
                _sink = sink;
            }

            public void Feed(string line)
            {
                _lineIndex++;
                FlushPending(_lineIndex);

                bool hasReward = line.Contains("보상으로", StringComparison.Ordinal);
                bool hasPartial = line.Contains("획득 제한으로", StringComparison.Ordinal);
                bool hasApetiria = line.Contains("아페티리아", StringComparison.Ordinal);
                if (!hasReward && !hasPartial && !hasApetiria)
                    return;

                string text = HtmlTagRegex.Replace(line, string.Empty);

                if (hasApetiria)
                {
                    // 난이도 판별용 마커 — 같은 마커가 이어지면 하나만 남긴다
                    string? marker = null;
                    if (text.Contains(ApetiriaHardMarker, StringComparison.Ordinal))
                        marker = ApetiriaHardMarker;
                    else if (text.Contains(ApetiriaClearMarker, StringComparison.Ordinal))
                        marker = ApetiriaClearMarker;
                    if (marker is not null && marker != _lastApetiriaMarker)
                    {
                        _lastApetiriaMarker = marker;
                        _sink(new SeedEntry(KindMarker, 0, marker));
                    }
                    if (!hasReward && !hasPartial)
                        return;
                }

                if (hasPartial)
                {
                    var partial = PartialSeedRegex.Match(text);
                    if (partial.Success)
                    {
                        long clipped = ParseAmount(partial);
                        if (clipped > 0)
                            _sink(new SeedEntry(KindPartial, clipped, text.Trim()));
                    }
                    return;
                }

                // 보급품 탈환은 한 판에 "콘텐츠 클리어 보상으로 3000만 SEED"와
                // "보급품 탈환 성공 보상으로 … 3000만 Seed" 두 줄이 찍힌다(실수령은 3000만 1회).
                // 중복 합산을 막기 위해 내용 중복인 성공 보상 줄은 제외한다.
                if (text.Contains("보급품 탈환 성공 보상으로", StringComparison.Ordinal))
                    return;

                if (text.Contains("퇴치 보상으로", StringComparison.Ordinal) &&
                    (text.Contains("레이티아", StringComparison.Ordinal) ||
                     text.Contains("설계자", StringComparison.Ordinal)))
                {
                    _lastRubiconaMarkerIndex = _lineIndex;
                    // 대기 중인 시드 줄은 모두 뒤 8줄 안에 있는 것들이므로(그 밖은 이미 주간으로 확정) 루비코나 몫
                    foreach (var (_, value, pendingText) in _pending)
                        _sink(new SeedEntry(KindDaily, value, pendingText));
                    _pending.Clear();
                    return;
                }

                if (!text.Contains("를 획득했", StringComparison.Ordinal))
                    return;

                var match = SeedRewardRegex.Match(text);
                if (!match.Success)
                    return;

                long amount = ParseAmount(match);
                if (amount <= 0)
                    return;

                if (_lastRubiconaMarkerIndex >= _lineIndex - MarkerLookBehind)
                    _sink(new SeedEntry(KindDaily, amount, text.Trim()));
                else
                    _pending.Add((_lineIndex, amount, text.Trim()));
            }

            /// <summary>더 올 줄이 없을 때(파일 끝) 대기 중인 시드를 주간 몫으로 확정한다.</summary>
            public void Complete() => FlushPending(int.MaxValue);

            private void FlushPending(int currentIndex)
            {
                int flushed = 0;
                foreach (var (index, value, text) in _pending)
                {
                    if (currentIndex != int.MaxValue && index + MarkerLookAhead >= currentIndex)
                        break;
                    _sink(new SeedEntry(KindWeekly, value, text));
                    flushed++;
                }
                if (flushed > 0)
                    _pending.RemoveRange(0, flushed);
            }
        }

        /// <summary>하루치 게임 로그를 처음부터 읽어 시드 항목과 읽은 시점의 파일 길이를 돌려준다.</summary>
        private (List<SeedEntry> Entries, long Length) ScanDayFile(string path)
        {
            var entries = new List<SeedEntry>();
            long length = 0;
            try
            {
                var encoding = Encoding.GetEncoding(949);
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                length = fs.Length;
                using var reader = new StreamReader(fs, encoding, detectEncodingFromByteOrderMarks: true);
                var collector = new DayCollector(entries.Add);
                string? line;
                while ((line = reader.ReadLine()) != null)
                    collector.Feed(line);
                collector.Complete();
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to scan seed rewards from {path}.", ex);
            }

            return (entries, length);
        }

        // ───────────────────────── 보관본 ─────────────────────────

        /// <summary>하루치 기록. 항목 목록과 "게임 로그 파일을 어디까지 반영했는지"를 함께 든다.</summary>
        private sealed class DayRecord
        {
            public List<SeedEntry> Entries { get; } = new();

            /// <summary>반영한 게임 로그 파일 길이(바이트). 파일이 이보다 길면 앱이 못 본 줄이 있다는 뜻.</summary>
            public long CoveredLength { get; set; } = -1;

            /// <summary>이 기록을 만든 분류 규칙 번호(ScanVersion). 낮으면 파일이 남아 있을 때 한 번 다시 읽는다.</summary>
            public int ScanVersion { get; set; }

            /// <summary>마지막 전체 스캔 뒤 실시간으로 추가된 항목 — 재스캔 때 스캔 결과에 없는 것만 남긴다.</summary>
            public List<SeedEntry> LiveSinceScan { get; } = new();
        }

        // 분류 규칙 번호 — 규칙이 바뀌면(마커 추가 등) 올려서 파일이 남아 있는 보관분을 한 번 다시 읽게 한다.
        // v3: 기록 단위를 "반영한 파일 길이"로 바꿈. 구버전 보관분은 오늘 파일을 읽은 결과에 완결 표시가 붙어
        //     다음 날부터 재스캔되지 않는 문제가 있었으므로 한 번 다시 읽어 길이를 채운다.
        private const int ScanVersion = 3;

        // 시작·날짜 전환 때 보충 스캔하는 범위. 그보다 오래된 날짜는 통계 창에서 조회할 때 미보관분만 읽는다.
        private const int CatchUpDays = 14;

        private readonly object ArchiveLock = new();
        private SortedDictionary<string, DayRecord>? _archive;

        /// <summary>보관본이 바뀌었을 때(실시간 기록·보충 스캔). 백그라운드 스레드에서 올라온다.</summary>
        public event Action? Changed;

        private string ArchivePath => Path.Combine(LogStoragePaths.SeedDirectory, "SeedHistory.html");

        private static readonly Regex ArchiveEntryRegex = new(
            "<div class=\"seed (?<kind>weekly|daily|partial|marker)\" data-date=\"(?<date>\\d{4}-\\d{2}-\\d{2})\" data-amount=\"(?<amount>\\d+)\">(?<text>.*?)</div>",
            RegexOptions.Compiled);

        private static readonly Regex ArchiveDayRegex = new(
            "class=\"day\" data-day=\"(?<date>\\d{4}-\\d{2}-\\d{2})\"(?: data-scan=\"(?<scan>\\d+)\")?(?: data-len=\"(?<len>\\d+)\")?",
            RegexOptions.Compiled);

        private static readonly Regex LogFileDateRegex = new(
            @"TWChatLog_(?<y>\d{4})_(?<m>\d{2})_(?<d>\d{2})\.html$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private DateTime ParseDateKey(string key)
            => DateTime.ParseExact(key, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        private string ToDateKey(DateTime date) => date.ToString("yyyy-MM-dd");

        private DateTime GetWeekStartOf(DateTime date)
            => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

        private string GetLogPath(string logDir, DateTime day)
            => Path.Combine(logDir, $"TWChatLog_{day:yyyy_MM_dd}.html");

        private bool TryGetLogFileDate(string path, out DateTime date)
        {
            date = default;
            var m = LogFileDateRegex.Match(Path.GetFileName(path));
            if (!m.Success)
                return false;
            return DateTime.TryParseExact(
                $"{m.Groups["y"].Value}-{m.Groups["m"].Value}-{m.Groups["d"].Value}",
                "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private DayRecord GetOrCreateRecord(SortedDictionary<string, DayRecord> archive, string key)
        {
            if (!archive.TryGetValue(key, out var record))
            {
                record = new DayRecord();
                archive[key] = record;
            }
            return record;
        }

        /// <summary>
        /// 하루치 파일을 처음부터 다시 읽어 그날 기록을 교체한다. 그 사이 실시간으로 들어온 항목 중
        /// 스캔 결과에 없는 것(스캔 시점 이후 줄)은 남긴다. 호출자가 ArchiveLock을 잡고 있어야 한다.
        /// </summary>
        private void RescanDay(SortedDictionary<string, DayRecord> archive, string key, string path)
        {
            var (entries, length) = ScanDayFile(path);
            var record = GetOrCreateRecord(archive, key);

            var scannedTexts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
                if (entry.Kind != KindMarker)
                    scannedTexts.Add(entry.Text);

            var keptLive = record.LiveSinceScan
                .Where(e => e.Kind != KindMarker && !scannedTexts.Contains(e.Text))
                .ToList();

            record.Entries.Clear();
            record.Entries.AddRange(entries);
            record.Entries.AddRange(keptLive);
            record.LiveSinceScan.Clear();
            record.LiveSinceScan.AddRange(keptLive);
            record.CoveredLength = Math.Max(record.CoveredLength, length);
            record.ScanVersion = ScanVersion;
        }

        /// <summary>파일이 자랐거나(앱이 못 본 줄) 구버전 규칙으로 읽은 날짜면 다시 읽는다. 읽었으면 true.</summary>
        private bool RescanDayIfStale(SortedDictionary<string, DayRecord> archive, string key, string path)
        {
            long length;
            try { length = new FileInfo(path).Length; }
            catch { return false; }

            if (archive.TryGetValue(key, out var record) &&
                record.ScanVersion >= ScanVersion &&
                record.CoveredLength == length)
                return false;

            RescanDay(archive, key, path);
            return true;
        }

        // ───────────────────────── 실시간 기록 ─────────────────────────

        private string? _liveDayKey;
        private DayCollector? _liveCollector;
        private bool _liveChanged;

        /// <summary>
        /// 실시간 파이프라인에서 줄마다 호출한다. 시드 줄이면 그 파일 날짜의 기록에 바로 추가한다.
        /// <paramref name="sourcePath"/>는 줄을 읽은 게임 로그 파일(날짜 판별용), <paramref name="filePosition"/>은
        /// 읽은 뒤의 파일 위치 — 다음 시작 때 "어디까지 봤는지"를 파일 길이와 비교하는 데 쓴다.
        /// </summary>
        public void ObserveLiveLine(string? sourcePath, string html, long filePosition)
        {
            if (string.IsNullOrEmpty(html))
                return;

            string key = ToDateKey(!string.IsNullOrEmpty(sourcePath) && TryGetLogFileDate(sourcePath, out var fileDate)
                ? fileDate
                : DateTime.Today);

            bool changed;
            lock (ArchiveLock)
            {
                var archive = LoadArchive();
                if (_liveCollector is null || _liveDayKey != key)
                {
                    // 날짜가 바뀌면 전날 대기분은 주간 몫으로 확정한다 (sink가 전날 키를 물고 있다)
                    _liveCollector?.Complete();
                    _liveDayKey = key;
                    string sinkKey = key;
                    _liveCollector = new DayCollector(entry => AppendLive(sinkKey, entry));
                }

                _liveCollector.Feed(html);

                var record = GetOrCreateRecord(archive, key);
                if (filePosition > record.CoveredLength)
                    record.CoveredLength = filePosition;

                changed = _liveChanged;
                _liveChanged = false;
            }

            if (changed)
            {
                ScheduleSave();
                RaiseChanged();
            }
        }

        // ArchiveLock 안에서만 호출된다 (DayCollector.Feed/Complete → sink)
        private void AppendLive(string key, SeedEntry entry)
        {
            var record = GetOrCreateRecord(_archive!, key);

            // 시작 직후에는 보충 스캔과 실시간 줄이 같은 구간을 겹쳐 볼 수 있다 — 같은 줄(시각 포함)은 한 번만
            if (entry.Kind != KindMarker)
            {
                foreach (var existing in record.Entries)
                    if (existing.Kind != KindMarker && existing.Text == entry.Text)
                        return;
            }

            record.Entries.Add(entry);
            record.LiveSinceScan.Add(entry);
            _liveChanged = true;
        }

        private void RaiseChanged()
        {
            try { Changed?.Invoke(); }
            catch (Exception ex) { AppLogger.Warn("Seed archive change handler failed.", ex); }
        }

        // 실시간 기록은 몇 초 안에 몰려 들어오므로 저장을 잠깐 모아서 한 번에 쓴다
        private int _savePending;

        private void ScheduleSave()
        {
            if (Interlocked.Exchange(ref _savePending, 1) == 1)
                return;

            _ = Task.Delay(1500).ContinueWith(_ =>
            {
                Interlocked.Exchange(ref _savePending, 0);
                lock (ArchiveLock)
                {
                    if (_archive is not null)
                        SaveArchive(_archive);
                }
            }, TaskScheduler.Default);
        }

        // ───────────────────────── 보충 스캔 ─────────────────────────

        /// <summary>
        /// 앱이 꺼져 있던 동안(그리고 오늘 앱을 켜기 전 구간) 쌓인 줄을 보충한다. 최근 CatchUpDays일의
        /// 게임 로그 중 기록보다 길어진 파일만 다시 읽는다. 시작·날짜 전환 때 호출한다.
        /// </summary>
        public Task CatchUpAsync(string? logDir)
        {
            return Task.Run(() =>
            {
                bool changed = false;
                try
                {
                    if (string.IsNullOrWhiteSpace(logDir) || !Directory.Exists(logDir))
                        return;

                    DateTime today = DateTime.Today;
                    DateTime from = today.AddDays(-CatchUpDays);
                    int rescanned = 0;

                    lock (ArchiveLock)
                    {
                        var archive = LoadArchive();
                        foreach (string path in Directory.EnumerateFiles(logDir, "TWChatLog_*.html"))
                        {
                            if (!TryGetLogFileDate(path, out var day) || day < from || day > today)
                                continue;
                            if (RescanDayIfStale(archive, ToDateKey(day), path))
                                rescanned++;
                        }

                        if (rescanned > 0)
                        {
                            SaveArchive(archive);
                            changed = true;
                        }
                    }

                    if (rescanned > 0)
                        AppLogger.Info($"Seed archive catch-up rescanned {rescanned} day file(s).");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Seed archive catch-up failed.", ex);
                }

                if (changed)
                    RaiseChanged();
            });
        }

        // ───────────────────────── 조회 ─────────────────────────

        /// <summary>
        /// 주간 범위의 클리어 보상 시드를 보관본에서 합산 — 주간(일반)과 일간(루비코나) 분리.
        /// 보관본에 없는 날짜(오래된 주 조회)와 구버전 규칙으로 읽은 날짜만 게임 로그를 한 번 읽는다.
        /// 오늘을 포함한 최근 날짜는 실시간 기록과 시작 시 보충 스캔이 채우므로 여기서는 읽지 않는다.
        /// </summary>
        public async Task<(long General, long Rubicona)> SumWeeklyClearSeedAsync(
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

                        string key = ToDateKey(day);
                        string path = string.IsNullOrWhiteSpace(logDir) ? string.Empty : GetLogPath(logDir, day);
                        bool fileExists = path.Length > 0 && File.Exists(path);

                        if (!archive.TryGetValue(key, out var record))
                        {
                            if (fileExists)
                                RescanDay(archive, key, path);
                            else
                                GetOrCreateRecord(archive, key).ScanVersion = ScanVersion;
                            record = archive[key];
                            dirty = true;
                        }
                        else if (record.ScanVersion < ScanVersion && fileExists)
                        {
                            RescanDay(archive, key, path);
                            dirty = true;
                        }

                        foreach (var entry in record.Entries)
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

        /// <summary>
        /// 아페티리아 난이도 판별: 어려움이면 true, 일반이면 false.
        /// 기본은 어려움이고, 이번 주 로그에서 어려움 상자 없이 클리어한 날이 마지막이면 일반으로 축소한다.
        /// 이번 주에 아직 안 돌았으면 어려움으로 둔다.
        /// SumWeeklyClearSeedAsync로 주간 범위를 채운 뒤에 호출해야 이번 주 기록이 반영된다.
        /// </summary>
        public bool GetApetiriaHard(DateTime weekStart, DateTime weekEnd)
        {
            lock (ArchiveLock)
            {
                var archive = LoadArchive();
                string startKey = ToDateKey(weekStart);
                string endKey = ToDateKey(weekEnd);

                foreach (var kv in archive.Reverse())
                {
                    if (string.CompareOrdinal(kv.Key, endKey) > 0)
                        continue;
                    if (string.CompareOrdinal(kv.Key, startKey) < 0)
                        break;
                    bool hard = false, cleared = false;
                    foreach (var entry in kv.Value.Entries)
                    {
                        if (entry.Kind != KindMarker) continue;
                        if (entry.Text == ApetiriaHardMarker) hard = true;
                        else if (entry.Text == ApetiriaClearMarker) cleared = true;
                    }
                    if (hard) return true;
                    if (cleared) return false;
                }

                return true;
            }
        }

        // ───────────────────────── 보관본 파일 ─────────────────────────

        private SortedDictionary<string, DayRecord> LoadArchive()
        {
            if (_archive is not null)
                return _archive;

            var result = new SortedDictionary<string, DayRecord>(StringComparer.Ordinal);
            try
            {
                if (File.Exists(ArchivePath))
                {
                    foreach (string line in File.ReadLines(ArchivePath))
                    {
                        var dayMatch = ArchiveDayRegex.Match(line);
                        if (dayMatch.Success)
                        {
                            var record = GetOrCreateRecord(result, dayMatch.Groups["date"].Value);
                            if (dayMatch.Groups["scan"].Success)
                                record.ScanVersion = int.Parse(dayMatch.Groups["scan"].Value);
                            if (dayMatch.Groups["len"].Success)
                                record.CoveredLength = long.Parse(dayMatch.Groups["len"].Value);
                            continue;
                        }

                        var entryMatch = ArchiveEntryRegex.Match(line);
                        if (!entryMatch.Success)
                            continue;

                        GetOrCreateRecord(result, entryMatch.Groups["date"].Value).Entries.Add(new SeedEntry(
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

        private (long Weekly, long Daily) SumOf(IEnumerable<SeedEntry> entries)
        {
            long weekly = 0, daily = 0;
            foreach (var entry in entries)
            {
                if (entry.Kind == KindDaily) daily += entry.Amount;
                else weekly += entry.Amount;
            }
            return (weekly, daily);
        }

        /// <summary>아카이브를 주별 섹션·합계가 붙은 열람용 HTML로 통째로 다시 쓴다.</summary>
        private void SaveArchive(SortedDictionary<string, DayRecord> archive)
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
                sb.AppendLine("    .seed.marker{color:#666;}");
                sb.AppendLine("    .note{color:#888;margin:0 0 8px;}");
                sb.AppendLine("  </style>");
                sb.AppendLine("</head>");
                sb.AppendLine("<body>");
                sb.AppendLine("<h1>시드 획득 내역</h1>");
                sb.AppendLine("<p class=\"note\">TWChatOverlay가 게임 로그에서 수집한 클리어 보상 시드 기록입니다. 파란색은 루비코나, 주황색은 주간 한도 직전 부분 지급입니다. 앱이 다시 읽는 데이터 파일이므로 내용을 직접 수정하지 마세요.</p>");

                foreach (var weekGroup in archive
                             .GroupBy(kv => GetWeekStartOf(ParseDateKey(kv.Key)))
                             .OrderBy(g => g.Key))
                {
                    DateTime ws = weekGroup.Key;
                    DateTime we = ws.AddDays(6);
                    var (weekly, daily) = SumOf(weekGroup.SelectMany(kv => kv.Value.Entries));

                    sb.AppendLine($"<h2 data-week=\"{ws:yyyy-MM-dd}\">{ws:M/d(ddd)} ~ {we:M/d(ddd)} — 일반지역 {FormatSeed(weekly)} · 루비코나 {FormatSeed(daily)} · 합계 {FormatSeed(weekly + daily)}</h2>");

                    foreach (var kv in weekGroup.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                    {
                        var record = kv.Value;
                        var (dayWeekly, dayDaily) = SumOf(record.Entries);

                        string dayLabel = record.Entries.Count == 0
                            ? "기록 없음"
                            : $"일반지역 {FormatSeed(dayWeekly)} · 루비코나 {FormatSeed(dayDaily)}";
                        string scanAttr = record.ScanVersion > 0 ? $" data-scan=\"{record.ScanVersion}\"" : string.Empty;
                        string lenAttr = record.CoveredLength >= 0 ? $" data-len=\"{record.CoveredLength}\"" : string.Empty;
                        sb.AppendLine($"<h3 class=\"day\" data-day=\"{kv.Key}\"{scanAttr}{lenAttr}>{ParseDateKey(kv.Key):M/d(ddd)} — {dayLabel}</h3>");

                        foreach (var entry in record.Entries)
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

        // ───────────────────────── 한도 ─────────────────────────

        // 게임의 주간 버킷 한도 이력: 2026-07-20 주부터 66억, 그 이전은 60억 (로그 실측으로 확인)
        private readonly DateTime WeeklyCap66Since = new(2026, 7, 20);

        /// <summary>
        /// 주간 표시용 보정: 과거 로그에는 당시 일일 컨텐츠(군영 퀘스트류) 시드에 식별 문구가 없어
        /// 일반지역 몫으로 합산된다. 당시 주간 한도를 넘는 초과분을 "기타"로 분리해 돌려준다
        /// (루비코나가 아니므로 루비코나 행에 합치지 않는다).
        /// </summary>
        public (long Weekly, long Other) SplitWeeklyOverflow(DateTime weekStart, long general)
        {
            long cap = GetBucketCaps(weekStart).General;
            long overflow = Math.Max(0, general - cap);
            return (general - overflow, overflow);
        }

        /// <summary>
        /// 게임이 정한 주간 획득 한도 — 일반지역(루비코나 제외)과 루비코나.
        /// 화면의 "실측 / 한도"에서 뒤 숫자로 쓴다. 체크리스트에서 무엇을 켰는지와는 무관하다.
        /// (켜 둔 항목 기준 합은 ComputeWeeklySeedCaps — 안 도는 컨텐츠가 있으면 한도보다 작게 나와 헷갈린다)
        /// </summary>
        public (long General, long Rubicona) GetBucketCaps(DateTime weekStart)
        {
            long general = weekStart >= WeeklyCap66Since ? WeeklyBucketCap : 60L * Eok;
            return (general, RubiconaBucketCap);
        }

        /// <summary>시드 금액을 "93.15억" / "8500만" 형태로 표기.</summary>
        public string FormatSeed(long seed)
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
