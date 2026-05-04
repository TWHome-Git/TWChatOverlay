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

        public int ExperienceEssenceCount { get; set; } = 0;

    }
}
