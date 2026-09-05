using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 심연의 보물창고 주간 통계 창: 이번 주 1~7회차의 금화 주머니 획득 수와 합계·평균을 보여준다.
    /// 어밴던로드 통계창과 같은 위치·크기 계열로 표시되며, 세션(입장 후 2분 30초)이 끝나고
    /// 잠시 뒤 자동으로 닫힌다.
    /// </summary>
    public sealed class TreasurySummaryWindow : Window
    {
        private const int MaxRuns = 7;
        private const string SeedPouchIconUri = "pack://application:,,,/Data/images/Item/시드.png";
        private static readonly TimeSpan AutoCloseDelay = TimeSpan.FromSeconds(30); // 마지막 획득 후 30초 무획득이면 닫힘

        private static TreasurySummaryWindow? _instance;

        private readonly TextBlock[] _runValues = new TextBlock[MaxRuns];
        private readonly TextBlock[] _runLabels = new TextBlock[MaxRuns];
        private readonly TextBlock _totalText;
        private readonly TextBlock _averageText;
        private readonly DispatcherTimer _closeTimer;
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
            Width = 384;
            SizeToContent = SizeToContent.Height;
            WindowFontService.Apply(this);

            var title = new TextBlock
            {
                Text = "심연의 보물창고",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "OverlayTitleAccentTextBrush");

            var subtitle = new TextBlock
            {
                Text = "이번 주 금화 주머니 획득",
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
            };
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");

            var icon = new Image
            {
                Width = 30,
                Height = 30,
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
            try { icon.Source = new BitmapImage(new Uri(SeedPouchIconUri, UriKind.Absolute)); } catch { }

            var closeButton = new Button
            {
                Content = "닫기",
                Height = 22,
                MinWidth = 40,
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 11,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
            };
            closeButton.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            closeButton.Click += (_, _) => { try { Close(); } catch { } };

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            header.Children.Add(icon);
            DockPanel.SetDock(closeButton, Dock.Right);
            header.Children.Add(closeButton);
            var titleStack = new StackPanel();
            titleStack.Children.Add(title);
            titleStack.Children.Add(subtitle);
            header.Children.Add(titleStack);

            var body = new StackPanel();
            body.Children.Add(header);

            for (int i = 0; i < MaxRuns; i++)
            {
                var row = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };
                _runValues[i] = new TextBlock { FontSize = 13, FontWeight = FontWeights.SemiBold };
                _runValues[i].SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                DockPanel.SetDock(_runValues[i], Dock.Right);
                row.Children.Add(_runValues[i]);

                _runLabels[i] = new TextBlock { Text = $"{i + 1}회차", FontSize = 13 };
                _runLabels[i].SetResourceReference(TextBlock.ForegroundProperty, "OverlayInfoTextBrush");
                row.Children.Add(_runLabels[i]);
                body.Children.Add(row);
            }

            var divider = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
            divider.SetResourceReference(Border.BackgroundProperty, "SeparatorBrush");
            body.Children.Add(divider);

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

            var root = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 12, 16, 12),
                Child = body,
            };
            root.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
            root.SetResourceReference(Border.BorderBrushProperty, "OverlayWindowBorderBrush");
            Content = root;

            // 자체 저장 위치 → 없으면 어밴던로드 통계창 위치 → 없으면 화면 중앙 상단
            var (left, top) = ToastPresentationHelper.ResolveBasePosition(
                settings.TreasurySummaryWindowLeft ?? settings.AbandonRoadSummaryWindowLeft,
                settings.TreasurySummaryWindowTop ?? settings.AbandonRoadSummaryWindowTop,
                384, 160);
            Left = left;
            Top = top;

            // 잠금 해제 모드에서 드래그로 이동 (자체 위치로 저장 — 어밴던 통계창과 독립)
            root.MouseLeftButtonDown += (_, e) =>
            {
                UiLockService.Select(this);
                if (!UiLockService.IsUnlocked || e.ButtonState != MouseButtonState.Pressed)
                    return;
                try { DragMove(); } catch { }
            };
            LocationChanged += (_, _) =>
            {
                if (!IsVisible || !UiLockService.IsUnlocked)
                    return;
                _settings.TreasurySummaryWindowLeft = Left;
                _settings.TreasurySummaryWindowTop = Top;
                ConfigService.SaveDeferred(_settings);
            };

            _closeTimer = new DispatcherTimer { Interval = AutoCloseDelay };
            _closeTimer.Tick += (_, _) =>
            {
                _closeTimer.Stop();
                try { Close(); } catch { }
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW);
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            try { _closeTimer.Stop(); } catch { }
            if (ReferenceEquals(_instance, this))
                _instance = null;
            base.OnClosed(e);
        }

        // 금화 주머니 1개당 시드 가치 (50만)
        private const long GoldPouchSeedValue = 500_000;

        private static string FormatGoldValue(long count)
            => WeeklySeedRewardService.FormatSeed(count * GoldPouchSeedValue);

        private void UpdateState(IReadOnlyList<int> runCounts, int currentRun)
        {
            for (int i = 0; i < MaxRuns; i++)
            {
                bool started = i < runCounts.Count;
                _runValues[i].Text = started ? $"{runCounts[i]}개 · {FormatGoldValue(runCounts[i])}" : "-";
                bool isCurrent = i == currentRun - 1;
                _runLabels[i].FontWeight = isCurrent ? FontWeights.Bold : FontWeights.Normal;
                _runValues[i].SetResourceReference(TextBlock.ForegroundProperty,
                    isCurrent ? "OverlayTitleAccentTextBrush" : "TextBrush");
            }

            int total = runCounts.Sum();
            _totalText.Text = $"{total}개 · {FormatGoldValue(total)}";
            _averageText.Text = runCounts.Count > 0
                ? $"{(double)total / runCounts.Count:F1}개"
                : "-";

            _closeTimer.Stop();
            if (!_isPreview && !_isManual)
                _closeTimer.Start();
        }

        /// <summary>통계 창을 띄우거나 갱신한다 (UI 스레드 마샬링 포함).</summary>
        /// <summary>도움말/README 렌더 전용: 창을 띄우지 않고 샘플 데이터가 채워진 인스턴스를 만든다.</summary>
        internal static TreasurySummaryWindow CreateForRender(ChatSettings settings, IReadOnlyList<int> runCounts, int currentRun)
        {
            var window = new TreasurySummaryWindow(settings);
            window.UpdateState(runCounts, currentRun);
            return window;
        }

        public static void ShowOrUpdate(ChatSettings settings, IReadOnlyList<int> runCounts, int currentRun)
        {
            if (TrayAllWindowsService.IsTrayed)
                return;

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_instance == null || !_instance.IsLoaded)
                        _instance = new TreasurySummaryWindow(settings);

                    _instance._isPreview = false;
                    _instance._isManual = false;
                    _instance.UpdateState(runCounts, currentRun);
                    if (!_instance.IsVisible)
                        _instance.Show();
                    TopmostWindowHelper.BringToTopmost(_instance);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to show treasury summary window.", ex);
                }
            }));
        }

        /// <summary>바로가기 버튼용: 저장된 이번 주 통계를 표시한다 (자동 닫힘 없음, 닫기 버튼으로 닫는다).</summary>
        public static void ShowStored(ChatSettings settings)
        {
            if (settings == null)
                return;

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_instance == null || !_instance.IsLoaded)
                        _instance = new TreasurySummaryWindow(settings);

                    _instance._isPreview = false;
                    _instance._isManual = true;
                    _instance.UpdateState(settings.Alerts.Dungeon.TreasuryRunCounts.ToArray(), currentRun: 0);
                    if (!_instance.IsVisible)
                        _instance.Show();
                    TopmostWindowHelper.BringToTopmost(_instance);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to show stored treasury summary.", ex);
                }
            }));
        }

        /// <summary>잠금 해제 모드용 위치 미리보기: 예시 데이터로 표시하고 자동 닫힘을 멈춘다.</summary>
        public static void ShowPositionPreview(ChatSettings settings)
        {
            if (settings == null || !settings.EnableTreasuryGoldCountAlert)
                return; // 기능이 꺼져 있으면 배치할 창도 없다

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_instance == null || !_instance.IsLoaded)
                        _instance = new TreasurySummaryWindow(settings);

                    _instance._isPreview = true;
                    _instance.UpdateState(new[] { 12, 9, 11 }, 3);
                    if (!_instance.IsVisible)
                        _instance.Show();
                    TopmostWindowHelper.BringToTopmost(_instance);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to show treasury summary preview.", ex);
                }
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
