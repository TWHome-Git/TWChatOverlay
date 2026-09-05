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
        private readonly TextBlock _seedGeneralText;
        private readonly TextBlock _seedRubiconaText;
        private readonly TextBlock _seedOtherText;
        private readonly UIElement _seedOtherRow;
        private readonly TextBlock _essenceValueText;
        private readonly TextBlock _subtitleText;
        private readonly Button _prevWeekButton;
        private readonly Button _nextWeekButton;
        private readonly StackPanel _listPanel;
        private DateTime _weekStart;
        private DateTime _weekEnd;
        private int _loadVersion;

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

            _subtitleText = new TextBlock
            {
                Text = $"{weekStart:M.d(ddd)} ~ {weekEnd:M.d(ddd)}",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 4, 0),
            };
            _subtitleText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");

            _prevWeekButton = CreateWeekNavButton("◀");
            _prevWeekButton.Click += (_, _) => ChangeWeek(-1);
            _nextWeekButton = CreateWeekNavButton("▶");
            _nextWeekButton.Click += (_, _) => ChangeWeek(1);
            _nextWeekButton.IsEnabled = false;

            var subtitleRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 2, 0, 0),
            };
            subtitleRow.Children.Add(_prevWeekButton);
            subtitleRow.Children.Add(_subtitleText);
            subtitleRow.Children.Add(_nextWeekButton);

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
            titleStack.Children.Add(subtitleRow);
            header.Children.Add(titleStack);

            _listPanel = new StackPanel();
            BuildRows(_listPanel, weekStart, weekEnd);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 420,
                Content = _listPanel,
            };

            var body = new StackPanel();
            body.Children.Add(header);
            body.Children.Add(BuildSeedRow("클리어 보상 시드 (일반지역)", new Thickness(2, 0, 2, 2), out _seedGeneralText));
            body.Children.Add(BuildSeedRow("클리어 보상 시드 (루비코나)", new Thickness(2, 0, 2, 2), out _seedRubiconaText));
            // 과거(개편 전) 주에만 존재하는 한도 외 몫 — 값이 있을 때만 표시
            _seedOtherRow = BuildSeedRow("클리어 보상 시드 (기타)", new Thickness(2, 0, 2, 2), out _seedOtherText);
            _seedOtherRow.Visibility = Visibility.Collapsed;
            body.Children.Add(_seedOtherRow);
            body.Children.Add(CreateDivider());
            body.Children.Add(scroll);
            body.Children.Add(CreateDivider());
            body.Children.Add(BuildEssenceRow(out _essenceValueText));
            RefreshEssenceCount();

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

        private static Button CreateWeekNavButton(string glyph)
        {
            var button = new Button
            {
                Content = glyph,
                Width = 22,
                Height = 18,
                FontSize = 9,
                Padding = new Thickness(0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
            };
            button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
            return button;
        }

        private static DateTime GetCurrentWeekStart()
        {
            DateTime today = DateTime.Today;
            return today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        }

        /// <summary>표시 주를 이동하고 아이템 목록·시드 행을 해당 주 기준으로 다시 집계한다.</summary>
        private void ChangeWeek(int deltaWeeks)
        {
            DateTime currentWeekStart = GetCurrentWeekStart();
            DateTime target = _weekStart.AddDays(7 * deltaWeeks);
            if (target > currentWeekStart)
                target = currentWeekStart;

            _weekStart = target;
            _weekEnd = target.AddDays(6);
            _subtitleText.Text = $"{_weekStart:M.d(ddd)} ~ {_weekEnd:M.d(ddd)}";
            _nextWeekButton.IsEnabled = _weekStart < currentWeekStart;

            _listPanel.Children.Clear();
            BuildRows(_listPanel, _weekStart, _weekEnd);
            RefreshEssenceCount();

            _seedGeneralText.Text = "계산 중…";
            _seedRubiconaText.Text = "계산 중…";
            _seedOtherRow.Visibility = Visibility.Collapsed;
            LoadSeedSummaryAsync();
        }

        /// <summary>클리어 보상 시드 행 — 실측 합산과 체크리스트 기반 주간 한도를 함께 표시.</summary>
        private static UIElement BuildSeedRow(string labelText, Thickness margin, out TextBlock valueText)
        {
            var row = new DockPanel { Margin = margin };

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
                Text = labelText,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            labelPanel.Children.Add(label);
            row.Children.Add(labelPanel);
            return row;
        }

        /// <summary>
        /// 주간 로그를 스캔해 실측 시드 합계를 채운다. 이번 주에는 체크리스트 기준 주간 한도를
        /// 병기하고, 과거 주에는 실측값만 표시한다 (당시 한도가 지금과 다를 수 있음).
        /// </summary>
        private async void LoadSeedSummaryAsync()
        {
            int version = ++_loadVersion;
            if (_settings is null)
            {
                _seedGeneralText.Text = "-";
                _seedRubiconaText.Text = "-";
                return;
            }

            bool isCurrentWeek = _weekStart == GetCurrentWeekStart();
            var (generalCap, rubiconaCap) = WeeklySeedRewardService.ComputeWeeklySeedCaps(_settings);
            string generalCapText = WeeklySeedRewardService.FormatSeed(generalCap);
            string rubiconaCapText = WeeklySeedRewardService.FormatSeed(rubiconaCap);
            try
            {
                var (general, rubicona) = await WeeklySeedRewardService.SumWeeklyClearSeedAsync(
                    _settings.ChatLogFolderPath, _weekStart, _weekEnd);
                if (!IsLoaded || version != _loadVersion) return;
                (general, long other) = WeeklySeedRewardService.SplitWeeklyOverflow(_weekStart, general);
                string generalText = WeeklySeedRewardService.FormatSeed(general);
                string rubiconaText = WeeklySeedRewardService.FormatSeed(rubicona);
                _seedGeneralText.Text = isCurrentWeek ? $"{generalText} / {generalCapText}" : generalText;
                _seedRubiconaText.Text = isCurrentWeek ? $"{rubiconaText} / {rubiconaCapText}" : rubiconaText;
                _seedOtherRow.Visibility = other > 0 ? Visibility.Visible : Visibility.Collapsed;
                _seedOtherText.Text = WeeklySeedRewardService.FormatSeed(other);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to compute weekly seed summary.", ex);
                if (IsLoaded && version == _loadVersion)
                {
                    _seedGeneralText.Text = isCurrentWeek ? $"- / {generalCapText}" : "-";
                    _seedRubiconaText.Text = isCurrentWeek ? $"- / {rubiconaCapText}" : "-";
                }
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
                .GroupBy(s => (Name: ItemCalendarEntryViewModel.ApplyDisplayNameAlias(s.DisplayName ?? s.ItemName ?? string.Empty), s.Grade))
                .Select(g => (g.Key.Name, g.Key.Grade, Count: g.Sum(s => Math.Max(1, s.Count))))
                .OrderBy(x => x.Grade switch
                {
                    ItemDropGrade.Special => 0,
                    ItemDropGrade.Rare => 1,
                    _ => 2,
                })
                .ThenByDescending(x => x.Count)
                .ThenBy(x => x.Name, StringComparer.Ordinal)
                .ToList();

            if (aggregated.Count == 0)
            {
                var empty = new TextBlock
                {
                    Text = "해당 주의 획득 기록이 없습니다.",
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

                // 아이템 아이콘 (달력과 같은 매핑; 없는 아이템은 빈 자리로 이름 정렬 유지)
                var iconHost = new Border
                {
                    Width = 18,
                    Height = 18,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                try
                {
                    var iconSource = ItemIconLoader.LoadTrimmed(ItemCalendarEntryViewModel.GetIconUri(name));
                    if (iconSource != null)
                    {
                        var iconImage = new Image
                        {
                            Source = iconSource,
                            Stretch = Stretch.Uniform,
                        };
                        RenderOptions.SetBitmapScalingMode(iconImage, BitmapScalingMode.NearestNeighbor);
                        iconHost.Child = iconImage;
                    }
                }
                catch { }
                DockPanel.SetDock(iconHost, Dock.Left);
                row.Children.Add(iconHost);

                var nameText = new TextBlock
                {
                    Text = name,
                    FontSize = 13,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                // 아이템 필터(획득 알림·달력)와 동일한 등급 색상을 따른다
                switch (grade)
                {
                    case ItemDropGrade.Rare:
                        nameText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A));
                        break;
                    case ItemDropGrade.Special:
                        nameText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x7E, 0xDB));
                        break;
                    default:
                        nameText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayInfoTextBrush");
                        break;
                }
                row.Children.Add(nameText);
                panel.Children.Add(row);
            }

        }

        private static Border CreateDivider()
        {
            var divider = new Border { Height = 1, Margin = new Thickness(0, 6, 0, 6) };
            divider.SetResourceReference(Border.BackgroundProperty, "SeparatorBrush");
            return divider;
        }

        /// <summary>경험의 정수 고정 행 (목록 아래 경계선 밑에 표시).</summary>
        private static UIElement BuildEssenceRow(out TextBlock valueText)
        {
            var row = new DockPanel { Margin = new Thickness(2, 0, 2, 0) };

            valueText = new TextBlock { Text = "-", FontSize = 13, FontWeight = FontWeights.SemiBold };
            valueText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayExpAccentBrush");
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
                        new Uri("pack://application:,,,/Data/images/Item/경험의 정수.png")),
                };
                RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
                labelPanel.Children.Add(icon);
            }
            catch { }

            var name = new TextBlock { Text = "경험의 정수", FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            name.SetResourceReference(TextBlock.ForegroundProperty, "OverlayExpAccentBrush");
            labelPanel.Children.Add(name);
            row.Children.Add(labelPanel);
            return row;
        }

        /// <summary>표시 중인 주의 경험의 정수 합계를 다시 읽는다 (달력과 같은 Exp 아카이브 소스).</summary>
        private void RefreshEssenceCount()
        {
            long essenceCount = 0;
            try
            {
                essenceCount = ItemCalendarWindow.ReadExperienceEssenceCountForRange(_weekStart, _weekEnd);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to read weekly experience essence count.", ex);
            }
            _essenceValueText.Text = $"x{essenceCount:N0}";
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
