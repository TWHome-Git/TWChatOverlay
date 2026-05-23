using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Services.LogAnalysis
{
    public sealed class DailyWeeklyLogAnalyzer
    {
        public const string MercurialCaveItemName = "머큐리얼 케이브";
        public const string CatacombsHellModeItemName = "카타콤 지옥";
        public const string NestOfShinjoHardItemName = "신조의 둥지 어려움";
        public const string AbyssDepthOneItemName = "어비스 - 심층Ⅰ";
        public const string AbyssDepthTwoItemName = "어비스 - 심층Ⅱ";
        public const string AbyssDepthThreeItemName = "어비스 - 심층Ⅲ";

        public const string AbandonRoadKeywordToken = "어밴던로드";
        public const string CravingPleasureLogKeyword = "남은 에너지는";
        public const string CravingPleasureKeywordToken = CravingPleasureLogKeyword;
        public const string AbyssKeywordToken = "어비스 - 심층";
        public const string CatacombsKeywordToken = "이번 주 사명의 계승자 닉스 보상을";
        public const string NestOfShinjoKeywordToken = "이번 주 신조 보상을";
        public const string SiochanheimKeywordToken = "시오칸하임";

        public const string AbyssSpecialKeyPrefix = "Abyss:";
        public const string CatacombsSpecialKey = "Catacombs";
        public const string SinjoSpecialKey = "Sinjo";
        public const string MercurialCaveSpecialKey = "MercurialCave";

        private const string MercurialSingleEntryKeyword = "머큐리얼 케이브의 [싱글 플레이] 모드로 입장 하였습니다.";
        private const string MercurialSingleRewardKeyword1 = "경험치가 5000000 올랐습니다";
        private const string MercurialSingleRewardKeyword2 = "콘텐츠 클리어 보상으로 1500만 SEED를 획득했습니다.";
        private const string MercurialEntryTokenDungeon = "머큐리얼 케이브";
        private const string MercurialEntryTokenMode = "싱글 플레이";
        private const string MercurialEntryTokenEnter = "입장";
        private const string MercurialRewardExpToken1 = "경험치가";
        private const string MercurialRewardExpToken2 = "올랐습니다";
        private const string MercurialRewardSeedToken1 = "콘텐츠 클리어 보상으로";
        private const string MercurialRewardSeedToken2 = "SEED";
        private const string MercurialRewardSeedToken3 = "획득했습니다";

        private static readonly Regex AbandonCountRegex = new(@"도전 횟수는\s*(\d+)번", RegexOptions.Compiled);
        private static readonly Regex PleasureEnergyRegex = new(@"남은\s*에너지는\s*\[\s*(\d+)\s*\]", RegexOptions.Compiled);
        private static readonly Regex AbyssEntryRegex = new(@"어비스 - 심층(?<floor>[ⅠⅡⅢ])\(보스전\)에 지옥 난이도로 입장하셨습니다", RegexOptions.Compiled);
        private static readonly Regex AbyssFloorRegex = new(@"어비스 - 심층(?<floor>[ⅠⅡⅢ])\(보스전\) 플레이를 이번 주에 \d+회 중\s*\d+회째", RegexOptions.Compiled);
        private static readonly Regex AbyssRewardRegex = new(@"이번 주 어비스 던전 보상을\s*\d+회 획득하셨습니다.*\[\s*(\d+)회\s*/\s*7회\s*\]?", RegexOptions.Compiled);
        private static readonly Regex CatacombsRewardRegex = new(@"이번 주 사명의 계승자 닉스 보상을\s*(\d+)회 획득\s*하셨습니다", RegexOptions.Compiled);
        private static readonly Regex SinjoRewardRegex = new(@"이번 주 신조 보상을\s*(\d+)회 획득 하셨습니다", RegexOptions.Compiled);

        private readonly IReadOnlyList<DailyWeeklyContentLog> _trackItems;
        private readonly HashSet<DailyWeeklyContentLog> _pendingDoubleKeyword = new();
        private readonly Dictionary<string, AccumulatedCountState> _countStates = new();
        private string? _pendingAbyssEntryFloor;
        private string? _pendingAbyssFloor;
        private bool _pendingMercurialSingleEntry;
        private bool _pendingMercurialRewardExpSeen;
        private bool _pendingMercurialRewardSeedSeen;

        public DailyWeeklyLogAnalyzer(IReadOnlyList<DailyWeeklyContentLog> trackItems)
        {
            _trackItems = trackItems;
        }

        public void Process(LogAnalysisResult analysis)
        {
            if (!analysis.ShouldRunDailyWeeklyContent)
                return;

            Process(analysis.Parsed.FormattedText);
        }

        public void Process(string text)
        {
            if (TryProcessMercurialSingleLog(text))
                return;

            if (TryProcessSpecialWeeklyCountLog(text))
                return;

            foreach (var item in _trackItems)
            {
                if (!item.IsEnabled || item.LogKeyword == null)
                    continue;

                if (item.LogKeyword2 == null)
                {
                    if (!text.Contains(item.LogKeyword, StringComparison.Ordinal))
                        continue;
                    if (item.Name == "렐릭" &&
                        !(text.Contains("고대 렐릭의 성소", StringComparison.Ordinal) &&
                          text.Contains("주간 무료 클리어 횟수", StringComparison.Ordinal)))
                        continue;

                    if (TryUpdateDetail(item, text, out int? count) && count.HasValue)
                        break;

                    MarkAndLog(item, "Primary keyword", text);
                    break;
                }

                if (text.Contains(item.LogKeyword, StringComparison.Ordinal))
                {
                    _pendingDoubleKeyword.Add(item);
                    AppLogger.Debug($"[DailyWeekly] Pending two-step primary matched. Item='{item.Name}', PendingCount={_pendingDoubleKeyword.Count}, Text='{text}'");
                }
                else if (_pendingDoubleKeyword.Contains(item) &&
                         text.Contains(item.LogKeyword2, StringComparison.Ordinal))
                {
                    _pendingDoubleKeyword.Remove(item);
                    MarkAndLog(item, "Secondary keyword", text);
                }
            }
        }

        public void ResetPending()
        {
            _pendingDoubleKeyword.Clear();
            _pendingAbyssEntryFloor = null;
            _pendingAbyssFloor = null;
            _pendingMercurialSingleEntry = false;
            _pendingMercurialRewardExpSeen = false;
            _pendingMercurialRewardSeedSeen = false;
        }

        public void ResetAccumulatedCounts()
        {
            _countStates.Clear();
        }

        private bool TryProcessMercurialSingleLog(string text)
        {
            if (IsMercurialEntryMessage(text))
            {
                _pendingMercurialSingleEntry = true;
                _pendingMercurialRewardExpSeen = false;
                _pendingMercurialRewardSeedSeen = false;
                return false;
            }

            if (_pendingMercurialSingleEntry)
            {
                if (IsMercurialRewardExpMessage(text))
                    _pendingMercurialRewardExpSeen = true;
                if (IsMercurialRewardSeedMessage(text))
                    _pendingMercurialRewardSeedSeen = true;

                if (_pendingMercurialRewardExpSeen && _pendingMercurialRewardSeedSeen)
                {
                    _pendingMercurialSingleEntry = false;
                    _pendingMercurialRewardExpSeen = false;
                    _pendingMercurialRewardSeedSeen = false;
                    MarkAndLog(_trackItems.FirstOrDefault(static item => item.Name == MercurialCaveItemName), "Mercurial single completion", text);
                    return true;
                }
            }

            return false;
        }

        private bool TryProcessSpecialWeeklyCountLog(string text)
        {
            var abyssEntryMatch = AbyssEntryRegex.Match(text);
            if (abyssEntryMatch.Success)
            {
                _pendingAbyssEntryFloor = abyssEntryMatch.Groups["floor"].Value;
                _pendingAbyssFloor = null;
                return true;
            }

            var abyssFloorMatch = AbyssFloorRegex.Match(text);
            if (abyssFloorMatch.Success)
            {
                string floor = abyssFloorMatch.Groups["floor"].Value;
                if (_pendingAbyssEntryFloor == floor)
                    _pendingAbyssFloor = floor;
                _pendingAbyssEntryFloor = null;
                return true;
            }

            var abyssRewardMatch = AbyssRewardRegex.Match(text);
            if (abyssRewardMatch.Success &&
                _pendingAbyssFloor != null)
            {
                MarkAndLog(FindAbyssItemByFloor(_trackItems, _pendingAbyssFloor), $"Abyss reward floor {_pendingAbyssFloor}", text);
                _pendingAbyssFloor = null;
                return true;
            }

            var sinjoRewardMatch = SinjoRewardRegex.Match(text);
            if (sinjoRewardMatch.Success && int.TryParse(sinjoRewardMatch.Groups[1].Value, out _))
            {
                MarkAndLog(_trackItems.FirstOrDefault(static item => item.Name == NestOfShinjoHardItemName), "Sinjo reward", text);
                return true;
            }

            var catacombsRewardMatch = CatacombsRewardRegex.Match(text);
            if (catacombsRewardMatch.Success && int.TryParse(catacombsRewardMatch.Groups[1].Value, out _))
            {
                MarkAndLog(_trackItems.FirstOrDefault(static item => item.Name == CatacombsHellModeItemName), "Catacombs reward", text);
                return true;
            }

            return false;
        }

        public static bool IsMercurialEntryMessage(string text)
        {
            if (text.Contains(MercurialSingleEntryKeyword, StringComparison.Ordinal))
                return true;

            return text.Contains(MercurialEntryTokenDungeon, StringComparison.Ordinal) &&
                   text.Contains(MercurialEntryTokenMode, StringComparison.Ordinal) &&
                   text.Contains(MercurialEntryTokenEnter, StringComparison.Ordinal);
        }

        public static bool IsMercurialRewardMessage(string text)
        {
            if (text.Contains(MercurialSingleRewardKeyword1, StringComparison.Ordinal) ||
                text.Contains(MercurialSingleRewardKeyword2, StringComparison.Ordinal))
                return true;

            bool expReward = text.Contains(MercurialRewardExpToken1, StringComparison.Ordinal) &&
                             text.Contains(MercurialRewardExpToken2, StringComparison.Ordinal);
            bool seedReward = text.Contains(MercurialRewardSeedToken1, StringComparison.Ordinal) &&
                              text.Contains(MercurialRewardSeedToken2, StringComparison.Ordinal) &&
                              text.Contains(MercurialRewardSeedToken3, StringComparison.Ordinal);
            return expReward || seedReward;
        }

        public static bool IsMercurialRewardExpMessage(string text)
            => !string.IsNullOrWhiteSpace(text) &&
               text.Contains(MercurialRewardExpToken1, StringComparison.Ordinal) &&
               text.Contains(MercurialRewardExpToken2, StringComparison.Ordinal);

        public static bool IsMercurialRewardSeedMessage(string text)
            => !string.IsNullOrWhiteSpace(text) &&
               text.Contains(MercurialRewardSeedToken1, StringComparison.Ordinal) &&
               text.Contains(MercurialRewardSeedToken2, StringComparison.Ordinal) &&
               text.Contains(MercurialRewardSeedToken3, StringComparison.Ordinal);

        public static bool TryMatchAbyssFloor(string text, out string floor)
        {
            var match = AbyssFloorRegex.Match(text);
            floor = match.Success ? match.Groups["floor"].Value : string.Empty;
            return match.Success;
        }

        public static bool TryMatchAbyssEntry(string text, out string floor)
        {
            var match = AbyssEntryRegex.Match(text);
            floor = match.Success ? match.Groups["floor"].Value : string.Empty;
            return match.Success;
        }

        public static bool TryMatchAbyssReward(string text, out int count)
        {
            var match = AbyssRewardRegex.Match(text);
            return TryParseFirstGroup(match, out count);
        }

        public static bool TryMatchSinjoReward(string text, out int count)
        {
            var match = SinjoRewardRegex.Match(text);
            return TryParseFirstGroup(match, out count);
        }

        public static bool TryMatchCatacombsReward(string text, out int count)
        {
            var match = CatacombsRewardRegex.Match(text);
            return TryParseFirstGroup(match, out count);
        }

        public static bool TryMatchAbandonRoadCount(string text, out int count)
        {
            var match = AbandonCountRegex.Match(text);
            return TryParseFirstGroup(match, out count);
        }

        public static bool TryUpdateDetail(DailyWeeklyContentLog item, string text, out int? count)
        {
            count = null;

            if (TryExtractDetailValue(item, text, out int value, out var detailKind))
            {
                if (detailKind == DailyWeeklyDetailKind.AbandonRoad && item.MaxCount == 0)
                {
                    item.MaxCount = 10;
                }
                else if (detailKind == DailyWeeklyDetailKind.CravingPleasure)
                {
                    if (item.MaxCount == 0)
                        item.MaxCount = 20;
                    item.SetCount(21 - value);
                    count = value;
                    return true;
                }

                item.SetCount(value);
                count = value;
                AppLogger.Debug($"[DailyWeekly] Detail count updated. Item='{item.Name}', Kind={detailKind}, Value={value}, Text='{text}'");
                return true;
            }

            return false;
        }

        public static bool TryExtractDetailValue(DailyWeeklyContentLog item, string text, out int value, out DailyWeeklyDetailKind detailKind)
        {
            value = 0;
            detailKind = DailyWeeklyDetailKind.None;

            if (item.LogKeyword?.Contains(AbandonRoadKeywordToken, StringComparison.Ordinal) == true)
            {
                detailKind = DailyWeeklyDetailKind.AbandonRoad;
                return TryParseFirstGroup(AbandonCountRegex.Match(text), out value);
            }

            if (item.LogKeyword?.Contains(CravingPleasureKeywordToken, StringComparison.Ordinal) == true)
            {
                detailKind = DailyWeeklyDetailKind.CravingPleasure;
                return TryParseFirstGroup(PleasureEnergyRegex.Match(text), out value);
            }

            return false;
        }

        public static void ApplySpecialCounts(IReadOnlyList<DailyWeeklyContentLog> items, IReadOnlyDictionary<string, int> specialCounts)
        {
            foreach (var kv in specialCounts)
            {
                if (kv.Key.StartsWith(AbyssSpecialKeyPrefix, StringComparison.Ordinal))
                {
                    string floor = kv.Key.Substring(AbyssSpecialKeyPrefix.Length);
                    string itemName = GetAbyssItemNameByFloor(floor);
                    if (string.IsNullOrEmpty(itemName))
                        continue;

                    items.FirstOrDefault(item => item.Name == itemName)?.SetCount(kv.Value);
                    continue;
                }

                if (kv.Key == CatacombsSpecialKey)
                {
                    items.FirstOrDefault(static item => item.Name == CatacombsHellModeItemName)?.SetCount(kv.Value);
                }
                else if (kv.Key == SinjoSpecialKey)
                {
                    items.FirstOrDefault(static item => item.Name == NestOfShinjoHardItemName)?.SetCount(kv.Value);
                }
                else if (kv.Key == MercurialCaveSpecialKey)
                {
                    var mercurial = items.FirstOrDefault(static item => item.Name == MercurialCaveItemName);
                    if (mercurial == null)
                        continue;

                    if (mercurial.HasCount)
                        mercurial.SetCount(kv.Value);
                    else
                        mercurial.Mark();
                }
            }
        }

        public static string GetAbyssItemNameByFloor(string floor)
            => floor switch
            {
                "Ⅰ" => AbyssDepthOneItemName,
                "Ⅱ" => AbyssDepthTwoItemName,
                "Ⅲ" => AbyssDepthThreeItemName,
                _ => string.Empty
            };

        private static DailyWeeklyContentLog? FindAbyssItemByFloor(IReadOnlyList<DailyWeeklyContentLog> items, string floor)
        {
            string name = GetAbyssItemNameByFloor(floor);
            return string.IsNullOrEmpty(name) ? null : items.FirstOrDefault(item => item.Name == name);
        }

        private static bool TryParseFirstGroup(Match match, out int value)
        {
            value = 0;
            return match.Success && int.TryParse(match.Groups[1].Value, out value);
        }

        private static void MarkAndLog(DailyWeeklyContentLog? item, string reason, string text)
        {
            if (item == null)
                return;

            item.Mark();
            string state = item.HasCount
                ? $"Count={item.CurrentCount}/{item.MaxCount}"
                : $"Cleared={item.IsCleared}";
            AppLogger.Debug($"[DailyWeekly] Content confirmed. Item='{item.Name}', Reason='{reason}', State={state}, Text='{text}'");
        }

        private void SetAccumulatedCount(DailyWeeklyContentLog? item, int rawCount)
        {
            if (item == null)
                return;

            if (!_countStates.TryGetValue(item.Name, out var state))
            {
                state = new AccumulatedCountState();
                _countStates[item.Name] = state;
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
    }

    public enum DailyWeeklyDetailKind
    {
        None,
        AbandonRoad,
        CravingPleasure
    }
}
