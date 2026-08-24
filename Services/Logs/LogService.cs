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
        bool IsStartupBackfill);

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

                    string newContent = DecodeIncrementalBytes(buffer, totalRead);
                    ProcessIncrementalContent(
                        newContent,
                        isRealTimeOverride ?? _experienceService.IsReady,
                        isStartupBackfill);
                    SaveCheckpoint();
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
        private void ProcessRawContent(string content, bool isRealTime, int takeLastCount = -1, bool isStartupBackfill = false)
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            var lines = LineSplitRegex.Split(content)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

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

                OnNewLogRead?.Invoke(new LogFeedItem(normalized, isRealTime, isStartupBackfill));
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
            ProcessRawContent(stale, _experienceService.IsReady);
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
            ProcessRawContent(readyContent, isRealTime, isStartupBackfill: isStartupBackfill);
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

                string tag = content.Substring(tagStart, i - tagStart + 1);
                if (Regex.IsMatch(tag, @"^</?br\s*/?>$", RegexOptions.IgnoreCase))
                    return i + 1;

                i = tagStart;
            }

            return -1;
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

        private void SaveCheckpoint()
        {
            try
            {
                Directory.CreateDirectory(StateDirectoryPath);
                var checkpoint = new LogPipelineCheckpoint
                {
                    LogPath = _logPath,
                    LastPosition = _lastPosition,
                    LastLogTimeText = _lastLogTimeText,
                    UpdatedAtUtc = DateTime.UtcNow
                };

                File.WriteAllText(CheckpointPath, JsonSerializer.Serialize(checkpoint, JsonOptions), Utf8BomEncoding);
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
