using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.Services.LogAnalysis;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 던전 숙제 체크리스트 오버레이 창입니다.
    /// </summary>
    public partial class DailyWeeklyContentWindow : Window, INotifyPropertyChanged
    {
        private readonly ChatSettings _settings;
        private readonly ObservableCollection<DailyWeeklyContentLog> _dailyContentItems = new();
        private readonly ObservableCollection<DailyWeeklyContentLog> _weeklyContentItems = new();
        private readonly ObservableCollection<DailyWeeklyContentLog> _completedItems = new();
        private DailyWeeklyLogAnalyzer _dailyWeeklyLogAnalyzer = null!;
        private bool _suppressSave = false;
        private bool _pendingRescanAfterSettings = false;
        private DispatcherTimer? _resetTimer;
        private DateTime _lastDailyResetDate;
        private DateTime _lastWeeklyResetKey;
        private DateTime _lastAbaddonResetKey;
        private const string DailyContentsGroupName = "일일 컨텐츠";
        private const string WeeklyContentsGroupName = "주간 컨텐츠";

        private const string MercurialCaveItemName = "머큐리얼 케이브";
        private const int MercurialCaveDefaultMaxCount = 4;
        private const string LuminousItemName = "루미너스";
        private const string EclipseBossRaidItemName = "이클립스 보스";
        private const string EclipseSubjugationItemName = "이클립스 토벌전";
        private const string SupplyRetrievalItemName = "보급품 탈환";
        private const string MoonQueensTrainingCenterItemName = "훈련소";
        private const string DetachedForceSubjugationItemName = "별동대";
        private const string ConfusedLandItemName = "혼란한 대지";
        private const string ColorlessLandItemName = "색을 잃은 땅";
        private const string CoreDungeonItemName = "코어던전";
        private const string ExcavationSiteItemName = "발굴지";
        private const string MiningSiteItemName = "채굴장";
        private const string DimensionalGapItemName = "차원의 틈";
        private const string AbyssalTreasuryItemName = "심연의 보물창고";
        private const string CravingPleasureItemName = "갈망하는 즐거움";
        private const string NestOfShinjoHardItemName = "신조의 둥지 어려움";
        private const string ApetiriaNormalItemName = "아페티리아 일반";
        private const string ApetiriaHardItemName = "아페티리아 어려움";
        private const string AbandonRoadGroupName = "어밴던로드";
        private const string SiegeOfSiochanheimBossesItemName = "시오칸 하임 보스 토벌전";
        private const string SiochanheimOdinAllOutWarItemName = "시오칸 하임 오딘 전면전";
        private const string CatacombsHellModeItemName = "카타콤 지옥";
        private const string PravaDefenseItemName = "프라바 방어전";
        private const string CleaningPartTimeJobItemName = "청소 아르바이트";

        private const string AbyssDepthOneItemName = "어비스 - 심층Ⅰ";
        private const string AbyssDepthTwoItemName = "어비스 - 심층Ⅱ";
        private const string AbyssDepthThreeItemName = "어비스 - 심층Ⅲ";
        private const string AbyssHellGroupName = "어비스 지옥";
        private const string RelicItemName = "렐릭";

        private const string LokagosItemName = "로카고스";
        private const string EthosItemName = "에토스";
        private const string CheriaItemName = "체리아";
        private const string MatiaItemName = "마티아";
        private const string TyrorosItemName = "티로로스";
        private const string LycosItemName = "라이코스";

        private const string ImmortalLandItemName = "필멸의 땅";
        private const string CardiffItemName = "카디프";
        private const string OrlanneItemName = "오를란느";

        private const string LokagosLogKeyword = "로카고스의 보관 주머니";
        private const string EthosLogKeyword = "에토스의 보관 주머니";
        private const string CheriaLogKeyword = "체리아의 보관 주머니";
        private const string MatiaLogKeyword = "마티아의 보관 주머니";
        private const string TyrorosLogKeyword = "티로로스의 보관 주머니";
        private const string LycosLogKeyword = "라이코스의 보관 주머니";

        private const string ImmortalLandLogKeyword = "이번 주 어밴던로드 필멸의 땅 지역의 도전 횟수는";
        private const string CardiffLogKeyword = "이번 주 어밴던로드 카디프 지역의 도전 횟수는";
        private const string OrlanneLogKeyword = "이번 주 어밴던로드 오를란느 지역의 도전 횟수는";

        private const string AbyssDepthOneLogKeyword = "어비스 - 심층Ⅰ(보스전) 플레이를 이번 주에 7회 중";
        private const string AbyssDepthTwoLogKeyword = "어비스 - 심층Ⅱ(보스전) 플레이를 이번 주에 7회 중";
        private const string AbyssDepthThreeLogKeyword = "어비스 - 심층Ⅲ(보스전) 플레이를 이번 주에 7회 중";

        private const string CleaningPartTimeJobLogKeyword = "청소 아르바이트 보상 조건을 달성하였습니다.";
        private const string PravaDefenseLogKeyword = "프라바 방어전 성공 보상으로 경험치 1000만을 획득";
        private const string NestOfShinjoLogKeyword = "이번 주 신조 보상을";
        private const string ApetiriaNormalLogKeyword = "[키시니크의 보관 주머니]";
        private const string ApetiriaHardLogKeyword = "[아페티리아 어려움 보상 상자]";
        private const string SiegeOfSiochanheimBossesLogKeyword = "시오칸하임 - 보스 토벌전의 클리어 횟수 :";
        private const string SiochanheimOdinAllOutWarLogKeyword = "시오칸하임 - 오딘 전면전의 클리어 횟수 :";

        private const string EclipseSubjugationLogKeyword = "이클립스 보스 토벌전 보상 상자";
        private const string SupplyRetrievalLogKeyword = "보급품 탈환에 성공하였";
        private const string MoonQueensTrainingCenterLogKeyword = "모든 훈련을 클리어했";
        private const string DetachedForceSubjugationLogKeyword = "별동대 토벌 보상으로 경험치";
        private const string ConfusedLandLogKeyword = "혼란한 대지 미션에 성공하여";
        private const string ColorlessLandLogKeyword = "색을 잃은 땅 미션에 성공하여";
        private const string CoreDungeonLogKeyword = "코어 던전 몬스터를 모두 퇴치하여";
        private const string ExcavationSiteLogKeyword = "모든 일반지역을 토벌하여";
        private const string RelicLogKeyword = "오늘 무료 클리어 횟수 : 1/1 회";
        private const string MiningSiteLogKeyword = "숨겨진 구역으로 이동할 수 있는 포탈이 맵 중앙";
        private const string DimensionalGapLogKeyword = "차원의 틈 봉인에 성공하였";
        private const string AbyssalTreasuryLogKeyword = "심연의 보물창고 밖으로 이동 됩니다";
        private const string LuminousLogKeyword = "금일 [루미너스] 보스 토벌에 성공하였습니다.";
        private const string CravingPleasureLogKeyword = "현재 남은 에너지는";

        public ObservableCollection<DailyWeeklyContentLog> TrackItems { get; }

        public ObservableCollection<DailyWeeklyContentLog> DailyContentItems => _dailyContentItems;

        public ObservableCollection<DailyWeeklyContentLog> WeeklyContentItems => _weeklyContentItems;

        public ObservableCollection<DailyWeeklyContentLog> CompletedItems => _completedItems;

        public bool HasCompletedItems => _completedItems.Count > 0;

        public string ProgressDisplay =>
            $"{TrackItems.Count(i => !i.IsSubItem && i.IsEnabled && i.IsCleared)} / {TrackItems.Count(i => !i.IsSubItem && i.IsEnabled)} 완료";

        public string DailyProgressDisplay =>
            $"{DailyContentItems.Count(i => !i.IsSubItem && i.IsEnabled && i.IsCleared)} / {DailyContentItems.Count(i => !i.IsSubItem && i.IsEnabled)} 완료";

        public string WeeklyProgressDisplay =>
            $"{WeeklyContentItems.Count(i => !i.IsSubItem && i.IsEnabled && i.IsCleared)} / {WeeklyContentItems.Count(i => !i.IsSubItem && i.IsEnabled)} 완료";

        private bool _isSettingsOpen;
        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set
            {
                if (_isSettingsOpen == value) return;
                _isSettingsOpen = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public DailyWeeklyContentWindow(ChatSettings settings)
        {
            _settings = settings;
            var lists = BuildTrackLists();
            TrackItems = lists.TrackItems;
            _dailyWeeklyLogAnalyzer = new DailyWeeklyLogAnalyzer(TrackItems);
            _dailyContentItems.Clear();
            foreach (var item in lists.DailyItems) _dailyContentItems.Add(item);
            _weeklyContentItems.Clear();
            foreach (var item in lists.WeeklyItems) _weeklyContentItems.Add(item);
            ApplySettings();
            InitializeComponent();
            DataContext = this;
            this.FontFamily = FontService.GetFont(_settings.FontFamily);

            foreach (var item in TrackItems)
                item.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(DailyWeeklyContentLog.IsCleared) ||
                        e.PropertyName == nameof(DailyWeeklyContentLog.IsEnabled))
                    {
                        OnPropertyChanged(nameof(ProgressDisplay));
                        OnPropertyChanged(nameof(DailyProgressDisplay));
                        OnPropertyChanged(nameof(WeeklyProgressDisplay));
                        if (e.PropertyName == nameof(DailyWeeklyContentLog.IsCleared))
                        {
                            ReorderItems();
                            if (!item.HasCount)
                                SaveItemConfig(item);
                        }
                    }
                    else if (e.PropertyName == nameof(DailyWeeklyContentLog.CurrentCount))
                    {
                        SaveItemConfig(item);
                    }
                };

            ReorderItems();
            InitializeResetTimer();
            LocationChanged += DailyWeeklyContentWindow_LocationChanged;
        }

        private void DailyWeeklyContentWindow_LocationChanged(object? sender, EventArgs e)
        {
            PersistWindowPosition();
        }

        protected override void OnClosed(EventArgs e)
        {
            _resetTimer?.Stop();

            try
            {
                PersistWindowPosition();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to persist DailyWeekly window position on close.", ex);
            }

            base.OnClosed(e);
        }

        private void PersistWindowPosition()
        {
            _settings.DailyWeeklyContentOverlayLeft = this.Left;
            _settings.DailyWeeklyContentOverlayTop = this.Top;
            ConfigService.SaveDeferred(_settings);
        }

        private static (ObservableCollection<DailyWeeklyContentLog> TrackItems,
            ObservableCollection<DailyWeeklyContentLog> DailyItems,
            ObservableCollection<DailyWeeklyContentLog> WeeklyItems) BuildTrackLists()
        {
            var subBosses = new DailyWeeklyContentLog[]
            {
                new() { Name = LokagosItemName, IsSubItem = true, LogKeyword = LokagosLogKeyword },
                new() { Name = EthosItemName,   IsSubItem = true, LogKeyword = EthosLogKeyword },
                new() { Name = CheriaItemName,   IsSubItem = true, LogKeyword = CheriaLogKeyword },
                new() { Name = MatiaItemName,   IsSubItem = true, LogKeyword = MatiaLogKeyword },
                new() { Name = TyrorosItemName, IsSubItem = true, LogKeyword = TyrorosLogKeyword },
                new() { Name = LycosItemName, IsSubItem = true, LogKeyword = LycosLogKeyword },
            };

            var eclipseBoss = new DailyWeeklyContentLog { Name = EclipseBossRaidItemName, Children = subBosses };

            var abaddonItems = new DailyWeeklyContentLog[]
            {
                new() { Name = ImmortalLandItemName, IsSubItem = true, IsWeekly = true, DefaultMaxCount = 10, MaxCount = 10, LogKeyword = ImmortalLandLogKeyword },
                new() { Name = CardiffItemName, IsSubItem = true, IsWeekly = true, DefaultMaxCount = 10, MaxCount = 10, LogKeyword = CardiffLogKeyword },
                new() { Name = OrlanneItemName, IsSubItem = true, IsWeekly = true, DefaultMaxCount = 10, MaxCount = 10, LogKeyword = OrlanneLogKeyword }
            };

            var abaddonGroup = new DailyWeeklyContentLog { Name = AbandonRoadGroupName, Children = abaddonItems, IsWeekly = true };

            var abyssItems = new DailyWeeklyContentLog[]
            {
                new() { Name = AbyssDepthOneItemName, IsSubItem = true, IsWeekly = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = AbyssDepthOneLogKeyword },
                new() { Name = AbyssDepthTwoItemName, IsSubItem = true, IsWeekly = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = AbyssDepthTwoLogKeyword },
                new() { Name = AbyssDepthThreeItemName, IsSubItem = true, IsWeekly = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = AbyssDepthThreeLogKeyword }
            };

            var abyssGroup = new DailyWeeklyContentLog { Name = AbyssHellGroupName, Children = abyssItems, IsWeekly = true };

            var weeklyItems = new DailyWeeklyContentLog[]
            {
                new() { Name = CleaningPartTimeJobItemName, IsWeekly = true, LogKeyword = CleaningPartTimeJobLogKeyword },
                new() { Name = PravaDefenseItemName, IsWeekly = true, DefaultMaxCount = 5, MaxCount = 5, LogKeyword = PravaDefenseLogKeyword },
                new() { Name = CatacombsHellModeItemName, IsWeekly = true, DefaultMaxCount = 5, MaxCount = 5 },
                abyssGroup,
                new() { Name = NestOfShinjoHardItemName, IsWeekly = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = NestOfShinjoLogKeyword },
                new() { Name = ApetiriaNormalItemName, IsWeekly = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = ApetiriaNormalLogKeyword  },
                new() { Name = ApetiriaHardItemName, IsWeekly = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = ApetiriaHardLogKeyword  },
                new() { Name = SiegeOfSiochanheimBossesItemName, IsWeekly = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SiegeOfSiochanheimBossesLogKeyword },
                new() { Name = SiochanheimOdinAllOutWarItemName, IsWeekly = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SiochanheimOdinAllOutWarLogKeyword },
                abaddonGroup
            };

            var dailyItems = new DailyWeeklyContentLog[]
            {
                new() { Name = MercurialCaveItemName, DefaultMaxCount = MercurialCaveDefaultMaxCount, MaxCount = MercurialCaveDefaultMaxCount },
                new() { Name = LuminousItemName, LogKeyword = LuminousLogKeyword },
                eclipseBoss,
                new() { Name = EclipseSubjugationItemName, DefaultMaxCount = 3, MaxCount = 3, LogKeyword = EclipseSubjugationLogKeyword },
                new() { Name = SupplyRetrievalItemName, LogKeyword = SupplyRetrievalLogKeyword },
                new() { Name = MoonQueensTrainingCenterItemName, LogKeyword = MoonQueensTrainingCenterLogKeyword },
                new() { Name = DetachedForceSubjugationItemName, LogKeyword = DetachedForceSubjugationLogKeyword },
                new() { Name = ConfusedLandItemName, LogKeyword = ConfusedLandLogKeyword },
                new() { Name = ColorlessLandItemName, LogKeyword = ColorlessLandLogKeyword },
                new() { Name = CoreDungeonItemName, LogKeyword = CoreDungeonLogKeyword },
                new() { Name = ExcavationSiteItemName, LogKeyword = ExcavationSiteLogKeyword },
                new() { Name = RelicItemName, LogKeyword = RelicLogKeyword },
                new() { Name = MiningSiteItemName, LogKeyword = MiningSiteLogKeyword },
                new() { Name = DimensionalGapItemName, LogKeyword = DimensionalGapLogKeyword },
                new() { Name = AbyssalTreasuryItemName, LogKeyword = AbyssalTreasuryLogKeyword },
                new() { Name = CravingPleasureItemName, DefaultMaxCount = 20, MaxCount = 20, ClearThreshold = 5, LogKeyword = CravingPleasureLogKeyword }
            };

            var weeklyGroup = new DailyWeeklyContentLog { Name = WeeklyContentsGroupName, Children = weeklyItems };
            var dailyGroup = new DailyWeeklyContentLog { Name = DailyContentsGroupName, Children = dailyItems };

            var list = new ObservableCollection<DailyWeeklyContentLog> { dailyGroup };
            foreach (var item in dailyItems)
            {
                list.Add(item);
                if (ReferenceEquals(item, eclipseBoss))
                    foreach (var boss in subBosses) list.Add(boss);
            }

            list.Add(weeklyGroup);
            foreach (var item in weeklyItems)
            {
                list.Add(item);
                if (ReferenceEquals(item, abyssGroup))
                    foreach (var child in abyssItems) list.Add(child);
                if (ReferenceEquals(item, abaddonGroup))
                    foreach (var child in abaddonItems) list.Add(child);
            }

            var dailyDisplay = new ObservableCollection<DailyWeeklyContentLog>();
            foreach (var item in dailyItems)
            {
                dailyDisplay.Add(item);
                if (ReferenceEquals(item, eclipseBoss))
                    foreach (var boss in subBosses) dailyDisplay.Add(boss);
            }

            var weeklyDisplay = new ObservableCollection<DailyWeeklyContentLog>();
            foreach (var item in weeklyItems)
            {
                weeklyDisplay.Add(item);
                if (ReferenceEquals(item, abyssGroup))
                    foreach (var child in abyssItems) weeklyDisplay.Add(child);
                if (ReferenceEquals(item, abaddonGroup))
                    foreach (var child in abaddonItems) weeklyDisplay.Add(child);
            }

            return (list, dailyDisplay, weeklyDisplay);
        }

        private void ReorderItems()
        {
            _completedItems.Clear();
            AddCompletedFrom(DailyContentItems);
            AddCompletedFrom(WeeklyContentItems);
            OnPropertyChanged(nameof(HasCompletedItems));
        }

        private void AddCompletedFrom(ObservableCollection<DailyWeeklyContentLog> source)
        {
            foreach (var item in source)
            {
                if (item.IsSubItem) continue;
                if (item.IsCleared)
                {
                    item.IsHidden = true;
                    _completedItems.Add(item);
                    if (item.HasChildren)
                        foreach (var child in item.Children!)
                        {
                            child.IsHidden = true;
                            _completedItems.Add(child);
                        }
                }
                else
                {
                    item.IsHidden = false;
                    if (item.HasChildren)
                        foreach (var child in item.Children!)
                            child.IsHidden = false;
                }
            }
        }

        private void ApplySettings()
        {
            foreach (var item in TrackItems)
            {
                if (!_settings.DungeonItemConfigs.TryGetValue(item.Name, out var config)) continue;
                item.IsEnabled = config.IsEnabled;
                if (config.RequiredCount > 0)
                    item.MaxCount = config.RequiredCount;
            }
        }

        private void SaveItemConfig(DailyWeeklyContentLog item)
        {
            if (_suppressSave) return;
            if (!_settings.DungeonItemConfigs.TryGetValue(item.Name, out var config))
                config = new DungeonItemConfig();
            config.IsEnabled = item.IsEnabled;
            config.RequiredCount = (item.MaxCount == item.DefaultMaxCount) ? 0 : item.MaxCount;
            config.CurrentCount = 0;
            config.IsCleared = false;
            config.SavedAt = DateTime.MinValue;
            _settings.DungeonItemConfigs[item.Name] = config;
            ConfigService.SaveDeferred(_settings);
        }

        private void SaveAllConfigs()
        {
            foreach (var item in TrackItems.Where(i => !i.HasChildren))
            {
                if (!_settings.DungeonItemConfigs.TryGetValue(item.Name, out var config))
                    config = new DungeonItemConfig();
                config.IsEnabled = item.IsEnabled;
                config.RequiredCount = (item.MaxCount == item.DefaultMaxCount) ? 0 : item.MaxCount;
                config.CurrentCount = 0;
                config.IsCleared = false;
                config.SavedAt = DateTime.MinValue;
                _settings.DungeonItemConfigs[item.Name] = config;
            }
            ConfigService.SaveDeferred(_settings);
        }

        /// <summary>
        /// 로그 파일 전체를 백그라운드에서 스캔하여 금일 클리어 현황을 복원합니다.
        /// </summary>
        public async Task ScanHistoricalLogsAsync()
        {
            DateTime today = DateTime.Today;

            var dailyItems = TrackItems
                .Where(i => (!i.IsWeekly) && (i.LogKeyword != null || i.Name == MercurialCaveItemName))
                .ToList();
            var weeklyItems = TrackItems.Where(i => i.LogKeyword != null && i.IsWeekly).ToList();

            _suppressSave = true;
            try
            {
                ResetScannedItems(dailyItems);
                ResetScannedItems(weeklyItems);
                await ScanAndApplyAsync(dailyItems, new[] { GetLogPath(today) });
                await ScanAndApplyAsync(weeklyItems, GetWeekLogPaths(today));
            }
            finally
            {
                _suppressSave = false;
                SaveAllConfigs();
            }
        }

        private static void ResetScannedItems(IEnumerable<DailyWeeklyContentLog> items)
        {
            foreach (var item in items)
            {
                if (item.HasChildren)
                {
                    continue;
                }

                item.Reset();
                item.Detail = null;
            }
        }

        private IEnumerable<string> GetWeekLogPaths(DateTime today)
        {
            int diff = (7 + (int)today.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            DateTime monday = today.AddDays(-diff);
            for (int i = 0; i < 7; i++)
                yield return GetLogPath(monday.AddDays(i));
        }

        private string GetLogPath(DateTime date)
            => Path.Combine(_settings.ChatLogFolderPath, $"TWChatLog_{date:yyyy_MM_dd}.html");

        private async Task ScanAndApplyAsync(IReadOnlyList<DailyWeeklyContentLog> items, IEnumerable<string> filePaths)
        {
            if (items.Count == 0) return;

            var counts = items
                .Where(i => i.LogKeyword != null)
                .ToDictionary(i => i.LogKeyword!, _ => 0);

            var scanList = items
                .Where(i => i.LogKeyword != null)
                .Select(i => (Primary: i.LogKeyword!, Secondary: i.LogKeyword2))
                .ToList();

            var lastMatchedLines = new Dictionary<DailyWeeklyContentLog, string>();
            var maxExtractedCounts = new Dictionary<DailyWeeklyContentLog, int>();
            var specialCounts = new Dictionary<string, int>();
            await Task.Run(() =>
            {
                try
                {
                    var seenPrimary = new HashSet<string>();
                    string? pendingAbyssFloor = null;
                    bool pendingMercurialEntry = false;
                    int mercurialClearCount = 0;
                    foreach (var filePath in filePaths)
                    {
                        if (!File.Exists(filePath)) continue;

                        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(stream, Encoding.GetEncoding(949));
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (DailyWeeklyLogAnalyzer.IsMercurialEntryMessage(line))
                            {
                                pendingMercurialEntry = true;
                            }
                            else if (pendingMercurialEntry && DailyWeeklyLogAnalyzer.IsMercurialRewardMessage(line))
                            {
                                mercurialClearCount++;
                                pendingMercurialEntry = false;
                            }

                            if (DailyWeeklyLogAnalyzer.TryMatchAbyssFloor(line, out string abyssFloor))
                                pendingAbyssFloor = abyssFloor;

                            if (DailyWeeklyLogAnalyzer.TryMatchAbyssReward(line, out int abyssValue) && pendingAbyssFloor != null)
                            {
                                string abyssKey = $"{DailyWeeklyLogAnalyzer.AbyssSpecialKeyPrefix}{pendingAbyssFloor}";
                                if (!specialCounts.TryGetValue(abyssKey, out int prevAbyss) || abyssValue > prevAbyss)
                                    specialCounts[abyssKey] = abyssValue;
                                pendingAbyssFloor = null;
                            }

                            if (DailyWeeklyLogAnalyzer.TryMatchSinjoReward(line, out int sinjoValue))
                            {
                                if (!specialCounts.TryGetValue(DailyWeeklyLogAnalyzer.SinjoSpecialKey, out int prevSinjo) || sinjoValue > prevSinjo)
                                    specialCounts[DailyWeeklyLogAnalyzer.SinjoSpecialKey] = sinjoValue;
                            }

                            foreach (var item in items)
                            {
                                if (item.LogKeyword == null || !line.Contains(item.LogKeyword)) continue;
                                if (DailyWeeklyLogAnalyzer.TryExtractDetailValue(item, line, out int v, out var detailKind) &&
                                    detailKind is DailyWeeklyDetailKind.Siochanheim or DailyWeeklyDetailKind.AbandonRoad)
                                {
                                    if (!maxExtractedCounts.TryGetValue(item, out int prev) || v > prev)
                                        maxExtractedCounts[item] = v;
                                }
                                else if (item.LogKeyword.Contains(DailyWeeklyLogAnalyzer.CravingPleasureKeywordToken, StringComparison.Ordinal))
                                {
                                    lastMatchedLines[item] = line;
                                }
                            }
                            foreach (var (primary, secondary) in scanList)
                            {
                                if (secondary == null)
                                {
                                    if (line.Contains(primary))
                                        counts[primary]++;
                                }
                                else
                                {
                                    if (line.Contains(primary))
                                        seenPrimary.Add(primary);
                                    else if (line.Contains(secondary) && seenPrimary.Remove(primary))
                                        counts[primary]++;
                                }
                            }
                        }
                    }

                    if (mercurialClearCount > 0)
                        specialCounts[DailyWeeklyLogAnalyzer.MercurialCaveSpecialKey] = mercurialClearCount;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"던전 로그 스캔 실패: {ex.Message}");
                }
            });

            foreach (var (item, maxVal) in maxExtractedCounts)
            {
                if (item.MaxCount == 0 && item.LogKeyword?.Contains(DailyWeeklyLogAnalyzer.AbandonRoadKeywordToken, StringComparison.Ordinal) == true)
                    item.MaxCount = 10;
                item.SetCount(maxVal);
            }
            foreach (var kvp in lastMatchedLines)
                DailyWeeklyLogAnalyzer.TryUpdateDetail(kvp.Key, kvp.Value, out _);

            foreach (var item in items)
            {
                if (item.LogKeyword == null) continue;
                if (item.LogKeyword.Contains(DailyWeeklyLogAnalyzer.AbandonRoadKeywordToken, StringComparison.Ordinal) ||
                    item.LogKeyword.Contains(DailyWeeklyLogAnalyzer.CravingPleasureKeywordToken, StringComparison.Ordinal) ||
                    item.LogKeyword.Contains(DailyWeeklyLogAnalyzer.AbyssKeywordToken, StringComparison.Ordinal) ||
                    item.LogKeyword.Contains(DailyWeeklyLogAnalyzer.NestOfShinjoKeywordToken, StringComparison.Ordinal) ||
                    item.LogKeyword.Contains(DailyWeeklyLogAnalyzer.SiochanheimKeywordToken, StringComparison.Ordinal))
                    continue;
                if (!counts.TryGetValue(item.LogKeyword, out int count) || count == 0) continue;
                int times = item.HasCount ? Math.Min(count, item.MaxCount) : 1;
                for (int i = 0; i < times; i++)
                    item.Mark();
            }

            DailyWeeklyLogAnalyzer.ApplySpecialCounts(items, specialCounts);
        }

        /// <summary>
        /// 시스템 로그 텍스트를 받아 키워드와 일치하는 항목을 자동으로 체크합니다.
        /// </summary>
        public void ProcessLog(string text)
        {
            _dailyWeeklyLogAnalyzer.Process(text);
        }

        public void ProcessLog(LogAnalysisResult analysis)
        {
            if (!analysis.ShouldRunDailyWeeklyContent)
                return;

            _dailyWeeklyLogAnalyzer.Process(analysis);
        }

        private async void Settings_Click(object sender, RoutedEventArgs e)
        {
            IsSettingsOpen = !IsSettingsOpen;
            if (!IsSettingsOpen && _pendingRescanAfterSettings)
            {
                _pendingRescanAfterSettings = false;
                await ScanHistoricalLogsAsync();
            }
        }

        private void CountUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DailyWeeklyContentLog item)
            {
                item.MaxCount = item.EffectiveRequiredCount + 1;
                SaveItemConfig(item);
                _pendingRescanAfterSettings = true;
            }
        }

        private void CountDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DailyWeeklyContentLog item)
            {
                int newCount = item.EffectiveRequiredCount - 1;
                if (newCount < 1) return;
                item.MaxCount = (newCount == 1 && item.DefaultMaxCount == 0) ? 0 : newCount;
                SaveItemConfig(item);
                _pendingRescanAfterSettings = true;
            }
        }

        private void SettingEnabled_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is DailyWeeklyContentLog item)
            {
                SaveItemConfig(item);
                if (item.Children != null)
                    foreach (var child in item.Children)
                        SaveItemConfig(child);
            }
        }

        private void Increment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is DailyWeeklyContentLog item)
                item.Mark();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

        private void ResizeGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                SendMessage(hwnd, 0x00A1, (IntPtr)17, IntPtr.Zero);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in TrackItems.Where(i => !i.IsSubItem))
                item.Reset();
            _dailyWeeklyLogAnalyzer.ResetPending();
        }

        private void InitializeResetTimer()
        {
            _lastDailyResetDate = DateTime.Today;
            _lastWeeklyResetKey = GetWeeklyResetKey(DateTime.Now);
            _lastAbaddonResetKey = GetAbaddonResetKey(DateTime.Now);

            _resetTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
            _resetTimer.Tick += ResetTimer_Tick;
            _resetTimer.Start();
        }

        private void ResetTimer_Tick(object? sender, EventArgs e)
        {
            var now = DateTime.Now;

            if (DateTime.Today != _lastDailyResetDate)
            {
                _lastDailyResetDate = DateTime.Today;
                ResetDailyItems();
            }

            var weeklyKey = GetWeeklyResetKey(now);
            if (weeklyKey != _lastWeeklyResetKey)
            {
                _lastWeeklyResetKey = weeklyKey;
                ResetWeeklyItems();
            }

            var abaddonKey = GetAbaddonResetKey(now);
            if (abaddonKey != _lastAbaddonResetKey)
            {
                _lastAbaddonResetKey = abaddonKey;
                ResetAbaddonItems();
            }
        }

        private static DateTime GetWeeklyResetKey(DateTime now)
        {
            int diff = (7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return now.Date.AddDays(-diff);
        }

        private static DateTime GetAbaddonResetKey(DateTime now)
        {
            int diff = (7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            DateTime monday = now.Date.AddDays(-diff);
            DateTime resetTime = monday.AddHours(6);
            return now >= resetTime ? resetTime : resetTime.AddDays(-7);
        }

        private void ResetDailyItems()
        {
            foreach (var item in TrackItems.Where(i => !i.IsSubItem && !i.IsWeekly && i.Name != WeeklyContentsGroupName))
                item.Reset();
            _dailyWeeklyLogAnalyzer.ResetPending();
        }

        private void ResetWeeklyItems()
        {
            foreach (var item in TrackItems.Where(i => !i.IsSubItem && i.IsWeekly && i.Name != AbandonRoadGroupName))
                item.Reset();
        }

        private void ResetAbaddonItems()
        {
            TrackItems.FirstOrDefault(i => i.Name == AbandonRoadGroupName)?.Reset();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try { _resetTimer?.Stop(); }
            catch (Exception ex) { AppLogger.Warn("Failed to stop DailyWeekly reset timer during close.", ex); }
            base.OnClosing(e);
        }


    }
}
