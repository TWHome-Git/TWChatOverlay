using System;
using System.Collections.Generic;
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
            if (!_buffers.TryGetValue(tabName, out var buffer)) return;

            buffer.Add(log);
            TrimIfNeeded(buffer);
        }

        public IReadOnlyList<LogParser.ParseResult> GetLogs(string tabName)
        {
            if (_buffers.TryGetValue(tabName, out var logs))
            {
                return logs;
            }

            return Array.Empty<LogParser.ParseResult>();
        }

        public void Replace(string tabName, IEnumerable<LogParser.ParseResult> logs)
        {
            if (!_buffers.TryGetValue(tabName, out var buffer)) return;

            buffer.Clear();
            foreach (var log in logs)
            {
                buffer.Add(log);
                TrimIfNeeded(buffer);
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
