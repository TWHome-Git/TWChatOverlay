using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class AbandonRoadSummaryWindow : Window, INotifyPropertyChanged
    {
        private static readonly string AbandonDirectoryPath = LogStoragePaths.AbandonDirectory;
        private static readonly Regex AbandonEntryRegex = new(
            "<div\\s+class=\"log\\s+Abandon\"(?<attrs>[^>]*)>(?<text>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AttrRegex = new(
            "(?<name>data-[a-z0-9\\-]+)=\"(?<value>[^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string LowMagicStoneIconUri = "pack://application:,,,/Data/images/Item/하급마정석.png";
        private const string MiddleMagicStoneIconUri = "pack://application:,,,/Data/images/Item/중급마정석.png";
        private const string HighMagicStoneIconUri = "pack://application:,,,/Data/images/Item/상급마정석.png";
        private const string TopMagicStoneIconUri = "pack://application:,,,/Data/images/Item/최상급마정석.png";
        private const string AbandonDaySummarySuffix = ".summary.day.json";

        private readonly ObservableCollection<AbandonMonthlyStoneSummaryEntryViewModel> _stoneEntries = new();
        private readonly DispatcherTimer _autoCloseTimer;
        private string _weekText = string.Empty;
        private string _summaryText = string.Empty;
        private bool _isLoading;
        private int _loadVersion;
        private bool _isPreviewMode;
        private DateTime _autoCloseAtUtc = DateTime.MinValue;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AbandonRoadSummaryWindow(ChatSettings settings, LogAnalysisService logAnalysisService)
        {
            _ = settings ?? throw new ArgumentNullException(nameof(settings));
            _ = logAnalysisService ?? throw new ArgumentNullException(nameof(logAnalysisService));

            InitializeComponent();
            DataContext = this;
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _autoCloseTimer.Tick += AutoCloseTimer_Tick;
            UpdateHeaderText(GetCurrentWeekRange());
        }

        public ObservableCollection<AbandonMonthlyStoneSummaryEntryViewModel> StoneEntries => _stoneEntries;

        public string WeekText
        {
            get => _weekText;
            private set
            {
                if (_weekText == value)
                    return;
                _weekText = value;
                OnPropertyChanged(nameof(WeekText));
            }
        }

        public string SummaryText
        {
            get => _summaryText;
            private set
            {
                if (_summaryText == value)
                    return;
                _summaryText = value;
                OnPropertyChanged(nameof(SummaryText));
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

        public Task LoadCurrentWeekAsync()
            => LoadWeekAsync(DateTime.Today);

        public void SetPreviewMode(bool isPreview)
        {
            _isPreviewMode = isPreview;
            if (isPreview)
            {
                _autoCloseTimer.Stop();
                _autoCloseAtUtc = DateTime.MinValue;
            }
        }

        public void StartAutoClose(int durationSeconds)
        {
            _isPreviewMode = false;

            if (durationSeconds <= 0)
            {
                _autoCloseTimer.Stop();
                _autoCloseAtUtc = DateTime.UtcNow;
                Close();
                return;
            }

            int clampedSeconds = Math.Max(1, Math.Min(300, durationSeconds));
            _autoCloseAtUtc = DateTime.UtcNow.AddSeconds(clampedSeconds);
            _autoCloseTimer.Start();
        }

        public bool IsAutoClosePending => !_isPreviewMode && _autoCloseTimer.IsEnabled && _autoCloseAtUtc > DateTime.UtcNow;

        private async Task LoadWeekAsync(DateTime today)
        {
            int version = ++_loadVersion;
            var (weekStart, weekEnd) = GetCurrentWeekRange(today);
            UpdateHeaderText((weekStart, weekEnd));
            IsLoading = true;

            try
            {
                AbandonMonthlySummarySnapshotEntry summary = await Task.Run(() => BuildWeeklySummary(weekStart, weekEnd)).ConfigureAwait(true);
                if (version != _loadVersion)
                    return;
                UpdateSummary(summary);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load Abandon weekly summary popup.", ex);
                if (version != _loadVersion)
                    return;
                UpdateSummary(new AbandonMonthlySummarySnapshotEntry { MonthStart = weekStart.Date });
            }
            finally
            {
                if (version == _loadVersion)
                    IsLoading = false;
            }
        }

        private AbandonMonthlySummarySnapshotEntry BuildWeeklySummary(DateTime weekStart, DateTime weekEnd)
        {
            var summary = new AbandonMonthlySummarySnapshotEntry { MonthStart = weekStart.Date };
            if (!Directory.Exists(AbandonDirectoryPath))
                return summary;

            for (DateTime day = weekStart.Date; day <= weekEnd.Date; day = day.AddDays(1))
            {
                string dayPath = Path.Combine(AbandonDirectoryPath, day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + AbandonDaySummarySuffix);
                if (!TryLoadSummarySnapshot(dayPath, out AbandonMonthlySummarySnapshotEntry daySummary))
                    continue;

                summary.TotalEntryFeeMan += daySummary.TotalEntryFeeMan;
                summary.Low += daySummary.Low;
                summary.LowLoss += daySummary.LowLoss;
                summary.Mid += daySummary.Mid;
                summary.High += daySummary.High;
                summary.Top += daySummary.Top;
            }

            return summary;
        }

        private static bool TryLoadSummarySnapshot(string path, out AbandonMonthlySummarySnapshotEntry summary)
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
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to read Abandon weekly summary snapshot '{path}'.", ex);
                return false;
            }
        }

        private static void SaveSummarySnapshot(string path, AbandonMonthlySummarySnapshotEntry summary)
        {
            try
            {
                string json = JsonSerializer.Serialize(summary);
                File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to write Abandon weekly summary snapshot '{path}'.", ex);
            }
        }

        public void UpdateSummary(AbandonMonthlySummarySnapshotEntry summary)
        {
            SummaryText = $"어밴던로드 이번주 합계: {FormatManAmount(summary.NetProfitMan)}";

            StoneEntries.Clear();
            StoneEntries.Add(new AbandonMonthlyStoneSummaryEntryViewModel("하급 마정석", LowMagicStoneIconUri, summary.Low));
            StoneEntries.Add(new AbandonMonthlyStoneSummaryEntryViewModel("중급 마정석", MiddleMagicStoneIconUri, summary.Mid));
            StoneEntries.Add(new AbandonMonthlyStoneSummaryEntryViewModel("상급 마정석", HighMagicStoneIconUri, summary.High));
            StoneEntries.Add(new AbandonMonthlyStoneSummaryEntryViewModel("최상급 마정석", TopMagicStoneIconUri, summary.Top));
            long lowLossCount = Math.Abs(summary.LowLoss);
            if (lowLossCount == 0 && summary.Low < 0)
                lowLossCount = Math.Abs(summary.Low);
            StoneEntries.Add(new AbandonMonthlyStoneSummaryEntryViewModel(
                "누에게 빼앗긴 마정석",
                LowMagicStoneIconUri,
                lowLossCount,
                "#FF6B6B",
                "#FF6B6B",
                $"{lowLossCount:N0}개"));
        }

        public void UpdateSummary(AbandonSummaryValue summary)
        {
            SummaryText = $"어밴던로드 이번주 합계: {FormatManAmount(summary.NetProfitMan)}";

            StoneEntries.Clear();
            StoneEntries.Add(new AbandonMonthlyStoneSummaryEntryViewModel("하급 마정석", LowMagicStoneIconUri, summary.Low));
            StoneEntries.Add(new AbandonMonthlyStoneSummaryEntryViewModel("중급 마정석", MiddleMagicStoneIconUri, summary.Mid));
            StoneEntries.Add(new AbandonMonthlyStoneSummaryEntryViewModel("상급 마정석", HighMagicStoneIconUri, summary.High));
            StoneEntries.Add(new AbandonMonthlyStoneSummaryEntryViewModel("최상급 마정석", TopMagicStoneIconUri, summary.Top));
            long lowLossCount = Math.Abs(summary.LowLoss);
            if (lowLossCount == 0 && summary.Low < 0)
                lowLossCount = Math.Abs(summary.Low);
            StoneEntries.Add(new AbandonMonthlyStoneSummaryEntryViewModel(
                "누에게 빼앗긴 마정석",
                LowMagicStoneIconUri,
                lowLossCount,
                "#FF6B6B",
                "#FF6B6B",
                $"{lowLossCount:N0}개"));
        }

        private static bool TryParseAbandonEntry(string line, out DateTime logDate, out string text)
        {
            logDate = DateTime.MinValue;
            text = string.Empty;
            Match match = AbandonEntryRegex.Match(line);
            if (!match.Success)
                return false;

            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match attrMatch in AttrRegex.Matches(match.Groups["attrs"].Value))
            {
                string key = attrMatch.Groups["name"].Value.Trim();
                string value = WebUtility.HtmlDecode(attrMatch.Groups["value"].Value).Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    attrs[key] = value;
            }

            if (!attrs.TryGetValue("data-date", out string? dateRaw) ||
                !DateTime.TryParseExact(dateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out logDate))
            {
                return false;
            }

            text = WebUtility.HtmlDecode(match.Groups["text"].Value).Trim();
            return !string.IsNullOrWhiteSpace(text);
        }

        private static string GetIsoWeekKey(DateTime date)
        {
            int isoYear = ISOWeek.GetYear(date);
            int isoWeek = ISOWeek.GetWeekOfYear(date);
            return $"{isoYear}-W{isoWeek:00}";
        }

        private static (DateTime WeekStart, DateTime WeekEnd) GetCurrentWeekRange(DateTime? now = null)
        {
            DateTime current = (now ?? DateTime.Today).Date;
            int diff = (7 + (int)current.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            DateTime weekStart = current.AddDays(-diff);
            DateTime weekEnd = weekStart.AddDays(6);
            return (weekStart, weekEnd);
        }

        private void UpdateHeaderText((DateTime WeekStart, DateTime WeekEnd) range)
        {
            WeekText = $"{range.WeekStart:yyyy.MM.dd} ~ {range.WeekEnd:yyyy.MM.dd}";
        }

        private static string FormatManAmount(long totalMan)
        {
            string sign = totalMan < 0 ? "-" : string.Empty;
            long abs = Math.Abs(totalMan);
            long eok = abs / 10000;
            long man = abs % 10000;
            return $"{sign}{eok:N0}억 {man:N0}만";
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _autoCloseTimer.Stop();
            base.OnClosed(e);
        }

        private void AutoCloseTimer_Tick(object? sender, EventArgs e)
        {
            if (_isPreviewMode)
                return;

            if (_autoCloseAtUtc == DateTime.MinValue)
                return;

            if (DateTime.UtcNow < _autoCloseAtUtc)
                return;

            _autoCloseTimer.Stop();
            Close();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = LoadCurrentWeekAsync();
        }

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
