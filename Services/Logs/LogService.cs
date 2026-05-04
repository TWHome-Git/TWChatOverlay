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
        bool IsRealTime);

    public sealed class LogPipelineCheckpoint
    {
        public string LogPath { get; set; } = string.Empty;
        public long LastPosition { get; set; }
        public string LastLogTimeText { get; set; } = string.Empty;
        public DateTime UpdatedAtUtc { get; set; }
    }

    /// <summary>
    /// 테일즈위버 로그(HTML)를 폴링 기반 단일 파이프라인으로 수집합니다.
    /// 캐시는 사용하지 않고 체크포인트(마지막 읽은 오프셋)만 유지합니다.
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
        private static readonly Regex ShoutLineRegex = new(
            @"^\s*<font[^>]*color=[""']?#?(?:white|ffffff)[""']?[^>]*>\s*(?<time>\[[^<]+?\])\s*</font>\s*<font[^>]*color=[""']?#?c896c8[""']?[^>]*>(?<content>.*?)</font>\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
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
        /// MainWindow에서 이벤트를 연결한 후 명시적으로 호출해야 합니다.
        /// </summary>
        public void Initialize()
        {
            UpdatePath(isInitialLoad: true);
            AppLogger.Info($"LogService initialized path: {_logPath}");
        }

        /// <summary>
        /// 날짜가 변경되었는지 확인하고 필요시 경로를 업데이트
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
        /// 현재 날짜에 맞는 로그 경로를 설정하고 체크포인트 기준으로 초기 위치를 결정합니다.
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
                        ReadLog(isRealTimeOverride: false);
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
        /// 초기 구동 시 기존 로그의 마지막 부분을 가져옵니다.
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
        /// 실시간으로 추가된 로그를 증분으로 읽습니다.
        /// </summary>
        public void ReadLog(bool? isRealTimeOverride = null)
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
                        isRealTimeOverride ?? _experienceService.IsReady);
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
        /// 읽어온 원문 HTML을 <br> 태그 단위로 분리해 이벤트를 발생시킵니다.
        /// </summary>
        private void ProcessRawContent(string content, bool isRealTime, int takeLastCount = -1)
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            var lines = LineSplitRegex.Split(content)
                .Where(l => !string.IsNullOrWhiteSpace(l))
                .ToList();

            lines = MergeWrappedShoutLines(lines);

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

                OnNewLogRead?.Invoke(new LogFeedItem(normalized, isRealTime));
            }
        }

        private static List<string> MergeWrappedShoutLines(IReadOnlyList<string> lines)
        {
            if (lines.Count <= 1)
                return lines.ToList();

            var merged = new List<string>(lines.Count);
            int index = 0;
            while (index < lines.Count)
            {
                string current = lines[index].Trim();
                if (index + 1 < lines.Count &&
                    TryMergeWrappedShout(current, lines[index + 1], out string joined))
                {
                    merged.Add(joined);
                    index += 2;
                    continue;
                }

                merged.Add(current);
                index++;
            }

            return merged;
        }

        private static bool TryMergeWrappedShout(string firstLine, string secondLine, out string mergedLine)
        {
            mergedLine = firstLine;

            var first = ShoutLineRegex.Match(firstLine);
            var second = ShoutLineRegex.Match(secondLine);
            if (!first.Success || !second.Success)
                return false;

            string time1 = NormalizeWhitespace(WebUtility.HtmlDecode(first.Groups["time"].Value));
            string time2 = NormalizeWhitespace(WebUtility.HtmlDecode(second.Groups["time"].Value));
            if (!string.Equals(time1, time2, StringComparison.Ordinal))
                return false;

            string content1 = first.Groups["content"].Value;
            string content2 = second.Groups["content"].Value;
            string plain1 = NormalizeWhitespace(WebUtility.HtmlDecode(content1));
            string plain2 = NormalizeWhitespace(WebUtility.HtmlDecode(content2));

            bool firstLooksLikeShout = plain1.Contains("외치기", StringComparison.OrdinalIgnoreCase);
            bool secondLooksLikeContinuation = !plain2.Contains("외치기", StringComparison.OrdinalIgnoreCase);
            if (!firstLooksLikeShout || !secondLooksLikeContinuation)
                return false;

            mergedLine =
                $@"<font color=""white"">{first.Groups["time"].Value}</font><font color=""#c896c8"">{content1}{content2}</font>";
            return true;
        }

        private static string NormalizeWhitespace(string text)
            => Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();

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

        private void ProcessIncrementalContent(string content, bool isRealTime)
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
            ProcessRawContent(readyContent, isRealTime);
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
