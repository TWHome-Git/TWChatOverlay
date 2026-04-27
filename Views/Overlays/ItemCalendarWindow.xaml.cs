using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class ItemCalendarWindow : Window, INotifyPropertyChanged
    {
        private static readonly string ItemLogDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Itemlog");
        private const string LowMagicStoneIconUri = "pack://application:,,,/Data/images/Item/하급마정석.png";
        private const string MiddleMagicStoneIconUri = "pack://application:,,,/Data/images/Item/중급마정석.png";
        private const string HighMagicStoneIconUri = "pack://application:,,,/Data/images/Item/상급마정석.png";
        private const string TopMagicStoneIconUri = "pack://application:,,,/Data/images/Item/최상급마정석.png";

        private static readonly Regex AbaddonEntryFeeRegex = new(
            @"입장료\s*(?<value>[\d,]+)\s*만\s*Seed",
            RegexOptions.Compiled);
        private static readonly Regex MagicStoneGainRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개",
            RegexOptions.Compiled);
        private static readonly Regex MagicStoneLossRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개를\s*빼앗겼습니다",
            RegexOptions.Compiled);

        private readonly ChatSettings _settings;
        private readonly LogAnalysisService _logAnalysisService;
        private readonly ObservableCollection<ItemCalendarDayViewModel> _days = new();
        private readonly ObservableCollection<ItemCalendarEntryViewModel> _monthlySummary = new();
        private readonly ObservableCollection<AbaddonMonthlyStoneSummaryEntryViewModel> _monthlyAbaddonSummary = new();
        private DateTime _currentMonthStart;
        private DateTime _loadedAbaddonMonthStart = DateTime.MinValue;
        private bool _isLoading;
        private string _monthText = string.Empty;
        private string _statusText = string.Empty;
        private string _monthlyAbaddonSeedText = string.Empty;
        private int _loadVersion;

        public event PropertyChangedEventHandler? PropertyChanged;

        public ItemCalendarWindow(ChatSettings settings, LogAnalysisService logAnalysisService)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logAnalysisService = logAnalysisService ?? throw new ArgumentNullException(nameof(logAnalysisService));

            InitializeComponent();
            DataContext = this;

            _currentMonthStart = GetMonthStart(DateTime.Today);
            UpdateHeaderText(_currentMonthStart);
            SetLoadingState(true, "이번달 아이템 로그를 불러오는 중...");
        }

        public ObservableCollection<ItemCalendarDayViewModel> Days => _days;

        public ObservableCollection<ItemCalendarEntryViewModel> MonthlySummary => _monthlySummary;

        public ObservableCollection<AbaddonMonthlyStoneSummaryEntryViewModel> MonthlyAbaddonSummary => _monthlyAbaddonSummary;

        public string MonthlyAbaddonSeedText
        {
            get => _monthlyAbaddonSeedText;
            private set
            {
                if (_monthlyAbaddonSeedText == value)
                    return;

                _monthlyAbaddonSeedText = value;
                OnPropertyChanged(nameof(MonthlyAbaddonSeedText));
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

        private async Task LoadMonthAsync(DateTime monthStart)
        {
            int version = ++_loadVersion;
            monthStart = GetMonthStart(monthStart);

            SetLoadingState(true, "이번달 아이템 로그를 불러오는 중...");
            UpdateHeaderText(monthStart);

            IReadOnlyList<ItemCalendarDayViewModel> days;
            IReadOnlyList<ItemLogSnapshotEntry> snapshots;
            AbaddonMonthlySummarySnapshotEntry abaddonSummary;
            try
            {
                var monthlyTask = Task.Run(() => MonthlyReadableLogExportService.LoadOrBuildMonth(monthStart, _settings.ChatLogFolderPath, _logAnalysisService));

                var monthlyData = await monthlyTask.ConfigureAwait(true);
                snapshots = monthlyData.ItemSnapshots;
                abaddonSummary = monthlyData.AbaddonSummary;
                days = await Task.Run(() => BuildMonthDays(monthStart, snapshots)).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load item calendar month.", ex);
                if (version != _loadVersion)
                    return;

                Days.Clear();
                SetLoadingState(false, "이번달 로그를 불러오지 못했습니다.");
                return;
            }

            if (version != _loadVersion)
                return;

            _currentMonthStart = monthStart;
            Days.Clear();
            foreach (var day in days)
                Days.Add(day);

            UpdateMonthlySummary(snapshots);
            UpdateMonthlyAbaddonSummary(abaddonSummary);

            int totalCount = Days.Sum(day => day.TotalCount);
            string status = totalCount > 0
                ? $"{GetMonthSummaryLabel(monthStart)} 총 {totalCount:N0}개 획득"
                : $"{GetMonthSummaryLabel(monthStart)}에 아이템 기록이 없습니다.";
            SetLoadingState(false, status);
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
                days.Add(BuildDay(date, isCurrentMonth, daySnapshots ?? new List<ItemLogSnapshotEntry>()));
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
        }

        private void UpdateMonthlyAbaddonSummary(AbaddonMonthlySummarySnapshotEntry summary)
        {
            MonthlyAbaddonSeedText = $"어밴던로드 시드 누적합계: {FormatManAmount(summary.NetProfitMan)}";

            MonthlyAbaddonSummary.Clear();
            MonthlyAbaddonSummary.Add(new AbaddonMonthlyStoneSummaryEntryViewModel("하급 마정석", LowMagicStoneIconUri, summary.Low));
            MonthlyAbaddonSummary.Add(new AbaddonMonthlyStoneSummaryEntryViewModel("중급 마정석", MiddleMagicStoneIconUri, summary.Mid));
            MonthlyAbaddonSummary.Add(new AbaddonMonthlyStoneSummaryEntryViewModel("상급 마정석", HighMagicStoneIconUri, summary.High));
            MonthlyAbaddonSummary.Add(new AbaddonMonthlyStoneSummaryEntryViewModel("최상급 마정석", TopMagicStoneIconUri, summary.Top));
        }

        private ItemCalendarDayViewModel BuildDay(DateTime date, bool isCurrentMonth, IReadOnlyCollection<ItemLogSnapshotEntry> snapshots)
        {
            var aggregated = new Dictionary<(string Name, ItemDropGrade Grade), int>();

            foreach (var snapshot in snapshots)
            {
                string displayName = string.IsNullOrWhiteSpace(snapshot.DisplayName)
                    ? snapshot.ItemName ?? "아이템"
                    : snapshot.DisplayName;

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

            return new ItemCalendarDayViewModel(date, isCurrentMonth, entries);
        }

        private AbaddonMonthlySummarySnapshotEntry LoadOrBuildMonthlyAbaddonSummary(DateTime monthStart)
        {
            return MonthlyReadableLogExportService.LoadOrBuildMonth(monthStart, _settings.ChatLogFolderPath, _logAnalysisService).AbaddonSummary;
        }

        private bool IsMonthlyAbaddonSummaryStale(string summaryPath, DateTime monthStart)
        {
            if (string.IsNullOrWhiteSpace(_settings.ChatLogFolderPath) ||
                !Directory.Exists(_settings.ChatLogFolderPath))
            {
                return false;
            }

            DateTime summaryWriteTimeUtc = File.GetLastWriteTimeUtc(summaryPath);
            foreach (string chatLogPath in Directory.EnumerateFiles(_settings.ChatLogFolderPath, "TWChatLog_*.html"))
            {
                DateTime logDate = ExtractDateFromChatLogPath(chatLogPath);
                if (logDate == DateTime.MinValue ||
                    logDate.Year != monthStart.Year ||
                    logDate.Month != monthStart.Month)
                {
                    continue;
                }

                if (File.GetLastWriteTimeUtc(chatLogPath) > summaryWriteTimeUtc)
                    return true;
            }

            return false;
        }

        private AbaddonMonthlySummarySnapshotEntry BuildMonthlyAbaddonSummaryFromChatLogs(DateTime monthStart)
        {
            var summary = new AbaddonMonthlySummarySnapshotEntry { MonthStart = monthStart.Date };

            if (string.IsNullOrWhiteSpace(_settings.ChatLogFolderPath) ||
                !Directory.Exists(_settings.ChatLogFolderPath))
            {
                return summary;
            }

            foreach (string chatLogPath in Directory.EnumerateFiles(_settings.ChatLogFolderPath, "TWChatLog_*.html")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                DateTime logDate = ExtractDateFromChatLogPath(chatLogPath);
                if (logDate == DateTime.MinValue ||
                    logDate.Year != monthStart.Year ||
                    logDate.Month != monthStart.Month)
                {
                    continue;
                }

                try
                {
                    using var stream = new FileStream(chatLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream, Encoding.GetEncoding(949));
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var analysis = _logAnalysisService.Analyze(line, isRealTime: false);
                        if (!analysis.IsSuccess)
                            continue;

                        TryAccumulateAbaddonMonthlySummary(analysis.Parsed.FormattedText, summary);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to rebuild monthly Abaddon summary from '{chatLogPath}'.", ex);
                }
            }

            return summary;
        }

        private static void SaveMonthlyAbaddonSummary(DateTime monthStart, AbaddonMonthlySummarySnapshotEntry summary)
        {
            try
            {
                Directory.CreateDirectory(ItemLogDirectoryPath);
                string path = GetMonthlyAbaddonSummaryPath(monthStart);
                File.WriteAllText(path, JsonSerializer.Serialize(summary), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save monthly Abaddon summary snapshot.", ex);
            }
        }

        private static DateTime ExtractDateFromChatLogPath(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = Regex.Match(fileName, @"(?<y>\d{4})_(?<m>\d{2})_(?<d>\d{2})$");
            if (!match.Success)
                return DateTime.MinValue;

            if (!int.TryParse(match.Groups["y"].Value, out int year) ||
                !int.TryParse(match.Groups["m"].Value, out int month) ||
                !int.TryParse(match.Groups["d"].Value, out int day))
                return DateTime.MinValue;

            return new DateTime(year, month, day);
        }

        private static bool TryAccumulateAbaddonMonthlySummary(string formattedText, AbaddonMonthlySummarySnapshotEntry summary)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
                return false;

            string body = Regex.Replace(formattedText, @"^\[[^\]]+\]\s*", string.Empty);
            if (body.Contains("주문을 통해", StringComparison.Ordinal))
                return false;

            var feeMatch = AbaddonEntryFeeRegex.Match(body);
            if (feeMatch.Success && TryParseLong(feeMatch.Groups["value"].Value, out long feeMan))
            {
                summary.TotalEntryFeeMan += feeMan;
                return true;
            }

            var lossMatch = MagicStoneLossRegex.Match(body);
            if (lossMatch.Success && TryParseLong(lossMatch.Groups["count"].Value, out long lossCount))
            {
                ApplyMagicStoneDelta(summary, lossMatch.Groups["grade"].Value, -lossCount);
                return true;
            }

            var gainMatch = MagicStoneGainRegex.Match(body);
            if (gainMatch.Success &&
                body.Contains("획득", StringComparison.Ordinal) &&
                TryParseLong(gainMatch.Groups["count"].Value, out long gainCount))
            {
                ApplyMagicStoneDelta(summary, gainMatch.Groups["grade"].Value, gainCount);
                return true;
            }

            return false;
        }

        private static void ApplyMagicStoneDelta(AbaddonMonthlySummarySnapshotEntry summary, string grade, long delta)
        {
            switch (grade)
            {
                case "하급":
                    summary.Low += delta;
                    break;
                case "중급":
                    summary.Mid += delta;
                    break;
                case "상급":
                    summary.High += delta;
                    break;
                case "최상급":
                    summary.Top += delta;
                    break;
            }
        }

        private static bool TryParseLong(string raw, out long value)
            => long.TryParse(raw.Replace(",", string.Empty).Trim(), out value);

        private static string GetMonthlyAbaddonSummaryPath(DateTime date)
            => Path.Combine(ItemLogDirectoryPath, $"AbaddonSummary_{date:yyyy_MM}.json");

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

        private void Close_Click(object sender, RoutedEventArgs e)
            => Close();

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
    }
}
