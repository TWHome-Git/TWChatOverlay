using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace TWChatOverlay.Services
{
    public enum LogLevel
    {
        TRACE = 0,
        DEBUG = 1,
        INFO = 2,
        WARN = 3,
        ERROR = 4,
        FATAL = 5
    }

    public static class AppLogger
    {
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Debug.log");
        private static readonly object _lock = new object();
        private static readonly ConcurrentQueue<string> _pendingLines = new();
        private static readonly Timer _flushTimer;
        private static LogLevel _minimumLevel = LogLevel.DEBUG;
        private static bool _isEnabled = true;
        private const long MaxFileSizeBytes = 5 * 1024 * 1024;
        private const int MaxPendingLines = 20000;
        private static readonly TimeSpan RotateCheckInterval = TimeSpan.FromSeconds(5);
        private static DateTime _lastRotateCheckUtc = DateTime.MinValue;
        private static int _flushInProgress;
        private static int _pendingLineCount;

        static AppLogger()
        {
            try
            {
                RotateIfNeeded();
                WriteHeader();
                _flushTimer = new Timer(_ => FlushPending(), null, TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(120));
                AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushPending(force: true);
            }
            catch
            {
                _flushTimer = new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
            }
        }

        public static LogLevel MinimumLevel
        {
            get => _minimumLevel;
            set => _minimumLevel = value;
        }

        public static bool IsEnabled
        {
            get => _isEnabled;
            set => _isEnabled = value;
        }

        public static void Trace(string message, [CallerMemberName] string? caller = null) => Write(LogLevel.TRACE, message, caller);
        public static void Debug(string message, [CallerMemberName] string? caller = null) => Write(LogLevel.DEBUG, message, caller);
        public static void Info(string message, [CallerMemberName] string? caller = null) => Write(LogLevel.INFO, message, caller);
        public static void Warn(string message, [CallerMemberName] string? caller = null) => Write(LogLevel.WARN, message, caller);
        public static void Error(string message, [CallerMemberName] string? caller = null) => Write(LogLevel.ERROR, message, caller);
        public static void Fatal(string message, [CallerMemberName] string? caller = null) => Write(LogLevel.FATAL, message, caller);

        public static void Warn(string message, Exception ex, [CallerMemberName] string? caller = null)
        {
            Write(LogLevel.WARN, $"{message} | {ex.GetType().Name}: {ex.Message}{Environment.NewLine}  StackTrace: {ex.StackTrace}", caller);
        }

        public static void Error(string message, Exception ex, [CallerMemberName] string? caller = null)
        {
            Write(LogLevel.ERROR, $"{message} | {ex.GetType().Name}: {ex.Message}{Environment.NewLine}  StackTrace: {ex.StackTrace}", caller);
        }

        public static void Fatal(string message, Exception ex, [CallerMemberName] string? caller = null)
        {
            Write(LogLevel.FATAL, $"{message} | {ex.GetType().Name}: {ex.Message}{Environment.NewLine}  StackTrace: {ex.StackTrace}", caller);
        }

        private static void Write(LogLevel level, string message, string? caller)
        {
            if (!_isEnabled) return;
            if (level < _minimumLevel) return;

            string levelLabel = level switch
            {
                LogLevel.TRACE => "TRACE",
                LogLevel.DEBUG => "DEBUG",
                LogLevel.INFO => "INFO ",
                LogLevel.WARN => "WARN ",
                LogLevel.ERROR => "ERROR",
                LogLevel.FATAL => "FATAL",
                _ => "UNKWN"
            };

            string priority = level switch
            {
                LogLevel.FATAL => "P0",
                LogLevel.ERROR => "P1",
                LogLevel.WARN => "P2",
                LogLevel.INFO => "P3",
                _ => "P4"
            };

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string callerPart = string.IsNullOrEmpty(caller) ? string.Empty : $" [{caller}]";
            string line = $"[{timestamp}] [{priority}] [{levelLabel}]{callerPart} {message}";

            _pendingLines.Enqueue(line);
            int count = Interlocked.Increment(ref _pendingLineCount);
            while (count > MaxPendingLines && _pendingLines.TryDequeue(out _))
            {
                count = Interlocked.Decrement(ref _pendingLineCount);
            }
        }

        private static void FlushPending(bool force = false)
        {
            if (Interlocked.Exchange(ref _flushInProgress, 1) != 0)
                return;

            try
            {
                if (!force && _pendingLines.IsEmpty)
                    return;

                var sb = new StringBuilder(4096);
                while (_pendingLines.TryDequeue(out var line))
                {
                    sb.AppendLine(line);
                    Interlocked.Decrement(ref _pendingLineCount);
                }

                if (sb.Length == 0)
                    return;

                lock (_lock)
                {
                    try
                    {
                        var nowUtc = DateTime.UtcNow;
                        if (force || nowUtc - _lastRotateCheckUtc >= RotateCheckInterval)
                        {
                            RotateIfNeeded();
                            _lastRotateCheckUtc = nowUtc;
                        }
                        File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _flushInProgress, 0);
            }
        }

        private static void RotateIfNeeded()
        {
            if (!File.Exists(LogFilePath)) return;

            var info = new FileInfo(LogFilePath);
            if (info.Length < MaxFileSizeBytes) return;

            string backup = Path.ChangeExtension(LogFilePath, ".old.log");
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(LogFilePath, backup);
        }

        private static void WriteHeader()
        {
            string version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
            string separator = new string('=', 70);
            string header = $"{separator}{Environment.NewLine}[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] TWChatOverlay v{version} START{Environment.NewLine}{separator}";
            lock (_lock)
            {
                try
                {
                    File.AppendAllText(LogFilePath, header + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                }
            }
        }
    }
}
