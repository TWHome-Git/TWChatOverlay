using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 테일즈위버 로그(HTML)를 실시간으로 감시하고 읽어오는 서비스
    /// </summary>
    public class LogService : IDisposable
    {
        #region Fields & Properties

        private string _logPath = null!;
        private long _lastPosition = 0;
        private readonly object _lockObj = new object();
        private readonly DispatcherTimer _pollingTimer;
        private FileSystemWatcher? _logWatcher;
        private int _immediateReadScheduled;
        private bool _disposed;
        private readonly ExperienceService _experienceService;
        private readonly ChatSettings _settings;
        private readonly Encoding _logEncoding;
        private readonly Decoder _logDecoder;
        private string _pendingRawContent = string.Empty;
        private const int InitialLogTailBytes = 2 * 1024 * 1024;

        public event Action<string>? OnNewLogRead;
        public event Action? InitialLogsLoaded;
        private static readonly Regex LineSplitRegex = new Regex(@"</?br\s*>|\r?\n", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        #endregion

        #region Constructor & Lifecycle

        public LogService(ExperienceService experienceService, ChatSettings settings)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _logEncoding = Encoding.GetEncoding(949);
            _logDecoder = _logEncoding.GetDecoder();
            _experienceService = experienceService;
            _settings = settings;

            _pollingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _pollingTimer.Tick += (s, e) =>
            {
                CheckDateAndPath();
                ReadLog();
            };
            AppLogger.Info("LogService initialized.");
        }

        public void Start()
        {
            if (_logWatcher != null)
                _logWatcher.EnableRaisingEvents = true;

            _pollingTimer.Start();
            AppLogger.Info("LogService polling started.");
        }

        public void Stop()
        {
            _pollingTimer.Stop();

            if (_logWatcher != null)
                _logWatcher.EnableRaisingEvents = false;

            AppLogger.Info("LogService polling stopped.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { Stop(); } catch { }
            DisposeWatcher();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Path Management
        /// <summary>
        /// MainWindow에서 이벤트를 연결한 후 명시적으로 호출해야 합니다.
        /// </summary>
        public void Initialize()
        {
            UpdatePath();
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
                UpdatePath();
            }
        }

        /// <summary>
        /// 현재 날짜에 맞는 로그 경로를 설정하고 초기 위치를 지정
        /// </summary>
        private void UpdatePath()
        {
            string today = DateTime.Now.ToString("yyyy_MM_dd");
            _logPath = Path.Combine(_settings.ChatLogFolderPath, $"TWChatLog_{today}.html");
            ConfigureWatcherForCurrentPath();

            if (File.Exists(_logPath))
            {
                _pendingRawContent = string.Empty;
                _logDecoder.Reset();
                LoadInitialLogs(1000);
                var fileInfo = new FileInfo(_logPath);
                _lastPosition = fileInfo.Length;
                AppLogger.Info($"Log file ready: {_logPath}, initial position={_lastPosition}.");
            }
            else
            {
                _lastPosition = 0;
                _pendingRawContent = string.Empty;
                _logDecoder.Reset();
                AppLogger.Warn($"Log file not found: {_logPath}.");
            }
        }

        private void ConfigureWatcherForCurrentPath()
        {
            try
            {
                string? directory = Path.GetDirectoryName(_logPath);
                string? fileName = Path.GetFileName(_logPath);

                if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName) || !Directory.Exists(directory))
                {
                    DisposeWatcher();
                    return;
                }

                bool needsRecreate =
                    _logWatcher == null ||
                    !string.Equals(_logWatcher.Path, directory, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(_logWatcher.Filter, fileName, StringComparison.OrdinalIgnoreCase);

                if (!needsRecreate)
                    return;

                DisposeWatcher();

                _logWatcher = new FileSystemWatcher(directory, fileName)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = _pollingTimer.IsEnabled
                };
                _logWatcher.Changed += LogWatcher_OnChanged;
                _logWatcher.Created += LogWatcher_OnChanged;
                _logWatcher.Renamed += LogWatcher_OnRenamed;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to configure log file watcher.", ex);
            }
        }

        private void DisposeWatcher()
        {
            try
            {
                if (_logWatcher == null) return;

                _logWatcher.EnableRaisingEvents = false;
                _logWatcher.Changed -= LogWatcher_OnChanged;
                _logWatcher.Created -= LogWatcher_OnChanged;
                _logWatcher.Renamed -= LogWatcher_OnRenamed;
                _logWatcher.Dispose();
                _logWatcher = null;
            }
            catch
            {
            }
        }

        private void LogWatcher_OnChanged(object sender, FileSystemEventArgs e)
        {
            if (!string.Equals(e.FullPath, _logPath, StringComparison.OrdinalIgnoreCase))
                return;

            ScheduleImmediateRead();
        }

        private void LogWatcher_OnRenamed(object sender, RenamedEventArgs e)
        {
            if (!string.Equals(e.FullPath, _logPath, StringComparison.OrdinalIgnoreCase))
                return;

            ScheduleImmediateRead();
        }

        private void ScheduleImmediateRead()
        {
            if (Interlocked.Exchange(ref _immediateReadScheduled, 1) != 0)
                return;

            try
            {
                Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
                {
                    Interlocked.Exchange(ref _immediateReadScheduled, 0);
                    CheckDateAndPath();
                    ReadLog();
                }), DispatcherPriority.Background);
            }
            catch
            {
                Interlocked.Exchange(ref _immediateReadScheduled, 0);
            }
        }

        #endregion

        #region Method

        /// <summary>
        /// 초기 구동 시 기존 로그의 마지막 부분을 가져옴
        /// </summary>
        private void LoadInitialLogs(int lineCount)
        {
            try
            {
                using var stream = new FileStream(_logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length == 0)
                {
                    InitialLogsLoaded?.Invoke();
                    return;
                }

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
                    ProcessRawContent(tailContent, lineCount);
                }

                InitialLogsLoaded?.Invoke();

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _experienceService.SetReady();
                }), DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load initial logs.", ex);
            }
        }

        /// <summary>
        /// 실시간으로 추가된 로그만 Seek를 이용해 빠르게 읽음
        /// </summary>
        public void ReadLog()
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
                        return;

                    string newContent = DecodeIncrementalBytes(buffer, totalRead);
                    ProcessIncrementalContent(newContent);
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
                ProcessRawContent(content);
            }
        }

        #endregion

        #region Processing

        /// <summary>
        /// 읽어온 원문 HTML을 <br> 태그 단위로 쪼개어 이벤트를 발생
        /// </summary>
        private void ProcessRawContent(string content, int takeLastCount = -1)
        {
            if (string.IsNullOrWhiteSpace(content)) return;

            var lines = LineSplitRegex.Split(content)
                                  .Where(l => !string.IsNullOrWhiteSpace(l))
                                  .ToList();

            if (takeLastCount > 0 && lines.Count > takeLastCount)
            {
                lines = lines.Skip(lines.Count - takeLastCount).ToList();
            }

            foreach (var line in lines)
            {
                OnNewLogRead?.Invoke(line.Trim());
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

        private void ProcessIncrementalContent(string content)
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
            ProcessRawContent(readyContent);
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
    }
}
