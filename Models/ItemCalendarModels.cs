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
        // 표시명(공백 제거) → Data/images/Item 파일명 매핑.
        // 새 이미지를 추가하면 여기에 한 줄 등록하면 달력에 아이콘으로 표시된다.
        private static readonly Dictionary<string, string> IconFilesByName = new(StringComparer.Ordinal)
        {
            // 기본 재화/드롭
            ["신조의정수"] = "신조의정수.png",
            ["경험의정수"] = "경험의 정수.png",
            ["월광석"] = "월광석.png",
            ["달의파편"] = "달의파편.png",
            ["응축된신조의가루"] = "응축된신조의가루.png",
            ["코어가루"] = "코어가루.png",
            ["코어결정"] = "코어결정.png",
            ["시드"] = "시드.png",
            ["하급마정석"] = "하급마정석.png",
            ["중급마정석"] = "중급마정석.png",
            ["상급마정석"] = "상급마정석.png",
            ["최상급마정석"] = "최상급마정석.png",
            ["갤럭시스톤"] = "갤럭시_스톤.png",
            ["계승의결정체"] = "계승의_결정체.png",
            ["달빛변환장치"] = "달빛_변환_장치.png",
            ["세크리드룬스톤"] = "세크리드_룬스톤.png",
            ["세크리드주화"] = "세크리드_주화.png",
            ["세크주머니"] = "세크리드_주화_주머니.png",
            ["에모티스"] = "에모티스.png",
            // 장비류는 표시명이 묶음이라 대표 아이콘(소드) 사용
            ["어비스장비"] = "어비스_소드.png",
            ["이클립스장비"] = "이클립스_소드.png",
            ["아크론문양"] = "아크론_요새의_문양.png",
            ["변환장비"] = "아크론_요새의_변환_장치.png",

            // 가짜 달여왕 군단 시리즈
            ["가짜각갑파편"] = "가짜_달여왕_군단의_각갑_파편.png",
            ["가짜갑옷파편"] = "가짜_달여왕_군단의_갑옷_파편.png",
            ["가짜건틀렛파편"] = "가짜_달여왕_군단의_건틀렛_파편.png",
            ["가짜무기파편"] = "가짜_달여왕_군단의_무기_파편.png",
            ["가짜문양"] = "가짜_달여왕_군단의_문양.png",
            ["가짜방패조각"] = "가짜_달여왕_군단의_방패_조각.png",
            ["가짜투구장식"] = "가짜_달여왕_군단의_투구_장식.png",
            ["가짜펜던트장식"] = "가짜_달여왕_군단의_펜던트_파편.png",
            ["가짜휘장장식"] = "가짜_달여왕_군단의_휘장_장식.png",

            // 고대 기사 시리즈
            ["고대각갑파편"] = "고대_기사의_각갑_파편.png",
            ["고대갑옷파편"] = "고대_기사의_갑옷_파편.png",
            ["고대건틀렛파편"] = "고대_기사의_건틀렛_조각.png",
            ["고대방패조각"] = "고대_기사의_방패_조각.png",
            ["고대투구파편"] = "고대_기사의_투구_파편.png",
            ["고대펜던트파편"] = "고대_기사의_팬던트_파편.png",
            ["고대휘장조각"] = "고대_기사의_휘장_조각.png",

            // 아크론 요새 시리즈
            ["요새가죽조각"] = "요새_문양이_새겨진_가죽_조각.png",
            ["요새금속파편"] = "요새_문양이_새겨진_금속_파편.png",
            ["요새목걸이조각"] = "요새_문양이_새겨진_목걸이_조각.png",
            ["요새판금조각"] = "요새_문양이_새겨진_판금_조각.png",
            ["요새보석파편"] = "요새_수호자의_보석_파편.png",
            ["요새보호구조각"] = "요새_수호자의_보호구_조각.png",
            ["요새부츠조각"] = "요새_수호자의_부츠_조각.png",
            ["요새장식깃털"] = "요새_수호자의_장식_깃털.png",

            // 어빌리티/연마 (사용자 제작 아이콘)
            ["안식어빌리티"] = "안식.png",
            ["야성어빌리티"] = "야성.png",
            ["상실어빌리티"] = "상실.png",
            ["렐릭부가재설정"] = "렐릭어빌.png",
            ["렐릭어빌재설정"] = "렐릭어빌.png",
            ["저격연마LV6"] = "저격연마.png",
            ["저격연마LV7"] = "저격연마.png",
            ["저격연마LV8"] = "저격연마.png",
            ["저격연마LV9"] = "저격연마.png",
            ["저격연마LV10"] = "저격연마.png",
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

            return IconFilesByName.TryGetValue(normalized, out string? fileName)
                ? $"pack://application:,,,/Data/images/Item/{fileName}"
                : null;
        }
        public Brush BorderBrush => Grade switch
        {
            ItemDropGrade.Special => new SolidColorBrush(Color.FromRgb(0xFF, 0x7E, 0xDB)),
            ItemDropGrade.Rare => new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A)),
            // 일반 등급도 테두리가 보이도록 밝은 회백색
            _ => new SolidColorBrush(Color.FromRgb(0xB4, 0xBB, 0xC2))
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
