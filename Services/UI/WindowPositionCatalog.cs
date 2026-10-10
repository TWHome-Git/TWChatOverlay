using System;
using System.Collections.Generic;
using System.Linq;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>알림 표시 위치 통합 설정에 따라 목록에 보일지 가려지는 구분.</summary>
    public enum WindowPositionScope
    {
        /// <summary>통합 여부와 무관하게 자기 자리를 쓰는 창.</summary>
        Always,
        /// <summary>통합 모드의 기준 위치 — 통합이 켜져 있을 때만 보인다.</summary>
        UnifiedAnchor,
        /// <summary>통합 모드에서는 기준 위치를 따르는 알림 — 통합이 꺼져 있을 때만 보인다.</summary>
        SeparateToast,
    }

    /// <summary>창 하나의 저장 위치를 읽고 쓰는 방법.</summary>
    public sealed class WindowPositionEntry
    {
        public WindowPositionEntry(string name, WindowPositionScope scope,
            Func<ChatSettings, (double? Left, double? Top)> read,
            Action<ChatSettings, double, double> write)
        {
            Name = name;
            Scope = scope;
            Read = read;
            Write = write;
        }

        public string Name { get; }
        public WindowPositionScope Scope { get; }
        public Func<ChatSettings, (double? Left, double? Top)> Read { get; }
        public Action<ChatSettings, double, double> Write { get; }
    }

    /// <summary>
    /// 잠금 해제 모드에서 창을 화면 밖으로 끌고 나가면 다시 잡을 수 없다.
    /// 설정 화면이 창마다 지금 좌표를 보여주고 0,0으로 되돌릴 수 있도록, 옮길 수 있는 창의 저장 위치를 한곳에 모았다.
    /// 실제로 떠 있는 창을 옮기는 일은 MainWindow.ReapplyStoredWindowPositions가 맡는다 — 여기서는 설정값만 다룬다.
    /// </summary>
    public static class WindowPositionCatalog
    {
        private static readonly WindowPositionEntry[] Entries =
        {
            // 메인 채팅창 위치는 예전 이름(LineMargin)으로 저장된다
            new("채팅창", WindowPositionScope.Always,
                s => (s.LineMarginLeft, s.LineMargin),
                (s, l, t) => { s.LineMarginLeft = l; s.LineMargin = t; }),
            new("메뉴 바", WindowPositionScope.Always,
                s => (s.MenuWindowLeft, s.MenuWindowTop),
                (s, l, t) => { s.MenuWindowLeft = l; s.MenuWindowTop = t; }),
            new("서브 메뉴", WindowPositionScope.Always,
                s => (s.SubMenuWindowLeft, s.SubMenuWindowTop),
                (s, l, t) => { s.SubMenuWindowLeft = l; s.SubMenuWindowTop = t; }),
            new("일일/주간 컨텐츠", WindowPositionScope.Always,
                s => (s.DailyWeeklyContentOverlayLeft, s.DailyWeeklyContentOverlayTop),
                (s, l, t) => { s.DailyWeeklyContentOverlayLeft = l; s.DailyWeeklyContentOverlayTop = t; }),
            new("서브 채팅창 1", WindowPositionScope.Always,
                s => (s.ChatCloneWindow1Left, s.ChatCloneWindow1Top),
                (s, l, t) => { s.ChatCloneWindow1Left = l; s.ChatCloneWindow1Top = t; }),
            new("서브 채팅창 2", WindowPositionScope.Always,
                s => (s.ChatCloneWindow2Left, s.ChatCloneWindow2Top),
                (s, l, t) => { s.ChatCloneWindow2Left = l; s.ChatCloneWindow2Top = t; }),
            new("에토스 방향 안내", WindowPositionScope.Always,
                s => (s.SubAddonWindowLeft, s.SubAddonWindowTop),
                (s, l, t) => { s.SubAddonWindowLeft = l; s.SubAddonWindowTop = t; }),
            // 경험치 추적창은 오른쪽 가장자리에 붙여 둘 수 있다 — 0,0으로 되돌릴 때는 그 고정을 푼다
            new("경험치 추적창", WindowPositionScope.Always,
                s => (s.ExpTrackerWindowLeft, s.ExpTrackerWindowTop),
                (s, l, t) => { s.ExpTrackerWindowRight = null; s.ExpTrackerWindowLeft = l; s.ExpTrackerWindowTop = t; }),
            new("버프 추적", WindowPositionScope.Always,
                s => (s.BuffTrackerWindowLeft, s.BuffTrackerWindowTop),
                (s, l, t) => { s.BuffTrackerWindowLeft = l; s.BuffTrackerWindowTop = t; }),
            new("던전 타이머", WindowPositionScope.Always,
                s => (s.ContentTimerWindowLeft, s.ContentTimerWindowTop),
                (s, l, t) => { s.ContentTimerWindowLeft = l; s.ContentTimerWindowTop = t; }),
            new("던전 특수 알림", WindowPositionScope.Always,
                s => (s.DungeonAlertWindowLeft, s.DungeonAlertWindowTop),
                (s, l, t) => { s.DungeonAlertWindowLeft = l; s.DungeonAlertWindowTop = t; }),
            new("아이템 달력", WindowPositionScope.Always,
                s => (s.ItemCalendarWindowLeft, s.ItemCalendarWindowTop),
                (s, l, t) => { s.ItemCalendarWindowLeft = l; s.ItemCalendarWindowTop = t; }),
            new("어밴던로드 통계", WindowPositionScope.Always,
                s => (s.AbandonRoadSummaryWindowLeft, s.AbandonRoadSummaryWindowTop),
                (s, l, t) => { s.AbandonRoadSummaryWindowLeft = l; s.AbandonRoadSummaryWindowTop = t; }),
            new("보물창고 통계", WindowPositionScope.Always,
                s => (s.TreasurySummaryWindowLeft, s.TreasurySummaryWindowTop),
                (s, l, t) => { s.TreasurySummaryWindowLeft = l; s.TreasurySummaryWindowTop = t; }),
            new("보급품 탈환", WindowPositionScope.Always,
                s => (s.RecaptureSupplyWindowLeft, s.RecaptureSupplyWindowTop),
                (s, l, t) => { s.RecaptureSupplyWindowLeft = l; s.RecaptureSupplyWindowTop = t; }),
            new("보급품 발판 순서", WindowPositionScope.Always,
                s => (s.RecaptureSupplyPadOrderWindowLeft, s.RecaptureSupplyPadOrderWindowTop),
                (s, l, t) => { s.RecaptureSupplyPadOrderWindowLeft = l; s.RecaptureSupplyPadOrderWindowTop = t; }),
            new("외치기 다시보기", WindowPositionScope.Always,
                s => (s.ShoutReplayWindowLeft, s.ShoutReplayWindowTop),
                (s, l, t) => { s.ShoutReplayWindowLeft = l; s.ShoutReplayWindowTop = t; }),
            new("메모", WindowPositionScope.Always,
                s => (s.MemoOverlayWindowLeft, s.MemoOverlayWindowTop),
                (s, l, t) => { s.MemoOverlayWindowLeft = l; s.MemoOverlayWindowTop = t; }),
            new("1:1 대화 에타 표시", WindowPositionScope.Always,
                s => (s.MessengerToastWindowLeft, s.MessengerToastWindowTop),
                (s, l, t) => { s.MessengerToastWindowLeft = l; s.MessengerToastWindowTop = t; }),

            // 알림 표시 위치 통합이 켜져 있으면 아래 알림들은 모두 이 한 자리에 쌓인다
            new("통합 알림 위치", WindowPositionScope.UnifiedAnchor,
                s => (s.ToastStackLeft, s.ToastStackTop),
                (s, l, t) => { s.ToastStackLeft = l; s.ToastStackTop = t; }),

            new("외치기 알림", WindowPositionScope.SeparateToast,
                s => (s.ShoutToastWindowLeft, s.ShoutToastWindowTop),
                (s, l, t) => { s.ShoutToastWindowLeft = l; s.ShoutToastWindowTop = t; }),
            new("던전 알림", WindowPositionScope.SeparateToast,
                s => (s.DungeonCountDisplayWindowLeft, s.DungeonCountDisplayWindowTop),
                (s, l, t) => { s.DungeonCountDisplayWindowLeft = l; s.DungeonCountDisplayWindowTop = t; }),
            new("경험치 알림", WindowPositionScope.SeparateToast,
                s => (s.ExperienceLimitAlertWindowLeft, s.ExperienceLimitAlertWindowTop),
                (s, l, t) => { s.ExperienceLimitAlertWindowLeft = l; s.ExperienceLimitAlertWindowTop = t; }),
            new("아이템 알림", WindowPositionScope.SeparateToast,
                s => (s.ItemDropWindowLeft, s.ItemDropWindowTop),
                (s, l, t) => { s.ItemDropWindowLeft = l; s.ItemDropWindowTop = t; }),
            new("필드 보스 알림", WindowPositionScope.SeparateToast,
                s => (s.BossAlertToastWindowLeft, s.BossAlertToastWindowTop),
                (s, l, t) => { s.BossAlertToastWindowLeft = l; s.BossAlertToastWindowTop = t; }),
        };

        /// <summary>지금 설정에서 목록에 보여야 할 창들. 알림 표시 위치 통합 여부로 알림 항목이 갈린다.</summary>
        public static IEnumerable<WindowPositionEntry> VisibleFor(ChatSettings settings)
        {
            bool unified = settings?.UnifiedToastStack != false;
            return Entries.Where(e => e.Scope switch
            {
                WindowPositionScope.UnifiedAnchor => unified,
                WindowPositionScope.SeparateToast => !unified,
                _ => true,
            });
        }
    }
}
