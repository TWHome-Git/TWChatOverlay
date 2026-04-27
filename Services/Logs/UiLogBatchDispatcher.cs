using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 로그 UI 갱신 요청을 배치로 묶어 디스패처에 전달합니다.
    /// </summary>
    public sealed class UiLogBatchDispatcher
    {
        private readonly Dispatcher _dispatcher;
        private readonly int _batchSize;
        private readonly int _maxQueueSize;
        private readonly object _lockObj = new();
        private readonly Queue<QueuedLogItem> _queue = new();
        private bool _isScheduled;

        private readonly struct QueuedLogItem
        {
            public QueuedLogItem(string html, bool isRealTime)
            {
                Html = html;
                IsRealTime = isRealTime;
            }

            public string Html { get; }
            public bool IsRealTime { get; }
        }

        /// <summary>
        /// 배치 디스패처를 생성합니다.
        /// </summary>
        public UiLogBatchDispatcher(Dispatcher dispatcher, int batchSize = 60, int maxQueueSize = 20000)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _batchSize = batchSize > 0 ? batchSize : 60;
            _maxQueueSize = maxQueueSize > 0 ? maxQueueSize : 20000;
        }

        /// <summary>
        /// 로그 항목을 큐에 넣고 필요 시 배치 처리 스케줄을 시작합니다.
        /// </summary>
        public void Enqueue(string html, bool isRealTime, Action<IReadOnlyList<(string Html, bool IsRealTime)>> onBatchReady)
        {
            if (string.IsNullOrWhiteSpace(html) || onBatchReady == null)
                return;

            bool shouldSchedule = false;
            lock (_lockObj)
            {
                while (_queue.Count >= _maxQueueSize)
                {
                    _queue.Dequeue();
                }

                _queue.Enqueue(new QueuedLogItem(html, isRealTime));
                if (!_isScheduled)
                {
                    _isScheduled = true;
                    shouldSchedule = true;
                }
            }

            if (shouldSchedule)
            {
                _dispatcher.BeginInvoke(new Action(() => Flush(onBatchReady)), DispatcherPriority.Render);
            }
        }

        private void Flush(Action<IReadOnlyList<(string Html, bool IsRealTime)>> onBatchReady)
        {
            List<(string Html, bool IsRealTime)> batch = new(_batchSize);
            bool hasMore;

            lock (_lockObj)
            {
                while (_queue.Count > 0 && batch.Count < _batchSize)
                {
                    var item = _queue.Dequeue();
                    batch.Add((item.Html, item.IsRealTime));
                }

                hasMore = _queue.Count > 0;
                _isScheduled = hasMore;
            }

            if (batch.Count > 0)
            {
                onBatchReady(batch);
            }

            if (hasMore)
            {
                _dispatcher.BeginInvoke(new Action(() => Flush(onBatchReady)), DispatcherPriority.Render);
            }
        }
    }
}
