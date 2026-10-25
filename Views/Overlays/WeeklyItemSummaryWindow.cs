using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 주간 득템 통계 창: 달력(수익 월별 통계)이 쓰는 아이템 아카이브를 이번 주(월~일) 범위로
    /// 집계해 아이템별 획득 개수를 보여준다. 일일/주간 컨텐츠 창의 바로가기 버튼으로 연다.
    /// </summary>
    public sealed class WeeklyItemSummaryWindow : OverlayWindowBase
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
        // 아이템 목록 읽기용. 시드 행(_loadVersion)과 따로 세어 시드 갱신이 목록 결과를 무효로 만들지 않게 한다.
        private int _itemLoadVersion;
        private bool _keepCenteredOnFirstFill = true;
        private bool _isClosed;

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

            DateTime today = DateTime.Today;
            DateTime weekStart = LogTextClassifier.GetWeekStart(today); // 월요일 시작
            DateTime weekEnd = weekStart.AddDays(6);
            _weekStart = weekStart;
            _weekEnd = weekEnd;

            // 시드 줄이 실시간으로 기록되거나 보충 스캔이 끝나면 열려 있는 창의 시드 행을 다시 채운다
            AppServices.Get<WeeklySeedRewardService>().Changed += OnSeedArchiveChanged;
            Closed += (_, _) => AppServices.Get<WeeklySeedRewardService>().Changed -= OnSeedArchiveChanged;

            var title = OverlayChrome.MakeTitle("주간 득템 통계");
            _subtitleText = OverlayChrome.MakeSubtitle($"{weekStart:M.d(ddd)} ~ {weekEnd:M.d(ddd)}", new Thickness(4, 0, 4, 0));

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

            var titleStack = new StackPanel();
            titleStack.Children.Add(title);
            titleStack.Children.Add(subtitleRow);
            var header = OverlayChrome.MakeHeader(titleStack, new Thickness(0, 0, 0, 8), OverlayChrome.MakeCloseButton(() => Close()));

            // 아이템 목록·경험의 정수는 창을 띄운 뒤 백그라운드에서 읽어 채운다 (LoadWeekItemsAsync)
            _listPanel = new StackPanel();

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
            body.Children.Add(OverlayChrome.MakeDivider());
            body.Children.Add(scroll);
            body.Children.Add(OverlayChrome.MakeDivider());
            body.Children.Add(BuildEssenceRow(out _essenceValueText));
            LoadWeekItemsAsync();

            var root = OverlayChrome.MakeRootBorder(body, new Thickness(16, 12, 16, 12));
            Content = root;

            // 저장된 자리가 있으면 거기서 열고(Manual), 없으면 화면 가운데(CenterScreen)
            ApplyStoredBounds(ResolveSettings());
            if (WindowStartupLocation == WindowStartupLocation.Manual)
                _keepCenteredOnFirstFill = false;

            // 제목 영역 드래그로 이동 (잠금과 무관)
            root.MouseLeftButtonDown += (_, e) =>
            {
                _keepCenteredOnFirstFill = false; // 사용자가 옮긴 위치는 건드리지 않는다
                TryBeginDrag(e);
            };
        }

        protected override bool KeepBelowSettingsHost => false;
        protected override bool UseToolWindowStyle => true;
        protected override bool DragRequiresUnlock => false;
        protected override bool PersistBoundsOnChange => false; // 드래그 끝·닫힘에 저장
        protected override ChatSettings? ResolveSettings() => _settings ?? base.ResolveSettings();

        protected override bool PersistBounds(ChatSettings settings)
        {
            settings.WeeklyItemSummaryWindowLeft = Left;
            settings.WeeklyItemSummaryWindowTop = Top;
            return true;
        }

        protected override (double? Left, double? Top, double? Width, double? Height)? StoredBounds(ChatSettings settings)
            => (settings.WeeklyItemSummaryWindowLeft, settings.WeeklyItemSummaryWindowTop, null, null);

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
            return LogTextClassifier.GetWeekStart(DateTime.Today);
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

            _keepCenteredOnFirstFill = false; // 주 이동 때는 지금처럼 위쪽 고정으로 늘어나고 줄어든다
            LoadWeekItemsAsync();

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
                    Source = PackImageCache.Get("pack://application:,,,/Data/images/Item/시드.png"),
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

        private void OnSeedArchiveChanged()
        {
            // 백그라운드 스레드에서 올라온다. 이미 진행 중인 계산은 _loadVersion으로 무효화된다.
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (IsLoaded)
                        LoadSeedSummaryAsync();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            catch { }
        }

        /// <summary>
        /// 보관본에서 실측 시드 합계를 채운다. 이번 주에는 체크리스트 기준 주간 한도를
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
            // 아페티리아 난이도(일반 7.35억/어려움 8.4억)는 로그로 판별하므로 주간 스캔 뒤에 예상치를 계산한다
            var (generalCap, rubiconaCap) = AppServices.Get<WeeklySeedRewardService>().ComputeWeeklySeedCaps(_settings);
            string generalCapText = AppServices.Get<WeeklySeedRewardService>().FormatSeed(generalCap);
            string rubiconaCapText = AppServices.Get<WeeklySeedRewardService>().FormatSeed(rubiconaCap);
            try
            {
                var (general, rubicona) = await AppServices.Get<WeeklySeedRewardService>().SumWeeklyClearSeedAsync(
                    _settings.ChatLogFolderPath, _weekStart, _weekEnd);
                if (!IsLoaded || version != _loadVersion) return;
                bool apetiriaHard = AppServices.Get<WeeklySeedRewardService>().GetApetiriaHard(_weekStart, _weekEnd);
                (generalCap, rubiconaCap) = AppServices.Get<WeeklySeedRewardService>().ComputeWeeklySeedCaps(_settings, apetiriaHard);
                generalCapText = AppServices.Get<WeeklySeedRewardService>().FormatSeed(generalCap);
                rubiconaCapText = AppServices.Get<WeeklySeedRewardService>().FormatSeed(rubiconaCap);
                (general, long other) = AppServices.Get<WeeklySeedRewardService>().SplitWeeklyOverflow(_weekStart, general);
                string generalText = AppServices.Get<WeeklySeedRewardService>().FormatSeed(general);
                string rubiconaText = AppServices.Get<WeeklySeedRewardService>().FormatSeed(rubicona);
                _seedGeneralText.Text = isCurrentWeek ? $"{generalText} / {generalCapText}" : generalText;
                _seedRubiconaText.Text = isCurrentWeek ? $"{rubiconaText} / {rubiconaCapText}" : rubiconaText;
                _seedOtherRow.Visibility = other > 0 ? Visibility.Visible : Visibility.Collapsed;
                _seedOtherText.Text = AppServices.Get<WeeklySeedRewardService>().FormatSeed(other);
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

        /// <summary>
        /// 표시 주의 아이템 목록과 경험의 정수 합계를 백그라운드에서 읽어 채운다.
        /// 읽는 동안 '계산 중…'을 보여 주고, 그 사이 주를 옮기거나 창을 닫으면 결과를 버린다.
        /// </summary>
        private async void LoadWeekItemsAsync()
        {
            int version = ++_itemLoadVersion;
            DateTime weekStart = _weekStart;
            DateTime weekEnd = _weekEnd;

            _listPanel.Children.Clear();
            _listPanel.Children.Add(CreateHintText("계산 중…"));
            _essenceValueText.Text = "계산 중…";

            List<(string Name, ItemDropGrade Grade, int Count)> aggregated;
            long essenceCount;
            try
            {
                (aggregated, essenceCount) = await Task.Run(() =>
                    (AggregateWeekItems(weekStart, weekEnd), ReadEssenceCount(weekStart, weekEnd)));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load weekly item summary.", ex);
                aggregated = new List<(string Name, ItemDropGrade Grade, int Count)>();
                essenceCount = 0;
            }

            if (_isClosed || version != _itemLoadVersion)
                return;

            double heightBefore = ActualHeight;
            _listPanel.Children.Clear();
            BuildRows(_listPanel, aggregated);
            _essenceValueText.Text = $"x{essenceCount:N0}";

            // 처음 채울 때는 내용이 늘어난 만큼 위로 올려 화면 가운데 배치를 유지한다 (CenterScreen은 표시 시점 크기 기준)
            if (_keepCenteredOnFirstFill)
            {
                _keepCenteredOnFirstFill = false;
                try
                {
                    UpdateLayout();
                    double grown = ActualHeight - heightBefore;
                    if (grown != 0 && heightBefore > 0)
                        Top -= grown / 2;
                }
                catch { }
            }
        }

        private static long ReadEssenceCount(DateTime weekStart, DateTime weekEnd)
        {
            try
            {
                return ItemCalendarWindow.ReadExperienceEssenceCountForRange(weekStart, weekEnd);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to read weekly experience essence count.", ex);
                return 0;
            }
        }

        private static TextBlock CreateHintText(string text)
        {
            var hint = new TextBlock
            {
                Text = text,
                FontSize = 12,
                Margin = new Thickness(2, 4, 2, 4),
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");
            return hint;
        }

        /// <summary>기간 내 아이템 스냅샷을 이름·등급별로 합산해 표시 순서대로 돌려준다. (UI 요소를 만들지 않으므로 백그라운드에서 호출)</summary>
        private static List<(string Name, ItemDropGrade Grade, int Count)> AggregateWeekItems(DateTime weekStart, DateTime weekEnd)
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

            return snapshots
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
        }

        private static void BuildRows(StackPanel panel, List<(string Name, ItemDropGrade Grade, int Count)> aggregated)
        {
            if (aggregated.Count == 0)
            {
                panel.Children.Add(CreateHintText("해당 주의 획득 기록이 없습니다."));
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
                if (ChatBrushResolver.ForItemGrade(grade) is SolidColorBrush gradeBrush)
                    nameText.Foreground = gradeBrush;
                else
                    nameText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayInfoTextBrush");
                row.Children.Add(nameText);
                panel.Children.Add(row);
            }

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
                    Source = PackImageCache.Get("pack://application:,,,/Data/images/Item/경험의 정수.png"),
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

        protected override void OnClosed(EventArgs e)
        {
            _isClosed = true;
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
