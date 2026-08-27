using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace TWChatOverlay.Services
{
    /// <summary>분석이 끝난 로그 한 줄. UI는 이 결과를 소비만 한다.</summary>
    public sealed record AnalyzedLogEvent(
        LogFeedItem Source,
        MainLogPipelineAnalysis Analysis)
    {
        public string Html => Source.Html;
        public bool IsRealTime => Source.IsRealTime;
        public bool IsStartupBackfill => Source.IsStartupBackfill;
    }

    /// <summary>
    /// 로그 줄의 파싱·분석을 백그라운드 스레드로 옮기는 파이프라인.
    ///
    ///   LogService(파일 테일) ─Channel─▶ 분석 루프(백그라운드 1개, 순서 보장)
    ///                                        │ MainLogPipelineCoordinator.Analyze
    ///                                        ▼
    ///                              UI 디스패처 배치(≤batchSize) ─▶ onBatchReady
    ///
    /// - 분석기는 모두 무상태이고 결과 브러시는 Freeze되어 있어 스레드 이동이 안전하다.
    /// - 소비자(분석 루프)가 하나라 줄 순서가 그대로 유지된다.
    /// - 한 줄의 분석 실패는 그 줄만 건너뛰고 로그를 남긴다 (배치 유실 방지).
    /// </summary>
    public sealed class LogAnalysisPipeline : IDisposable
    {
        private readonly MainLogPipelineCoordinator _coordinator;
        private readonly Dispatcher _dispatcher;
        private readonly Action<IReadOnlyList<AnalyzedLogEvent>> _onBatchReady;
        private readonly Action<AnalyzedLogEvent>? _backgroundHandler;
        private readonly int _batchSize;

        private readonly Channel<LogFeedItem> _channel;
        private readonly Task _analysisLoop;

        private readonly object _readyLock = new();
        private readonly Queue<AnalyzedLogEvent> _readyQueue = new();
        private bool _isFlushScheduled;
        private bool _disposed;

        public LogAnalysisPipeline(
            MainLogPipelineCoordinator coordinator,
            Dispatcher dispatcher,
            Action<IReadOnlyList<AnalyzedLogEvent>> onBatchReady,
            Action<AnalyzedLogEvent>? backgroundHandler = null,
            int batchSize = 60,
            int maxQueueSize = 20000)
        {
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _onBatchReady = onBatchReady ?? throw new ArgumentNullException(nameof(onBatchReady));
            _backgroundHandler = backgroundHandler;
            _batchSize = batchSize > 0 ? batchSize : 60;

            _channel = Channel.CreateBounded<LogFeedItem>(new BoundedChannelOptions(maxQueueSize > 0 ? maxQueueSize : 20000)
            {
                // 폭주 시 가장 오래된 줄부터 버린다 (기존 UiLogBatchDispatcher와 같은 정책)
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
            });

            _analysisLoop = Task.Run(RunAnalysisLoopAsync);
        }

        /// <summary>로그 줄을 파이프라인에 넣는다. 어느 스레드에서 호출해도 된다.</summary>
        public void Enqueue(LogFeedItem item)
        {
            if (_disposed || item == null || string.IsNullOrWhiteSpace(item.Html))
                return;

            _channel.Writer.TryWrite(item);
        }

        private async Task RunAnalysisLoopAsync()
        {
            await foreach (LogFeedItem item in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                AnalyzedLogEvent? analyzed = null;
                try
                {
                    var analysis = _coordinator.Analyze(item.Html, item.IsRealTime);
                    analyzed = new AnalyzedLogEvent(item, analysis);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Background log analysis failed; line skipped. Html='{item.Html}'", ex);
                }

                if (analyzed == null)
                    continue;

                // UI가 필요 없는 부수효과(아카이브 파일 쓰기 등)는 여기 분석 스레드에서 처리한다
                if (_backgroundHandler != null)
                {
                    try { _backgroundHandler(analyzed); }
                    catch (Exception ex) { AppLogger.Warn("Background log handler failed.", ex); }
                }

                EnqueueReady(analyzed);
            }
        }

        private void EnqueueReady(AnalyzedLogEvent analyzed)
        {
            bool shouldSchedule = false;
            lock (_readyLock)
            {
                _readyQueue.Enqueue(analyzed);
                if (!_isFlushScheduled)
                {
                    _isFlushScheduled = true;
                    shouldSchedule = true;
                }
            }

            if (shouldSchedule)
            {
                try
                {
                    _dispatcher.BeginInvoke(new Action(FlushToUi), DispatcherPriority.Background);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to schedule UI flush for analyzed log batch.", ex);
                    lock (_readyLock) { _isFlushScheduled = false; }
                }
            }
        }

        private void FlushToUi()
        {
            List<AnalyzedLogEvent> batch = new(_batchSize);
            bool hasMore;

            lock (_readyLock)
            {
                while (_readyQueue.Count > 0 && batch.Count < _batchSize)
                    batch.Add(_readyQueue.Dequeue());

                hasMore = _readyQueue.Count > 0;
                _isFlushScheduled = hasMore;
            }

            if (batch.Count > 0)
            {
                try
                {
                    _onBatchReady(batch);
                }
                catch (Exception ex)
                {
                    // 배치 콜백 자체가 죽어도 다음 배치 스케줄은 계속된다
                    AppLogger.Warn("Analyzed log batch handler failed.", ex);
                }
            }

            if (hasMore)
                _dispatcher.BeginInvoke(new Action(FlushToUi), DispatcherPriority.Background);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try { _channel.Writer.TryComplete(); } catch { }
            try { _analysisLoop.Wait(TimeSpan.FromSeconds(1)); } catch { }
        }
    }
}
