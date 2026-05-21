using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
        private readonly Dictionary<string, AccumulatedCountState> _AbandonCountStates = new();
        private readonly ScanCache _scanCache = new();
        private bool _suppressSave = false;
        private bool _pendingRescanAfterSettings = false;
        private bool _isLoading;
        private DispatcherTimer? _resetTimer;
        private DateTime _lastDailyResetDate;
        private DateTime _lastWeeklyResetKey;
        private DateTime _lastAbandonResetKey;
        private const string DailyContentsGroupName = "일일 컨텐츠";
        private const string WeeklyContentsGroupName = "주간 컨텐츠";
        private const string WeeklyMercurialGroupName = "머큐리얼";
        private const string WeeklyAbyssGroupName = "어비스";
        private const string WeeklyEclipseGroupName = "이클립스";
        private const string WeeklyOtherGroupName = "기타지역";

        private const string SallionItemName = "샐리온";
        private const string SeleanaItemName = "샐레아나";
        private const string SilaironItemName = "실라이론";
        private const string SilbanItemName = "실반";
        private const string LuminousItemName = "루미너스";
        private const string LuminousExItemName = "루미너스(EX)";
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
        private const string VestigeItemName = "베스티지";
        private const string OrlyDefenseHellItemName = "오를리 방어전 지옥";
        private const string NestOfShinjoHardItemName = "신조의 둥지 어려움";
        private const string ApetiriaItemName = "아페티리아";
        private const string AbandonRoadGroupName = "어밴던로드";
        private const string SiegeOfSiochanheimBossesItemName = "시오칸 하임 보스 토벌전";
        private const string SiochanheimOdinAllOutWarItemName = "시오칸 하임 오딘 전면전";
        private const string CatacombsHellModeItemName = "카타콤 지옥";
        private const string PravaDefenseItemName = "프라바 방어전";
        private const string CleaningPartTimeJobItemName = "청소 아르바이트";
        private const string ApetiriaExItemName = "아페티리아 EX";
        private const string EclipseCoreMasterGroupName = "이클립스 코어 마스터";
        private const string AbysCoreMasterGroupName = "어비스 코어 마스터";
        private const string MercurialCoreMasterGroupName = "머큐리얼 코어 마스터";
        private const string MercurialWeeklyDungeonGroupName = "머큐리얼 주간";
        private const string SallionCoreDungeonItemName = "샐리온 코어 마스터 던전";
        private const string SeleanaCoreDungeonItemName = "샐레아나 코어 마스터 던전";
        private const string SilaironCoreDungeonItemName = "실라이론 코어 마스터 던전";
        private const string SilbanCoreDungeonItemName = "실반 코어 마스터 던전";
        private const string LuminousCoreDungeonItemName = "루미너스 코어 마스터 던전";
        private const string FinalBattleItemName = "최후의 결전";
        private const string FollowingJoyNormalItemName = "추종하는 환희(일반)";
        private const string GazingSorrowNormalItemName = "응시하는 슬픔(일반)";
        private const string FollowingJoyHardItemName = "추종하는 환희(어려움)";
        private const string GazingSorrowHardItemName = "응시하는 슬픔(어려움)";
        private const string AfterimageOfJoyItemName = "환희의 잔상";

        private const string LokagosCoreMasterItemName = "로카고스 코어 마스터";
        private const string EthosCoreMasterItemName = "에토스 코어 마스터";
        private const string CheriaCoreMasterItemName = "체리아 코어 마스터";
        private const string MatiaCoreMasterItemName = "마티아 코어 마스터";
        private const string LycosCoreMasterItemName = "라이코스 코어 마스터";
        private const string TyrorosCoreMasterItemName = "티로로스 코어 마스터";

        private const string AbyssDepthOneCoreMasterItemName = "심층Ⅰ 코어 마스터";
        private const string AbyssDepthTwoCoreMasterItemName = "심층Ⅱ 코어 마스터";
        private const string AbyssDepthThreeCoreMasterItemName = "심층Ⅲ 코어 마스터";

        private const string SallionCoreMasterItemName = SallionItemName;
        private const string SeleanaCoreMasterItemName = SeleanaItemName;
        private const string SilaironCoreMasterItemName = SilaironItemName;
        private const string SilbanCoreMasterItemName = SilbanItemName;
        private const string LuminousCoreMasterItemName = LuminousItemName;
        private const string LuminousExCoreMasterItemName = LuminousExItemName;

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

        private const string LokagosLogKeyword = "이클립스 보스전(로카고스) 클리어 횟수:";
        private const string EthosLogKeyword = "이클립스 보스전(에토스) 클리어 횟수:";
        private const string CheriaLogKeyword = "이클립스 보스전(체리아) 클리어 횟수:";
        private const string MatiaLogKeyword = "이클립스 보스전(마티아) 클리어 횟수:";
        private const string TyrorosLogKeyword = "이클립스 보스전(티로로스) 클리어 횟수:";
        private const string LycosLogKeyword = "이클립스 보스전(라이코스) 클리어 횟수:";

        private const string ImmortalLandLogKeyword = "이번 주 어밴던로드 필멸의 땅 지역의 도전 횟수는";
        private const string CardiffLogKeyword = "이번 주 어밴던로드 카디프 지역의 도전 횟수는";
        private const string OrlanneLogKeyword = "이번 주 어밴던로드 오를란느 지역의 도전 횟수는";

        private const string AbyssDepthOneLogKeyword = "어비스 - 심층Ⅰ(보스전) 플레이를 이번 주에 7회 중";
        private const string AbyssDepthTwoLogKeyword = "어비스 - 심층Ⅱ(보스전) 플레이를 이번 주에 7회 중";
        private const string AbyssDepthThreeLogKeyword = "어비스 - 심층Ⅲ(보스전) 플레이를 이번 주에 7회 중";

        private const string CleaningPartTimeJobLogKeyword = "청소 아르바이트 보상 조건을 달성하였습니다.";
        private const string PravaDefenseLogKeyword = "프라바 방어전 성공 보상으로 경험치 1000만을 획득";
        private const string CatacombsHellModeLogKeyword = "이번 주 사명의 계승자 닉스 보상을";
        private const string NestOfShinjoLogKeyword = "이번 주 신조 보상을";
        private const string ApetiriaLogKeyword = "아페티리아 클리어 횟수:";
        private const string SiegeOfSiochanheimBossesLogKeyword = "시오칸하임 - 보스 토벌전의 클리어 횟수 :";
        private const string SiochanheimOdinAllOutWarLogKeyword = "시오칸하임 - 오딘 전면전의 클리어 횟수 :";
        private const string ApetiriaExLogKeyword = "아페티리아(EX) 클리어 횟수:";
        private const string LokagosCoreMasterLogKeyword = "로카고스 코어 마스터 클리어 횟수:";
        private const string EthosCoreMasterLogKeyword = "에토스 코어 마스터 클리어 횟수:";
        private const string CheriaCoreMasterLogKeyword = "체리아 코어 마스터 클리어 횟수:";
        private const string MatiaCoreMasterLogKeyword = "마티아 코어 마스터 클리어 횟수:";
        private const string LycosCoreMasterLogKeyword = "라이코스 코어 마스터 클리어 횟수:";
        private const string TyrorosCoreMasterLogKeyword = "티로로스 코어 마스터 클리어 횟수:";
        private const string AbyssDepthOneCoreMasterLogKeyword = "코어 마스터 - 심층Ⅰ 클리어 횟수:";
        private const string AbyssDepthTwoCoreMasterLogKeyword = "코어 마스터 - 심층Ⅱ 클리어 횟수:";
        private const string AbyssDepthThreeCoreMasterLogKeyword = "코어 마스터 - 심층Ⅲ 클리어 횟수:";
        private const string SallionCoreMasterLogKeyword = "샐리온 클리어 횟수:";
        private const string SeleanaCoreMasterLogKeyword = "샐레아나 클리어 횟수:";
        private const string SilaironCoreMasterLogKeyword = "실라이론 클리어 횟수:";
        private const string SilbanCoreMasterLogKeyword = "실반 클리어 횟수:";
        private const string LuminousCoreMasterLogKeyword = "루미너스 클리어 횟수:";
        private const string LuminousExCoreMasterLogKeyword = "루미너스(EX) 클리어 횟수:";
        private const string SallionCoreDungeonLogKeyword = "샐리온 코어 마스터 던전 클리어 횟수:";
        private const string SeleanaCoreDungeonLogKeyword = "샐레아나 코어 마스터 던전 클리어 횟수:";
        private const string SilaironCoreDungeonLogKeyword = "실라이론 코어 마스터 던전 클리어 횟수:";
        private const string SilbanCoreDungeonLogKeyword = "실반 코어 마스터 던전 클리어 횟수:";
        private const string LuminousCoreDungeonLogKeyword = "루미너스 코어 마스터 던전 클리어 횟수:";
        private const string FinalBattleLogKeyword = "티로로스의 계략을 막아내었습니다. 잠시 후 기억의 숲 전초기지로 이동됩니다.";
        private const string FollowingJoyNormalLogKeyword = "레이티아 퇴치 보상으로 레이티아 보상 상자 (일반) 1개, 루비코나 코어 상자 20개를";
        private const string GazingSorrowNormalLogKeyword = "설계자 퇴치 보상으로 설계자 보상 상자 (일반) 1개, 루비코나 코어 상자 20개를 획득";
        private const string FollowingJoyHardLogKeyword = "레이티아 퇴치 보상으로 레이티아 보상 상자 (어려움) 1개, 루비코나 코어 상자 30개";
        private const string GazingSorrowHardLogKeyword = "설계자 퇴치 보상으로 설계자 보상 상자 (어려움) 1개, 루비코나 코어 상자 30개";
        private const string AfterimageOfJoyLogKeyword = "[환희의 레이티아 보상 상자] 아이템을 1개 획득하였습니다.";

        private const string EclipseSubjugationLogKeyword = "이클립스 보스 토벌전 클리어 횟수:";
        private const string SupplyRetrievalLogKeyword = "보급품 탈환 클리어 횟수:";
        private const string MoonQueensTrainingCenterLogKeyword = "달여왕 군대 훈련소 클리어 횟수:";
        private const string DetachedForceSubjugationLogKeyword = "별동대 토벌 클리어 횟수:";
        private const string ConfusedLandLogKeyword = "혼란한 대지 미션에 성공하여";
        private const string ColorlessLandLogKeyword = "색을 잃은 땅 미션에 성공하여";
        private const string CoreDungeonLogKeyword = "보스 몬스터를 퇴치하세요.";
        private const string CoreDungeonLogKeyword2 = "던전을 클리어 하였습니다. 곧 마을로 돌아가게 됩니다.";
        private const string ExcavationSiteLogKeyword = "모든 일반지역을 토벌하여";
        private const string RelicLogKeyword = "?고대 렐릭의 성소? - 주간 무료 클리어 횟수 :";
        private const string RelicWeeklyClearToken1 = "고대 렐릭의 성소";
        private const string RelicWeeklyClearToken2 = "주간 무료 클리어 횟수";
        private const string FreeClearCountKeyword = "오늘 무료 클리어 횟수 : 1/1 회";
        private const string MiningSiteLogKeyword = "숨겨진 구역으로 이동할 수 있는 포탈이 맵 중앙";
        private const string DimensionalGapLogKeyword = "차원의 틈 봉인에 성공하였";
        private const string AbyssalTreasuryLogKeyword = "심연의 보물창고 입장 횟수:";
        private const string CravingPleasureLogKeyword = "남은 에너지는";
        private const string VestigeLogKeyword = "[성난 빅테디의 별사탕] 아이템을 획득하였습니다.";
        private const string OrlyDefenseHellEntryLogKeyword = "남은 공격 횟수 : 1";
        private const string OrlyDefenseHellClearLogKeyword = "[경험의 정수] 아이템을 획득하였습니다.";

        private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhiteSpaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex ContentArchiveEntryRegex = new(
            "<div\\s+class=\"log\\s+content\"(?<attrs>[^>]*)>(?<text>.*?)</div>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ArchiveAttrRegex = new(
            "(?<name>data-[a-z0-9\\-]+)=\"(?<value>[^\"]*)\"",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex AbandonRoadCountRegex = new(
            @"이번\s*주\s*어밴던\s*로드\s*(?<region>필멸의\s*땅|카디프|오를란느)\s*지역의\s*도전\s*횟수는\s*(?<count>\d+)\s*번",
            RegexOptions.Compiled);
        private static readonly Regex CravingPleasureEnergyRegex = new(
            @"남은\s*에너지는\s*\[\s*(?<remain>\d+)\s*\]",
            RegexOptions.Compiled);
        private static readonly Regex FreeClearTitleRegex = new(
            @"[?？]\s*(?<title>[^?？]+?)\s*[?？]\s*-\s*오늘 무료 클리어 횟수\s*:\s*1/1\s*회",
            RegexOptions.Compiled);

        private sealed class ItemStateSnapshot
        {
            public bool IsEnabled { get; init; }
            public int MaxCount { get; init; }
            public int CurrentCount { get; init; }
            public bool IsCleared { get; init; }
            public string? Detail { get; init; }
        }

        private sealed class ScanCache
        {
            public ItemStateSnapshotMap? Snapshot { get; set; }
            public bool IsDirty { get; set; } = true;
            public DateTime DailyResetDate { get; set; } = DateTime.MinValue;
            public DateTime WeeklyResetKey { get; set; } = DateTime.MinValue;
            public DateTime AbandonResetKey { get; set; } = DateTime.MinValue;
        }

        private sealed class ItemStateSnapshotMap : Dictionary<string, ItemStateSnapshot>
        {
            public ItemStateSnapshotMap()
                : base(StringComparer.Ordinal)
            {
            }
        }

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
        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (_isLoading == value) return;
                _isLoading = value;
                OnPropertyChanged();
            }
        }

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

        public double DailyWeeklyContentFontSize
        {
            get => _settings.DailyWeeklyContentFontSize;
            set
            {
                double clamped = Math.Max(10.0, Math.Min(28.0, value));
                if (Math.Abs(_settings.DailyWeeklyContentFontSize - clamped) < 0.0001) return;
                _settings.DailyWeeklyContentFontSize = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DailyWeeklyContentFontSizeDisplay));
                ConfigService.SaveDeferred(_settings);
            }
        }

        public string DailyWeeklyContentFontSizeDisplay => $"{Math.Round(DailyWeeklyContentFontSize):0}px";

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
            if (_settings.DailyWeeklyContentOverlayWidth.HasValue && _settings.DailyWeeklyContentOverlayWidth.Value > 100)
                Width = _settings.DailyWeeklyContentOverlayWidth.Value;
            if (_settings.DailyWeeklyContentOverlayHeight.HasValue && _settings.DailyWeeklyContentOverlayHeight.Value > 100)
                Height = _settings.DailyWeeklyContentOverlayHeight.Value;
            DataContext = this;
            this.FontFamily = FontService.GetFont(_settings.FontFamily);
            InitializeScanCache();

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
                        }
                    }
                };

            ReorderItems();
            InitializeResetTimer();
            LocationChanged += DailyWeeklyContentWindow_LocationChanged;
        }

        private void InitializeScanCache()
        {
            _scanCache.Snapshot = null;
            _scanCache.IsDirty = true;
            _scanCache.DailyResetDate = DateTime.MinValue;
            _scanCache.WeeklyResetKey = DateTime.MinValue;
            _scanCache.AbandonResetKey = DateTime.MinValue;
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
            _settings.DailyWeeklyContentOverlayWidth = this.Width;
            _settings.DailyWeeklyContentOverlayHeight = this.Height;
            ConfigService.SaveDeferred(_settings);
        }

        private static (ObservableCollection<DailyWeeklyContentLog> TrackItems,
            ObservableCollection<DailyWeeklyContentLog> DailyItems,
            ObservableCollection<DailyWeeklyContentLog> WeeklyItems) BuildTrackLists()
        {
            var subBosses = new DailyWeeklyContentLog[]
            {
                new() { Name = LokagosItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = LokagosLogKeyword },
                new() { Name = EthosItemName,   IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = EthosLogKeyword },
                new() { Name = CheriaItemName,  IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = CheriaLogKeyword },
                new() { Name = MatiaItemName,   IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = MatiaLogKeyword },
                new() { Name = TyrorosItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = TyrorosLogKeyword },
                new() { Name = LycosItemName,   IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = LycosLogKeyword },
            };

            var eclipseBoss = new DailyWeeklyContentLog { Name = EclipseBossRaidItemName, Children = subBosses };

            var AbandonItems = new DailyWeeklyContentLog[]
            {
                new() { Name = ImmortalLandItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 10, MaxCount = 10, LogKeyword = ImmortalLandLogKeyword },
                new() { Name = CardiffItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 10, MaxCount = 10, LogKeyword = CardiffLogKeyword },
                new() { Name = OrlanneItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 10, MaxCount = 10, LogKeyword = OrlanneLogKeyword }
            };

            var AbandonGroup = new DailyWeeklyContentLog { Name = AbandonRoadGroupName, Children = AbandonItems, IsWeekly = true };

            var abyssItems = new DailyWeeklyContentLog[]
            {
                new() { Name = AbyssDepthOneItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = AbyssDepthOneLogKeyword },
                new() { Name = AbyssDepthTwoItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = AbyssDepthTwoLogKeyword },
                new() { Name = AbyssDepthThreeItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = AbyssDepthThreeLogKeyword }
            };

            var abyssGroup = new DailyWeeklyContentLog { Name = AbyssHellGroupName, Children = abyssItems, IsWeekly = true };
            var eclipseCoreMasterItems = new DailyWeeklyContentLog[]
            {
                new() { Name = LokagosCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = LokagosCoreMasterLogKeyword },
                new() { Name = EthosCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = EthosCoreMasterLogKeyword },
                new() { Name = CheriaCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = CheriaCoreMasterLogKeyword },
                new() { Name = MatiaCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = MatiaCoreMasterLogKeyword },
                new() { Name = LycosCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = LycosCoreMasterLogKeyword },
                new() { Name = TyrorosCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = TyrorosCoreMasterLogKeyword }
            };
            var eclipseCoreMasterGroup = new DailyWeeklyContentLog { Name = EclipseCoreMasterGroupName, Children = eclipseCoreMasterItems, IsWeekly = true };

            var abyssCoreMasterItems = new DailyWeeklyContentLog[]
            {
                new() { Name = AbyssDepthOneCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = AbyssDepthOneCoreMasterLogKeyword },
                new() { Name = AbyssDepthTwoCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = AbyssDepthTwoCoreMasterLogKeyword },
                new() { Name = AbyssDepthThreeCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = AbyssDepthThreeCoreMasterLogKeyword }
            };
            var abyssCoreMasterGroup = new DailyWeeklyContentLog { Name = AbysCoreMasterGroupName, Children = abyssCoreMasterItems, IsWeekly = true };

            var mercurialWeeklyDungeonItems = new DailyWeeklyContentLog[]
            {
                new() { Name = SallionCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SallionCoreMasterLogKeyword },
                new() { Name = SeleanaCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SeleanaCoreMasterLogKeyword },
                new() { Name = SilaironCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SilaironCoreMasterLogKeyword },
                new() { Name = SilbanCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SilbanCoreMasterLogKeyword },
                new() { Name = LuminousCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = LuminousCoreMasterLogKeyword },
                new() { Name = LuminousExCoreMasterItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = LuminousExCoreMasterLogKeyword }
            };
            var mercurialWeeklyDungeonGroup = new DailyWeeklyContentLog { Name = MercurialWeeklyDungeonGroupName, Children = mercurialWeeklyDungeonItems, IsWeekly = true };
            var mercurialCoreMasterItems = new DailyWeeklyContentLog[]
            {
                new() { Name = SallionCoreDungeonItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SallionCoreDungeonLogKeyword },
                new() { Name = SeleanaCoreDungeonItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SeleanaCoreDungeonLogKeyword },
                new() { Name = SilaironCoreDungeonItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SilaironCoreDungeonLogKeyword },
                new() { Name = SilbanCoreDungeonItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SilbanCoreDungeonLogKeyword },
                new() { Name = LuminousCoreDungeonItemName, IsSubItem = true, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = LuminousCoreDungeonLogKeyword }
            };
            var mercurialCoreMasterGroup = new DailyWeeklyContentLog { Name = MercurialCoreMasterGroupName, Children = mercurialCoreMasterItems, IsWeekly = true };

            var eclipseSubjugation = new DailyWeeklyContentLog { Name = EclipseSubjugationItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 21, MaxCount = 21, LogKeyword = EclipseSubjugationLogKeyword };
            var supplyRetrieval = new DailyWeeklyContentLog { Name = SupplyRetrievalItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SupplyRetrievalLogKeyword };
            var trainingCenter = new DailyWeeklyContentLog { Name = MoonQueensTrainingCenterItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = MoonQueensTrainingCenterLogKeyword };
            var detachedForce = new DailyWeeklyContentLog { Name = DetachedForceSubjugationItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = DetachedForceSubjugationLogKeyword };
            var coreDungeon = new DailyWeeklyContentLog
            {
                Name = CoreDungeonItemName,
                IsWeekly = true,
                AllowCountOverMax = true,
                DefaultMaxCount = 7,
                MaxCount = 7,
                LogKeyword = CoreDungeonLogKeyword,
                LogKeyword2 = CoreDungeonLogKeyword2
            };
            var excavationSite = new DailyWeeklyContentLog { Name = ExcavationSiteItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = ExcavationSiteLogKeyword };
            var relic = new DailyWeeklyContentLog { Name = RelicItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = RelicLogKeyword };
            var dimensionalGap = new DailyWeeklyContentLog { Name = DimensionalGapItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = DimensionalGapLogKeyword };
            var abyssalTreasury = new DailyWeeklyContentLog { Name = AbyssalTreasuryItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = AbyssalTreasuryLogKeyword };
            var cleaningPartTime = new DailyWeeklyContentLog { Name = CleaningPartTimeJobItemName, IsWeekly = true, LogKeyword = CleaningPartTimeJobLogKeyword };
            var pravaDefense = new DailyWeeklyContentLog { Name = PravaDefenseItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 5, MaxCount = 5, LogKeyword = PravaDefenseLogKeyword };
            var vestige = new DailyWeeklyContentLog { Name = VestigeItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = VestigeLogKeyword };
            var orlyDefense = new DailyWeeklyContentLog { Name = OrlyDefenseHellItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = OrlyDefenseHellEntryLogKeyword, LogKeyword2 = OrlyDefenseHellClearLogKeyword };
            var apetiriaEx = new DailyWeeklyContentLog { Name = ApetiriaExItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = ApetiriaExLogKeyword };
            var finalBattle = new DailyWeeklyContentLog { Name = FinalBattleItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 10, MaxCount = 10, LogKeyword = FinalBattleLogKeyword };
            var catacombsHell = new DailyWeeklyContentLog { Name = CatacombsHellModeItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 5, MaxCount = 5, LogKeyword = CatacombsHellModeLogKeyword };
            var shinjoHard = new DailyWeeklyContentLog { Name = NestOfShinjoHardItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = NestOfShinjoLogKeyword };
            var apetiria = new DailyWeeklyContentLog { Name = ApetiriaItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = ApetiriaLogKeyword };
            var siochanBosses = new DailyWeeklyContentLog { Name = SiegeOfSiochanheimBossesItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SiegeOfSiochanheimBossesLogKeyword };
            var siochanOdin = new DailyWeeklyContentLog { Name = SiochanheimOdinAllOutWarItemName, IsWeekly = true, AllowCountOverMax = true, DefaultMaxCount = 7, MaxCount = 7, LogKeyword = SiochanheimOdinAllOutWarLogKeyword };

            var mercurialRegionGroup = new DailyWeeklyContentLog
            {
                Name = WeeklyMercurialGroupName,
                IsRegionGroup = true,
                IsWeekly = true,
                Children = new[] { mercurialCoreMasterGroup, mercurialWeeklyDungeonGroup }
            };
            var abyssRegionGroup = new DailyWeeklyContentLog { Name = WeeklyAbyssGroupName, IsRegionGroup = true, IsWeekly = true, Children = new[] { abyssCoreMasterGroup, abyssGroup, abyssalTreasury, dimensionalGap } };
            var eclipseRegionGroup = new DailyWeeklyContentLog { Name = WeeklyEclipseGroupName, IsRegionGroup = true, IsWeekly = true, Children = new[] { eclipseCoreMasterGroup, eclipseBoss, eclipseSubjugation, supplyRetrieval, trainingCenter, detachedForce, apetiriaEx, apetiria, finalBattle } };
            var otherRegionGroup = new DailyWeeklyContentLog { Name = WeeklyOtherGroupName, IsRegionGroup = true, IsWeekly = true, Children = new[] { coreDungeon, excavationSite, relic, cleaningPartTime, pravaDefense, vestige, orlyDefense, catacombsHell, shinjoHard, siochanBosses, siochanOdin, AbandonGroup } };

            var weeklyItems = new DailyWeeklyContentLog[] { mercurialRegionGroup, abyssRegionGroup, eclipseRegionGroup, otherRegionGroup };

            var dailyItems = new DailyWeeklyContentLog[]
            {
                new() { Name = ConfusedLandItemName, LogKeyword = ConfusedLandLogKeyword },
                new() { Name = ColorlessLandItemName, LogKeyword = ColorlessLandLogKeyword },
                new() { Name = MiningSiteItemName, LogKeyword = MiningSiteLogKeyword },
                new() { Name = CravingPleasureItemName, AllowCountOverMax = true, DefaultMaxCount = 5, MaxCount = 5, LogKeyword = CravingPleasureLogKeyword }
                ,new() { Name = FollowingJoyNormalItemName, AllowCountOverMax = true, DefaultMaxCount = 1, MaxCount = 1, LogKeyword = FollowingJoyNormalLogKeyword }
                ,new() { Name = GazingSorrowNormalItemName, AllowCountOverMax = true, DefaultMaxCount = 1, MaxCount = 1, LogKeyword = GazingSorrowNormalLogKeyword }
                ,new() { Name = FollowingJoyHardItemName, AllowCountOverMax = true, DefaultMaxCount = 1, MaxCount = 1, LogKeyword = FollowingJoyHardLogKeyword }
                ,new() { Name = GazingSorrowHardItemName, AllowCountOverMax = true, DefaultMaxCount = 1, MaxCount = 1, LogKeyword = GazingSorrowHardLogKeyword }
                ,new() { Name = AfterimageOfJoyItemName, AllowCountOverMax = true, DefaultMaxCount = 1, MaxCount = 1, LogKeyword = AfterimageOfJoyLogKeyword }
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
                if (item.HasChildren)
                {
                    foreach (var child in item.Children!)
                    {
                        list.Add(child);
                        if (child.HasChildren)
                            foreach (var sub in child.Children!)
                                list.Add(sub);
                    }
                }
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
                if (item.HasChildren)
                {
                    foreach (var child in item.Children!)
                    {
                        weeklyDisplay.Add(child);
                        if (child.HasChildren)
                            foreach (var sub in child.Children!)
                                weeklyDisplay.Add(sub);
                    }
                }
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
                if (!_settings.DungeonItemConfigs.TryGetValue(item.Name, out var config))
                    continue;

                item.IsEnabled = config.IsEnabled;
                int configured = config.RequiredCount > 0 ? config.RequiredCount : item.DefaultMaxCount;
                if (item.DefaultMaxCount > 0)
                    configured = Math.Max(1, configured);
                item.MaxCount = configured;
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
            _scanCache.IsDirty = true;
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
            _scanCache.IsDirty = true;
        }

        /// <summary>
        /// 로그 파일 전체를 백그라운드에서 스캔하여 금일 클리어 현황을 복원합니다.
        /// </summary>
        public async Task ScanHistoricalLogsAsync()
        {
            IsLoading = true;
            try
            {
                var snapshot = await EnsureScanCacheAsync(forceRescan: false);
                ApplySnapshot(snapshot);
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task EnsureInitialTabsLoadedAsync()
        {
            if (_scanCache.Snapshot != null)
                return;

            IsLoading = true;
            try
            {
                await EnsureScanCacheAsync(forceRescan: false);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task<ItemStateSnapshotMap> EnsureScanCacheAsync(bool forceRescan)
        {
            var cache = _scanCache;

            bool resetBoundaryChanged =
                cache.DailyResetDate != _lastDailyResetDate ||
                cache.WeeklyResetKey != _lastWeeklyResetKey ||
                cache.AbandonResetKey != _lastAbandonResetKey;

            if (forceRescan || resetBoundaryChanged || cache.IsDirty || cache.Snapshot == null)
            {
                await ScanHistoricalLogsCoreAsync();
                cache.Snapshot = CaptureSnapshot();
                cache.IsDirty = false;
                cache.DailyResetDate = _lastDailyResetDate;
                cache.WeeklyResetKey = _lastWeeklyResetKey;
                cache.AbandonResetKey = _lastAbandonResetKey;
            }

            return cache.Snapshot;
        }

        private async Task ScanHistoricalLogsCoreAsync()
        {
            DateTime today = DateTime.Today;

            var dailyItems = TrackItems
                .Where(i => (!i.IsWeekly) && i.LogKeyword != null)
                .ToList();
            var weeklyItems = TrackItems
                .Where(i => i.IsWeekly && i.LogKeyword != null)
                .ToList();

            _suppressSave = true;
            try
            {
                ApplySettings();
                ResetScannedItems(dailyItems);
                ResetScannedItems(weeklyItems);
                _dailyWeeklyLogAnalyzer.ResetPending();
                _dailyWeeklyLogAnalyzer.ResetAccumulatedCounts();
                _AbandonCountStates.Clear();

                await ScanAndApplyAsync(dailyItems, new[] { GetLogPath(today) }, today.Date);
                await ScanAndApplyAsync(weeklyItems, GetWeekLogPaths(today), null);
            }
            finally
            {
                _suppressSave = false;
            }
        }

        private ItemStateSnapshotMap CaptureSnapshot()
        {
            var snapshot = new ItemStateSnapshotMap();
            foreach (var item in TrackItems)
            {
                int currentCount = item.HasCount ? item.CurrentCount : (item.IsCleared ? 1 : 0);
                snapshot[item.Name] = new ItemStateSnapshot
                {
                    IsEnabled = item.IsEnabled,
                    MaxCount = item.MaxCount,
                    CurrentCount = currentCount,
                    IsCleared = item.IsCleared,
                    Detail = item.Detail
                };
            }

            return snapshot;
        }

        private void ApplySnapshot(ItemStateSnapshotMap snapshot)
        {
            _suppressSave = true;
            try
            {
                foreach (var item in TrackItems)
                {
                    if (!snapshot.TryGetValue(item.Name, out var state))
                        continue;

                    item.IsEnabled = state.IsEnabled;
                    item.MaxCount = state.MaxCount;
                    item.Detail = state.Detail;

                    if (item.HasChildren)
                        continue;

                    if (item.MaxCount > 0)
                    {
                        item.SetCount(state.CurrentCount);
                    }
                    else
                    {
                        item.IsCleared = state.IsCleared;
                    }

                    EnsureCountModeForFixedItems(item);
                }
            }
            finally
            {
                _suppressSave = false;
            }
            ReorderItems();
            OnPropertyChanged(nameof(ProgressDisplay));
            OnPropertyChanged(nameof(DailyProgressDisplay));
            OnPropertyChanged(nameof(WeeklyProgressDisplay));
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
                EnsureCountModeForFixedItems(item);
            }
        }

        private static void EnsureCountModeForFixedItems(DailyWeeklyContentLog item)
        {
            if (item.HasChildren)
                return;

            if (item.Name == FollowingJoyNormalItemName ||
                item.Name == GazingSorrowNormalItemName ||
                item.Name == FollowingJoyHardItemName ||
                item.Name == GazingSorrowHardItemName ||
                item.Name == AfterimageOfJoyItemName)
            {
                if (item.MaxCount <= 0)
                    item.MaxCount = Math.Max(1, item.DefaultMaxCount);
            }
        }

        private IEnumerable<string> GetWeekLogPaths(DateTime today)
        {
            string weekPath = GetContentWeekLogPath(today);
            if (!string.IsNullOrWhiteSpace(weekPath))
                yield return weekPath;
        }

        private string GetLogPath(DateTime date)
            => GetContentWeekLogPath(date);

        private static string GetContentWeekLogPath(DateTime date)
        {
            string root = LogStoragePaths.ContentDirectory;
            int isoYear = ISOWeek.GetYear(date);
            int isoWeek = ISOWeek.GetWeekOfYear(date);
            return Path.Combine(root, $"{isoYear}-W{isoWeek:00}.html");
        }

        private async Task ScanAndApplyAsync(IReadOnlyList<DailyWeeklyContentLog> items, IEnumerable<string> filePaths, DateTime? onlyDate)
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
                    var accumulatedLastRawCounts = new Dictionary<string, int>();
                    var accumulatedOffsets = new Dictionary<string, int>();

                    int AccumulateCount(string key, int rawCount)
                    {
                        accumulatedLastRawCounts.TryGetValue(key, out int lastRawCount);
                        accumulatedOffsets.TryGetValue(key, out int offset);

                        if (rawCount < lastRawCount)
                            offset += lastRawCount;

                        accumulatedLastRawCounts[key] = rawCount;
                        accumulatedOffsets[key] = offset;
                        return offset + rawCount;
                    }

                    foreach (var filePath in filePaths)
                    {
                        if (!File.Exists(filePath)) continue;

                        bool isDedicatedContentLog = IsDedicatedContentLogPath(filePath);
                        Encoding encoding = isDedicatedContentLog ? Encoding.UTF8 : Encoding.GetEncoding(949);
                        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                        using var reader = new StreamReader(stream, encoding);
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string effectiveLine = line;
                            DateTime? logDate = null;
                            if (isDedicatedContentLog)
                            {
                                if (!TryParseContentArchiveLine(line, out effectiveLine, out DateTime parsedDate))
                                    continue;
                                logDate = parsedDate.Date;
                            }

                            if (onlyDate.HasValue && logDate.HasValue && logDate.Value != onlyDate.Value.Date)
                                continue;


                            if (DailyWeeklyLogAnalyzer.TryMatchSinjoReward(effectiveLine, out _))
                            {
                                specialCounts[DailyWeeklyLogAnalyzer.SinjoSpecialKey] =
                                    specialCounts.TryGetValue(DailyWeeklyLogAnalyzer.SinjoSpecialKey, out int existingSinjo)
                                        ? existingSinjo + 1
                                        : 1;
                            }

                            if (DailyWeeklyLogAnalyzer.TryMatchCatacombsReward(effectiveLine, out _))
                            {
                                specialCounts[DailyWeeklyLogAnalyzer.CatacombsSpecialKey] =
                                    specialCounts.TryGetValue(DailyWeeklyLogAnalyzer.CatacombsSpecialKey, out int existingCatacombs)
                                        ? existingCatacombs + 1
                                        : 1;
                            }

                            string normalizedLine = NormalizeLogText(effectiveLine);
                            if (TryExtractAbandonRoadCount(normalizedLine, out string AbandonItemName, out int AbandonValue))
                            {
                                var AbandonItem = items.FirstOrDefault(i => i.Name == AbandonItemName);
                                if (AbandonItem != null)
                                {
                                    maxExtractedCounts[AbandonItem] = AccumulateCount(AbandonItem.Name, AbandonValue);
                                }
                            }

                            foreach (var item in items)
                            {
                                if (item.LogKeyword == null || !effectiveLine.Contains(item.LogKeyword)) continue;
                                if (item.Name == RelicItemName &&
                                    !IsRelicWeeklyClearLog(effectiveLine))
                                {
                                    continue;
                                }
                            }
                            foreach (var (primary, secondary) in scanList)
                            {
                                if (secondary == null)
                                {
                                    if (effectiveLine.Contains(primary))
                                    {
                                        if (string.Equals(primary, RelicLogKeyword, StringComparison.Ordinal))
                                        {
                                            if (!IsRelicWeeklyClearLog(effectiveLine))
                                                continue;
                                        }
                                        counts[primary]++;
                                    }
                                }
                                else
                                {
                                    if (effectiveLine.Contains(primary))
                                        seenPrimary.Add(primary);
                                    else if (effectiveLine.Contains(secondary) && seenPrimary.Remove(primary))
                                        counts[primary]++;
                                }
                            }

                            if (effectiveLine.Contains(FreeClearCountKeyword, StringComparison.Ordinal))
                            {
                                Match titleMatch = FreeClearTitleRegex.Match(effectiveLine);
                                if (titleMatch.Success)
                                {
                                    string title = titleMatch.Groups["title"].Value.Trim();
                                    if (title.Contains("환희의 잔상", StringComparison.Ordinal))
                                    {
                                        if (counts.ContainsKey(AfterimageOfJoyLogKeyword))
                                            counts[AfterimageOfJoyLogKeyword]++;
                                    }
                                    else if (title.Contains("고대 렐릭의 성소", StringComparison.Ordinal))
                                    {
                                        if (counts.ContainsKey(RelicLogKeyword))
                                            counts[RelicLogKeyword]++;
                                    }
                                }
                                else if (effectiveLine.Contains("렐릭", StringComparison.Ordinal))
                                {
                                    if (counts.ContainsKey(RelicLogKeyword))
                                        counts[RelicLogKeyword]++;
                                }
                            }
                        }
                    }

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
                if (!item.IsEnabled) continue;
                if (item.LogKeyword.Contains(DailyWeeklyLogAnalyzer.AbandonRoadKeywordToken, StringComparison.Ordinal) ||
                    item.LogKeyword.Contains(DailyWeeklyLogAnalyzer.CatacombsKeywordToken, StringComparison.Ordinal) ||
                    item.LogKeyword.Contains(DailyWeeklyLogAnalyzer.NestOfShinjoKeywordToken, StringComparison.Ordinal))
                    continue;
                if (!counts.TryGetValue(item.LogKeyword, out int count) || count == 0) continue;
                if (count <= 0) continue;
                if (item.Name == FollowingJoyNormalItemName ||
                    item.Name == GazingSorrowNormalItemName ||
                    item.Name == FollowingJoyHardItemName ||
                    item.Name == GazingSorrowHardItemName ||
                    item.Name == AfterimageOfJoyItemName)
                {
                    AppLogger.Info($"DailyWeekly scan count. Item={item.Name}, Count={count}, Max={item.MaxCount}, HasCount={item.HasCount}, OnlyDate={onlyDate:yyyy-MM-dd}");
                }
                int times = item.HasCount ? count : 1;
                for (int i = 0; i < times; i++)
                    item.Mark();
            }

            DailyWeeklyLogAnalyzer.ApplySpecialCounts(items, specialCounts);
        }

        private static bool IsDedicatedContentLogPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string fullPath = Path.GetFullPath(path);
            string contentRoot = Path.GetFullPath(LogStoragePaths.ContentDirectory);
            return fullPath.StartsWith(contentRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRelicWeeklyClearLog(string text)
            => !string.IsNullOrWhiteSpace(text) &&
               text.Contains(RelicWeeklyClearToken1, StringComparison.Ordinal) &&
               text.Contains(RelicWeeklyClearToken2, StringComparison.Ordinal);

        private static bool TryParseContentArchiveLine(string line, out string text, out DateTime logDate)
        {
            text = string.Empty;
            logDate = DateTime.MinValue;

            Match match = ContentArchiveEntryRegex.Match(line);
            if (!match.Success)
                return false;

            string attrsRaw = match.Groups["attrs"].Value;
            string? dateRaw = null;
            foreach (Match attrMatch in ArchiveAttrRegex.Matches(attrsRaw))
            {
                string key = attrMatch.Groups["name"].Value.Trim();
                if (!string.Equals(key, "data-date", StringComparison.OrdinalIgnoreCase))
                    continue;

                dateRaw = WebUtility.HtmlDecode(attrMatch.Groups["value"].Value).Trim();
                break;
            }

            if (string.IsNullOrWhiteSpace(dateRaw) ||
                !DateTime.TryParseExact(dateRaw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out logDate))
            {
                return false;
            }

            text = WebUtility.HtmlDecode(match.Groups["text"].Value).Trim();
            return !string.IsNullOrWhiteSpace(text);
        }

        /// <summary>
        /// 시스템 로그 텍스트를 받아 키워드와 일치하는 항목을 자동으로 체크합니다.
        /// </summary>
        public void ProcessLog(string text)
        {
            _dailyWeeklyLogAnalyzer.Process(text);
            UpdateCacheAfterRealtimeProcess(wasAppliedToCurrentView: true);
        }

        public bool TryProcessAbandonOrCravingLog(string rawLog)
        {
            string text = NormalizeLogText(rawLog);
            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool handled = TryProcessAbandonRoadLog(text) || TryProcessCravingPleasureLog(text);
            if (handled)
                UpdateCacheAfterRealtimeProcess(wasAppliedToCurrentView: true);

            return handled;
        }

        public void ProcessLog(LogAnalysisResult analysis)
        {
            if (!analysis.ShouldRunDailyWeeklyContent)
                return;

            _dailyWeeklyLogAnalyzer.Process(analysis);
            UpdateCacheAfterRealtimeProcess(wasAppliedToCurrentView: true);
        }

        private void UpdateCacheAfterRealtimeProcess(bool wasAppliedToCurrentView)
        {
            if (!wasAppliedToCurrentView)
                return;

            _scanCache.Snapshot = CaptureSnapshot();
            _scanCache.IsDirty = false;
            _scanCache.DailyResetDate = _lastDailyResetDate;
            _scanCache.WeeklyResetKey = _lastWeeklyResetKey;
            _scanCache.AbandonResetKey = _lastAbandonResetKey;
        }

        private bool TryProcessAbandonRoadLog(string text)
        {
            if (!TryExtractAbandonRoadCount(text, out string itemName, out int count))
                return false;

            DailyWeeklyContentLog? item = TrackItems.FirstOrDefault(i => i.Name == itemName);
            if (item == null || !item.IsEnabled)
                return false;

            item.Mark();
            return true;
        }

        private bool TryProcessCravingPleasureLog(string text)
        {
            if (!TryExtractCravingPleasureCount(text, out _))
                return false;

            DailyWeeklyContentLog? item = TrackItems.FirstOrDefault(i => i.Name == CravingPleasureItemName);
            if (item == null || !item.IsEnabled)
                return false;

            item.Mark();
            return true;
        }

        private static bool TryExtractAbandonRoadCount(string text, out string itemName, out int count)
        {
            itemName = string.Empty;
            count = 0;

            Match match = AbandonRoadCountRegex.Match(text);
            if (!match.Success || !int.TryParse(match.Groups["count"].Value, out count))
                return false;

            string region = WhiteSpaceRegex.Replace(match.Groups["region"].Value, string.Empty);
            itemName = region switch
            {
                "필멸의땅" => ImmortalLandItemName,
                "카디프" => CardiffItemName,
                "오를란느" => OrlanneItemName,
                _ => string.Empty
            };

            return !string.IsNullOrEmpty(itemName);
        }

        private static bool TryExtractCravingPleasureCount(string text, out int count)
        {
            count = 0;

            Match match = CravingPleasureEnergyRegex.Match(text);
            if (!match.Success || !int.TryParse(match.Groups["remain"].Value, out int remain))
                return false;

            count = 21 - remain;
            return true;
        }

        private static string NormalizeLogText(string rawLog)
        {
            if (string.IsNullOrWhiteSpace(rawLog))
                return string.Empty;

            string decoded = WebUtility.HtmlDecode(rawLog).Replace("&nbsp", " ");
            decoded = HtmlTagRegex.Replace(decoded, " ");
            return WhiteSpaceRegex.Replace(decoded, " ").Trim();
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
                if (item.Children != null)
                {
                    foreach (var child in EnumerateDescendants(item))
                    {
                        child.IsEnabled = item.IsEnabled;
                    }
                }

                SaveItemConfig(item);
                foreach (var child in EnumerateDescendants(item))
                {
                    SaveItemConfig(child);
                }
            }
        }

        private static IEnumerable<DailyWeeklyContentLog> EnumerateDescendants(DailyWeeklyContentLog root)
        {
            if (root.Children == null)
                yield break;

            foreach (var child in root.Children)
            {
                yield return child;
                foreach (var grandChild in EnumerateDescendants(child))
                    yield return grandChild;
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
            _ = RefreshCurrentWeekContentLogAsync();
        }

        private async Task RefreshCurrentWeekContentLogAsync()
        {
            try
            {
                DateTime today = DateTime.Today;
                DateTime weekStart = GetWeeklyResetKey(today);
                DateTime weekEnd = weekStart.AddDays(6);
                string weekPath = GetContentWeekLogPath(today);
                string? weekDir = Path.GetDirectoryName(weekPath);
                if (!string.IsNullOrWhiteSpace(weekDir))
                    Directory.CreateDirectory(weekDir);

                if (File.Exists(weekPath))
                    File.Delete(weekPath);

                var keywords = TrackItems
                    .Where(i => !string.IsNullOrWhiteSpace(i.LogKeyword))
                    .Select(i => i.LogKeyword!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                var keyword2 = TrackItems
                    .Where(i => !string.IsNullOrWhiteSpace(i.LogKeyword2))
                    .Select(i => i.LogKeyword2!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                string logDir = _settings.ChatLogFolderPath;
                if (string.IsNullOrWhiteSpace(logDir) || !Directory.Exists(logDir))
                    return;

                var parser = new LogAnalysisService(_settings);
                var lines = new List<string>();
                var dedupe = new HashSet<string>(StringComparer.Ordinal);

                for (DateTime day = weekStart; day <= weekEnd; day = day.AddDays(1))
                {
                    string path = Path.Combine(logDir, $"TWChatLog_{day:yyyy_MM_dd}.html");
                    if (!File.Exists(path))
                        continue;

                    string raw;
                    using (var sr = new StreamReader(path, Encoding.GetEncoding(949)))
                        raw = await sr.ReadToEndAsync();

                    foreach (string part in Regex.Split(raw, @"</?br\s*>|\r?\n", RegexOptions.IgnoreCase))
                    {
                        if (string.IsNullOrWhiteSpace(part))
                            continue;

                        var a = parser.Analyze(part, isRealTime: false, default);
                        if (!a.IsSuccess || string.IsNullOrWhiteSpace(a.Parsed.FormattedText))
                            continue;

                        string text = a.Parsed.FormattedText.Trim();
                        bool match = keywords.Any(k => text.Contains(k, StringComparison.Ordinal)) ||
                                     keyword2.Any(k => text.Contains(k, StringComparison.Ordinal));
                        if (!match)
                            continue;

                        string key = $"{day:yyyy-MM-dd}|{text}";
                        if (!dedupe.Add(key))
                            continue;

                        lines.Add($"<div class=\"log content\" data-date=\"{day:yyyy-MM-dd}\">{WebUtility.HtmlEncode(text)}</div>");
                    }
                }

                using (var fs = new FileStream(weekPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                {
                    byte[] bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble();
                    if (bom.Length > 0)
                        await fs.WriteAsync(bom);

                    using var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    await writer.WriteLineAsync("<!doctype html>");
                    await writer.WriteLineAsync("<html lang=\"ko\">");
                    await writer.WriteLineAsync("<head>");
                    await writer.WriteLineAsync("  <meta charset=\"utf-8\" />");
                    await writer.WriteLineAsync($"  <title>{ISOWeek.GetYear(today)}-W{ISOWeek.GetWeekOfYear(today):00}</title>");
                    await writer.WriteLineAsync("  <style>");
                    await writer.WriteLineAsync("    body{background:#111;color:#eee;font-family:'Malgun Gothic',sans-serif;font-size:13px;line-height:1.45;padding:12px;}");
                    await writer.WriteLineAsync("    .log{margin:2px 0;padding:2px 0;border-bottom:1px solid rgba(255,255,255,.06);}");
                    await writer.WriteLineAsync("    .summary{margin:2px 0;padding:4px 0;color:#9ad3ff;font-weight:600;border-bottom:1px dashed rgba(154,211,255,.35);}");
                    await writer.WriteLineAsync("  </style>");
                    await writer.WriteLineAsync("</head>");
                    await writer.WriteLineAsync("<body>");
                    foreach (string line in lines)
                        await writer.WriteLineAsync(line);
                }
                _scanCache.IsDirty = true;
                await ScanHistoricalLogsAsync();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to refresh weekly content log.", ex);
            }
        }

        private void InitializeResetTimer()
        {
            _lastDailyResetDate = DateTime.Today;
            _lastWeeklyResetKey = GetWeeklyResetKey(DateTime.Now);
            _lastAbandonResetKey = GetAbandonResetKey(DateTime.Now);

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

            var AbandonKey = GetAbandonResetKey(now);
            if (AbandonKey != _lastAbandonResetKey)
            {
                _lastAbandonResetKey = AbandonKey;
                ResetAbandonItems();
            }
        }

        private static DateTime GetWeeklyResetKey(DateTime now)
        {
            int diff = (7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return now.Date.AddDays(-diff);
        }

        private static DateTime GetAbandonResetKey(DateTime now)
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
            _scanCache.IsDirty = true;
        }

        private void ResetWeeklyItems()
        {
            foreach (var item in TrackItems.Where(i => !i.IsSubItem && i.IsWeekly && i.Name != AbandonRoadGroupName))
                item.Reset();
            _dailyWeeklyLogAnalyzer.ResetAccumulatedCounts();
            _scanCache.IsDirty = true;
        }

        private void ResetAbandonItems()
        {
            TrackItems.FirstOrDefault(i => i.Name == AbandonRoadGroupName)?.Reset();
            _AbandonCountStates.Clear();
            _scanCache.IsDirty = true;
        }

        private static void SetAccumulatedCount(
            Dictionary<string, AccumulatedCountState> states,
            DailyWeeklyContentLog item,
            int rawCount)
        {
            if (!states.TryGetValue(item.Name, out var state))
            {
                state = new AccumulatedCountState();
                states[item.Name] = state;
            }

            if (rawCount < state.LastRawCount)
                state.Offset += state.LastRawCount;

            state.LastRawCount = rawCount;
            item.SetCount(state.Offset + rawCount);
        }

        private sealed class AccumulatedCountState
        {
            public int LastRawCount { get; set; }
            public int Offset { get; set; }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try { _resetTimer?.Stop(); }
            catch (Exception ex) { AppLogger.Warn("Failed to stop DailyWeekly reset timer during close.", ex); }
            base.OnClosing(e);
        }


    }
}
