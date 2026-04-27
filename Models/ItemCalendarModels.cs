using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Models
{
    public sealed class ItemCalendarDayViewModel
    {
        public ItemCalendarDayViewModel(DateTime date, bool isCurrentMonth, IEnumerable<ItemCalendarEntryViewModel> entries)
        {
            Date = date.Date;
            IsCurrentMonth = isCurrentMonth;

            Entries = new ObservableCollection<ItemCalendarEntryViewModel>(
                entries.OrderByDescending(entry => GetGradeSortOrder(entry.Grade))
                       .ThenByDescending(entry => entry.Count)
                       .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase));

            TotalCount = Entries.Sum(entry => entry.Count);
            DistinctCount = Entries.Count;
        }

        public DateTime Date { get; }

        public bool IsCurrentMonth { get; }

        public string DayLabel => Date.ToString("ddd", CultureInfo.GetCultureInfo("ko-KR"));

        public string DateLabel => Date.Day.ToString(CultureInfo.InvariantCulture);

        public ObservableCollection<ItemCalendarEntryViewModel> Entries { get; }

        public int TotalCount { get; }

        public int DistinctCount { get; }

        public bool IsHighlighted => TotalCount >= 5;

        public string SummaryText => TotalCount > 0 ? $"총 {TotalCount:N0}개" : "기록 없음";

        public double CellOpacity => IsCurrentMonth ? 1.0 : 0.45;

        public Brush DayAccentBrush => IsHighlighted
            ? new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xD8, 0x4A))
            : Brushes.Transparent;

        public Brush DayBadgeBorderBrush => IsHighlighted
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A))
            : new SolidColorBrush(Color.FromRgb(0x45, 0x4E, 0x57));

        public Brush DayBadgeForeground => IsHighlighted ? Brushes.White : Brushes.WhiteSmoke;

        private static int GetGradeSortOrder(ItemDropGrade grade)
        {
            return grade switch
            {
                ItemDropGrade.Special => 2,
                ItemDropGrade.Rare => 1,
                _ => 0
            };
        }
    }

    public sealed class ItemCalendarEntryViewModel
    {
        public ItemCalendarEntryViewModel(string displayName, ItemDropGrade grade, int count)
        {
            DisplayName = displayName;
            Grade = grade;
            Count = Math.Max(1, count);
        }

        public string DisplayName { get; }

        public ItemDropGrade Grade { get; }

        public int Count { get; }

        public string DisplayText => Count > 1 ? $"{DisplayName} x{Count}" : DisplayName;

        public Brush BorderBrush => Grade switch
        {
            ItemDropGrade.Special => new SolidColorBrush(Color.FromRgb(0xFF, 0x7E, 0xDB)),
            ItemDropGrade.Rare => new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A)),
            _ => new SolidColorBrush(Color.FromRgb(0x4E, 0x57, 0x60))
        };
    }

    public sealed class AbaddonMonthlyStoneSummaryEntryViewModel
    {
        public AbaddonMonthlyStoneSummaryEntryViewModel(string displayName, string iconUri, long count)
        {
            DisplayName = displayName;
            IconUri = iconUri;
            Count = count;
        }

        public string DisplayName { get; }

        public string IconUri { get; }

        public long Count { get; }

        public string CountText => Count >= 0 ? $"+{Count:N0}" : $"{Count:N0}";
    }
}
