using System;
using System.IO;
using System.Text;
using System.Threading;

namespace TWChatOverlay.Services
{
    public static class PerformanceDiagnosticsService
    {
        private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Performance.log");
        private static readonly object _fileLock = new();
        private static readonly TimeSpan SummaryInterval = TimeSpan.FromSeconds(15);

        private static int _enabled;
        private static Timer? _summaryTimer;

        private static long _incomingLogCount;
        private static long _uiProcessedCount;
        private static long _uiDroppedCount;
        private static long _uiFlushCount;
        private static long _uiDelaySumMicroseconds;
        private static long _uiDelayMaxMicroseconds;
        private static long _uiQueueMax;

        public static void SetEnabled(bool enabled)
        {
            int next = enabled ? 1 : 0;
            int previous = Interlocked.Exchange(ref _enabled, next);
            if (previous == next)
            {
                return;
            }

            if (enabled)
            {
                ResetWindowCounters();
                _summaryTimer ??= new Timer(_ => WriteSummary(), null, SummaryInterval, SummaryInterval);
                _summaryTimer.Change(SummaryInterval, SummaryInterval);
                AppendLine("Performance diagnostics enabled.");
            }
            else
            {
                _summaryTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                AppendLine("Performance diagnostics disabled.");
                ResetWindowCounters();
            }
        }

        public static void Shutdown()
        {
            Interlocked.Exchange(ref _enabled, 0);
            _summaryTimer?.Dispose();
            _summaryTimer = null;
            ResetWindowCounters();
        }

        public static void RecordIncomingLog()
        {
            if (!IsEnabled())
            {
                return;
            }

            Interlocked.Increment(ref _incomingLogCount);
        }

        public static void RecordUiQueueDrop(int droppedCount)
        {
            if (droppedCount <= 0 || !IsEnabled())
            {
                return;
            }

            Interlocked.Add(ref _uiDroppedCount, droppedCount);
        }

        public static void RecordUiQueueLength(int queueLength)
        {
            if (queueLength <= 0 || !IsEnabled())
            {
                return;
            }

            long currentMax = Volatile.Read(ref _uiQueueMax);
            while (queueLength > currentMax)
            {
                long previous = Interlocked.CompareExchange(ref _uiQueueMax, queueLength, currentMax);
                if (previous == currentMax)
                {
                    break;
                }

                currentMax = previous;
            }
        }

        public static void RecordUiBatchProcessed(int count, double avgDelayMs, double maxDelayMs)
        {
            if (count <= 0 || !IsEnabled())
            {
                return;
            }

            Interlocked.Increment(ref _uiFlushCount);
            Interlocked.Add(ref _uiProcessedCount, count);

            long avgMicroseconds = (long)Math.Max(0, avgDelayMs * 1000.0);
            long maxMicroseconds = (long)Math.Max(0, maxDelayMs * 1000.0);

            Interlocked.Add(ref _uiDelaySumMicroseconds, avgMicroseconds * count);

            long currentMax = Volatile.Read(ref _uiDelayMaxMicroseconds);
            while (maxMicroseconds > currentMax)
            {
                long previous = Interlocked.CompareExchange(ref _uiDelayMaxMicroseconds, maxMicroseconds, currentMax);
                if (previous == currentMax)
                {
                    break;
                }

                currentMax = previous;
            }
        }

        private static bool IsEnabled()
        {
            return Volatile.Read(ref _enabled) == 1;
        }

        private static void WriteSummary()
        {
            if (!IsEnabled())
            {
                return;
            }

            long incoming = Interlocked.Exchange(ref _incomingLogCount, 0);
            long processed = Interlocked.Exchange(ref _uiProcessedCount, 0);
            long dropped = Interlocked.Exchange(ref _uiDroppedCount, 0);
            long flush = Interlocked.Exchange(ref _uiFlushCount, 0);
            long queueMax = Interlocked.Exchange(ref _uiQueueMax, 0);
            long delaySumUs = Interlocked.Exchange(ref _uiDelaySumMicroseconds, 0);
            long delayMaxUs = Interlocked.Exchange(ref _uiDelayMaxMicroseconds, 0);

            double avgDelayMs = processed > 0 ? (delaySumUs / (double)processed) / 1000.0 : 0.0;
            double maxDelayMs = delayMaxUs / 1000.0;

            string message =
                $"PERF interval={SummaryInterval.TotalSeconds:F0}s incoming={incoming} processed={processed} dropped={dropped} flush={flush} queueMax={queueMax} avgDelayMs={avgDelayMs:F1} maxDelayMs={maxDelayMs:F1}";
            AppendLine(message);
        }

        private static void ResetWindowCounters()
        {
            Interlocked.Exchange(ref _incomingLogCount, 0);
            Interlocked.Exchange(ref _uiProcessedCount, 0);
            Interlocked.Exchange(ref _uiDroppedCount, 0);
            Interlocked.Exchange(ref _uiFlushCount, 0);
            Interlocked.Exchange(ref _uiDelaySumMicroseconds, 0);
            Interlocked.Exchange(ref _uiDelayMaxMicroseconds, 0);
            Interlocked.Exchange(ref _uiQueueMax, 0);
        }

        private static void AppendLine(string message)
        {
            try
            {
                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                lock (_fileLock)
                {
                    File.AppendAllText(LogFilePath, line + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }
    }
}
