using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 탭별 로그 버퍼를 보관하고 최대 개수를 관리합니다.
    /// </summary>
    public sealed class LogTabBufferStore
    {
        private readonly Dictionary<string, List<LogParser.ParseResult>> _buffers = new()
        {
            { "Basic", new List<LogParser.ParseResult>() },
            { "General", new List<LogParser.ParseResult>() },
            { "Team", new List<LogParser.ParseResult>() },
            { "Club", new List<LogParser.ParseResult>() },
            { "Shout", new List<LogParser.ParseResult>() },
            { "System", new List<LogParser.ParseResult>() },
            { "Item", new List<LogParser.ParseResult>() }
        };

        private readonly int _maxCountPerTab;
        private readonly int _trimThresholdPerTab;

        // 버퍼는 로그 파이프라인과 UI 코드에서 접근된다. 현재는 모두 UI 스레드에서 호출되지만,
        // 자료구조 손상을 원천 차단하기 위해 모든 접근을 이 락으로 보호한다.
        private readonly object _sync = new();

        /// <summary>
        /// 탭 버퍼 저장소를 생성합니다.
        /// </summary>
        public LogTabBufferStore(int maxCountPerTab = 50000)
        {
            _maxCountPerTab = maxCountPerTab > 0 ? maxCountPerTab : 50000;
            _trimThresholdPerTab = _maxCountPerTab + Math.Max(1, (int)Math.Ceiling(_maxCountPerTab * 0.2));
        }

        public void Add(string tabName, LogParser.ParseResult log)
        {
            lock (_sync)
            {
                if (!_buffers.TryGetValue(tabName, out var buffer)) return;

                buffer.Add(log);
                TrimIfNeeded(buffer);
            }
        }

        /// <summary>
        /// 해당 탭 로그의 스냅샷(복사본)을 반환합니다. 라이브 리스트를 노출하지 않아
        /// 호출자가 열거하는 동안 다른 곳에서 버퍼가 수정되어도 안전합니다.
        /// </summary>
        public IReadOnlyList<LogParser.ParseResult> GetLogs(string tabName)
        {
            lock (_sync)
            {
                if (_buffers.TryGetValue(tabName, out var logs))
                {
                    return logs.ToList();
                }
            }

            return Array.Empty<LogParser.ParseResult>();
        }

        public IReadOnlyDictionary<string, IReadOnlyList<LogParser.ParseResult>> GetAllLogsSnapshot()
        {
            lock (_sync)
            {
                var snapshot = new Dictionary<string, IReadOnlyList<LogParser.ParseResult>>(StringComparer.Ordinal);
                foreach (var pair in _buffers)
                {
                    snapshot[pair.Key] = pair.Value.ToList();
                }
                return snapshot;
            }
        }

        public void Replace(string tabName, IEnumerable<LogParser.ParseResult> logs)
        {
            lock (_sync)
            {
                if (!_buffers.TryGetValue(tabName, out var buffer)) return;

                buffer.Clear();
                foreach (var log in logs)
                {
                    buffer.Add(log);
                    TrimIfNeeded(buffer);
                }
            }
        }

        private void TrimIfNeeded(List<LogParser.ParseResult> buffer)
        {
            if (buffer.Count <= _trimThresholdPerTab) return;

            buffer.RemoveRange(0, buffer.Count - _maxCountPerTab);
        }

        public void UpdateAllBrushes(Func<ChatCategory, SolidColorBrush> brushFactory)
        {
            if (brushFactory == null) return;

            lock (_sync)
            {
                foreach (var buffer in _buffers.Values)
                {
                    foreach (var log in buffer)
                    {
                        log.Brush = brushFactory(log.Category);
                    }
                }
            }
        }
    }
}
