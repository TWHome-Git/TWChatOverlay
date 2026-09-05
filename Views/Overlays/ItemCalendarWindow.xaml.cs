using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class ItemCalendarWindow : Window, INotifyPropertyChanged
    {
        private static readonly string LogsRootDirectoryPath = LogStoragePaths.RootDirectory;
        private static readonly string ItemDirectoryPath = LogStoragePaths.ItemDirectory;
        private static readonly string ExpDirectoryPath = LogStoragePaths.ExpDirectory;
        private static readonly string AbandonDirectoryPath = LogStoragePaths.AbandonDirectory;

        private const string LowMagicStoneIconUri = "pack://application:,,,/Data/images/Item/하급마정석.png";
        private const string MiddleMagicStoneIconUri = "pack://application:,,,/Data/images/Item/중급마정석.png";
        private const string HighMagicStoneIconUri = "pack://application:,,,/Data/images/Item/상급마정석.png";
        private const string TopMagicStoneIconUri = "pack://application:,,,/Data/images/Item/최상급마정석.png";

        private static readonly Regex ItemEntryRegex = new(
            "<div\\s+class=\"log\\s+item\"(?<attrs>[^>]*)>(?<text>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AbandonEntryRegex = new(
            "<div\\s+class=\"log\\s+Abandon\"(?<attrs>[^>]*)>(?<text>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AttrRegex = new(
            "(?<name>data-[a-z0-9\\-]+)=\"(?<value>[^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WeekKeyRegex = new(
            @"^(?<year>\d{4})-W(?<week>\d{2})$",
            RegexOptions.Compiled);
        private static readonly Regex ExpEntryRegex = new(
            "<div\\s+class=\"log\\s+exp\"(?<attrs>[^>]*)>(?<text>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ExperienceEssenceGainRegex = new(
            @"경험의\s*정수\s*(?<count>\d+)\s*개를\s*획득\s*하였습니다",
            RegexOptions.Compiled);
        private const string AbandonDaySummarySuffix = ".summary.day.json";

        private readonly ChatSettings _settings;
        private readonly ObservableCollection<ItemCalendarDayViewModel> _days = new();
        private readonly ObservableCollection<ItemCalendarEntryViewModel> _monthlySummary = new();
        private readonly ObservableCollection<AbandonMonthlyStoneSummaryEntryViewModel> _monthlyAbandonSummary = new();
        private readonly List<ItemLogSnapshotEntry> _currentMonthSnapshots = new();
        private AbandonMonthlySummarySnapshotEntry _currentMonthAbandonSummary = new();
        private readonly object _todayLock = new();
        private readonly List<ItemLogSnapshotEntry> _todaySnapshots = new();
        private readonly Dictionary<DateTime, int> _pendingExperienceEssenceByDate = new();
        private DateTime _currentMonthStart;
        private DateTime _loadedMonthStart = DateTime.MinValue;
        private DateTime _loadedTodayDate = DateTime.MinValue;
        private bool _hasLoadedMonth;
        private bool _isLoading;
        private string _monthText = string.Empty;
        private string _statusText = string.Empty;
        private int _loadProgressValue;
        private string _monthlyAbandonSeedText = string.Empty;
        private string _monthlyExperienceEssenceSeedText = string.Empty;
        private int _loadVersion;
        private DispatcherTimer? _midnightTimer;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ItemCalendarWindow(ChatSettings settings, LogAnalysisService logAnalysisService)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _ = logAnalysisService ?? throw new ArgumentNullException(nameof(logAnalysisService));

            InitializeComponent();
            WindowFontService.Apply(this);
            OsSnapGuard.Disable(this); // 상단 드래그 시 OS 스냅(최대화) 차단
            DataContext = this;

            _currentMonthStart = GetMonthStart(DateTime.Today);
            UpdateHeaderText(_currentMonthStart);
            SetLoadingState(false, string.Empty);
            SetupMidnightTimer();
        }

        public ObservableCollection<ItemCalendarDayViewModel> Days => _days;
        public ObservableCollection<ItemCalendarEntryViewModel> MonthlySummary => _monthlySummary;
        public ObservableCollection<AbandonMonthlyStoneSummaryEntryViewModel> MonthlyAbandonSummary => _monthlyAbandonSummary;

        /// <summary>날짜 칸 아이템을 그림으로 볼지(true) 텍스트로 볼지(false).</summary>
        public bool UseIconDisplay
        {
            get => _settings.ItemCalendarUseIcons;
            set
            {
                if (_settings.ItemCalendarUseIcons == value) return;
                _settings.ItemCalendarUseIcons = value;
                OnPropertyChanged(nameof(UseIconDisplay));
                ConfigService.SaveDeferred(_settings);
            }
        }

        /// <summary>달력 본문 기준 폰트 크기 (헤더 슬라이더로 조절, 10~28).</summary>
        public double ItemCalendarFontSize
        {
            get => _settings.ItemCalendarFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(28.0, value));
                if (Math.Abs(_settings.ItemCalendarFontSize - clamped) < 0.0001) return;
                _settings.ItemCalendarFontSize = clamped;
                OnPropertyChanged(nameof(ItemCalendarFontSize));
                OnPropertyChanged(nameof(ItemCalendarFontSizeDisplay));
                ConfigService.SaveDeferred(_settings);
            }
        }

        public string ItemCalendarFontSizeDisplay => $"{Math.Round(ItemCalendarFontSize):0}px";

        public string MonthlyAbandonSeedText
        {
            get => _monthlyAbandonSeedText;
            private set
            {
                if (_monthlyAbandonSeedText == value)
                    return;
                _monthlyAbandonSeedText = value;
                OnPropertyChanged(nameof(MonthlyAbandonSeedText));
            }
        }

        public string MonthlyExperienceEssenceSeedText
        {
            get => _monthlyExperienceEssenceSeedText;
            private set
            {
                if (_monthlyExperienceEssenceSeedText == value)
                    return;
                _monthlyExperienceEssenceSeedText = value;
                OnPropertyChanged(nameof(MonthlyExperienceEssenceSeedText));
            }
        }

        private string _monthlySeedTotalText = string.Empty;
        public string MonthlySeedTotalText
        {
            get => _monthlySeedTotalText;
            private set
            {
                if (_monthlySeedTotalText == value)
                    return;
                _monthlySeedTotalText = value;
                OnPropertyChanged(nameof(MonthlySeedTotalText));
            }
        }

        private string _monthlySeedDetailText = string.Empty;
        public string MonthlySeedDetailText
        {
            get => _monthlySeedDetailText;
            private set
            {
                if (_monthlySeedDetailText == value)
                    return;
                _monthlySeedDetailText = value;
                OnPropertyChanged(nameof(MonthlySeedDetailText));
            }
        }

        /// <summary>
        /// 클리어 보상 시드 월 합계 — 주간 시작일(월요일)이 표시 중인 달에 속하는 주들을 통째로 합산한다
        /// (어밴던로드 주간 통계와 동일한 주 단위 기준).
        /// </summary>
        private async Task RefreshMonthlySeedSummaryAsync(DateTime monthStart)
        {
            try
            {
                DateTime monthEnd = monthStart.AddMonths(1).AddDays(-1);
                int diff = ((int)monthStart.DayOfWeek + 6) % 7;
                DateTime firstMonday = diff == 0 ? monthStart : monthStart.AddDays(7 - diff);

                long general = 0;
                long rubicona = 0;
                for (DateTime monday = firstMonday; monday <= monthEnd; monday = monday.AddDays(7))
                {
                    var (g, r) = await WeeklySeedRewardService.SumWeeklyClearSeedAsync(
                        _settings.ChatLogFolderPath, monday, monday.AddDays(6));
                    general += g;
                    rubicona += r;
                }

                if (_currentMonthStart != monthStart)
                    return;

                MonthlySeedTotalText = $"클리어 보상 시드 합계: {WeeklySeedRewardService.FormatSeed(general + rubicona)}";
                MonthlySeedDetailText = $"일반지역 {WeeklySeedRewardService.FormatSeed(general)} · 루비코나 {WeeklySeedRewardService.FormatSeed(rubicona)}";
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to refresh monthly seed summary.", ex);
            }
        }

        public string MonthText
        {
            get => _monthText;
            private set
            {
                if (_monthText == value)
                    return;
                _monthText = value;
                OnPropertyChanged(nameof(MonthText));
            }
        }

        public string StatusText
        {
            get => _statusText;
            private set
            {
                if (_statusText == value)
                    return;
                _statusText = value;
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public int LoadProgressValue
        {
            get => _loadProgressValue;
            private set
            {
                if (_loadProgressValue == value)
                    return;
                _loadProgressValue = value;
                OnPropertyChanged(nameof(LoadProgressValue));
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (_isLoading == value)
                    return;
                _isLoading = value;
                OnPropertyChanged(nameof(IsLoading));
            }
        }

        public Task LoadCurrentMonthAsync()
            => LoadMonthAsync(_currentMonthStart);

        public Task LoadCurrentWeekAsync()
            => LoadCurrentMonthAsync();

        public Task RefreshCurrentMonthAsync()
            => LoadMonthAsync(_currentMonthStart, forceReload: true);

        public bool IsMonthLoaded(DateTime monthStart)
            => _hasLoadedMonth && _loadedMonthStart == GetMonthStart(monthStart);

        private async Task LoadMonthAsync(DateTime monthStart, bool forceReload = false)
        {
            _ = forceReload;
            int version = ++_loadVersion;
            monthStart = GetMonthStart(monthStart);
            UpdateHeaderText(monthStart);
            SetLoadingState(true, "전용 로그를 불러오는 중...");

            (List<ItemLogSnapshotEntry> Snapshots, AbandonMonthlySummarySnapshotEntry AbandonSummary) data;
            try
            {
                data = await Task.Run(() => BuildMonthData(monthStart)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load month data from dedicated logs.", ex);
                if (version != _loadVersion)
                    return;

                Days.Clear();
                MonthlySummary.Clear();
                MonthlyAbandonSummary.Clear();
                MonthlyAbandonSeedText = "어밴던로드 누적 합계: 0 Seed";
                MonthlyExperienceEssenceSeedText = "경험의 정수 누적 합계: 0개";
                MonthlySeedTotalText = "클리어 보상 시드 합계: 0";
                MonthlySeedDetailText = string.Empty;
                SetLoadingState(false, "로그를 불러오지 못했습니다.");
                return;
            }

            if (version != _loadVersion)
                return;

            ApplyLoadedMonth(monthStart, data.Snapshots, data.AbandonSummary);
            SetLoadingState(false, string.Empty);
            _ = RefreshMonthlySeedSummaryAsync(monthStart);
        }

        /// <summary>
        /// 주간 득템 통계용: 기간 내 아이템 획득 스냅샷을 읽는다 (달력과 같은 아카이브 소스).
        /// </summary>
        internal static List<ItemLogSnapshotEntry> ReadItemSnapshotsForRange(DateTime startDate, DateTime endDate)
        {
            var result = new List<ItemLogSnapshotEntry>();
            if (!Directory.Exists(ItemDirectoryPath))
                return result;

            foreach (string path in Directory.EnumerateFiles(ItemDirectoryPath, "*.html").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var entry in ReadItemEntries(path))
                {
                    if (entry.LogDate.Date < startDate.Date || entry.LogDate.Date > endDate.Date)
                        continue;

                    var snapshot = CreateItemSnapshot(entry);
                    if (snapshot != null)
                        result.Add(snapshot);
                }
            }

            return result;
        }

        /// <summary>
        /// 주간 득템 통계용: 기간 내 경험의 정수 획득 개수를 합산한다 (달력과 같은 아카이브 소스).
        /// </summary>
        internal static long ReadExperienceEssenceCountForRange(DateTime startDate, DateTime endDate)
        {
            long total = 0;
            if (!Directory.Exists(ExpDirectoryPath))
                return total;

            foreach (string path in Directory.EnumerateFiles(ExpDirectoryPath, "*.html").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                foreach (var entry in ReadExpEntries(path))
                {
                    if (entry.LogDate.Date < startDate.Date || entry.LogDate.Date > endDate.Date)
                        continue;

                    if (TryExtractExperienceEssenceGain(entry.Text, out int gain))
                        total += gain;
                }
            }

            return total;
        }

        /// <summary>도움말/README 렌더 전용: 주어진 샘플 스냅샷으로 이번 달 달력을 구성한다. (파일 IO 없음)</summary>
        internal void ApplySampleMonthForRender(IReadOnlyList<ItemLogSnapshotEntry> snapshots)
        {
            DateTime monthStart = GetMonthStart(DateTime.Today);
            ApplyLoadedMonth(monthStart, snapshots, new AbandonMonthlySummarySnapshotEntry { MonthStart = monthStart });
            MonthlySeedTotalText = "클리어 보상 시드 합계: 376.15억";
            MonthlySeedDetailText = "일반지역 264.15억 · 루비코나 112억";
            SetLoadingState(false, string.Empty);
        }

        private (List<ItemLogSnapshotEntry> Snapshots, AbandonMonthlySummarySnapshotEntry AbandonSummary) BuildMonthData(DateTime monthStart)
        {
            var snapshots = new List<ItemLogSnapshotEntry>();
            var essenceCountByDate = new Dictionary<DateTime, int>();
            var AbandonSummary = new AbandonMonthlySummarySnapshotEntry { MonthStart = monthStart.Date };
            var weeklyAggregatedSummary = new AbandonMonthlySummarySnapshotEntry { MonthStart = monthStart.Date };
            DateTime monthEnd = monthStart.AddMonths(1).AddDays(-1).Date;
            Directory.CreateDirectory(AbandonDirectoryPath);
            if (Directory.Exists(ItemDirectoryPath))
            {
                foreach (string path in Directory.EnumerateFiles(ItemDirectoryPath, "*.html").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var entry in ReadItemEntries(path))
                    {
                        if (entry.LogDate.Year != monthStart.Year || entry.LogDate.Month != monthStart.Month)
                            continue;

                        var snapshot = CreateItemSnapshot(entry);
                        if (snapshot != null)
                            snapshots.Add(snapshot);
                    }
                }
            }

            if (Directory.Exists(AbandonDirectoryPath))
            {
                foreach (DateTime day in EachDay(monthStart, monthEnd))
                {
                    string summaryPath = Path.Combine(AbandonDirectoryPath, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + AbandonDaySummarySuffix);
                    if (TryLoadAbandonWeeklySummarySnapshot(summaryPath, out AbandonMonthlySummarySnapshotEntry daySummary))
                        AccumulateAbandonSummary(weeklyAggregatedSummary, daySummary);
                }
            }

            if (Directory.Exists(ExpDirectoryPath))
            {
                foreach (string path in Directory.EnumerateFiles(ExpDirectoryPath, "*.html").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var entry in ReadExpEntries(path))
                    {
                        DateTime day = entry.LogDate.Date;
                        if (day.Year != monthStart.Year || day.Month != monthStart.Month)
                            continue;

                        if (!TryExtractExperienceEssenceGain(entry.Text, out int gain))
                            continue;

                        essenceCountByDate[day] = essenceCountByDate.TryGetValue(day, out int prev) ? prev + gain : gain;
                    }
                }
            }

            lock (_todayLock)
            {
                _pendingExperienceEssenceByDate.Clear();
                foreach (var pair in essenceCountByDate)
                    _pendingExperienceEssenceByDate[pair.Key] = pair.Value;
            }

            // Weekly summary files are the source-of-truth for calendar aggregation.
            // If monthly summary lags behind, prefer the recomputed weekly aggregate.
            if (weeklyAggregatedSummary.TotalEntryFeeMan != 0 ||
                weeklyAggregatedSummary.Low != 0 ||
                weeklyAggregatedSummary.Mid != 0 ||
                weeklyAggregatedSummary.High != 0 ||
                weeklyAggregatedSummary.Top != 0)
            {
                AbandonSummary = weeklyAggregatedSummary;
            }

            return (snapshots, AbandonSummary);
        }

        private static IEnumerable<ContentEntry> ReadExpEntries(string path)
        {
            foreach (string line in File.ReadLines(path))
            {
                Match match = ExpEntryRegex.Match(line);
                if (!match.Success)
                    continue;

                var attrs = ParseAttributes(match.Groups["attrs"].Value);
                if (!TryGetLogDate(attrs, out DateTime date))
                    continue;

                string text = WebUtility.HtmlDecode(match.Groups["text"].Value).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                yield return new ContentEntry(date, text);
            }
        }

        private static bool TryExtractExperienceEssenceGain(string text, out int count)
        {
            count = 0;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            Match m = ExperienceEssenceGainRegex.Match(text);
            return int.TryParse(m.Groups["count"].Value, out count) && count > 0;
        }

        private static IEnumerable<ItemEntry> ReadItemEntries(string path)
        {
            foreach (string line in File.ReadLines(path))
            {
                Match match = ItemEntryRegex.Match(line);
                if (!match.Success)
                    continue;

                var attrs = ParseAttributes(match.Groups["attrs"].Value);
                if (!TryGetLogDate(attrs, out DateTime date))
                    continue;
                if (!attrs.TryGetValue("data-item-name", out string? itemName) || string.IsNullOrWhiteSpace(itemName))
                    continue;

                string gradeRaw = attrs.TryGetValue("data-item-grade", out string? g) ? g : "Normal";
                ItemDropGrade grade = ItemDropGrade.Normal;
                _ = Enum.TryParse(gradeRaw, ignoreCase: true, out grade);

                int count = 1;
                if (attrs.TryGetValue("data-item-count", out string? c) &&
                    int.TryParse(c, out int parsedCount) &&
                    parsedCount > 0)
                {
                    count = parsedCount;
                }

                string text = WebUtility.HtmlDecode(match.Groups["text"].Value).Trim();
                yield return new ItemEntry(date, text, itemName.Trim(), grade, count);
            }
        }

        private static IEnumerable<ContentEntry> ReadContentEntries(string path)
        {
            foreach (string line in File.ReadLines(path))
            {
                Match match = AbandonEntryRegex.Match(line);
                if (!match.Success)
                    continue;

                var attrs = ParseAttributes(match.Groups["attrs"].Value);
                if (!TryGetLogDate(attrs, out DateTime date))
                    continue;

                string text = WebUtility.HtmlDecode(match.Groups["text"].Value).Trim();
                if (string.IsNullOrWhiteSpace(text))
                    continue;

                yield return new ContentEntry(date, text);
            }
        }

        private static IEnumerable<ContentEntry> ReadAbandonEntries(string path)
            => ReadContentEntries(path);

        private static Dictionary<string, string> ParseAttributes(string attrsRaw)
        {
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in AttrRegex.Matches(attrsRaw ?? string.Empty))
            {
                string name = match.Groups["name"].Value.Trim();
                string value = WebUtility.HtmlDecode(match.Groups["value"].Value).Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    attrs[name] = value;
            }

            return attrs;
        }

        private static bool TryGetLogDate(IReadOnlyDictionary<string, string> attrs, out DateTime date)
        {
            date = DateTime.MinValue;
            if (!attrs.TryGetValue("data-date", out string? dateRaw) || string.IsNullOrWhiteSpace(dateRaw))
                return false;

            return DateTime.TryParseExact(
                dateRaw,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        private static ItemLogSnapshotEntry? CreateItemSnapshot(ItemEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.ItemName))
                return null;

            string displayName = ItemCalendarEntryViewModel.ApplyDisplayNameAlias(
                DropItemResolver.GetTrackedItemDisplayName(entry.ItemName));
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = entry.ItemName;

            if (IsMagicStoneItem(entry.ItemName) || IsMagicStoneItem(displayName))
                return null;

            return new ItemLogSnapshotEntry
            {
                Date = entry.LogDate.Date,
                ItemName = entry.ItemName,
                DisplayName = displayName,
                Grade = entry.ItemGrade,
                Count = Math.Max(1, entry.ItemCount),
                FormattedText = entry.Text
            };
        }

        private static bool IsMagicStoneItem(string? name)
            => !string.IsNullOrWhiteSpace(name) &&
               name.Contains("마정석", StringComparison.Ordinal);

        private void ApplyLoadedMonth(DateTime monthStart, IReadOnlyList<ItemLogSnapshotEntry> snapshots, AbandonMonthlySummarySnapshotEntry AbandonSummary)
        {
            _currentMonthStart = monthStart;
            _loadedMonthStart = monthStart;
            _hasLoadedMonth = true;
            _currentMonthAbandonSummary = new AbandonMonthlySummarySnapshotEntry
            {
                MonthStart = AbandonSummary.MonthStart,
                TotalEntryFeeMan = AbandonSummary.TotalEntryFeeMan,
                Low = AbandonSummary.Low,
                LowGain = AbandonSummary.LowGain,
                LowLoss = AbandonSummary.LowLoss,
                Mid = AbandonSummary.Mid,
                MidGain = AbandonSummary.MidGain,
                MidLoss = AbandonSummary.MidLoss,
                High = AbandonSummary.High,
                HighGain = AbandonSummary.HighGain,
                HighLoss = AbandonSummary.HighLoss,
                Top = AbandonSummary.Top,
                TopGain = AbandonSummary.TopGain,
                TopLoss = AbandonSummary.TopLoss
            };

            lock (_todayLock)
            {
                _currentMonthSnapshots.Clear();
                _currentMonthSnapshots.AddRange(snapshots);
            }

            ApplyDefaultFilteredMonthView(monthStart);
            UpdateMonthlyAbandonSummary(_currentMonthAbandonSummary);
            CacheTodaySnapshots(monthStart, snapshots);
            UpdateStatusForToday();
        }

        private void SetupMidnightTimer()
        {
            _midnightTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = GetTimeUntilNextMidnight()
            };
            _midnightTimer.Tick += MidnightTimer_Tick;
            _midnightTimer.Start();
        }

        private TimeSpan GetTimeUntilNextMidnight()
        {
            DateTime now = DateTime.Now;
            DateTime nextMidnight = now.Date.AddDays(1);
            TimeSpan remaining = nextMidnight - now;
            return remaining <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : remaining;
        }

        private void MidnightTimer_Tick(object? sender, EventArgs e)
        {
            if (_midnightTimer == null)
                return;

            _midnightTimer.Interval = GetTimeUntilNextMidnight();
            if (_currentMonthStart.Year == DateTime.Today.Year && _currentMonthStart.Month == DateTime.Today.Month)
            {
                _ = LoadCurrentMonthAsync();
            }
        }

        private void ApplyDefaultFilteredMonthView(DateTime monthStart)
        {
            List<ItemLogSnapshotEntry> allSnapshots;
            lock (_todayLock)
            {
                allSnapshots = _currentMonthSnapshots.ToList();
            }

            var filtered = FilterSnapshotsForCurrentView(allSnapshots);
            var days = BuildMonthDays(monthStart, filtered);

            Days.Clear();
            foreach (var day in days)
                Days.Add(day);

            UpdateMonthlySummary(filtered);
        }

        private List<ItemLogSnapshotEntry> FilterSnapshotsForCurrentView(IEnumerable<ItemLogSnapshotEntry> snapshots)
        {
            return snapshots.ToList();
        }

        private IReadOnlyList<ItemCalendarDayViewModel> BuildMonthDays(DateTime monthStart, IReadOnlyList<ItemLogSnapshotEntry> snapshots)
        {
            DateTime gridStart = GetGridStart(monthStart);
            var snapshotsByDate = snapshots
                .GroupBy(snapshot => snapshot.Date.Date)
                .ToDictionary(group => group.Key, group => group.ToList());

            var days = new List<ItemCalendarDayViewModel>(42);
            for (int offset = 0; offset < 42; offset++)
            {
                DateTime date = gridStart.AddDays(offset);
                bool isCurrentMonth = date.Month == monthStart.Month && date.Year == monthStart.Year;

                snapshotsByDate.TryGetValue(date.Date, out List<ItemLogSnapshotEntry>? daySnapshots);
                int essenceCount = 0;
                lock (_todayLock)
                {
                    _pendingExperienceEssenceByDate.TryGetValue(date.Date, out essenceCount);
                }
                days.Add(BuildDay(date, isCurrentMonth, daySnapshots ?? new List<ItemLogSnapshotEntry>(), essenceCount));
            }

            return days;
        }

        private void UpdateMonthlySummary(IReadOnlyList<ItemLogSnapshotEntry> snapshots)
        {
            var summary = snapshots
                .Where(snapshot => !string.IsNullOrWhiteSpace(snapshot.ItemName) || !string.IsNullOrWhiteSpace(snapshot.DisplayName))
                .GroupBy(snapshot => string.IsNullOrWhiteSpace(snapshot.ItemName) ? snapshot.DisplayName! : snapshot.ItemName!)
                .Select(group =>
                {
                    int totalCount = group.Sum(item => Math.Max(1, item.Count));
                    ItemDropGrade bestGrade = group.MaxBy(item => GetGradeSortOrder(item.Grade))?.Grade ?? ItemDropGrade.Normal;
                    string displayName = group.Key;
                    return new ItemCalendarEntryViewModel(displayName, bestGrade, totalCount);
                })
                .OrderByDescending(entry => GetGradeSortOrder(entry.Grade))
                .ThenByDescending(entry => entry.Count)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            MonthlySummary.Clear();
            foreach (var entry in summary)
                MonthlySummary.Add(entry);
            RefreshMonthlyExperienceEssenceSummary();
        }

        private void RefreshMonthlyExperienceEssenceSummary()
        {
            long totalExperienceEssenceCount = Days.Sum(day => (long)day.ExperienceEssenceCount);
            MonthlyExperienceEssenceSeedText = $"경험의 정수 누적 합계: {totalExperienceEssenceCount:N0}개";
        }

        private void CacheTodaySnapshots(DateTime monthStart, IReadOnlyList<ItemLogSnapshotEntry> snapshots)
        {
            DateTime today = DateTime.Today;
            if (monthStart.Year != today.Year || monthStart.Month != today.Month)
                return;

            lock (_todayLock)
            {
                _loadedTodayDate = today;
                _todaySnapshots.Clear();
                _todaySnapshots.AddRange(snapshots.Where(snapshot => snapshot.Date.Date == today));
            }
        }

        private void UpdateMonthlyAbandonSummary(AbandonMonthlySummarySnapshotEntry summary)
        {
            MonthlyAbandonSeedText = $"어밴던로드 누적 합계: {FormatManAmount(summary.NetProfitMan)}";

            MonthlyAbandonSummary.Clear();
            MonthlyAbandonSummary.Add(new AbandonMonthlyStoneSummaryEntryViewModel("하급 마정석", LowMagicStoneIconUri, summary.Low));
            MonthlyAbandonSummary.Add(new AbandonMonthlyStoneSummaryEntryViewModel("중급 마정석", MiddleMagicStoneIconUri, summary.Mid));
            MonthlyAbandonSummary.Add(new AbandonMonthlyStoneSummaryEntryViewModel("상급 마정석", HighMagicStoneIconUri, summary.High));
            MonthlyAbandonSummary.Add(new AbandonMonthlyStoneSummaryEntryViewModel("최상급 마정석", TopMagicStoneIconUri, summary.Top));
            long lowLossCount = Math.Abs(summary.LowLoss);
            long derivedLowLoss = Math.Max(0, summary.LowGain - summary.Low);
            if (derivedLowLoss > 0 && lowLossCount != derivedLowLoss)
                lowLossCount = derivedLowLoss;
            else if (lowLossCount == 0 && summary.Low < 0)
                lowLossCount = Math.Abs(summary.Low);
            MonthlyAbandonSummary.Add(new AbandonMonthlyStoneSummaryEntryViewModel(
                "누에게 빼앗긴 마정석",
                LowMagicStoneIconUri,
                lowLossCount,
                "#FF6B6B",
                "#FF6B6B",
                $"{lowLossCount:N0}개"));
        }

        private ItemCalendarDayViewModel BuildDay(DateTime date, bool isCurrentMonth, IReadOnlyCollection<ItemLogSnapshotEntry> snapshots, int experienceEssenceCount)
        {
            var aggregated = new Dictionary<(string Name, ItemDropGrade Grade), int>();

            foreach (var snapshot in snapshots)
            {
                // 전용 아이콘이 있는 아이템은 묶음명(abbr) 대신 실제 아이템명으로 표시
                string displayName = ItemCalendarEntryViewModel.ResolvePreferredDisplayName(snapshot.ItemName, snapshot.DisplayName);

                var key = (displayName, snapshot.Grade);
                int addCount = Math.Max(1, snapshot.Count);
                aggregated[key] = aggregated.TryGetValue(key, out int count) ? count + addCount : addCount;
            }

            var entries = aggregated
                .Select(pair => new ItemCalendarEntryViewModel(pair.Key.Name, pair.Key.Grade, pair.Value))
                .OrderByDescending(entry => GetGradeSortOrder(entry.Grade))
                .ThenByDescending(entry => entry.Count)
                .ThenBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var day = new ItemCalendarDayViewModel(date, isCurrentMonth, entries);
            day.ExperienceEssenceCount = experienceEssenceCount;
            return day;
        }

        public void ApplyRealtimeItemLog(LogParser.ParseResult itemLog, DateTime date)
        {
            if (itemLog == null || !itemLog.IsTrackedItemDrop || string.IsNullOrWhiteSpace(itemLog.FormattedText))
                return;

            date = date.Date;
            if (date != DateTime.Today)
                return;

            if (_currentMonthStart.Year != date.Year || _currentMonthStart.Month != date.Month)
                return;

            var snapshot = new ItemLogSnapshotEntry
            {
                Date = date,
                ItemName = itemLog.TrackedItemName,
                DisplayName = string.IsNullOrWhiteSpace(itemLog.TrackedItemName)
                    ? "아이템"
                    : ItemCalendarEntryViewModel.ApplyDisplayNameAlias(
                        DropItemResolver.GetTrackedItemDisplayName(itemLog.TrackedItemName)),
                Grade = itemLog.TrackedItemGrade,
                Count = Math.Max(1, itemLog.TrackedItemCount),
                FormattedText = itemLog.FormattedText
            };

            lock (_todayLock)
            {
                if (_loadedTodayDate != date)
                {
                    _todaySnapshots.Clear();
                    _loadedTodayDate = date;
                }

                _todaySnapshots.Add(snapshot);
                _currentMonthSnapshots.Add(snapshot);
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplySnapshotToToday(snapshot);
                List<ItemLogSnapshotEntry> monthSnapshots;
                lock (_todayLock)
                {
                    monthSnapshots = _currentMonthSnapshots.ToList();
                }

                UpdateMonthlySummary(FilterSnapshotsForCurrentView(monthSnapshots));
                UpdateStatusForToday();
            }), DispatcherPriority.Background);
        }

        private void ApplySnapshotToToday(ItemLogSnapshotEntry snapshot)
        {
            var todayCell = Days.FirstOrDefault(day => day.Date.Date == snapshot.Date.Date);
            if (todayCell == null)
                return;

            todayCell.AddSnapshot(new ItemCalendarEntryViewModel(
                ItemCalendarEntryViewModel.ResolvePreferredDisplayName(snapshot.ItemName, snapshot.DisplayName),
                snapshot.Grade,
                Math.Max(1, snapshot.Count)));
        }

        public void ApplyRealtimeAbandonLog(string formattedText, DateTime date)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
                return;

            date = date.Date;
            if (_currentMonthStart.Year != date.Year || _currentMonthStart.Month != date.Month)
                return;
            // Avoid double counting: month totals are loaded from persisted daily summaries.
            // Realtime abandon lines can overlap with data already reflected in summary files.
        }

        public void ApplyRealtimeExperienceEssenceLog(string formattedText, DateTime date)
        {
            if (!TryExtractExperienceEssenceGain(formattedText, out int gain))
                return;

            date = date.Date;
            if (_currentMonthStart.Year != date.Year || _currentMonthStart.Month != date.Month)
                return;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var day = Days.FirstOrDefault(d => d.Date.Date == date);
                if (day != null)
                {
                    day.ExperienceEssenceCount += gain;
                    RefreshMonthlyExperienceEssenceSummary();
                    return;
                }

                lock (_todayLock)
                {
                    _pendingExperienceEssenceByDate[date] = _pendingExperienceEssenceByDate.TryGetValue(date, out int prev)
                        ? prev + gain
                        : gain;
                }
            }), DispatcherPriority.Background);
        }

        private void UpdateStatusForToday()
        {
            int totalCount = Days.Sum(day => day.TotalCount);
            bool hasAbandonSummary =
                _currentMonthAbandonSummary.TotalEntryFeeMan != 0 ||
                _currentMonthAbandonSummary.Low != 0 ||
                _currentMonthAbandonSummary.Mid != 0 ||
                _currentMonthAbandonSummary.High != 0 ||
                _currentMonthAbandonSummary.Top != 0;

            string status = totalCount > 0 || hasAbandonSummary
                ? $"{GetMonthSummaryLabel(_currentMonthStart)} 총 {totalCount:N0}개 획득"
                : $"{GetMonthSummaryLabel(_currentMonthStart)} 로그가 없어 Logs\\Abandon에 일별 요약이 생성되면 표시됩니다.";
            SetLoadingState(false, status);
        }

        private static bool TryGetWeekKeyFromPath(string path, out string weekKey)
        {
            weekKey = Path.GetFileNameWithoutExtension(path);
            return WeekKeyRegex.IsMatch(weekKey);
        }

        private static bool TryGetWeekDateRange(string weekKey, out DateTime weekStart, out DateTime weekEnd)
        {
            weekStart = DateTime.MinValue;
            weekEnd = DateTime.MinValue;

            Match match = WeekKeyRegex.Match(weekKey);
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups["year"].Value, out int year) ||
                !int.TryParse(match.Groups["week"].Value, out int week))
            {
                return false;
            }

            try
            {
                weekStart = ISOWeek.ToDateTime(year, week, DayOfWeek.Monday).Date;
                weekEnd = weekStart.AddDays(6);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryLoadAbandonWeeklySummarySnapshot(string path, out AbandonMonthlySummarySnapshotEntry summary)
        {
            summary = new AbandonMonthlySummarySnapshotEntry();
            if (!File.Exists(path))
                return false;

            try
            {
                string json = File.ReadAllText(path, Encoding.UTF8);
                var loaded = JsonSerializer.Deserialize<AbandonMonthlySummarySnapshotEntry>(json);
                if (loaded == null)
                    return false;

                summary = loaded;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void AccumulateAbandonSummary(AbandonMonthlySummarySnapshotEntry target, AbandonMonthlySummarySnapshotEntry delta)
        {
            target.TotalEntryFeeMan += delta.TotalEntryFeeMan;
            target.Low += delta.Low;
            target.LowGain += delta.LowGain;
            target.LowLoss += delta.LowLoss;
            target.Mid += delta.Mid;
            target.MidGain += delta.MidGain;
            target.MidLoss += delta.MidLoss;
            target.High += delta.High;
            target.HighGain += delta.HighGain;
            target.HighLoss += delta.HighLoss;
            target.Top += delta.Top;
            target.TopGain += delta.TopGain;
            target.TopLoss += delta.TopLoss;
        }
        private static IEnumerable<DateTime> EachDay(DateTime start, DateTime end)
        {
            for (DateTime day = start.Date; day <= end.Date; day = day.AddDays(1))
                yield return day;
        }

        private static DateTime GetMonthStart(DateTime date)
            => new(date.Year, date.Month, 1);

        private static DateTime GetGridStart(DateTime monthStart)
        {
            int diff = ((int)monthStart.DayOfWeek + 6) % 7;
            return monthStart.AddDays(-diff);
        }

        private static int GetGradeSortOrder(ItemDropGrade grade)
        {
            return grade switch
            {
                ItemDropGrade.Special => 2,
                ItemDropGrade.Rare => 1,
                _ => 0
            };
        }

        private void UpdateHeaderText(DateTime monthStart)
        {
            MonthText = monthStart.ToString("yyyy년 M월", CultureInfo.GetCultureInfo("ko-KR"));
        }

        private void SetLoadingState(bool isLoading, string statusText)
        {
            IsLoading = isLoading;
            StatusText = statusText;
            if (!isLoading)
                LoadProgressValue = 0;
        }

        private static string FormatManAmount(long totalMan)
        {
            if (totalMan == 0)
                return "0 Seed";

            string sign = totalMan < 0 ? "-" : string.Empty;
            long abs = Math.Abs(totalMan);
            long eok = abs / 10000;
            long man = abs % 10000;
            if (eok == 0)
                return $"{sign}{man:N0}만 Seed";

            return man == 0
                ? $"{sign}{eok:N0}억 Seed"
                : $"{sign}{eok:N0}억 {man:N0}만 Seed";
        }

        private static string GetMonthSummaryLabel(DateTime monthStart)
            => $"{monthStart.Month}월";

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private void PreviousMonth_Click(object sender, RoutedEventArgs e)
            => _ = LoadMonthAsync(_currentMonthStart.AddMonths(-1));

        private void NextMonth_Click(object sender, RoutedEventArgs e)
            => _ = LoadMonthAsync(_currentMonthStart.AddMonths(1));

        private void ThisMonth_Click(object sender, RoutedEventArgs e)
            => _ = LoadMonthAsync(DateTime.Today);

        private void Refresh_Click(object sender, RoutedEventArgs e)
            => _ = RefreshCurrentMonthAsync();

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.Tag is not string key)
                return;

            System.Windows.Point? anchor = null;
            try
            {
                // 버튼 오른쪽 살짝 위 지점 (장치 px → DIP 변환)
                var devicePoint = button.PointToScreen(new System.Windows.Point(button.ActualWidth + 8, -4));
                var source = PresentationSource.FromVisual(button);
                anchor = source?.CompositionTarget != null
                    ? source.CompositionTarget.TransformFromDevice.Transform(devicePoint)
                    : devicePoint;
            }
            catch { }

            HelpWindow.ShowTopic(key, this, anchor);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
            => Close();

        // 달력 창은 잠금 모드와 무관하게 항상 이동 가능
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Days.Count == 0)
                _ = LoadCurrentMonthAsync();
        }

        private sealed record ItemEntry(DateTime LogDate, string Text, string ItemName, ItemDropGrade ItemGrade, int ItemCount);
        private sealed record ContentEntry(DateTime LogDate, string Text);
    }
}
