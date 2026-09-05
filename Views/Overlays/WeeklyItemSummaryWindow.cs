using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 주간 득템 통계 창: 달력(수익 월별 통계)이 쓰는 아이템 아카이브를 이번 주(월~일) 범위로
    /// 집계해 아이템별 획득 개수를 보여준다. 일일/주간 컨텐츠 창의 바로가기 버튼으로 연다.
    /// </summary>
    public sealed class WeeklyItemSummaryWindow : Window
    {
        private static WeeklyItemSummaryWindow? _instance;

        private readonly ChatSettings? _settings;
        private readonly TextBlock _seedValueText;
        private readonly DateTime _weekStart;
        private readonly DateTime _weekEnd;

        private WeeklyItemSummaryWindow(ChatSettings? settings)
        {
            _settings = settings;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Width = 384;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowFontService.Apply(this);

            DateTime today = DateTime.Today;
            DateTime weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); // 월요일 시작
            DateTime weekEnd = weekStart.AddDays(6);
            _weekStart = weekStart;
            _weekEnd = weekEnd;

            var title = new TextBlock
            {
                Text = "주간 득템 통계",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "OverlayTitleAccentTextBrush");

            var subtitle = new TextBlock
            {
                Text = $"{weekStart:M.d(ddd)} ~ {weekEnd:M.d(ddd)}",
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
            };
            subtitle.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");

            var closeButton = new Button
            {
                Content = "닫기",
                Height = 22,
                MinWidth = 40,
                Padding = new Thickness(8, 0, 8, 0),
                FontSize = 11,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top,
            };
            closeButton.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            closeButton.Click += (_, _) => { try { Close(); } catch { } };

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(closeButton, Dock.Right);
            header.Children.Add(closeButton);
            var titleStack = new StackPanel();
            titleStack.Children.Add(title);
            titleStack.Children.Add(subtitle);
            header.Children.Add(titleStack);

            var listPanel = new StackPanel();
            BuildRows(listPanel, weekStart, weekEnd);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 420,
                Content = listPanel,
            };

            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(BuildSeedRow(out _seedValueText));
            body.Children.Add(scroll);

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

            // 제목 영역 드래그로 이동
            root.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };
        }

        /// <summary>클리어 보상 시드 (주간) 행 — 실측 합산과 체크리스트 기반 주간 한도를 함께 표시.</summary>
        private static UIElement BuildSeedRow(out TextBlock valueText)
        {
            var row = new DockPanel { Margin = new Thickness(2, 0, 2, 8) };

            valueText = new TextBlock
            {
                Text = "계산 중…",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            valueText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayTitleAccentTextBrush");
            DockPanel.SetDock(valueText, Dock.Right);
            row.Children.Add(valueText);

            var labelPanel = new StackPanel { Orientation = Orientation.Horizontal };
            try
            {
                var icon = new Image
                {
                    Width = 18,
                    Height = 18,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri("pack://application:,,,/Data/images/Item/시드.png")),
                };
                RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
                labelPanel.Children.Add(icon);
            }
            catch { }

            var label = new TextBlock
            {
                Text = "클리어 보상 시드",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            labelPanel.Children.Add(label);
            row.Children.Add(labelPanel);
            return row;
        }

        /// <summary>주간 로그를 스캔해 실측 시드 합계를 채우고, 체크리스트 기준 주간 한도를 병기한다.</summary>
        private async void LoadSeedSummaryAsync()
        {
            if (_settings is null)
            {
                _seedValueText.Text = "-";
                return;
            }

            string expected = WeeklySeedRewardService.FormatSeed(
                WeeklySeedRewardService.ComputeExpectedWeeklySeed(_settings));
            try
            {
                long actual = await WeeklySeedRewardService.SumWeeklyClearSeedAsync(
                    _settings.ChatLogFolderPath, _weekStart, _weekEnd);
                if (!IsLoaded) return;
                _seedValueText.Text = $"{WeeklySeedRewardService.FormatSeed(actual)} / {expected}";
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to compute weekly seed summary.", ex);
                if (IsLoaded)
                    _seedValueText.Text = $"- / {expected}";
            }
        }

        private static void BuildRows(StackPanel panel, DateTime weekStart, DateTime weekEnd)
        {
            List<ItemLogSnapshotEntry> snapshots;
            try
            {
                snapshots = ItemCalendarWindow.ReadItemSnapshotsForRange(weekStart, weekEnd);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to read weekly item snapshots.", ex);
                snapshots = new List<ItemLogSnapshotEntry>();
            }

            var aggregated = snapshots
                .Where(s => !string.IsNullOrWhiteSpace(s.ItemName))
                .GroupBy(s => (Name: s.DisplayName ?? s.ItemName ?? string.Empty, s.Grade))
                .Select(g => (g.Key.Name, g.Key.Grade, Count: g.Sum(s => Math.Max(1, s.Count))))
                .OrderByDescending(x => x.Grade == ItemDropGrade.Rare || x.Grade == ItemDropGrade.Special)
                .ThenByDescending(x => x.Count)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .ToList();

            if (aggregated.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "이번 주 획득 기록이 없습니다.",
                    FontSize = 12,
                    Margin = new Thickness(2, 4, 2, 4),
                };
                empty.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
                panel.Children.Add(empty);
                return;
            }

            foreach (var (name, grade, count) in aggregated)
            {
                var row = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };

                var countText = new TextBlock { Text = $"x{count}", FontSize = 13, FontWeight = FontWeights.SemiBold };
                countText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                DockPanel.SetDock(countText, Dock.Right);
                row.Children.Add(countText);

                bool isRare = grade == ItemDropGrade.Rare || grade == ItemDropGrade.Special;
                var nameText = new TextBlock
                {
                    Text = name,
                    FontSize = 13,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                nameText.SetResourceReference(TextBlock.ForegroundProperty,
                    isRare ? "OverlayTitleAccentTextBrush" : "OverlayInfoTextBrush");
                row.Children.Add(nameText);
                panel.Children.Add(row);
            }

            var divider = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
            divider.SetResourceReference(Border.BackgroundProperty, "SeparatorBrush");
            panel.Children.Add(divider);

            int total = aggregated.Sum(x => x.Count);
            var totalRow = new DockPanel { Margin = new Thickness(2, 1, 2, 1) };
            var totalValue = new TextBlock { Text = $"x{total}", FontSize = 14, FontWeight = FontWeights.Bold };
            totalValue.SetResourceReference(TextBlock.ForegroundProperty, "OverlayTitleAccentTextBrush");
            DockPanel.SetDock(totalValue, Dock.Right);
            totalRow.Children.Add(totalValue);
            var totalLabel = new TextBlock { Text = "총 획득", FontSize = 14, FontWeight = FontWeights.Bold };
            totalLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            totalRow.Children.Add(totalLabel);
            panel.Children.Add(totalRow);
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
            if (ReferenceEquals(_instance, this))
                _instance = null;
            base.OnClosed(e);
        }

        /// <summary>창을 연다 (이미 열려 있으면 최신 데이터로 다시 연다).</summary>
        public static void ShowWindow(ChatSettings? settings = null)
        {
            try
            {
                if (_instance?.IsLoaded == true)
                {
                    try { _instance.Close(); } catch { }
                }

                _instance = new WeeklyItemSummaryWindow(settings);
                _instance.Show();
                TopmostWindowHelper.BringToTopmost(_instance);
                _instance.LoadSeedSummaryAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to show weekly item summary window.", ex);
            }
        }
    }
}
