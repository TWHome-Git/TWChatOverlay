using System;
using System.Collections.Generic;
using TWChatOverlay.Models;

namespace TWChatOverlay.Views
{
    /// <summary>어밴던로드 마정석 4종 아이콘 URI. 통계창·달력·채팅 요약 문단이 같은 그림을 쓴다.</summary>
    public static class AbandonStoneIcons
    {
        public const string Low = "pack://application:,,,/Data/images/Item/하급마정석.png";
        public const string Middle = "pack://application:,,,/Data/images/Item/중급마정석.png";
        public const string High = "pack://application:,,,/Data/images/Item/상급마정석.png";
        public const string Top = "pack://application:,,,/Data/images/Item/최상급마정석.png";
    }

    /// <summary>
    /// 어밴던로드 마정석 5행(하·중·상·최상·누에게 빼앗긴)을 채우는 공용 조각.
    /// 어밴던로드 통계창(주간 스냅샷·실시간 값)과 아이템 달력(월간)이 같은 모양으로 채운다.
    /// </summary>
    public static class AbandonStoneRows
    {
        private const string LossColor = "#FF6B6B";

        /// <summary>
        /// <paramref name="target"/>을 비우고 5행을 채운다.
        /// '누에게 빼앗긴' 수는 LowLoss 절댓값. 그게 0이고 Low가 음수면 Low의 절댓값을 쓴다.
        /// <paramref name="lowGain"/>이 있으면(달력) 얻은 하급 − 순 하급으로 계산한 값이 있고 다르면 그 값을 우선한다.
        /// </summary>
        public static void Fill(ICollection<AbandonMonthlyStoneSummaryEntryViewModel> target,
            long low, long mid, long high, long top, long lowLoss, long? lowGain = null)
        {
            target.Clear();
            target.Add(new AbandonMonthlyStoneSummaryEntryViewModel("하급 마정석", AbandonStoneIcons.Low, low));
            target.Add(new AbandonMonthlyStoneSummaryEntryViewModel("중급 마정석", AbandonStoneIcons.Middle, mid));
            target.Add(new AbandonMonthlyStoneSummaryEntryViewModel("상급 마정석", AbandonStoneIcons.High, high));
            target.Add(new AbandonMonthlyStoneSummaryEntryViewModel("최상급 마정석", AbandonStoneIcons.Top, top));

            long lowLossCount = Math.Abs(lowLoss);
            long derivedLowLoss = lowGain.HasValue ? Math.Max(0, lowGain.Value - low) : 0;
            if (derivedLowLoss > 0 && lowLossCount != derivedLowLoss)
                lowLossCount = derivedLowLoss;
            else if (lowLossCount == 0 && low < 0)
                lowLossCount = Math.Abs(low);

            target.Add(new AbandonMonthlyStoneSummaryEntryViewModel(
                "누에게 빼앗긴 마정석",
                AbandonStoneIcons.Low,
                lowLossCount,
                LossColor,
                LossColor,
                $"{lowLossCount:N0}개"));
        }
    }
}
