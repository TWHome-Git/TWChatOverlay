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
using TWChatOverlay.Services.LogAnalysis;

namespace TWChatOverlay.Views
{
    public partial class AbaddonRoadSummaryWindow : Window, INotifyPropertyChanged
    {
        private static readonly string ItemLogDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Itemlog");
        private static readonly Regex AbaddonEntryFeeRegex = new(
            @"입장료\s*(?<value>[\d,]+)\s*만\s*Seed",
            RegexOptions.Compiled);
        private static readonly Regex MagicStoneGainRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개",
            RegexOptions.Compiled);
        private static readonly Regex MagicStoneLossRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개를\s*빼앗겼습니다",
            RegexOptions.Compiled);

        private const string LowMagicStoneIconUri = "pack://application:,,,/Data/images/Item/하급마정석.png";
        private const string MiddleMagicStoneIconUri = "pack://application:,,,/Data/images/Item/중급마정석.png";
        private const string HighMagicStoneIconUri = "pack://application:,,,/Data/images/Item/상급마정석.png";
        private const string TopMagicStoneIconUri = "pack://application:,,,/Data/images/Item/최상급마정석.png";

        private readonly ChatSettings _settings;
        private readonly LogAnalysisService _logAnalysisService;
        private readonly ObservableCollection<AbaddonMonthlyStoneSummaryEntryViewModel> _stoneEntries = new();
        private string _weekText = string.Empty;
        private string _summaryText = string.Empty;
        private bool _isLoading;
        private int _loadVersion;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AbaddonRoadSummaryWindow(ChatSettings settings, LogAnalysisService logAnalysisService)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logAnalysisService = logAnalysisService ?? throw new ArgumentNullException(nameof(logAnalysisService));

            InitializeComponent();
            DataContext = this;
            UpdateHeaderText(GetCurrentWeekRange());
        }

        public ObservableCollection<AbaddonMonthlyStoneSummaryEntryViewModel> StoneEntries => _stoneEntries;

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

        private async Task LoadWeekAsync(DateTime today)
        {
            int version = ++_loadVersion;
            var (weekStart, weekEnd) = GetCurrentWeekRange(today);
            UpdateHeaderText((weekStart, weekEnd));
            IsLoading = true;

            try
            {
                AbaddonMonthlySummarySnapshotEntry summary = await Task.Run(() => BuildWeeklySummary(weekStart, today)).ConfigureAwait(true);

                if (version != _loadVersion)
                    return;

                UpdateSummary(summary);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load abaddon weekly summary popup.", ex);
                if (version != _loadVersion)
                    return;

                UpdateSummary(new AbaddonMonthlySummarySnapshotEntry { MonthStart = weekStart.Date });
            }
            finally
            {
                if (version == _loadVersion)
                    IsLoading = false;
            }
        }

        private AbaddonMonthlySummarySnapshotEntry BuildWeeklySummary(DateTime weekStart, DateTime today)
        {
            var summary = new AbaddonMonthlySummarySnapshotEntry { MonthStart = weekStart.Date };

            foreach (string chatLogPath in GetWeekLogPaths(weekStart, today))
            {
                if (!File.Exists(chatLogPath))
                    continue;

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

                        TryAccumulateWeeklySummary(analysis.Parsed.FormattedText, summary);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to scan weekly abaddon summary from '{chatLogPath}'.", ex);
                }
            }

            return summary;
        }

        private void UpdateSummary(AbaddonMonthlySummarySnapshotEntry summary)
        {
            SummaryText = $"어밴던로드 이번주 합계: {FormatManAmount(summary.NetProfitMan)}";

            StoneEntries.Clear();
            StoneEntries.Add(new AbaddonMonthlyStoneSummaryEntryViewModel("하급 마정석", LowMagicStoneIconUri, summary.Low));
            StoneEntries.Add(new AbaddonMonthlyStoneSummaryEntryViewModel("중급 마정석", MiddleMagicStoneIconUri, summary.Mid));
            StoneEntries.Add(new AbaddonMonthlyStoneSummaryEntryViewModel("상급 마정석", HighMagicStoneIconUri, summary.High));
            StoneEntries.Add(new AbaddonMonthlyStoneSummaryEntryViewModel("최상급 마정석", TopMagicStoneIconUri, summary.Top));
        }

        private static bool TryAccumulateWeeklySummary(string formattedText, AbaddonMonthlySummarySnapshotEntry summary)
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

        private IEnumerable<string> GetWeekLogPaths(DateTime weekStart, DateTime today)
        {
            for (int i = 0; i <= (today.Date - weekStart.Date).Days; i++)
            {
                DateTime date = weekStart.AddDays(i);
                yield return Path.Combine(_settings.ChatLogFolderPath, $"TWChatLog_{date:yyyy_MM_dd}.html");
            }
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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = LoadCurrentWeekAsync();
        }

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
