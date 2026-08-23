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

            // 어비스/이클립스 개별 장비 — 실제 아이템명으로 표시 (자동 생성 매핑)
            ["어비스건틀렛"] = "어비스_건틀렛.png",
            ["어비스글라디우스"] = "어비스_글라디우스.png",
            ["어비스글라디우스(sub)"] = "어비스_글라디우스(sub).png",
            ["어비스나이프"] = "어비스_나이프.png",
            ["어비스대거"] = "어비스_대거.png",
            ["어비스레이피어"] = "어비스_레이피어.png",
            ["어비스로드"] = "어비스_로드.png",
            ["어비스로브"] = "어비스_로브.png",
            ["어비스리스트"] = "어비스_리스트.png",
            ["어비스리플렉트아머"] = "어비스_리플렉트_아머.png",
            ["어비스마나피스톨"] = "어비스_마나피스톨.png",
            ["어비스마법탄창"] = "어비스_마법_탄창.png",
            ["어비스메이스"] = "어비스_메이스.png",
            ["어비스메일"] = "어비스_메일.png",
            ["어비스물리탄창"] = "어비스_탄창.png",
            ["어비스방패"] = "어비스_실드.png",
            ["어비스밴드"] = "어비스_밴드.png",
            ["어비스부츠"] = "어비스_부츠.png",
            ["어비스블레이드"] = "어비스_블레이드.png",
            ["어비스사이드"] = "어비스_사이드.png",
            ["어비스셉터"] = "어비스_셉터.png",
            ["어비스소드"] = "어비스_소드.png",
            ["어비스소드셰이프"] = "어비스_소드셰이프.png",
            ["어비스수정구"] = "어비스_수정구.png",
            ["어비스슈츠"] = "어비스_슈츠.png",
            ["어비스스몰소드"] = "어비스_스몰소드.png",
            ["어비스스태프"] = "어비스_스태프.png",
            ["어비스스틱"] = "어비스_스틱.png",
            ["어비스스펠북"] = "어비스_스펠북.png",
            ["어비스스피어"] = "어비스_스피어.png",
            ["어비스시미터"] = "어비스_시미터.png",
            ["어비스아머"] = "어비스_아머.png",
            ["어비스아뮬렛"] = "어비스_아뮬렛.png",
            ["어비스아밍소드"] = "어비스_아밍소드.png",
            ["어비스암릿"] = "어비스_암릿.png",
            ["어비스액스"] = "어비스_액스.png",
            ["어비스완드"] = "어비스_완드.png",
            ["어비스윙"] = "어비스_윙.png",
            ["어비스카라"] = "어비스_카라.png",
            ["어비스크리스"] = "어비스_크리스.png",
            ["어비스크리스(sub)"] = "어비스_크리스(sub).png",
            ["어비스클로"] = "어비스_클로.png",
            ["어비스토템"] = "어비스_토템.png",
            ["어비스페이크소드"] = "어비스_페이크소드.png",
            ["어비스펜듈럼"] = "어비스_펜듈럼.png",
            ["어비스플레일"] = "어비스_플레일.png",
            ["어비스피스톨"] = "어비스_피스톨.png",
            ["어비스해머"] = "어비스_해머.png",
            ["어비스핸드런처"] = "어비스_핸드런처.png",
            ["어비스핸드벨"] = "어비스_핸드벨.png",
            ["어비스헬름"] = "어비스_헬름.png",
            ["어비스휩"] = "어비스_휩.png",
            ["이클립스건틀렛"] = "이클립스_건틀렛.png",
            ["이클립스글라디우스"] = "이클립스_글라디우스.png",
            ["이클립스글라디우스(sub)"] = "이클립스_글라디우스(sub).png",
            ["이클립스나이프"] = "이클립스_나이프.png",
            ["이클립스대거"] = "이클립스_대거.png",
            ["이클립스레이피어"] = "이클립스_레이피어.png",
            ["이클립스로드"] = "이클립스_로드.png",
            ["이클립스로브"] = "이클립스_로브.png",
            ["이클립스리스트"] = "이클립스_리스트.png",
            ["이클립스리플렉트아머"] = "이클립스_리플렉트_아머.png",
            ["이클립스마나피스톨"] = "이클립스_마나피스톨.png",
            ["이클립스마법탄창"] = "이클립스_마법_탄창.png",
            ["이클립스메이스"] = "이클립스_메이스.png",
            ["이클립스메일"] = "이클립스_메일.png",
            ["이클립스물리탄창"] = "이클립스_탄창.png",
            ["이클립스방패"] = "이클립스_실드.png",
            ["이클립스밴드"] = "이클립스_밴드.png",
            ["이클립스부츠"] = "이클립스_부츠.png",
            ["이클립스블레이드"] = "이클립스_블레이드.png",
            ["이클립스사이드"] = "이클립스_사이드.png",
            ["이클립스셉터"] = "이클립스_셉터.png",
            ["이클립스소드"] = "이클립스_소드.png",
            ["이클립스소드셰이프"] = "이클립스_소드셰이프.png",
            ["이클립스수정구"] = "이클립스_수정구.png",
            ["이클립스슈츠"] = "이클립스_슈츠.png",
            ["이클립스스몰소드"] = "이클립스_스몰소드.png",
            ["이클립스스태프"] = "이클립스_스태프.png",
            ["이클립스스틱"] = "이클립스_스틱.png",
            ["이클립스스펠북"] = "이클립스_스펠북.png",
            ["이클립스스피어"] = "이클립스_스피어.png",
            ["이클립스시미터"] = "이클립스_시미터.png",
            ["이클립스아머"] = "이클립스_아머.png",
            ["이클립스아뮬렛"] = "이클립스_아뮬렛.png",
            ["이클립스아밍소드"] = "이클립스_아밍소드.png",
            ["이클립스암릿"] = "이클립스_암릿.png",
            ["이클립스액스"] = "이클립스_액스.png",
            ["이클립스완드"] = "이클립스_완드.png",
            ["이클립스윙"] = "이클립스_윙.png",
            ["이클립스카라"] = "이클립스_카라.png",
            ["이클립스크리스"] = "이클립스_크리스.png",
            ["이클립스크리스(sub)"] = "이클립스_크리스(sub).png",
            ["이클립스클로"] = "이클립스_클로.png",
            ["이클립스토템"] = "이클립스_토템.png",
            ["이클립스페이크소드"] = "이클립스_페이크소드.png",
            ["이클립스펜듈럼"] = "이클립스_펜듈럼.png",
            ["이클립스플레일"] = "이클립스_플레일.png",
            ["이클립스피스톨"] = "이클립스_피스톨.png",
            ["이클립스해머"] = "이클립스_해머.png",
            ["이클립스핸드런처"] = "이클립스_핸드런처.png",
            ["이클립스핸드벨"] = "이클립스_핸드벨.png",
            ["이클립스헬름"] = "이클립스_헬름.png",
            ["이클립스휩"] = "이클립스_휩.png",
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
            ["고대펜던트파편"] = "고대_기사의_펜던트_파편.png",
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
            ["은월의결정(좌)"] = "은월.png",
            ["은월의결정(우)"] = "은월.png",
            ["에오니스라피스"] = "에오니스_라피스.png",
            ["렐릭부가재설정"] = "어빌리티_매직리폼.png",
            ["렐릭어빌재설정"] = "어빌리티_매직리폼.png",
            ["가짜무구"] = "가짜_달여왕_군단의_무구.png",
            ["룬의결정"] = "룬의_결정.png",
            ["설계자의반지"] = "설계자의_반지.png",
            ["신조의깃털"] = "고대_신조의_깃털.png",
            ["에테르오브"] = "에테르_오브.png",
            ["수호자의영혼석"] = "수호자의_영혼석.png",
            ["신조의눈물"] = "신조의_눈물.png",
            ["연금술사의시험관"] = "연금술사의_시험관.png",
            ["연금술사의에테르주사기"] = "연금술사의_에테르_주사기.png",
            ["되돌리기주문서"] = "장비_강화_되돌리기_주문서.png",
            ["재설정주문서"] = "장비_강화_재설정_주문서.png",
            ["찬란한마력가루"] = "찬란한_마력의_가루.png",

            // 어빌리티 개별 아이템 — 원본명으로 표시 + 계열 공용 아이콘 (툴팁으로 이름 확인)
            ["안식의매서운칼날"] = "안식.png",
            ["안식의예리한칼날"] = "안식.png",
            ["안식의마력증폭"] = "안식.png",
            ["안식의백마법증폭"] = "안식.png",
            ["불사의적중검"] = "불사.png",
            ["불사의치명타"] = "불사.png",
            ["불사의생명력"] = "불사.png",
            ["불사의기민함"] = "불사.png",
            ["야성의매서운칼날"] = "야성.png",
            ["야성의예리한칼날"] = "야성.png",
            ["야성의마력증폭"] = "야성.png",
            ["야성의백마법증폭"] = "야성.png",
            ["야성의적중검"] = "야성.png",
            ["야성의치명타"] = "야성.png",
            ["야성의생명력"] = "야성.png",
            ["야성의기민함"] = "야성.png",
            ["야성의민첩함"] = "야성.png",
            ["야성의마법보호(갑옷)"] = "야성.png",
            ["야성의마법보호(손목)"] = "야성.png",
            ["상실의매서운칼날"] = "상실.png",
            ["상실의예리한칼날"] = "상실.png",
            ["상실의마력증폭"] = "상실.png",
            ["상실의백마법증폭"] = "상실.png",
            ["상실의적중검"] = "상실.png",
            ["상실의치명타"] = "상실.png",
            ["상실의생명력"] = "상실.png",
            ["상실의기민함"] = "상실.png",
            ["상실의민첩함"] = "상실.png",
            ["상실의마법보호(갑옷)"] = "상실.png",
            ["상실의마법보호(손목)"] = "상실.png",
            // 갑옷 연마는 각 어빌리티 계열 아이콘 사용
            ["야성의갑옷연마"] = "야성.png",
            ["상실의갑옷연마"] = "상실.png",
            ["고급방어구연마"] = "연마.png",
            ["고급체력연마"] = "연마.png",
            ["연마강화"] = "연마.png",
            ["저격연마LV6"] = "연마.png",
            ["저격연마LV7"] = "연마.png",
            ["저격연마LV8"] = "연마.png",
            ["저격연마LV9"] = "연마.png",
            ["저격연마LV10"] = "연마.png",
        };

        public ItemCalendarEntryViewModel(string displayName, ItemDropGrade grade, int count)
        {
            DisplayName = displayName;
            Grade = grade;
            Count = Math.Max(1, count);
            IconUri = ResolveIconUri(displayName);
            FallbackGlyph = ResolveFallbackGlyph(displayName);
        }

        public string DisplayName { get; }
        public ItemDropGrade Grade { get; }
        public int Count { get; }
        // 갯수는 1개여도 항상 표기 (x1)
        public string DisplayText => $"{DisplayName} x{Count:N0}";

        /// <summary>아이콘 이미지가 있으면 pack URI, 없으면 null(텍스트로 표시).</summary>
        public string? IconUri { get; }
        public bool HasIcon => IconUri != null;

        /// <summary>이미지가 없는 아이템의 한 글자 대체 표기 (예: 테네브리스 → "테").</summary>
        public string? FallbackGlyph { get; }
        public bool HasGlyph => !HasIcon && FallbackGlyph != null;

        public bool ShowCountBadge => HasIcon || HasGlyph;
        public string CountBadgeText => $"x{Count:N0}";

        private static string? ResolveFallbackGlyph(string displayName)
        {
            // 테네브리스 장비: 아직 이미지를 구할 수 없어 임시로 [테] 글자 표기
            if (!string.IsNullOrEmpty(displayName) && displayName.StartsWith("테네브리스", StringComparison.Ordinal))
                return "테";

            return null;
        }

        private static string? ResolveIconUri(string displayName)
        {
            string normalized = (displayName ?? string.Empty).Replace(" ", "", StringComparison.Ordinal);

            return IconFilesByName.TryGetValue(normalized, out string? fileName)
                ? $"pack://application:,,,/Data/images/Item/{fileName}"
                : null;
        }

        /// <summary>
        /// 아이템명(원본 또는 표시명)으로 아이콘 pack URI를 찾는다. 토스트 등 다른 화면에서도 사용.
        /// 원본명으로 못 찾으면 줄임 표시명(abbr)으로 한 번 더 시도한다.
        /// </summary>
        public static string? GetIconUri(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            string? uri = ResolveIconUri(name);
            if (uri != null)
                return uri;

            try
            {
                string display = DropItemResolver.GetTrackedItemDisplayName(name);
                if (!string.Equals(display, name, StringComparison.Ordinal))
                    return ResolveIconUri(display);
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 원본 아이템명에 전용 아이콘이 있으면 원본명을 그대로 표시하고
        /// (예: '이클립스 장비' 대신 '이클립스 부츠'), 없으면 줄임 표시명(abbr)을 쓴다.
        /// </summary>
        public static string ResolvePreferredDisplayName(string? itemName, string? displayName)
        {
            if (!string.IsNullOrWhiteSpace(itemName))
            {
                string normalized = itemName.Replace(" ", "", StringComparison.Ordinal);
                if (IconFilesByName.ContainsKey(normalized))
                    return itemName;
            }

            return string.IsNullOrWhiteSpace(displayName) ? (itemName ?? "아이템") : displayName!;
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
