using System;
using System.Text.Json.Serialization;

namespace TWChatOverlay.Models
{
    public sealed class AbaddonMonthlySummarySnapshotEntry
    {
        public DateTime MonthStart { get; set; }

        public long TotalEntryFeeMan { get; set; }

        public long Low { get; set; }

        public long Mid { get; set; }

        public long High { get; set; }

        public long Top { get; set; }

        [JsonIgnore]
        public long StoneRevenueMan =>
            (Low * 50) +
            (Mid * 500) +
            (High * 5000) +
            (Top * 50000);

        [JsonIgnore]
        public long NetProfitMan => StoneRevenueMan - TotalEntryFeeMan;
    }
}
