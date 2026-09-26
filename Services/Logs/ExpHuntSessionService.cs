using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TWChatOverlay.Models;
using TWChatOverlay.Services.LogAnalysis;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 사냥 기록: 경험치가 이어서 들어온 구간을 한 판으로 묶어 따로 남긴다.
    ///
    /// 원본 채팅 로그는 하루에 수십만 줄이라 매번 다시 읽으면 비싸다. 그래서
    ///  - 판이 끝날 때마다 요약 한 줄(시작·끝·총경험치·횟수)만 파일 끝에 덧붙이고,
    ///  - 지난 로그는 처음 한 번만 훑은 뒤 어디까지 읽었는지를 남겨 다음부터는 새 날짜만 본다.
    /// </summary>
    public sealed class ExpHuntSessionService
    {
        /// <summary>이 시간 동안 경험치가 없으면 판이 끝난 것으로 본다 (경험치 추적창의 [중지] 기준과 같다).</summary>
        public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(1);
        /// <summary>이보다 짧은 판은 남기지 않는다 (지나가다 한두 마리 잡은 것).</summary>
        public static readonly TimeSpan MinimumDuration = TimeSpan.FromMinutes(3);
        /// <summary>이보다 적게 번 판도 남기지 않는다 — 던전 한 판, 퀘스트 보상 같은 자투리.</summary>
        public const long MinimumTotalExp = 10_000_000_000;

        /// <summary>남길 만한 판인지 (시간도 길고 번 것도 있는 판).</summary>
        private static bool IsWorthKeeping(ExpHuntSession session)
            => session.Duration >= MinimumDuration && session.TotalExp > MinimumTotalExp;

        private static readonly Regex LogTimeRegex = new(@"^\[\s*(\d{1,2})시\s*(\d{1,2})분\s*(\d{1,2})초\s*\]", RegexOptions.Compiled);
        private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex FileDateRegex = new(@"TWChatLog_(?<y>\d{4})_(?<m>\d{2})_(?<d>\d{2})\.html$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly string _sessionFilePath;
        private readonly string _scanStateFilePath;
        private readonly object _syncRoot = new();

        // 진행 중인 판 (실시간)
        private DateTime? _liveStartedAt;
        private DateTime _liveLastGainAt;
        private long _liveTotalExp;
        private int _liveGainCount;

        public ExpHuntSessionService()
        {
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "ExpHunt");
            _sessionFilePath = Path.Combine(folder, "sessions.csv");
            _scanStateFilePath = Path.Combine(folder, "scanned.txt");

            // 사냥을 멈춘 판을 제때 마감한다 (다음 경험치를 기다리지 않고)
            _idleTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            _idleTimer.Tick += (_, _) => CheckIdle(DateTime.Now);
        }

        private readonly System.Windows.Threading.DispatcherTimer _idleTimer;

        public void Start() => _idleTimer.Start();

        public void Stop()
        {
            _idleTimer.Stop();
            FlushLive();
        }

        /// <summary>기록이 하나라도 생기거나 바뀌면 알린다 (기록 창 갱신용).</summary>
        public event Action? SessionsChanged;

        // ===== 실시간 =====

        /// <summary>경험치를 얻을 때마다 부른다. 판이 끊기면 앞 판을 파일에 남긴다.</summary>
        public void AddGain(DateTime at, long gained)
        {
            if (gained <= 0)
                return;

            lock (_syncRoot)
            {
                if (_liveStartedAt.HasValue && at - _liveLastGainAt > SessionGap)
                    FinishLiveUnlocked();

                _liveStartedAt ??= at;
                _liveLastGainAt = at;
                _liveTotalExp += gained;
                _liveGainCount++;
            }
        }

        /// <summary>마지막 획득에서 시간이 지났으면 진행 중인 판을 끝낸다. 1초 타이머 등에서 부른다.</summary>
        public void CheckIdle(DateTime now)
        {
            lock (_syncRoot)
            {
                if (_liveStartedAt.HasValue && now - _liveLastGainAt > SessionGap)
                    FinishLiveUnlocked();
            }
        }

        /// <summary>앱을 끌 때처럼 진행 중인 판을 지금 시점으로 마감한다.</summary>
        public void FlushLive()
        {
            lock (_syncRoot)
                FinishLiveUnlocked();
        }

        private void FinishLiveUnlocked()
        {
            if (_liveStartedAt is not DateTime started)
                return;

            var session = new ExpHuntSession
            {
                StartedAt = started,
                EndedAt = _liveLastGainAt,
                TotalExp = _liveTotalExp,
                GainCount = _liveGainCount,
            };

            _liveStartedAt = null;
            _liveTotalExp = 0;
            _liveGainCount = 0;

            if (!IsWorthKeeping(session))
                return;

            AppendUnlocked(new[] { session });
        }

        // ===== 파일 =====

        /// <summary>남아 있는 판 기록 (오래된 것 → 최근 순).</summary>
        public IReadOnlyList<ExpHuntSession> Load()
        {
            lock (_syncRoot)
                return LoadUnlocked();
        }

        private List<ExpHuntSession> LoadUnlocked()
        {
            var list = new List<ExpHuntSession>();
            try
            {
                if (!File.Exists(_sessionFilePath))
                    return list;

                foreach (string line in File.ReadLines(_sessionFilePath, Encoding.UTF8))
                {
                    // 기준이 바뀌기 전에 쌓인 자투리 판은 읽을 때 걸러낸다
                    if (ExpHuntSession.FromLine(line) is ExpHuntSession session && IsWorthKeeping(session))
                        list.Add(session);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to read hunt session file.", ex);
            }
            list.Sort((a, b) => a.StartedAt.CompareTo(b.StartedAt));
            return list;
        }

        /// <summary>판 기록을 파일 끝에 덧붙인다. 같은 시각으로 시작한 판은 건너뛴다.</summary>
        private void AppendUnlocked(IEnumerable<ExpHuntSession> sessions)
        {
            var incoming = sessions.Where(static s => s != null).ToList();
            if (incoming.Count == 0)
                return;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_sessionFilePath)!);
                // 같은 판인지는 시작 시각으로 본다. 실시간으로 적은 판과 로그에서 다시 읽은 판은
                // 시각이 1~2초 어긋날 수 있어 조금 넉넉하게 잡는다 (판 사이는 최소 1분이라 겹칠 일이 없다)
                var known = LoadUnlocked().Select(static s => s.StartedAt).ToList();
                var lines = new List<string>();
                foreach (var session in incoming)
                {
                    if (known.Any(at => Math.Abs((at - session.StartedAt).TotalSeconds) <= 30))
                        continue;
                    known.Add(session.StartedAt);
                    lines.Add(session.ToLine());
                }

                if (lines.Count == 0)
                    return;

                File.AppendAllLines(_sessionFilePath, lines, Encoding.UTF8);
                AppLogger.Info($"Hunt sessions appended. Count={lines.Count}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to append hunt sessions.", ex);
                return;
            }

            try { SessionsChanged?.Invoke(); } catch { }
        }

        // ===== 지난 로그 훑기 =====

        /// <summary>
        /// 아직 안 읽은 날짜의 채팅 로그에서 판을 뽑아 기록에 더한다.
        /// 오늘 로그도 읽는다 — 앱을 켜기 전에 사냥한 몫이 빠지면 안 되기 때문이다.
        /// 다만 오늘은 아직 이어지는 중이라 "읽은 날"로 적지 않고, 다음에 켤 때 다시 읽는다
        /// (이미 있는 판은 시작 시각으로 걸러진다).
        /// </summary>
        public Task<int> BackfillAsync(string? chatLogFolder)
        {
            return Task.Run(() =>
            {
                string folder = string.IsNullOrWhiteSpace(chatLogFolder)
                    ? @"C:\Nexon\TalesWeaver\ChatLog"
                    : chatLogFolder!;
                if (!Directory.Exists(folder))
                    return 0;

                DateTime scannedUntil = ReadScannedUntil();
                DateTime today = DateTime.Today;
                var targets = new List<(string Path, DateTime Date)>();
                foreach (string path in Directory.EnumerateFiles(folder, "TWChatLog_*.html"))
                {
                    if (TryReadFileDate(path) is not DateTime date)
                        continue;
                    if (date <= scannedUntil || date > today)
                        continue;
                    targets.Add((path, date));
                }

                if (targets.Count == 0)
                    return 0;

                targets.Sort((a, b) => a.Date.CompareTo(b.Date));
                var found = new List<ExpHuntSession>();
                DateTime lastDone = scannedUntil;
                foreach (var (path, date) in targets)
                {
                    try
                    {
                        found.AddRange(ReadSessionsFromLog(path, date));
                        if (date < today)
                            lastDone = date;   // 오늘은 아직 안 끝났으니 읽은 날로 치지 않는다
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Failed to scan hunt sessions. File='{Path.GetFileName(path)}'", ex);
                    }
                }

                lock (_syncRoot)
                    AppendUnlocked(found);
                WriteScannedUntil(lastDone);
                AppLogger.Info($"Hunt session backfill done. Files={targets.Count}, Sessions={found.Count}");
                return found.Count;
            });
        }

        /// <summary>로그 파일 하루치에서 판을 뽑는다.</summary>
        private static List<ExpHuntSession> ReadSessionsFromLog(string path, DateTime date)
        {
            var sessions = new List<ExpHuntSession>();
            DateTime? startedAt = null;
            DateTime lastGainAt = default;
            long totalExp = 0;
            int gainCount = 0;
            int hourOffset = 0;      // 자정을 넘겨 시각이 되돌아가면 하루를 더한다
            int previousHour = -1;

            void Close()
            {
                if (startedAt is not DateTime started)
                    return;
                var session = new ExpHuntSession
                {
                    StartedAt = started,
                    EndedAt = lastGainAt,
                    TotalExp = totalExp,
                    GainCount = gainCount,
                };
                if (IsWorthKeeping(session))
                    sessions.Add(session);
                startedAt = null;
                totalExp = 0;
                gainCount = 0;
            }

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // 채팅 로그는 CP949
            Encoding encoding = Encoding.GetEncoding(949);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, encoding);
            string? raw;
            while ((raw = reader.ReadLine()) != null)
            {
                // 경험치 줄만 본다 — 대부분의 줄을 여기서 걸러 비싼 처리를 건너뛴다
                if (raw.IndexOf("경험치", StringComparison.Ordinal) < 0)
                    continue;

                string text = HtmlTagRegex.Replace(raw, string.Empty).Trim();
                Match time = LogTimeRegex.Match(text);
                if (!time.Success)
                    continue;

                int hour = int.Parse(time.Groups[1].Value, CultureInfo.InvariantCulture);
                if (previousHour >= 0 && hour < previousHour)
                    hourOffset += 24;   // 00시로 되돌아갔다 = 다음 날
                previousHour = hour;

                DateTime at = date.AddHours(hour + hourOffset)
                    .AddMinutes(int.Parse(time.Groups[2].Value, CultureInfo.InvariantCulture))
                    .AddSeconds(int.Parse(time.Groups[3].Value, CultureInfo.InvariantCulture));

                string body = text.Substring(time.Length).Trim();
                long gained = ExperienceLogAnalyzer.ExtractGain(body, isSystemLog: true);
                if (gained <= 0)
                    continue;

                if (startedAt.HasValue && at - lastGainAt > SessionGap)
                    Close();

                startedAt ??= at;
                lastGainAt = at;
                totalExp += gained;
                gainCount++;
            }

            Close();
            return sessions;
        }

        private static DateTime? TryReadFileDate(string path)
        {
            Match match = FileDateRegex.Match(path);
            if (!match.Success)
                return null;
            return new DateTime(
                int.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups["d"].Value, CultureInfo.InvariantCulture));
        }

        private DateTime ReadScannedUntil()
        {
            try
            {
                if (!File.Exists(_scanStateFilePath))
                    return DateTime.MinValue;
                string text = File.ReadAllText(_scanStateFilePath, Encoding.UTF8).Trim();
                return DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
                    ? date
                    : DateTime.MinValue;
            }
            catch
            {
                return DateTime.MinValue;
            }
        }

        private void WriteScannedUntil(DateTime date)
        {
            if (date == DateTime.MinValue)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_scanStateFilePath)!);
                File.WriteAllText(_scanStateFilePath, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save hunt session scan state.", ex);
            }
        }
    }
}
