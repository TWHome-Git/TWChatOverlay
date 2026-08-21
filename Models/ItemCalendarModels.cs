using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Models
{
    public sealed class ItemCalendarDayViewModel : INotifyPropertyChanged
    {
        private readonly List<ItemCalendarEntryViewModel> _entries = new();
        private readonly ObservableCollection<ItemCalendarEntryViewModel> _observableEntries = new();
        private int _totalCount;
        private int _distinctCount;
        private int _experienceEssenceCount;

        public ItemCalendarDayViewModel(DateTime date, bool isCurrentMonth, IEnumerable<ItemCalendarEntryViewModel> entries)
        {
            Date = date.Date;
            IsCurrentMonth = isCurrentMonth;
            PropertyChanged = delegate { };
            ReplaceEntries(entries);
        }

        public DateTime Date { get; }
        public bool IsCurrentMonth { get; }
        public string DayLabel => Date.ToString("ddd", CultureInfo.GetCultureInfo("ko-KR"));
        public string DateLabel => Date.Day.ToString(CultureInfo.InvariantCulture);
        public ObservableCollection<ItemCalendarEntryViewModel> Entries => _observableEntries;

        public int TotalCount
        {
            get => _totalCount;
            private set
            {
                if (_totalCount == value) return;
                _totalCount = value;
                OnPropertyChanged(nameof(TotalCount));
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(IsHighlighted));
                OnPropertyChanged(nameof(DayAccentBrush));
                OnPropertyChanged(nameof(DayBadgeBorderBrush));
                OnPropertyChanged(nameof(DayBadgeForeground));
            }
        }

        public int DistinctCount
        {
            get => _distinctCount;
            private set
            {
                if (_distinctCount == value) return;
                _distinctCount = value;
                OnPropertyChanged(nameof(DistinctCount));
            }
        }

        public int ExperienceEssenceCount
        {
            get => _experienceEssenceCount;
            set
            {
                if (_experienceEssenceCount == value) return;
                _experienceEssenceCount = Math.Max(0, value);
                OnPropertyChanged(nameof(ExperienceEssenceCount));
                OnPropertyChanged(nameof(HasExperienceEssence));
                OnPropertyChanged(nameof(ExperienceEssenceText));
            }
        }

        public bool HasExperienceEssence => ExperienceEssenceCount > 0;
        public string ExperienceEssenceText => $"경험의 정수 {ExperienceEssenceCount:N0}개";
        public bool IsHighlighted => TotalCount >= 5;
        public string SummaryText => TotalCount > 0 ? $"총 {TotalCount:N0}개" : "기록 없음";
        public double CellOpacity => IsCurrentMonth ? 1.0 : 0.45;
        public Brush DayAccentBrush => IsHighlighted ? new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xD8, 0x4A)) : Brushes.Transparent;
        public Brush DayBadgeBorderBrush => IsHighlighted ? new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A)) : new SolidColorBrush(Color.FromRgb(0x45, 0x4E, 0x57));
        public Brush DayBadgeForeground => IsHighlighted ? Brushes.White : Brushes.WhiteSmoke;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void AddSnapshot(ItemCalendarEntryViewModel entry)
        {
            _entries.Add(entry);
            RebuildEntries();
        }

        public void ReplaceEntries(IEnumerable<ItemCalendarEntryViewModel> entries)
        {
            _entries.Clear();
            _entries.AddRange(entries);
            RebuildEntries();
        }

        private void RebuildEntries()
        {
            var ordered = _entries
                .OrderByDescending(entry => GetGradeSortOrder(entry.Grade))
                .ThenByDescending(entry => entry.Count)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _observableEntries.Clear();
            foreach (var entry in ordered)
                _observableEntries.Add(entry);

            TotalCount = _observableEntries.Sum(entry => entry.Count);
            DistinctCount = _observableEntries.Count;
        }

        private void OnPropertyChanged(string propertyName) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private static int GetGradeSortOrder(ItemDropGrade grade) => grade switch
        {
            ItemDropGrade.Special => 2,
            ItemDropGrade.Rare => 1,
            _ => 0
        };
    }

    public sealed class ItemCalendarEntryViewModel
    {
        // Data/images/Item 에 실제로 존재하는 아이콘 (파일명은 공백 없음)
        private static readonly HashSet<string> KnownIconNames = new(StringComparer.Ordinal)
        {
            "경험의정수", "달의파편", "상급마정석", "시드", "신조의정수", "월광석",
            "응축된신조의가루", "중급마정석", "최상급마정석", "코어가루", "코어결정", "하급마정석",
        };

        public ItemCalendarEntryViewModel(string displayName, ItemDropGrade grade, int count)
        {
            DisplayName = displayName;
            Grade = grade;
            Count = Math.Max(1, count);
            IconUri = ResolveIconUri(displayName);
        }

        public string DisplayName { get; }
        public ItemDropGrade Grade { get; }
        public int Count { get; }
        public string DisplayText => Count > 1 ? $"{DisplayName} x{Count}" : DisplayName;

        /// <summary>아이콘 이미지가 있으면 pack URI, 없으면 null(텍스트로 표시).</summary>
        public string? IconUri { get; }
        public bool HasIcon => IconUri != null;
        public bool ShowCountBadge => HasIcon && Count > 1;
        public string CountBadgeText => $"x{Count:N0}";

        private static string? ResolveIconUri(string displayName)
        {
            string normalized = (displayName ?? string.Empty).Replace(" ", "", StringComparison.Ordinal);

            // "경험의 정수" 이미지는 파일명이 "경험의 정수.png"(공백 포함)라 별도 처리
            if (normalized == "경험의정수")
                return "pack://application:,,,/Data/images/Item/경험의 정수.png";

            return KnownIconNames.Contains(normalized)
                ? $"pack://application:,,,/Data/images/Item/{normalized}.png"
                : null;
        }
        public Brush BorderBrush => Grade switch
        {
            ItemDropGrade.Special => new SolidColorBrush(Color.FromRgb(0xFF, 0x7E, 0xDB)),
            ItemDropGrade.Rare => new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A)),
            _ => new SolidColorBrush(Color.FromRgb(0x4E, 0x57, 0x60))
        };
    }

    public sealed class AbandonMonthlyStoneSummaryEntryViewModel
    {
        public AbandonMonthlyStoneSummaryEntryViewModel(string displayName, string iconUri, long count, string? countForegroundHex = null, string? nameForegroundHex = null, string? customCountText = null)
        {
            DisplayName = displayName;
            IconUri = iconUri;
            Count = count;
            CountForegroundHex = string.IsNullOrWhiteSpace(countForegroundHex) ? "#DCE3EA" : countForegroundHex;
            NameForegroundHex = string.IsNullOrWhiteSpace(nameForegroundHex) ? "#FFFFFF" : nameForegroundHex;
            CustomCountText = customCountText;
        }

        public string DisplayName { get; }
        public string IconUri { get; }
        public long Count { get; }
        public string CountForegroundHex { get; }
        public string NameForegroundHex { get; }
        public string? CustomCountText { get; }
        public string CountText => !string.IsNullOrWhiteSpace(CustomCountText) ? CustomCountText : (Count >= 0 ? $"+{Count:N0}" : $"{Count:N0}");
    }
}
