using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    public sealed record LogFeedItem(
        string Html,
        bool IsRealTime,
        bool IsStartupBackfill,
        string SourcePath = "",
        long CheckpointPosition = -1);

    public sealed class LogPipelineCheckpoint
    {
        public string LogPath { get; set; } = string.Empty;
        public long LastPosition { get; set; }
        public string LastLogTimeText { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
    }

    /// <summary>
    /// ?뚯씪利덉쐞踰?濡쒓렇(HTML)瑜??대쭅 湲곕컲 ?⑥씪 ?뚯씠?꾨씪?몄쑝濡??섏쭛?⑸땲??
    /// 罹먯떆???ъ슜?섏? ?딄퀬 泥댄겕?ъ씤??留덉?留??쎌? ?ㅽ봽??留??좎??⑸땲??
    /// </summary>
    public class LogService : IDisposable
    {
        #region Fields & Properties

        private string _logPath = null!;
        private long _lastPosition;
        private FileStream? _logStream;
        private readonly object _lockObj = new();
        private readonly Timer _pollingTimer;
        private bool _disposed;
        private int _isPolling;
        private readonly ExperienceService _experienceService;
        private readonly ChatSettings _settings;
        private readonly Encoding _logEncoding;
        private readonly Decoder _logDecoder;
        private string _pendingRawContent = string.Empty;
        private DateTime _pendingSinceUtc = DateTime.MinValue;
        private long _lastSavedCheckpointPosition = -1;

        // UI가 소비를 알린 최신 위치 (실제 체크포인트 저장은 폴링 스레드에서)
        private readonly object _consumedSync = new();
        private string? _pendingConsumedPath;
        private long _pendingConsumedPosition = -1;

        // 게임이 로그 꼬리를 다시 쓰는 경우(타임스탬프 보정 등)를 감지하기 위한 연속성 검증 상태.
        // _lastTailBytes: 직전에 읽은 내용의 끝 바이트(원본과 대조), _resyncAnchorText: 마지막으로
        // 소비한 완결 줄의 메시지 부분(타임스탬프 뒤) — 재작성으로 시각이 바뀌어도 내용으로 위치를 되찾는다.
        private byte[] _lastTailBytes = Array.Empty<byte>();
        // 마지막으로 소비한 줄들의 메시지 부분(타임스탬프 뒤), 오래된 → 최신.
        // 사냥 중엔 같은 메시지(경험치 등)가 반복되므로 한 줄이 아니라 연속 시퀀스로 위치를 판정한다.
        private readonly List<string> _resyncAnchors = new();
        private const int ResyncAnchorCount = 6;
        private const int TailVerifyLength = 256;
        private const int ResyncSearchBack = 128 * 1024;
        private const int ResyncSearchForward = 4096;
        private string _lastLogTimeText = string.Empty;

        private const int InitialLogTailBytes = 2 * 1024 * 1024;
        private const int PollingIntervalMilliseconds = 30;
        // 줄 구분자(<br>/개행) 없이 파일에 남은 조각을 완결로 간주하기까지의 대기 시간.
        // 게임이 줄 끝을 아직 안 썼을 짧은 순간은 넘기고, 그 이상 조용하면 마지막 줄로 보고 내보낸다.
        private const int PendingFlushMilliseconds = 500;
        private static readonly string StateDirectoryPath = LogStoragePaths.StateDirectory;
        private static readonly string CheckpointPath = Path.Combine(StateDirectoryPath, "log_pipeline_checkpoint.json");
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General);
        private static readonly UTF8Encoding Utf8BomEncoding = new(encoderShouldEmitUTF8Identifier: true);

        public event Action<LogFeedItem>? OnNewLogRead;
        public event Action? InitialLogsLoaded;
        private static readonly Regex LineSplitRegex = new(@"</?br\s*>|\r?\n", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LeadingTimeRegex = new(@"\[\s*(?<time>[^\]]+)\s*\]", RegexOptions.Compiled);

        #endregion

        #region Constructor & Lifecycle

        public LogService(ExperienceService experienceService, ChatSettings settings)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _logEncoding = Encoding.GetEncoding(949);
            _logDecoder = _logEncoding.GetDecoder();
            _experienceService = experienceService;
            _settings = settings;

            _pollingTimer = new Timer(_ => PollLog(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            AppLogger.Info("LogService initialized.");
        }

        public void Start()
        {
            if (_disposed)
                return;

            _pollingTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(PollingIntervalMilliseconds));
            AppLogger.Info("LogService polling started.");
        }

        public void Stop()
        {
            if (_disposed)
                return;

            _pollingTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            AppLogger.Info("LogService polling stopped.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { Stop(); } catch (Exception ex) { AppLogger.Warn("Failed to stop LogService during dispose.", ex); }
            try { _pollingTimer.Dispose(); } catch (Exception ex) { AppLogger.Warn("Failed to dispose polling timer.", ex); }
            try { FlushConsumedCheckpoint(); } catch (Exception ex) { AppLogger.Warn("Failed to flush checkpoint during dispose.", ex); }
            lock (_lockObj) { CloseStream(); }
            GC.SuppressFinalize(this);
        }

        #endregion

        private void PollLog()
        {
            if (_disposed)
                return;

            if (Interlocked.Exchange(ref _isPolling, 1) == 1)
                return;

            try
            {
                CheckDateAndPath();
                ReadLog();
                FlushConsumedCheckpoint();
            }
            catch (Exception ex)
            {
                AppLogger.Error("LogService polling failed.", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _isPolling, 0);
            }
        }

        #region Path Management

        /// <summary>
        /// MainWindow?먯꽌 ?대깽?몃? ?곌껐????紐낆떆?곸쑝濡??몄텧?댁빞 ?⑸땲??
        /// </summary>
        public void Initialize()
        {
            UpdatePath(isInitialLoad: true);
            AppLogger.Info($"LogService initialized path: {_logPath}");
        }

        /// <summary>
        /// ?좎쭨媛 蹂寃쎈릺?덈뒗吏 ?뺤씤?섍퀬 ?꾩슂??寃쎈줈瑜??낅뜲?댄듃
        /// </summary>
        private void CheckDateAndPath()
        {
            string today = DateTime.Now.ToString("yyyy_MM_dd");
            string expectedPath = Path.Combine(_settings.ChatLogFolderPath, $"TWChatLog_{today}.html");

            if (_logPath != expectedPath)
            {
                AppLogger.Info($"Detected log path rollover. Updating path from '{_logPath}' to '{expectedPath}'.");

                // 전환 전에 옛 파일의 남은 내용과 대기 조각을 마저 처리해 자정 부근 줄 유실을 막는다
                try
                {
                    ReadLog();
                    lock (_lockObj) { FlushPendingContent(force: true); }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to drain previous log file before rollover.", ex);
                }

                UpdatePath(isInitialLoad: false);
            }
        }

        /// <summary>
        /// ?꾩옱 ?좎쭨??留욌뒗 濡쒓렇 寃쎈줈瑜??ㅼ젙?섍퀬 泥댄겕?ъ씤??湲곗??쇰줈 珥덇린 ?꾩튂瑜?寃곗젙?⑸땲??
        /// </summary>
        private void UpdatePath(bool isInitialLoad)
        {
            lock (_lockObj)
            {
                string today = DateTime.Now.ToString("yyyy_MM_dd");
                _logPath = Path.Combine(_settings.ChatLogFolderPath, $"TWChatLog_{today}.html");
                // 경로가 바뀌었으므로 기존 스트림을 닫아 새 경로로 재오픈되게 한다.
                CloseStream();
                _pendingRawContent = string.Empty;
                _lastLogTimeText = string.Empty;
                _logDecoder.Reset();

                if (File.Exists(_logPath))
                {
                    var fileInfo = new FileInfo(_logPath);
                    long sourceLength = fileInfo.Length;
                    // 프로그램 시작 시에는 체크포인트로 이어읽지 않고 항상 최근 로그(tail)를 표시해,
                    // 재실행해도 채팅창이 비지 않고 최근 과거 외치기/대화를 볼 수 있게 한다.
                    // (같은 날 재실행 = 그날 첫 실행과 동일한 표시 동작으로 통일)
                    bool resumedFromCheckpoint = !isInitialLoad && TryRestoreCheckpoint(sourceLength);

                    if (!resumedFromCheckpoint)
                    {
                        LoadInitialLogsFromTail(1000);
                        _lastPosition = sourceLength;
                        SaveCheckpoint();
                    }

                    if (_lastPosition < sourceLength)
                    {
                        // Startup backfill must flow through the same real-time pipeline
                        // so content/abandon/exp/item/shout analyzers are not skipped.
                        ReadLog(isRealTimeOverride: true, isStartupBackfill: true);
                    }

                    AppLogger.Info($"Log file ready: {_logPath}, resume position={_lastPosition}.");
                }
                else
                {
                    _lastPosition = 0;
                    AppLogger.Warn($"Log file not found: {_logPath}.");
                }
            }

            if (!isInitialLoad)
            {
                // 롤오버/경로 변경으로 버퍼만 채워진 줄들이 화면에도 반영되도록 전체 갱신을 요청한다
                InitialLogsLoaded?.Invoke();
            }

            if (isInitialLoad)
            {
                InitialLogsLoaded?.Invoke();
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _experienceService.SetReady();
                }), DispatcherPriority.Loaded);
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// 珥덇린 援щ룞 ??湲곗〈 濡쒓렇??留덉?留?遺遺꾩쓣 媛?몄샃?덈떎.
        /// </summary>
        private void LoadInitialLogsFromTail(int lineCount)
        {
            try
            {
                using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length == 0)
                    return;

                int bytesToRead = (int)Math.Min(stream.Length, InitialLogTailBytes);
                stream.Seek(-bytesToRead, SeekOrigin.End);

                byte[] buffer = new byte[bytesToRead];
                int totalRead = 0;
                while (totalRead < buffer.Length)
                {
                    int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                    if (read <= 0)
                        break;

                    totalRead += read;
                }

                if (totalRead > 0)
                {
                    string tailContent = _logEncoding.GetString(buffer, 0, totalRead);
                    ProcessRawContent(tailContent, isRealTime: false, lineCount);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load initial logs.", ex);
            }
        }

        /// <summary>
        /// ?ㅼ떆媛꾩쑝濡?異붽???濡쒓렇瑜?利앸텇?쇰줈 ?쎌뒿?덈떎.
        /// </summary>
        public void ReadLog(bool? isRealTimeOverride = null, bool isStartupBackfill = false)
        {
            lock (_lockObj)
            {
                try
                {
                    if (!File.Exists(_logPath))
                    {
                        // 파일이 사라졌으면(예: 삭제/이동) 기존 핸들을 닫고 다음 틱에 재오픈되게 한다.
                        CloseStream();
                        return;
                    }

                    EnsureStreamOpen();

                    long length = _logStream!.Length;
                    if (length < _lastPosition)
                    {
                        // 파일이 잘렸거나 같은 이름으로 재생성됨 → 스트림을 재오픈해 새 파일 핸들을 확보한다.
                        CloseStream();
                        _lastPosition = 0;
                        _pendingRawContent = string.Empty;
                        _lastTailBytes = Array.Empty<byte>();
                        _logDecoder.Reset();

                        if (!File.Exists(_logPath))
                            return;

                        EnsureStreamOpen();
                        length = _logStream!.Length;
                    }

                    if (length <= _lastPosition)
                    {
                        // 새 내용이 없는 동안 대기 조각이 오래 묵으면 마지막 줄로 간주해 내보낸다
                        FlushPendingContent(force: false);
                        return;
                    }

                    // 게임이 꼬리를 다시 쓰면(같은 길이로 타임스탬프만 보정하는 경우 포함) 오프셋이
                    // 어긋나 그 사이 줄이 통째로 유실된다 — 직전 읽기 꼬리가 그대로인지 확인하고 복구한다.
                    VerifyReadContinuity(length);
                    if (_lastPosition >= length)
                        return; // 재동기화 결과 새로 읽을 내용이 없음

                    long bytesToRead = length - _lastPosition;
                    if (bytesToRead > int.MaxValue)
                    {
                        AppLogger.Warn($"Incremental log read is too large ({bytesToRead:N0} bytes). Resetting read position to file end.");
                        _lastPosition = length;
                        _pendingRawContent = string.Empty;
                        _logDecoder.Reset();
                        SaveCheckpoint();
                        return;
                    }

                    _logStream.Seek(_lastPosition, SeekOrigin.Begin);
                    byte[] buffer = new byte[(int)bytesToRead];
                    int totalRead = 0;
                    while (totalRead < buffer.Length)
                    {
                        int read = _logStream.Read(buffer, totalRead, buffer.Length - totalRead);
                        if (read <= 0)
                            break;

                        totalRead += read;
                    }

                    _lastPosition = _logStream.Position;
                    if (totalRead == 0)
                    {
                        SaveCheckpoint();
                        return;
                    }

                    UpdateTailBytes(buffer, totalRead);
                    string newContent = DecodeIncrementalBytes(buffer, totalRead);
                    ProcessIncrementalContent(
                        newContent,
                        isRealTimeOverride ?? _experienceService.IsReady,
                        isStartupBackfill);
                    // 체크포인트는 소비(UI 배치 처리) 확인 후 NotifyConsumed에서 저장한다 — 읽자마자 저장하면
                    // 처리 전에 앱이 죽었을 때 그 줄들이 유실된다
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Failed to read incremental log content.", ex);
                    // IOException 등으로 핸들이 손상되었을 수 있으니 닫아서 다음 틱에 재오픈되게 한다.
                    CloseStream();
                }
            }
        }

        /// <summary>
        /// 직전에 읽은 꼬리 바이트가 파일에 그대로 있는지 확인한다. 게임이 로그 꼬리를 다시 쓰면
        /// (관찰상 타임스탬프를 쓰기 시점으로 보정하며 재기록) 오프셋이 어긋나 줄이 유실되므로,
        /// 마지막으로 소비한 줄의 '메시지 내용'으로 새 위치를 찾아 재동기화한다.
        /// 반드시 _lockObj를 보유한 상태에서 호출해야 한다.
        /// </summary>
        private void VerifyReadContinuity(long length)
        {
            try
            {
                if (_lastPosition <= 0)
                    return;

                if (_lastTailBytes.Length == 0)
                {
                    CaptureTailBytesFromFile();
                    return;
                }

                int tailLen = (int)Math.Min(_lastTailBytes.Length, _lastPosition);
                var current = new byte[tailLen];
                _logStream!.Seek(_lastPosition - tailLen, SeekOrigin.Begin);
                if (ReadExactly(current, tailLen) == tailLen &&
                    current.AsSpan().SequenceEqual(_lastTailBytes.AsSpan(_lastTailBytes.Length - tailLen)))
                {
                    return; // 연속성 유지 — 정상 경로
                }

                // 오프셋이 어긋남 → 마지막 소비 줄들의 메시지 시퀀스(타임스탬프 제외)로 위치를 되찾는다
                {
                    long searchStart = Math.Max(0, _lastPosition - ResyncSearchBack);
                    long searchEnd = Math.Min(length, _lastPosition + ResyncSearchForward);
                    int windowLen = (int)(searchEnd - searchStart);
                    var window = new byte[windowLen];
                    _logStream.Seek(searchStart, SeekOrigin.Begin);
                    if (ReadExactly(window, windowLen) == windowLen)
                    {
                        long newPos = TryFindResyncPosition(window, searchStart);
                        if (newPos >= 0)
                        {
                            AppLogger.Warn($"Log tail was rewritten by the game. Resynced read position {_lastPosition} -> {newPos}.");
                            _lastPosition = newPos;
                            _pendingRawContent = string.Empty;
                            _logDecoder.Reset();
                            _lastTailBytes = Array.Empty<byte>();
                            return;
                        }
                    }
                }

                // 되찾지 못함 → 현재 위치를 유지하되 다음 줄 경계로 스냅한다 (조각 줄 방지).
                // 전체 재읽기는 수천 줄 중복을 쏟아내므로 하지 않는다 — 파일 교체는 길이 축소 경로가 잡는다.
                AppLogger.Warn("Log tail no longer matches and sequence resync failed. Snapping to next line boundary.");
                SnapToNextLineBoundary(length);
                _pendingRawContent = string.Empty;
                _logDecoder.Reset();
                _lastTailBytes = Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Log read continuity check failed.", ex);
            }
        }

        /// <summary>
        /// 마지막 소비 줄의 메시지 부분(첫 타임스탬프 font 태그 뒤)을 재동기화 앵커로 저장한다.
        /// 게임이 꼬리를 다시 쓸 때 타임스탬프는 바뀌지만 메시지 내용은 그대로이기 때문.
        /// </summary>
        private void UpdateResyncAnchor(IReadOnlyList<string> lines)
        {
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                int cut = line.IndexOf("</font>", StringComparison.OrdinalIgnoreCase);
                string message = cut >= 0 ? line[(cut + 7)..].Trim() : line;
                if (message.Length < 6)
                    continue;

                _resyncAnchors.Add(message);
                if (_resyncAnchors.Count > ResyncAnchorCount)
                    _resyncAnchors.RemoveAt(0);
            }
        }

        /// <summary>체크포인트 복원 직후 등 꼬리 표본이 없을 때 현재 파일에서 채운다.</summary>
        private void CaptureTailBytesFromFile()
        {
            int len = (int)Math.Min(TailVerifyLength, _lastPosition);
            if (len <= 0)
                return;

            var tail = new byte[len];
            _logStream!.Seek(_lastPosition - len, SeekOrigin.Begin);
            if (ReadExactly(tail, len) == len)
                _lastTailBytes = tail;
        }

        /// <summary>읽은 버퍼의 끝부분으로 연속성 검증용 꼬리 바이트를 갱신한다.</summary>
        private void UpdateTailBytes(byte[] buffer, int count)
        {
            if (count >= TailVerifyLength)
            {
                _lastTailBytes = new byte[TailVerifyLength];
                Array.Copy(buffer, count - TailVerifyLength, _lastTailBytes, 0, TailVerifyLength);
                return;
            }

            int keepOld = Math.Min(_lastTailBytes.Length, TailVerifyLength - count);
            var next = new byte[keepOld + count];
            if (keepOld > 0)
                Array.Copy(_lastTailBytes, _lastTailBytes.Length - keepOld, next, 0, keepOld);
            Array.Copy(buffer, 0, next, keepOld, count);
            _lastTailBytes = next;
        }

        private int ReadExactly(byte[] buffer, int count)
        {
            int total = 0;
            while (total < count)
            {
                int read = _logStream!.Read(buffer, total, count - total);
                if (read <= 0)
                    break;
                total += read;
            }

            return total;
        }

        private static readonly byte[] FontCloseBytes = System.Text.Encoding.ASCII.GetBytes("</font>");

        /// <summary>
        /// 재기록된 창(window) 안에서 마지막 소비 줄 시퀀스와 가장 잘 맞는 위치를 찾는다.
        /// 후보 줄의 메시지가 최신 앵커와 같고, 그 앞 줄들이 이전 앵커들과 연속으로 일치할수록 높은 점수.
        /// 동점이면 기존 읽기 위치에 가장 가까운 후보를 고른다 (반복 메시지 오인 방지).
        /// 반환: 파일 절대 위치(그 줄의 개행 다음), 실패 시 -1.
        /// </summary>
        private long TryFindResyncPosition(byte[] window, long windowStart)
        {
            if (_resyncAnchors.Count == 0)
                return -1;

            // 창을 줄 단위로 나눈다. cp949 트레일 바이트 범위(0x41~)에 0x0A가 없어 '\n' 분리는 안전하다.
            var lineStarts = new List<int> { 0 };
            for (int i = 0; i < window.Length; i++)
            {
                if (window[i] == (byte)'\n' && i + 1 < window.Length)
                    lineStarts.Add(i + 1);
            }

            var anchorBytes = _resyncAnchors.Select(a => _logEncoding.GetBytes(a)).ToArray();
            long expectedOffset = _lastPosition - windowStart;

            int bestScore = 0;
            long bestDistance = long.MaxValue;
            long bestResume = -1;

            for (int li = lineStarts.Count - 1; li >= 0; li--)
            {
                int start = lineStarts[li];
                int end = li + 1 < lineStarts.Count ? lineStarts[li + 1] - 1 : window.Length; // '\n' 앞까지
                if (!LineMessageEquals(window, start, end, anchorBytes[^1]))
                    continue;

                // 앞 줄들이 이전 앵커들과 연속으로 일치하는 개수를 센다
                int score = 1;
                for (int k = 2; k <= anchorBytes.Length && li - (k - 1) >= 0; k++)
                {
                    int ps = lineStarts[li - (k - 1)];
                    int pe = lineStarts[li - (k - 2)] - 1;
                    if (!LineMessageEquals(window, ps, pe, anchorBytes[^k]))
                        break;
                    score++;
                }

                long resume = li + 1 < lineStarts.Count ? lineStarts[li + 1] : window.Length;
                long distance = Math.Abs(resume - expectedOffset);
                if (score > bestScore || (score == bestScore && distance < bestDistance))
                {
                    bestScore = score;
                    bestDistance = distance;
                    bestResume = resume;
                }
            }

            // 앵커가 여러 개면 최소 2줄 연속 일치를 요구한다 (반복 한 줄 오인 방지)
            int required = Math.Min(2, _resyncAnchors.Count);
            if (bestScore < required || bestResume < 0)
                return -1;

            return windowStart + bestResume;
        }

        private static readonly byte[] BrCloseBytes = System.Text.Encoding.ASCII.GetBytes("</br>");
        private static readonly byte[] BrOpenBytes = System.Text.Encoding.ASCII.GetBytes("<br>");

        /// <summary>
        /// 줄 바이트 구간의 메시지 부분(첫 &lt;/font&gt; 뒤)이 앵커와 같은지.
        /// 앵커는 줄 분리 시 &lt;/br&gt;이 제거된 텍스트에서 왔으므로 바이트 쪽도 끝의 br 태그를 떼고 비교한다.
        /// </summary>
        private static bool LineMessageEquals(byte[] window, int start, int end, byte[] anchor)
        {
            int cut = IndexOfBytes(window, start, end, FontCloseBytes);
            int msgStart = cut >= 0 ? cut + FontCloseBytes.Length : start;
            int msgEnd = end;

            static void TrimRange(byte[] bytes, ref int s, ref int e)
            {
                while (s < e && (bytes[s] == (byte)' ' || bytes[s] == (byte)'\t'))
                    s++;
                while (e > s && (bytes[e - 1] == (byte)' ' || bytes[e - 1] == (byte)'\r' || bytes[e - 1] == (byte)'\t'))
                    e--;
            }

            TrimRange(window, ref msgStart, ref msgEnd);

            if (msgEnd - msgStart >= BrCloseBytes.Length &&
                window.AsSpan(msgEnd - BrCloseBytes.Length, BrCloseBytes.Length).SequenceEqual(BrCloseBytes))
                msgEnd -= BrCloseBytes.Length;
            else if (msgEnd - msgStart >= BrOpenBytes.Length &&
                     window.AsSpan(msgEnd - BrOpenBytes.Length, BrOpenBytes.Length).SequenceEqual(BrOpenBytes))
                msgEnd -= BrOpenBytes.Length;

            TrimRange(window, ref msgStart, ref msgEnd);

            return msgEnd - msgStart == anchor.Length &&
                   window.AsSpan(msgStart, anchor.Length).SequenceEqual(anchor);
        }

        private static int IndexOfBytes(byte[] haystack, int start, int end, byte[] needle)
        {
            for (int i = start; i <= end - needle.Length; i++)
            {
                if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
                    return i;
            }

            return -1;
        }

        /// <summary>현재 위치가 줄 중간일 수 있을 때, 다음 '\n' 직후로 스냅해 조각 줄을 피한다.</summary>
        private void SnapToNextLineBoundary(long length)
        {
            try
            {
                _logStream!.Seek(_lastPosition, SeekOrigin.Begin);
                var buffer = new byte[4096];
                long pos = _lastPosition;
                while (pos < length)
                {
                    int read = _logStream.Read(buffer, 0, (int)Math.Min(buffer.Length, length - pos));
                    if (read <= 0)
                        break;

                    for (int i = 0; i < read; i++)
                    {
                        if (buffer[i] == (byte)'\n')
                        {
                            _lastPosition = pos + i + 1;
                            return;
                        }
                    }

                    pos += read;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to snap log position to line boundary.", ex);
            }
        }

        /// <summary>
        /// 지속 로그 스트림이 없으면 현재 경로로 연다. 매 틱 재오픈하는 대신 이 핸들을 재사용한다.
        /// 게임 클라이언트의 로그 쓰기와 파일 교체를 막지 않도록 ReadWrite | Delete 공유로 연다.
        /// 반드시 _lockObj를 보유한 상태에서 호출해야 한다.
        /// </summary>
        private void EnsureStreamOpen()
        {
            if (_logStream != null)
                return;

            _logStream = new FileStream(
                _logPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
        }

        /// <summary>
        /// 지속 로그 스트림을 닫는다. 경로 변경/파일 잘림/예외/종료 시 재오픈을 위해 호출한다.
        /// 반드시 _lockObj를 보유한 상태(또는 Dispose)에서 호출해야 한다.
        /// </summary>
        private void CloseStream()
        {
            if (_logStream == null)
                return;

            try { _logStream.Dispose(); }
            catch (Exception ex) { AppLogger.Warn("Failed to dispose log file stream.", ex); }
            finally { _logStream = null; }
        }

        public void InjectTestContent(string content)
        {
            lock (_lockObj)
            {
                ProcessRawContent(content, isRealTime: false);
            }
        }

        #endregion

        #region Processing

        /// <summary>
        /// ?쎌뼱???먮Ц HTML??<br> ?쒓렇 ?⑥쐞濡?遺꾨━???대깽?몃? 諛쒖깮?쒗궢?덈떎.
        /// </summary>
        private void ProcessRawContent(string content, bool isRealTime, int takeLastCount = -1, bool isStartupBackfill = false, string sourcePath = "", long checkpointPosition = -1)
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            var lines = LineSplitRegex.Split(content)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            UpdateResyncAnchor(lines);

            lines = ShoutLineMergeHelper.MergeWrappedShoutLines(lines);
            lines = SystemLineMergeHelper.MergeWrappedSystemLines(lines);

            if (takeLastCount > 0 && lines.Count > takeLastCount)
            {
                lines = lines.Skip(lines.Count - takeLastCount).ToList();
            }

            foreach (string line in lines)
            {
                string normalized = line.Trim();
                if (normalized.Length == 0)
                    continue;

                string? logTimeText = ExtractLogTimeText(normalized);
                if (!string.IsNullOrWhiteSpace(logTimeText))
                    _lastLogTimeText = logTimeText;

                OnNewLogRead?.Invoke(new LogFeedItem(normalized, isRealTime, isStartupBackfill, sourcePath, checkpointPosition));
            }
        }


        private string DecodeIncrementalBytes(byte[] buffer, int byteCount)
        {
            if (byteCount <= 0)
                return string.Empty;

            char[] chars = new char[_logEncoding.GetMaxCharCount(byteCount)];
            _logDecoder.Convert(
                buffer,
                0,
                byteCount,
                chars,
                0,
                chars.Length,
                flush: false,
                out _,
                out int charsUsed,
                out _);

            return charsUsed <= 0 ? string.Empty : new string(chars, 0, charsUsed);
        }

        /// <summary>
        /// UI 파이프라인이 이 위치까지의 줄을 실제로 소비했음을 알린다. 이때 체크포인트를 저장해
        /// "읽었지만 처리 전에 죽으면 유실"되는 창을 없앤다. 경로가 바뀌었으면(롤오버) 무시한다.
        /// </summary>
        public void NotifyConsumed(string sourcePath, long position)
        {
            if (string.IsNullOrEmpty(sourcePath) || position < 0)
                return;

            // UI 스레드에서 호출된다 — 파일 IO와 _lockObj(폴링 스레드가 파일 읽는 동안 보유) 경합을 피하기 위해
            // 위치만 기록하고 실제 저장은 폴링 스레드(FlushConsumedCheckpoint)에서 한다.
            lock (_consumedSync)
            {
                if (!string.Equals(sourcePath, _pendingConsumedPath, StringComparison.OrdinalIgnoreCase))
                {
                    _pendingConsumedPath = sourcePath;
                    _pendingConsumedPosition = position;
                }
                else if (position > _pendingConsumedPosition)
                {
                    _pendingConsumedPosition = position;
                }
            }
        }

        /// <summary>UI가 알린 소비 위치를 체크포인트로 저장한다. 폴링 스레드(및 Dispose)에서만 호출.</summary>
        private void FlushConsumedCheckpoint()
        {
            string? path;
            long position;
            lock (_consumedSync)
            {
                path = _pendingConsumedPath;
                position = _pendingConsumedPosition;
            }

            if (path == null || position < 0)
                return;

            lock (_lockObj)
            {
                if (!string.Equals(path, _logPath, StringComparison.OrdinalIgnoreCase))
                    return;
                if (position <= _lastSavedCheckpointPosition || position > _lastPosition)
                    return;

                SaveCheckpoint(position);
            }
        }

        /// <summary>
        /// 줄 구분자가 아직 안 와서 대기 중인 조각을 내보낸다.
        /// force=true(롤오버 등)면 즉시, 아니면 PendingFlushMilliseconds 이상 조용했을 때만.
        /// 반드시 _lockObj를 보유한 상태에서 호출해야 한다.
        /// </summary>
        private void FlushPendingContent(bool force)
        {
            if (_pendingRawContent.Length == 0)
                return;

            if (!force && (DateTime.UtcNow - _pendingSinceUtc).TotalMilliseconds < PendingFlushMilliseconds)
                return;

            string stale = _pendingRawContent;
            _pendingRawContent = string.Empty;
            _pendingSinceUtc = DateTime.MinValue;
            ProcessRawContent(stale, _experienceService.IsReady, sourcePath: _logPath, checkpointPosition: _lastPosition);
        }

        private void ProcessIncrementalContent(string content, bool isRealTime, bool isStartupBackfill)
        {
            if (string.IsNullOrEmpty(content))
                return;

            string combined = _pendingRawContent + content;
            int completeEnd = FindLastCompleteLogBoundary(combined);
            if (completeEnd < 0)
            {
                _pendingRawContent = combined;
                _pendingSinceUtc = DateTime.UtcNow;
                return;
            }

            string readyContent = combined.Substring(0, completeEnd);
            _pendingRawContent = completeEnd < combined.Length ? combined.Substring(completeEnd) : string.Empty;
            _pendingSinceUtc = _pendingRawContent.Length > 0 ? DateTime.UtcNow : DateTime.MinValue;
            ProcessRawContent(readyContent, isRealTime, isStartupBackfill: isStartupBackfill, sourcePath: _logPath, checkpointPosition: _lastPosition);
        }

        private static int FindLastCompleteLogBoundary(string content)
        {
            for (int i = content.Length - 1; i >= 0; i--)
            {
                char ch = content[i];
                if (ch == '\n')
                    return i + 1;

                if (ch != '>' || i < 3)
                    continue;

                int tagStart = content.LastIndexOf('<', i);
                if (tagStart < 0)
                    break;

                if (IsBrTag(content, tagStart, i))
                    return i + 1;

                i = tagStart;
            }

            return -1;
        }

        /// <summary>content[start..end](포함)가 &lt;br&gt; / &lt;/br&gt; / &lt;br/&gt; 형태인지 — 부분 문자열·정규식 할당 없이 판정.</summary>
        private static bool IsBrTag(string content, int start, int end)
        {
            int i = start;
            if (i >= end || content[i] != '<')
                return false;
            i++;
            if (i < end && content[i] == '/')
                i++;
            if (i + 1 >= end)
                return false;
            if (char.ToLowerInvariant(content[i]) != 'b' || char.ToLowerInvariant(content[i + 1]) != 'r')
                return false;
            i += 2;
            while (i < end && char.IsWhiteSpace(content[i]))
                i++;
            if (i < end && content[i] == '/')
                i++;
            return i == end && content[end] == '>';
        }

        #endregion

        #region Checkpoint

        private bool TryRestoreCheckpoint(long sourceLength)
        {
            LogPipelineCheckpoint? checkpoint = LoadCheckpoint();
            if (checkpoint == null)
                return false;

            if (!string.Equals(checkpoint.LogPath, _logPath, StringComparison.OrdinalIgnoreCase))
                return false;

            if (checkpoint.LastPosition < 0 || checkpoint.LastPosition > sourceLength)
                return false;

            _lastPosition = checkpoint.LastPosition;
            _lastLogTimeText = checkpoint.LastLogTimeText ?? string.Empty;
            return true;
        }

        private static LogPipelineCheckpoint? LoadCheckpoint()
        {
            if (!File.Exists(CheckpointPath))
                return null;

            try
            {
                string json = File.ReadAllText(CheckpointPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<LogPipelineCheckpoint>(json, JsonOptions);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load log checkpoint '{CheckpointPath}'.", ex);
                return null;
            }
        }

        private void SaveCheckpoint(long? positionOverride = null)
        {
            try
            {
                long position = positionOverride ?? _lastPosition;
                Directory.CreateDirectory(StateDirectoryPath);
                var checkpoint = new LogPipelineCheckpoint
                {
                    LogPath = _logPath,
                    LastPosition = position,
                    LastLogTimeText = _lastLogTimeText,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                File.WriteAllText(CheckpointPath, JsonSerializer.Serialize(checkpoint, JsonOptions), Utf8BomEncoding);
                _lastSavedCheckpointPosition = position;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to save log checkpoint '{CheckpointPath}'.", ex);
            }
        }

        private static string? ExtractLogTimeText(string line)
        {
            Match match = LeadingTimeRegex.Match(WebUtility.HtmlDecode(line));
            if (!match.Success)
                return null;

            string value = match.Groups["time"].Value.Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        #endregion
    }
}
