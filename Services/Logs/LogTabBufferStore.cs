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

        // 증분 렌더용 버전: 탭별 누적 추가 수(트림과 무관하게 단조 증가)와
        // 세대(Replace 등 전체 교체 시 증가 — 소비자는 세대가 바뀌면 전체를 다시 그린다)
        private readonly Dictionary<string, long> _totalAppended = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _generation = new(StringComparer.Ordinal);

        public int MaxCountPerTab => _maxCountPerTab;

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
                _totalAppended[tabName] = _totalAppended.GetValueOrDefault(tabName) + 1;
                TrimIfNeeded(buffer);
            }
        }

        /// <summary>증분 렌더용 버전 조회: (누적 추가 수, 세대).</summary>
        public (long TotalAppended, int Generation) GetVersion(string tabName)
        {
            lock (_sync)
            {
                return (_totalAppended.GetValueOrDefault(tabName), _generation.GetValueOrDefault(tabName));
            }
        }

        /// <summary>
        /// 누적 추가 수 <paramref name="afterTotalAppended"/> 이후에 추가된 로그의 스냅샷을 반환한다.
        /// 새 로그가 트림으로 이미 버퍼를 넘겼거나 세대가 바뀐 경우 null을 반환한다 — 호출자는 전체를 다시 그려야 한다.
        /// </summary>
        public IReadOnlyList<LogParser.ParseResult>? GetLogsAppendedAfter(
            string tabName, long afterTotalAppended, int expectedGeneration,
            out long totalAppended, out int generation)
        {
            lock (_sync)
            {
                totalAppended = _totalAppended.GetValueOrDefault(tabName);
                generation = _generation.GetValueOrDefault(tabName);

                if (!_buffers.TryGetValue(tabName, out var buffer))
                    return Array.Empty<LogParser.ParseResult>();

                if (generation != expectedGeneration || afterTotalAppended > totalAppended)
                    return null;

                long newCountLong = totalAppended - afterTotalAppended;
                if (newCountLong == 0)
                    return Array.Empty<LogParser.ParseResult>();
                if (newCountLong >= buffer.Count)
                    return null; // 중간이 트림으로 유실됨 — 전체 재구축 필요

                int newCount = (int)newCountLong;
                return buffer.GetRange(buffer.Count - newCount, newCount);
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

                _generation[tabName] = _generation.GetValueOrDefault(tabName) + 1;
                _totalAppended[tabName] = buffer.Count;
            }
        }

        private void TrimIfNeeded(List<LogParser.ParseResult> buffer)
        {
            if (buffer.Count <= _trimThresholdPerTab) return;

            buffer.RemoveRange(0, buffer.Count - _maxCountPerTab);
        }

        public void UpdateAllBrushes(Func<LogParser.ParseResult, SolidColorBrush> brushFactory)
        {
            if (brushFactory == null) return;

            lock (_sync)
            {
                foreach (var pair in _buffers)
                {
                    foreach (var log in pair.Value)
                    {
                        log.Brush = brushFactory(log);
                    }

                    // 이미 그려진 문단은 옛 브러시를 물고 있으므로 소비자가 전체를 다시 그리게 한다
                    _generation[pair.Key] = _generation.GetValueOrDefault(pair.Key) + 1;
                }
            }
        }
    }
}
