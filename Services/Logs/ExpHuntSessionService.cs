using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
                // 판과 판 사이는 최소 1분이 비어 있으므로, 시간대가 겹치면 같은 판이다.
                // (앱을 켠 뒤부터 센 실시간 판과, 나중에 로그에서 통째로 읽은 판은 시작 시각이 달라진다)
                var kept = LoadUnlocked();
                var appended = new List<string>();
                bool rewrite = false;
                foreach (var session in incoming)
                {
                    int index = kept.FindIndex(existing => Overlaps(existing, session));
                    if (index >= 0)
                    {
                        // 더 길게 잡힌 쪽(대개 로그에서 읽은 쪽)이 실제 판에 가깝다
                        if (session.Duration > kept[index].Duration || session.TotalExp > kept[index].TotalExp)
                        {
                            kept[index] = session;
                            rewrite = true;
                        }
                        continue;
                    }

                    kept.Add(session);
                    appended.Add(session.ToLine());
                }

                if (rewrite)
                {
                    kept.Sort((x, y) => x.StartedAt.CompareTo(y.StartedAt));
                    File.WriteAllLines(_sessionFilePath, kept.Select(static s => s.ToLine()), Encoding.UTF8);
                    AppLogger.Info($"Hunt sessions rewritten. Count={kept.Count}");
                }
                else if (appended.Count > 0)
                {
                    File.AppendAllLines(_sessionFilePath, appended, Encoding.UTF8);
                    AppLogger.Info($"Hunt sessions appended. Count={appended.Count}");
                }
                else
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to append hunt sessions.", ex);
                return;
            }

            try { SessionsChanged?.Invoke(); } catch { }
        }

        /// <summary>이미 쌓인 기록에서 시간대가 겹치는 판을 하나로 합친다 (예전에 두 번 들어간 것 정리).</summary>
        private void DedupeExistingUnlocked()
        {
            var all = LoadUnlocked();
            var merged = new List<ExpHuntSession>();
            foreach (var session in all)
            {
                int index = merged.FindIndex(existing => Overlaps(existing, session));
                if (index < 0)
                {
                    merged.Add(session);
                    continue;
                }

                if (session.Duration > merged[index].Duration || session.TotalExp > merged[index].TotalExp)
                    merged[index] = session;
            }

            if (merged.Count == all.Count)
                return;

            try
            {
                merged.Sort((x, y) => x.StartedAt.CompareTo(y.StartedAt));
                Directory.CreateDirectory(Path.GetDirectoryName(_sessionFilePath)!);
                File.WriteAllLines(_sessionFilePath, merged.Select(static s => s.ToLine()), Encoding.UTF8);
                AppLogger.Info($"Hunt sessions deduped. {all.Count} -> {merged.Count}");
                SessionsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to dedupe hunt sessions.", ex);
            }
        }

        /// <summary>두 판의 시간대가 겹치는지 (1분 여유). 판 사이는 최소 1분이 비므로 겹치면 같은 판이다.</summary>
        private static bool Overlaps(ExpHuntSession a, ExpHuntSession b)
        {
            TimeSpan slack = TimeSpan.FromSeconds(30);
            return a.StartedAt - slack <= b.EndedAt && b.StartedAt - slack <= a.EndedAt;
        }

        // ===== 지난 로그 훑기 =====

        // 이번 실행에서 아직 읽지 않은 날짜(scannedUntil 이후 ~ 오늘). 시작 패스가 전경(최근분)·배경(나머지)으로 나뉘어도
        // 여기서 이어지므로 "어디까지 읽었나"는 빈 날짜 없이 연속으로만 올라간다.
        private HashSet<DateTime>? _pendingScanDates;
        private DateTime _scannedUntil = DateTime.MinValue;
        private DateTime _maxScannedPastDate = DateTime.MinValue;
        private bool _legacyMaintenanceDone;

        /// <summary>
        /// 원본 로그 공용 패스에 올라타는 소비자. 아직 안 읽은 날짜의 채팅 로그에서 판을 뽑아 기록에 더한다.
        /// 오늘 로그도 읽는다 — 앱을 켜기 전에 사냥한 몫이 빠지면 안 되기 때문이다.
        /// 다만 오늘은 아직 이어지는 중이라 "읽은 날"로 적지 않고, 다음에 켤 때 다시 읽는다
        /// (이미 있는 판은 시간대 겹침으로 걸러진다).
        /// </summary>
        public IRawLogScanConsumer CreateScanConsumer(string? chatLogFolder)
            => new ScanConsumer(this, ResolveFolder(chatLogFolder));

        /// <summary>공용 패스 없이 혼자 훑을 때(테스트·수동). 시작 경로는 <see cref="CreateScanConsumer"/>를 쓴다.</summary>
        public Task BackfillAsync(string? chatLogFolder)
        {
            string folder = ResolveFolder(chatLogFolder);
            if (!Directory.Exists(folder))
                return Task.CompletedTask;
            return RawLogScanService.RunAsync(folder, new[] { CreateScanConsumer(folder) });
        }

        private static string ResolveFolder(string? chatLogFolder)
            => string.IsNullOrWhiteSpace(chatLogFolder) ? @"C:\Nexon\TalesWeaver\ChatLog" : chatLogFolder!;

        /// <summary>읽을 날짜 목록을 한 번만 만든다. _syncRoot 안에서 부른다.</summary>
        private void EnsureScanPlanUnlocked(string folder)
        {
            if (_pendingScanDates != null)
                return;

            RunLegacyMaintenanceOnceUnlocked();

            _scannedUntil = ReadScannedUntil();
            _maxScannedPastDate = _scannedUntil;
            DateTime today = DateTime.Today;
            var pending = new HashSet<DateTime>();
            foreach (RawLogFile file in RawChatLogReader.EnumerateFiles(folder))
            {
                if (file.Date <= _scannedUntil || file.Date > today)
                    continue;
                pending.Add(file.Date);
            }
            _pendingScanDates = pending;
        }

        /// <summary>날짜 하나를 다 읽었다. 그 앞 날짜가 전부 끝났을 때만 "읽은 날"을 올린다. _syncRoot 안에서 부른다.</summary>
        private void MarkDateScannedUnlocked(DateTime date)
        {
            if (_pendingScanDates == null)
                return;

            _pendingScanDates.Remove(date);
            DateTime today = DateTime.Today;
            if (date >= today)
                return; // 오늘은 아직 안 끝났으니 읽은 날로 치지 않는다

            if (date > _maxScannedPastDate)
                _maxScannedPastDate = date;

            DateTime newUntil = _maxScannedPastDate;
            foreach (DateTime pending in _pendingScanDates)
            {
                if (pending < today && pending.AddDays(-1) < newUntil)
                    newUntil = pending.AddDays(-1);
            }

            if (newUntil > _scannedUntil)
            {
                _scannedUntil = newUntil;
                WriteScannedUntil(newUntil);
            }
        }

        private sealed class ScanConsumer : IRawLogTailConsumer
        {
            private readonly ExpHuntSessionService _owner;
            private readonly string _folder;
            private HuntDayScanner? _day;
            private long _fileLength;
            private int _files;
            private int _sessions;

            public (RawLogFile File, long Length)? ConsumedToday { get; private set; }

            public ScanConsumer(ExpHuntSessionService owner, string folder)
            {
                _owner = owner;
                _folder = folder;
            }

            public void BeginScan(IReadOnlyList<RawLogFile> files)
            {
                lock (_owner._syncRoot)
                    _owner.EnsureScanPlanUnlocked(_folder);
            }

            public bool BeginFile(RawLogFile file, long fileLength)
            {
                lock (_owner._syncRoot)
                {
                    if (_owner._pendingScanDates == null || !_owner._pendingScanDates.Contains(file.Date))
                        return false;
                }

                _day = new HuntDayScanner(file.Date);
                _fileLength = fileLength;
                return true;
            }

            public void OnLine(RawLogLine line) => _day?.Feed(line);

            public void BeginTail(RawLogFile file) => _day = new HuntDayScanner(file.Date);

            public void EndTail(RawLogFile file, long newLength)
            {
                HuntDayScanner? day = _day;
                _day = null;
                if (day == null)
                    return;

                // 틈 구간의 판은 시간대 겹침으로 기존 판과 합쳐진다 (AppendUnlocked)
                List<ExpHuntSession> found = day.Complete();
                lock (_owner._syncRoot)
                    _owner.AppendUnlocked(found);
                ConsumedToday = (file, newLength);
            }

            public void EndFile(RawLogFile file, RawLogReadOutcome outcome)
            {
                HuntDayScanner? day = _day;
                _day = null;
                if (day == null)
                    return;

                List<ExpHuntSession> found = day.Complete();
                _files++;
                _sessions += found.Count;
                lock (_owner._syncRoot)
                {
                    _owner.AppendUnlocked(found);
                    if (outcome == RawLogReadOutcome.Completed)
                        _owner.MarkDateScannedUnlocked(file.Date);
                }
                if (outcome == RawLogReadOutcome.Completed && file.Date == DateTime.Today)
                    ConsumedToday = (file, _fileLength);
            }

            public void EndScan(bool completed)
            {
                if (_files > 0)
                    AppLogger.Info($"Hunt session backfill done. Files={_files}, Sessions={_sessions}");
            }
        }

        /// <summary>로그 파일 하루치에서 판을 뽑는다 (공용 리더로 읽어 <see cref="HuntDayScanner"/>에 넣는다).</summary>
        public static List<ExpHuntSession> ReadSessionsFromLog(string path, DateTime date)
        {
            var scanner = new HuntDayScanner(date);
            foreach (string html in RawChatLogReader.ReadMergedLines(path))
                scanner.Feed(html, null);
            return scanner.Complete();
        }

        /// <summary>하루치 줄을 순서대로 받아 사냥 판으로 묶는다. 파일 스캔과 공용 패스가 같은 규칙을 쓴다.</summary>
        public sealed class HuntDayScanner
        {
            private readonly DateTime _date;
            private readonly List<ExpHuntSession> _sessions = new();
            private DateTime? _startedAt;
            private DateTime _lastGainAt;
            private long _totalExp;
            private int _gainCount;
            private int _hourOffset;      // 자정을 넘겨 시각이 되돌아가면 하루를 더한다
            private int _previousHour = -1;

            public HuntDayScanner(DateTime date)
            {
                _date = date.Date;
            }

            public void Feed(RawLogLine line) => Feed(line.Html, line.PlainText);

            /// <param name="html">줄 HTML. <paramref name="plainText"/>가 null이면 여기서 태그를 뗀다.</param>
            public void Feed(string html, string? plainText)
            {
                // 경험치 줄만 본다 — 대부분의 줄을 여기서 걸러 비싼 처리를 건너뛴다
                if (html.IndexOf("경험치", StringComparison.Ordinal) < 0)
                    return;

                string text = plainText ?? RawChatLogReader.StripTags(html);
                if (!RawChatLogReader.TryParseLogTime(text, out TimeSpan time, out int matchLength))
                    return;

                int hour = time.Hours;
                if (_previousHour >= 0 && hour < _previousHour)
                    _hourOffset += 24;   // 00시로 되돌아갔다 = 다음 날
                _previousHour = hour;

                DateTime at = _date.AddHours(_hourOffset).Add(time);

                string body = text.Substring(matchLength).Trim();
                long gained = ExperienceLogAnalyzer.ExtractGain(body, isSystemLog: true);
                if (gained <= 0 || !ExperienceLogAnalyzer.IsHuntingGain(body))
                    return; // 보상 경험치는 판에 넣지 않는다

                if (_startedAt.HasValue && at - _lastGainAt > SessionGap)
                    Close();

                _startedAt ??= at;
                _lastGainAt = at;
                _totalExp += gained;
                _gainCount++;
            }

            /// <summary>파일 끝: 진행 중인 판을 닫고 남길 판 목록을 돌려준다.</summary>
            public List<ExpHuntSession> Complete()
            {
                Close();
                return _sessions;
            }

            private void Close()
            {
                if (_startedAt is not DateTime started)
                    return;
                var session = new ExpHuntSession
                {
                    StartedAt = started,
                    EndedAt = _lastGainAt,
                    TotalExp = _totalExp,
                    GainCount = _gainCount,
                };
                if (IsWorthKeeping(session))
                    _sessions.Add(session);
                _startedAt = null;
                _totalExp = 0;
                _gainCount = 0;
            }
        }

        #region 레거시 유지보수 (실행당 1회)

        /// <summary>
        /// [레거시] 기록 규칙 버전. 2: 보상 경험치를 판에서 뺐다 — 그 전에 쌓인 판은 보상이 섞여 있으므로
        /// 옛 파일을 sessions.v1.csv로 옮기고 지난 로그 전체를 다시 읽어 새로 만든다 (한 번만).
        /// </summary>
        private const int RecordVersion = 2;

        /// <summary>[레거시] 옛 버전 기록 재구축 + 예전에 두 번 들어간 판 정리. 실행당 한 번만 돈다.</summary>
        private void RunLegacyMaintenanceOnceUnlocked()
        {
            if (_legacyMaintenanceDone)
                return;
            _legacyMaintenanceDone = true;
            RebuildIfOldVersionUnlocked();
            DedupeExistingUnlocked();
        }

        private void RebuildIfOldVersionUnlocked()
        {
            string versionPath = Path.Combine(Path.GetDirectoryName(_sessionFilePath)!, "version.txt");
            try
            {
                if (File.Exists(versionPath) && int.TryParse(File.ReadAllText(versionPath).Trim(), out int version) && version >= RecordVersion)
                    return;

                Directory.CreateDirectory(Path.GetDirectoryName(_sessionFilePath)!);
                if (File.Exists(_sessionFilePath))
                    File.Move(_sessionFilePath, Path.Combine(Path.GetDirectoryName(_sessionFilePath)!, "sessions.v1.csv"), overwrite: true);
                if (File.Exists(_scanStateFilePath))
                    File.Delete(_scanStateFilePath);
                File.WriteAllText(versionPath, RecordVersion.ToString(CultureInfo.InvariantCulture), Encoding.UTF8);
                AppLogger.Info($"Hunt sessions reset for rebuild (record version {RecordVersion}).");
                SessionsChanged?.Invoke();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to reset hunt sessions for rebuild.", ex);
            }
        }

        #endregion

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
