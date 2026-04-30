using System;
using TWChatOverlay.Services;

namespace TWChatOverlay.Models
{
    public sealed class ItemLogSnapshotEntry
    {
        public DateTime Date { get; set; }

        public string? ItemName { get; set; }

        public string? DisplayName { get; set; }

        public ItemDropGrade Grade { get; set; } = ItemDropGrade.Normal;

        public int Count { get; set; } = 1;

        public string? FormattedText { get; set; }

        /// <summary>
        /// 0=기본(통합), 1=프로필1, 2=프로필2
        /// </summary>
        public int ProfileSlot { get; set; } = 0;
    }
}
