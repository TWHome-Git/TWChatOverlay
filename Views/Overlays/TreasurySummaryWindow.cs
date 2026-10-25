using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 심연의 보물창고 주간 통계 창: 이번 주 1~7회차의 금화 주머니 획득 수와 합계·평균을 보여준다.
    /// 어밴던로드 통계창과 같은 위치·크기 계열로 표시되며, 세션(입장 후 2분 30초)이 끝나고
    /// 잠시 뒤 자동으로 닫힌다.
    /// </summary>
    public sealed class TreasurySummaryWindow : ToastWindowBase
    {
        private const int MaxRuns = 7;
        private const double DefaultWidth = 384;
        private const double DefaultTop = 160;
        private const string SeedPouchIconUri = "pack://application:,,,/Data/images/Item/시드.png";
        private static readonly TimeSpan AutoCloseDelay = TimeSpan.FromSeconds(30); // 마지막 획득 후 30초 무획득이면 닫힘

        private static TreasurySummaryWindow? _instance;

        private readonly TextBlock[] _runValues = new TextBlock[MaxRuns];
        private readonly TextBlock[] _runLabels = new TextBlock[MaxRuns];
        private readonly Image[] _runIcons = new Image[MaxRuns];
        private readonly TextBlock _totalText;
        private readonly TextBlock _averageText;
        private readonly ChatSettings _settings;
        private bool _isPreview;
        private bool _isManual; // 바로가기 버튼으로 연 경우 — 자동 닫힘 없음

        private TreasurySummaryWindow(ChatSettings settings)
        {
            _settings = settings;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Width = DefaultWidth;
            SizeToContent = SizeToContent.Height;

            var titleStack = new StackPanel();
            titleStack.Children.Add(OverlayChrome.MakeTitle("심연의 보물창고"));
            titleStack.Children.Add(OverlayChrome.MakeSubtitle("이번 주 금화 주머니 획득"));
            var header = OverlayChrome.MakeHeader(titleStack, new Thickness(0, 0, 0, 8),
                OverlayChrome.MakeCloseButton(() => Close()));

            var body = new StackPanel();
            body.Children.Add(header);

            for (int i = 0; i < MaxRuns; i++)
            {
                var row = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };
                _runValues[i] = new TextBlock
                {
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _runValues[i].SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

                // 개수 앞 금화 주머니 아이콘 (기록 없는 회차에서는 숨김)
                _runIcons[i] = new Image
                {
                    Width = 16,
                    Height = 16,
                    Margin = new Thickness(0, 0, 5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed,
                };
                RenderOptions.SetBitmapScalingMode(_runIcons[i], BitmapScalingMode.NearestNeighbor);
                try { _runIcons[i].Source = PackImageCache.Get(SeedPouchIconUri); } catch { }

                var valuePanel = new StackPanel { Orientation = Orientation.Horizontal };
                valuePanel.Children.Add(_runIcons[i]);
                valuePanel.Children.Add(_runValues[i]);
                DockPanel.SetDock(valuePanel, Dock.Right);
                row.Children.Add(valuePanel);

                _runLabels[i] = new TextBlock { Text = $"{i + 1}회차", FontSize = 13 };
                _runLabels[i].SetResourceReference(TextBlock.ForegroundProperty, "OverlayInfoTextBrush");
                row.Children.Add(_runLabels[i]);
                body.Children.Add(row);
            }

            body.Children.Add(OverlayChrome.MakeDivider());

            var totalRow = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };
            _totalText = new TextBlock { FontSize = 14, FontWeight = FontWeights.Bold };
            _totalText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayTitleAccentTextBrush");
            DockPanel.SetDock(_totalText, Dock.Right);
            totalRow.Children.Add(_totalText);
            var totalLabel = new TextBlock { Text = "합계", FontSize = 14, FontWeight = FontWeights.Bold };
            totalLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            totalRow.Children.Add(totalLabel);
            body.Children.Add(totalRow);

            var avgRow = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };
            _averageText = new TextBlock { FontSize = 13 };
            _averageText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            DockPanel.SetDock(_averageText, Dock.Right);
            avgRow.Children.Add(_averageText);
            var avgLabel = new TextBlock { Text = "회차 평균", FontSize = 13 };
            avgLabel.SetResourceReference(TextBlock.ForegroundProperty, "OverlayInfoTextBrush");
            avgRow.Children.Add(avgLabel);
            body.Children.Add(avgRow);

            var root = OverlayChrome.MakeRootBorder(body, new Thickness(16, 12, 16, 12));
            Content = root;

            // 자체 저장 위치 → 없으면 어밴던로드 통계창 위치 → 없으면 화면 중앙 상단
            var (left, top) = ResolveDefaultPosition(settings);
            Left = left;
            Top = top;

            // 잠금 해제 모드에서 드래그로 이동 (자체 위치로 저장 — 어밴던 통계창과 독립)
            root.MouseLeftButtonDown += (_, e) => TryBeginDrag(e);
        }

        protected override bool KeepBelowSettingsHost => false;
        protected override bool UseToolWindowStyle => true;
        protected override ChatSettings? ResolveSettings() => _settings;
        // 자동으로 뜰 때의 위치는 기본 위치 계산 결과이므로, 사용자가 잠금 해제 중 옮긴 것만 저장한다
        protected override bool PersistOnlyWhenUnlocked => true;
        protected override bool PersistBoundsOnChange => true;

        protected override bool PersistBounds(ChatSettings settings)
        {
            if (!IsVisible)
                return false;

            settings.TreasurySummaryWindowLeft = Left;
            settings.TreasurySummaryWindowTop = Top;
            return true;
        }

        protected override (double? Left, double? Top, double? Width, double? Height)? StoredBounds(ChatSettings settings)
        {
            var (left, top) = ResolveDefaultPosition(settings);
            return (left, top, null, null);
        }

        /// <summary>자체 저장 위치 → 없으면 어밴던로드 통계창 위치 → 없으면 화면 중앙 상단.</summary>
        private static (double Left, double Top) ResolveDefaultPosition(ChatSettings settings)
            => ToastPresentationHelper.ResolveBasePosition(
                settings.TreasurySummaryWindowLeft ?? settings.AbandonRoadSummaryWindowLeft,
                settings.TreasurySummaryWindowTop ?? settings.AbandonRoadSummaryWindowTop,
                DefaultWidth, DefaultTop);

        /// <summary>30초 무획득이면 페이드 없이 닫는다 (기존 동작).</summary>
        protected override void OnAutoCloseElapsed()
        {
            try { Close(); } catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (ReferenceEquals(_instance, this))
                _instance = null;
            base.OnClosed(e);
        }

        // 금화 주머니 1개당 시드 가치 (50만)
        private const long GoldPouchSeedValue = 500_000;

        private static string FormatGoldValue(long count)
            => AppServices.Get<WeeklySeedRewardService>().FormatSeed(count * GoldPouchSeedValue);

        private void UpdateState(IReadOnlyList<int> runCounts, int currentRun)
        {
            for (int i = 0; i < MaxRuns; i++)
            {
                bool started = i < runCounts.Count;
                _runValues[i].Text = started ? $"{runCounts[i]}개 · {FormatGoldValue(runCounts[i])}" : "-";
                _runIcons[i].Visibility = started ? Visibility.Visible : Visibility.Collapsed;
                bool isCurrent = i == currentRun - 1;
                _runLabels[i].FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal;
                _runValues[i].SetResourceReference(TextBlock.ForegroundProperty,
                    isCurrent ? "OverlayTitleAccentTextBrush" : "TextBrush");
            }

            int total = runCounts.Sum();
            _totalText.Text = $"{total}개 · {FormatGoldValue(total)}";
            if (runCounts.Count > 0)
            {
                double average = (double)total / runCounts.Count;
                long averageSeed = (long)Math.Round(average * GoldPouchSeedValue);
                _averageText.Text = $"{average:F1}개 · {AppServices.Get<WeeklySeedRewardService>().FormatSeed(averageSeed)}";
            }
            else
            {
                _averageText.Text = "-";
            }

            // 미리보기·바로가기로 연 창은 저절로 닫히지 않는다
            if (_isPreview || _isManual)
                CancelAutoClose();
            else
                StartAutoClose(AutoCloseDelay);
        }

        /// <summary>도움말/README 렌더 전용: 창을 띄우지 않고 샘플 데이터가 채워진 인스턴스를 만든다.</summary>
        internal static TreasurySummaryWindow CreateForRender(ChatSettings settings, IReadOnlyList<int> runCounts, int currentRun)
        {
            var window = new TreasurySummaryWindow(settings);
            window.UpdateState(runCounts, currentRun);
            return window;
        }

        /// <summary>
        /// 세 공개 진입점이 함께 쓰는 블록: 인스턴스 확보 → 모드 설정 → 내용 채움 → Show → 최상위.
        /// <paramref name="manual"/>이 null이면 직전 값을 그대로 둔다 (위치 미리보기는 바로가기 여부를 건드리지 않는다).
        /// </summary>
        private static void EnsureShown(ChatSettings settings, bool preview, bool? manual, Action<TreasurySummaryWindow> fill, string failureLog)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_instance == null || !_instance.IsLoaded)
                        _instance = new TreasurySummaryWindow(settings);

                    _instance._isPreview = preview;
                    if (manual.HasValue)
                        _instance._isManual = manual.Value;
                    fill(_instance);
                    if (!_instance.IsVisible)
                        _instance.Show();
                    _instance.BringToFront();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn(failureLog, ex);
                }
            }));
        }

        /// <summary>통계 창을 띄우거나 갱신한다 (UI 스레드 마샬링 포함).</summary>
        public static void ShowOrUpdate(ChatSettings settings, IReadOnlyList<int> runCounts, int currentRun)
        {
            if (AppServices.Get<TrayAllWindowsService>().IsTrayed)
                return;

            EnsureShown(settings, preview: false, manual: false,
                w => w.UpdateState(runCounts, currentRun),
                "Failed to show treasury summary window.");
        }

        /// <summary>바로가기 버튼용: 저장된 이번 주 통계를 표시한다 (자동 닫힘 없음, 닫기 버튼으로 닫는다).</summary>
        public static void ShowStored(ChatSettings settings)
        {
            if (settings == null)
                return;

            EnsureShown(settings, preview: false, manual: true,
                w =>
                {
                    w.UpdateState(settings.Alerts.Dungeon.TreasuryRunCounts.ToArray(), currentRun: 0);
                    // 실시간 감지가 놓친 판(기능 꺼짐·앱 미실행)까지 이번 주 로그 아카이브로 복원
                    _ = RefreshFromLogArchiveAsync(settings);
                },
                "Failed to show stored treasury summary.");
        }

        /// <summary>이번 주 보물창고 이력을 로그 아카이브에서 재집계해 창과 저장값을 갱신한다.</summary>
        private static async Task RefreshFromLogArchiveAsync(ChatSettings settings)
        {
            try
            {
                DateTime today = DateTime.Today;
                DateTime weekStart = LogTextClassifier.GetWeekStart(today);
                var (counts, _) = await AppServices.Get<TreasuryHistoryService>().GetWeekAsync(settings.ChatLogFolderPath, weekStart);

                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var dungeon = settings.Alerts.Dungeon;
                        dungeon.TreasuryWeekKey = LogTextClassifier.GetIsoWeekKey(today);
                        dungeon.TreasuryRunCounts.Clear();
                        foreach (int count in counts)
                            dungeon.TreasuryRunCounts.Add(count);
                        ConfigService.SaveDeferred(settings);

                        if (_instance?.IsLoaded == true && _instance.IsVisible && _instance._isManual)
                            _instance.UpdateState(counts, currentRun: 0);
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn("Failed to apply rebuilt treasury counts.", ex);
                    }
                }));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to rebuild treasury counts from log archive.", ex);
            }
        }

        /// <summary>잠금 해제 모드용 위치 미리보기: 예시 데이터로 표시하고 자동 닫힘을 멈춘다.</summary>
        public static void ShowPositionPreview(ChatSettings settings)
        {
            if (settings == null || !settings.EnableTreasuryGoldCountAlert)
                return; // 기능이 꺼져 있으면 배치할 창도 없다

            EnsureShown(settings, preview: true, manual: null,
                w => w.UpdateState(new[] { 12, 9, 11 }, 3),
                "Failed to show treasury summary preview.");
        }

        /// <summary>창이 떠 있으면 설정에 저장된 위치로 옮긴다. 설정 전체 교체(프로필 불러오기) 뒤에 쓴다.</summary>
        public static void ApplyStoredPosition(ChatSettings settings)
        {
            if (settings == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _instance;
                if (window == null || !window.IsLoaded)
                    return;
                window.ApplyStoredBounds(settings);
            }));
        }

        /// <summary>잠금 해제 종료 시 미리보기 창을 닫는다 (실제 세션 창은 유지).</summary>
        public static void ClosePositionPreview()
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_instance?._isPreview == true)
                {
                    try { _instance.Close(); } catch { }
                }
            }));
        }
    }
}
