using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class AbandonRoadSummaryWindow : OverlayWindowBase, INotifyPropertyChanged
    {
        private static readonly string AbandonDirectoryPath = LogStoragePaths.AbandonDirectory;

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
        private DateTime _displayWeekStart;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AbandonRoadSummaryWindow()
        {
            InitializeComponent();
            DataContext = this;
            // 1회성: StartAutoClose가 마감까지 남은 시간으로 Interval을 정해 건다
            _autoCloseTimer = new DispatcherTimer();
            _autoCloseTimer.Tick += AutoCloseTimer_Tick;
            _displayWeekStart = GetCurrentWeekRange().WeekStart;
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
            ArmAutoCloseTimer(TimeSpan.FromSeconds(clampedSeconds));
        }

        /// <summary>마감까지 남은 시간으로 1회성 타이머를 다시 건다. 이미 돌던 타이머는 멈추고 새 마감 기준으로 시작한다.</summary>
        private void ArmAutoCloseTimer(TimeSpan delay)
        {
            _autoCloseTimer.Stop();
            _autoCloseTimer.Interval = delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1);
            _autoCloseTimer.Start();
        }

        public bool IsAutoClosePending => !_isPreviewMode && _autoCloseTimer.IsEnabled && _autoCloseAtUtc > DateTime.UtcNow;

        /// <summary>표시 주를 이동한다 (이번 주 이후로는 이동 불가).</summary>
        private void ChangeWeek(int deltaWeeks)
        {
            DateTime currentWeekStart = GetCurrentWeekRange().WeekStart;
            DateTime target = _displayWeekStart.AddDays(7 * deltaWeeks);
            if (target > currentWeekStart)
                target = currentWeekStart;

            // 과거 주를 보는 동안에는 자동 닫힘을 멈춘다 (사용자가 조작 중)
            _autoCloseTimer.Stop();
            _autoCloseAtUtc = DateTime.MinValue;
            _ = LoadWeekAsync(target);
        }

        private void PrevWeek_Click(object sender, RoutedEventArgs e) => ChangeWeek(-1);

        private void NextWeek_Click(object sender, RoutedEventArgs e) => ChangeWeek(1);

        private async Task LoadWeekAsync(DateTime today)
        {
            int version = ++_loadVersion;
            var (weekStart, weekEnd) = GetCurrentWeekRange(today);
            _displayWeekStart = weekStart;
            NextWeekButton.IsEnabled = weekStart < GetCurrentWeekRange().WeekStart;
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

        public void UpdateSummary(AbandonMonthlySummarySnapshotEntry summary)
        {
            SummaryText = $"어밴던로드 주간 합계: {FormatManAmount(summary.NetProfitMan)}";

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
            // 실시간 갱신은 이번 주 데이터 — 과거 주를 보는 중이면 화면을 덮어쓰지 않는다
            if (_displayWeekStart != GetCurrentWeekRange().WeekStart)
                return;

            SummaryText = $"어밴던로드 주간 합계: {FormatManAmount(summary.NetProfitMan)}";

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

        // 위치 저장은 MainWindow가 닫힐 때 처리한다
        protected override bool PersistBoundsOnChange => false;

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => TryBeginDrag(e);

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
            if (_isPreviewMode || _autoCloseAtUtc == DateTime.MinValue)
            {
                _autoCloseTimer.Stop();
                return;
            }

            TimeSpan remaining = _autoCloseAtUtc - DateTime.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                // 타이머가 마감보다 조금 일찍 깨어난 경우 남은 만큼만 다시 기다린다
                ArmAutoCloseTimer(remaining);
                return;
            }

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
