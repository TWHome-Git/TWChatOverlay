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
    public partial class AbandonRoadSummaryWindow : ToastWindowBase, INotifyPropertyChanged
    {
        private static readonly string AbandonDirectoryPath = LogStoragePaths.AbandonDirectory;

        private const string AbandonDaySummarySuffix = ".summary.day.json";

        private readonly ObservableCollection<AbandonMonthlyStoneSummaryEntryViewModel> _stoneEntries = new();
        private string _weekText = string.Empty;
        private string _summaryText = string.Empty;
        private bool _isLoading;
        private int _loadVersion;
        private bool _isPreviewMode;
        private DateTime _displayWeekStart;

        public event PropertyChangedEventHandler? PropertyChanged;

        public AbandonRoadSummaryWindow()
        {
            InitializeComponent();
            DataContext = this;
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
                CancelAutoClose();
        }

        /// <summary>표시 시간이 지나면 닫는다 (1~300초). 0 이하면 바로 닫는다.</summary>
        public new void StartAutoClose(int durationSeconds)
        {
            _isPreviewMode = false;

            if (durationSeconds <= 0)
            {
                CancelAutoClose();
                Close();
                return;
            }

            base.StartAutoClose(durationSeconds);
        }

        public bool IsAutoClosePending => !_isPreviewMode && IsAutoCloseArmed;

        /// <summary>시간이 다 되면 페이드 없이 바로 닫는다 (기존 동작). 미리보기는 닫지 않는다.</summary>
        protected override void OnAutoCloseElapsed()
        {
            if (_isPreviewMode)
                return;
            Close();
        }

        /// <summary>표시 주를 이동한다 (이번 주 이후로는 이동 불가).</summary>
        private void ChangeWeek(int deltaWeeks)
        {
            DateTime currentWeekStart = GetCurrentWeekRange().WeekStart;
            DateTime target = _displayWeekStart.AddDays(7 * deltaWeeks);
            if (target > currentWeekStart)
                target = currentWeekStart;

            // 과거 주를 보는 동안에는 자동 닫힘을 멈춘다 (사용자가 조작 중)
            CancelAutoClose();
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
            => FillSummary(summary.NetProfitMan, summary.Low, summary.Mid, summary.High, summary.Top, summary.LowLoss);

        public void UpdateSummary(AbandonSummaryValue summary)
        {
            // 실시간 갱신은 이번 주 데이터 — 과거 주를 보는 중이면 화면을 덮어쓰지 않는다
            if (_displayWeekStart != GetCurrentWeekRange().WeekStart)
                return;

            FillSummary(summary.NetProfitMan, summary.Low, summary.Mid, summary.High, summary.Top, summary.LowLoss);
        }

        private void FillSummary(long netProfitMan, long low, long mid, long high, long top, long lowLoss)
        {
            SummaryText = $"어밴던로드 주간 합계: {AbandonSummaryCalculator.FormatManAmount(netProfitMan)}";
            AbandonStoneRows.Fill(StoneEntries, low, mid, high, top, lowLoss);
        }

        private static (DateTime WeekStart, DateTime WeekEnd) GetCurrentWeekRange(DateTime? now = null)
        {
            DateTime weekStart = LogTextClassifier.GetWeekStart((now ?? DateTime.Today).Date);
            DateTime weekEnd = weekStart.AddDays(6);
            return (weekStart, weekEnd);
        }

        private void UpdateHeaderText((DateTime WeekStart, DateTime WeekEnd) range)
        {
            WeekText = $"{range.WeekStart:yyyy.MM.dd} ~ {range.WeekEnd:yyyy.MM.dd}";
        }

        // 위치는 드래그 끝·닫힘에 베이스 경로로 저장한다 (알림 베이스 기본은 꺼짐이라 닫힘 저장만 켠다)
        protected override bool PersistBoundsOnClose => true;

        protected override bool PersistBounds(ChatSettings settings)
        {
            settings.AbandonRoadSummaryWindowLeft = Left;
            settings.AbandonRoadSummaryWindowTop = Top;
            return true;
        }

        protected override (double? Left, double? Top, double? Width, double? Height)? StoredBounds(ChatSettings settings)
            => (settings.AbandonRoadSummaryWindowLeft, settings.AbandonRoadSummaryWindowTop, null, null);

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => TryBeginDrag(e);

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
