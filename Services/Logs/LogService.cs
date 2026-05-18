using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
        private readonly object _lockObj = new();
        private readonly DispatcherTimer _pollingTimer;
        private bool _disposed;
        private readonly ExperienceService _experienceService;
        private readonly ChatSettings _settings;
        private readonly Encoding _logEncoding;
        private readonly Decoder _logDecoder;
        private string _pendingRawContent = string.Empty;
        private string _lastLogTimeText = string.Empty;

        private const int InitialLogTailBytes = 2 * 1024 * 1024;
        private const int PollingIntervalMilliseconds = 30;
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

            _pollingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PollingIntervalMilliseconds) };
            _pollingTimer.Tick += (_, _) =>
            {
                CheckDateAndPath();
                ReadLog();
            };
            AppLogger.Info("LogService initialized.");
        }

        public void Start()
        {
            _pollingTimer.Start();
            AppLogger.Info("LogService polling started.");
        }

        public void Stop()
        {
            _pollingTimer.Stop();
            AppLogger.Info("LogService polling stopped.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { Stop(); } catch { }
            GC.SuppressFinalize(this);
        }

        #endregion

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
                _pendingRawContent = string.Empty;
                _lastLogTimeText = string.Empty;
                _logDecoder.Reset();

                if (File.Exists(_logPath))
                {
                    var fileInfo = new FileInfo(_logPath);
                    long sourceLength = fileInfo.Length;
                    bool resumedFromCheckpoint = TryRestoreCheckpoint(sourceLength);

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
                    if (!File.Exists(_logPath)) return;

                    using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (stream.Length < _lastPosition)
                    {
                        _lastPosition = 0;
                        _pendingRawContent = string.Empty;
                        _logDecoder.Reset();
                    }
                    if (stream.Length <= _lastPosition) return;

                    long bytesToRead = stream.Length - _lastPosition;
                    if (bytesToRead > int.MaxValue)
                    {
                        AppLogger.Warn($"Incremental log read is too large ({bytesToRead:N0} bytes). Resetting read position to file end.");
                        _lastPosition = stream.Length;
                        _pendingRawContent = string.Empty;
                        _logDecoder.Reset();
                        SaveCheckpoint();
                        return;
                    }

                    stream.Seek(_lastPosition, SeekOrigin.Begin);
                    byte[] buffer = new byte[(int)bytesToRead];
                    int totalRead = 0;
                    while (totalRead < buffer.Length)
                    {
                        int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
                        if (read <= 0)
                            break;

                        totalRead += read;
                    }

                    _lastPosition = stream.Position;
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
                }
            }
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

        private void ProcessIncrementalContent(string content, bool isRealTime, bool isStartupBackfill)
        {
            if (string.IsNullOrEmpty(content))
                return;

            string combined = _pendingRawContent + content;
            int completeEnd = FindLastCompleteLogBoundary(combined);
            if (completeEnd < 0)
            {
                _pendingRawContent = combined;
                return;
            }

            string readyContent = combined.Substring(0, completeEnd);
            _pendingRawContent = completeEnd < combined.Length ? combined.Substring(completeEnd) : string.Empty;
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
