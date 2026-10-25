using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

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

    /// <summary>창 하나의 저장 위치를 읽고 쓰고, 떠 있는 그 창을 저장된 자리로 옮기는 방법.</summary>
    public sealed class WindowPositionEntry
    {
        private readonly Action<ChatSettings, double?, double?> _move;

        public WindowPositionEntry(string name, WindowPositionScope scope,
            Func<ChatSettings, (double? Left, double? Top)> read,
            Action<ChatSettings, double, double> write,
            Action<ChatSettings, double?, double?> move)
        {
            Name = name;
            Scope = scope;
            Read = read;
            Write = write;
            _move = move;
        }

        public string Name { get; }
        public WindowPositionScope Scope { get; }
        public Func<ChatSettings, (double? Left, double? Top)> Read { get; }
        public Action<ChatSettings, double, double> Write { get; }

        /// <summary>
        /// 떠 있는 이 창만 저장된 자리로 옮긴다. 창이 없으면 아무것도 하지 않는다.
        /// 다른 창은 건드리지 않는다 — 초기화 한 번에 설정 창까지 끌려가면 안 된다.
        /// </summary>
        public void MoveToStored(ChatSettings settings)
        {
            try
            {
                (double? left, double? top) = Read(settings);
                _move(settings, left, top);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to move window '{Name}' to stored position.", ex);
            }
        }
    }

    /// <summary>
    /// 잠금 해제 모드에서 창을 화면 밖으로 끌고 나가면 다시 잡을 수 없다.
    /// 설정 화면이 창마다 지금 좌표를 보여주고 0,0으로 되돌릴 수 있도록, 옮길 수 있는 창의 저장 위치를 한곳에 모았다.
    /// 떠 있는 창을 옮기는 일은 각 창의 OverlayWindowBase.ApplyStoredBounds(복원 경로 하나)에 맡기고, 서비스가 들고 있는 창·일반 Window만 위임한다.
    /// </summary>
    public static class WindowPositionCatalog
    {
        private static readonly WindowPositionEntry[] Entries =
        {
            // 메인 채팅창 위치는 예전 이름(LineMargin)으로 저장된다
            new("채팅창", WindowPositionScope.Always,
                s => (s.LineMarginLeft, s.LineMargin),
                (s, l, t) => { s.LineMarginLeft = l; s.LineMargin = t; },
                (s, l, t) => MoveMainWindow(l, t)),
            new("메뉴 바", WindowPositionScope.Always,
                s => (s.MenuWindowLeft, s.MenuWindowTop),
                (s, l, t) => { s.MenuWindowLeft = l; s.MenuWindowTop = t; },
                (s, l, t) => ForEach<MenuWindow>(w => w.ApplyStoredPosition())),
            new("서브 메뉴", WindowPositionScope.Always,
                s => (s.SubMenuWindowLeft, s.SubMenuWindowTop),
                (s, l, t) => { s.SubMenuWindowLeft = l; s.SubMenuWindowTop = t; },
                (s, l, t) => ForEach<SubMenuWindow>(w => w.ApplyStoredPosition())),
            new("일일/주간 컨텐츠", WindowPositionScope.Always,
                s => (s.DailyWeeklyContentOverlayLeft, s.DailyWeeklyContentOverlayTop),
                (s, l, t) => { s.DailyWeeklyContentOverlayLeft = l; s.DailyWeeklyContentOverlayTop = t; },
                (s, l, t) => MoveStored<DailyWeeklyContentWindow>(s)),
            new("서브 채팅창 1", WindowPositionScope.Always,
                s => (s.ChatCloneWindow1Left, s.ChatCloneWindow1Top),
                (s, l, t) => { s.ChatCloneWindow1Left = l; s.ChatCloneWindow1Top = t; },
                (s, l, t) => ForEach<ChatCloneWindow>(w => { if (w.Slot == 1) w.ApplyStoredBounds(s); })),
            new("서브 채팅창 2", WindowPositionScope.Always,
                s => (s.ChatCloneWindow2Left, s.ChatCloneWindow2Top),
                (s, l, t) => { s.ChatCloneWindow2Left = l; s.ChatCloneWindow2Top = t; },
                (s, l, t) => ForEach<ChatCloneWindow>(w => { if (w.Slot == 2) w.ApplyStoredBounds(s); })),
            new("에토스 방향 안내", WindowPositionScope.Always,
                s => (s.SubAddonWindowLeft, s.SubAddonWindowTop),
                (s, l, t) => { s.SubAddonWindowLeft = l; s.SubAddonWindowTop = t; },
                (s, l, t) => Move<SubAddonWindow>(l, t)),
            // 경험치 추적창은 오른쪽 가장자리에 붙여 둘 수 있다 — 자리를 지정할 때는 그 고정을 푼다
            new("경험치 추적창", WindowPositionScope.Always,
                s => (s.ExpTrackerWindowLeft, s.ExpTrackerWindowTop),
                (s, l, t) => { s.ExpTrackerWindowRight = null; s.ExpTrackerWindowLeft = l; s.ExpTrackerWindowTop = t; },
                (s, l, t) => MoveStored<ExpTrackerWindow>(s)),
            // 버프 추적은 실제 창과 잠금 해제 미리보기 창이 한 자리를 쓴다
            new("버프 추적", WindowPositionScope.Always,
                s => (s.BuffTrackerWindowLeft, s.BuffTrackerWindowTop),
                (s, l, t) => { s.BuffTrackerWindowLeft = l; s.BuffTrackerWindowTop = t; },
                (s, l, t) => { MoveStored<BuffTrackerWindow>(s); MoveStored<BuffTrackerHelperWindow>(s); }),
            new("던전 타이머", WindowPositionScope.Always,
                s => (s.ContentTimerWindowLeft, s.ContentTimerWindowTop),
                (s, l, t) => { s.ContentTimerWindowLeft = l; s.ContentTimerWindowTop = t; },
                (s, l, t) => AppServices.Get<ContentTimerService>().ApplyStoredBounds(s)),
            new("던전 특수 알림", WindowPositionScope.Always,
                s => (s.DungeonAlertWindowLeft, s.DungeonAlertWindowTop),
                (s, l, t) => { s.DungeonAlertWindowLeft = l; s.DungeonAlertWindowTop = t; },
                (s, l, t) => DungeonAlertWindow.ApplyStoredPosition()),
            new("아이템 달력", WindowPositionScope.Always,
                s => (s.ItemCalendarWindowLeft, s.ItemCalendarWindowTop),
                (s, l, t) => { s.ItemCalendarWindowLeft = l; s.ItemCalendarWindowTop = t; },
                (s, l, t) => MoveStored<ItemCalendarWindow>(s)),
            new("어밴던로드 통계", WindowPositionScope.Always,
                s => (s.AbandonRoadSummaryWindowLeft, s.AbandonRoadSummaryWindowTop),
                (s, l, t) => { s.AbandonRoadSummaryWindowLeft = l; s.AbandonRoadSummaryWindowTop = t; },
                (s, l, t) => MoveStored<AbandonRoadSummaryWindow>(s)),
            new("보물창고 통계", WindowPositionScope.Always,
                s => (s.TreasurySummaryWindowLeft, s.TreasurySummaryWindowTop),
                (s, l, t) => { s.TreasurySummaryWindowLeft = l; s.TreasurySummaryWindowTop = t; },
                (s, l, t) => TreasurySummaryWindow.ApplyStoredPosition(s)),
            new("보급품 탈환", WindowPositionScope.Always,
                s => (s.RecaptureSupplyWindowLeft, s.RecaptureSupplyWindowTop),
                (s, l, t) => { s.RecaptureSupplyWindowLeft = l; s.RecaptureSupplyWindowTop = t; },
                (s, l, t) => MoveStored<RecaptureSupplyWindow>(s)),
            new("보급품 발판 순서", WindowPositionScope.Always,
                s => (s.RecaptureSupplyPadOrderWindowLeft, s.RecaptureSupplyPadOrderWindowTop),
                (s, l, t) => { s.RecaptureSupplyPadOrderWindowLeft = l; s.RecaptureSupplyPadOrderWindowTop = t; },
                (s, l, t) => MoveStored<RecaptureSupplyPadOrderWindow>(s)),
            new("외치기 다시보기", WindowPositionScope.Always,
                s => (s.ShoutReplayWindowLeft, s.ShoutReplayWindowTop),
                (s, l, t) => { s.ShoutReplayWindowLeft = l; s.ShoutReplayWindowTop = t; },
                (s, l, t) => MoveStored<ShoutReplayWindow>(s)),
            new("메모", WindowPositionScope.Always,
                s => (s.MemoOverlayWindowLeft, s.MemoOverlayWindowTop),
                (s, l, t) => { s.MemoOverlayWindowLeft = l; s.MemoOverlayWindowTop = t; },
                (s, l, t) => MoveStored<MemoOverlayWindow>(s)),
            new("던전 타이머 기록 추이", WindowPositionScope.Always,
                s => (s.DungeonTimerDetailWindowLeft, s.DungeonTimerDetailWindowTop),
                (s, l, t) => { s.DungeonTimerDetailWindowLeft = l; s.DungeonTimerDetailWindowTop = t; },
                (s, l, t) => MoveStored<DungeonTimerDetailWindow>(s)),
            new("주간 득템 통계", WindowPositionScope.Always,
                s => (s.WeeklyItemSummaryWindowLeft, s.WeeklyItemSummaryWindowTop),
                (s, l, t) => { s.WeeklyItemSummaryWindowLeft = l; s.WeeklyItemSummaryWindowTop = t; },
                (s, l, t) => MoveStored<WeeklyItemSummaryWindow>(s)),
            new("사냥 기록", WindowPositionScope.Always,
                s => (s.ExpHuntRecordWindowLeft, s.ExpHuntRecordWindowTop),
                (s, l, t) => { s.ExpHuntRecordWindowLeft = l; s.ExpHuntRecordWindowTop = t; },
                (s, l, t) => MoveStored<ExpHuntRecordWindow>(s)),
            new("1:1 대화 에타 표시", WindowPositionScope.Always,
                s => (s.MessengerToastWindowLeft, s.MessengerToastWindowTop),
                (s, l, t) => { s.MessengerToastWindowLeft = l; s.MessengerToastWindowTop = t; },
                (s, l, t) => AppServices.Get<MessengerEtaToastService>().ReapplyPreviewPosition(s)),

            // 알림 표시 위치 통합이 켜져 있으면 아래 알림들은 모두 이 한 자리에 쌓인다.
            // 알림 창의 자리는 스택이 정하므로 앵커만 바뀌면 다시 늘어놓기만 하면 된다.
            new("통합 알림 위치", WindowPositionScope.UnifiedAnchor,
                s => (s.ToastStackLeft, s.ToastStackTop),
                (s, l, t) => { s.ToastStackLeft = l; s.ToastStackTop = t; },
                (s, l, t) => ReflowToasts(s)),

            new("외치기 알림", WindowPositionScope.SeparateToast,
                s => (s.ShoutToastWindowLeft, s.ShoutToastWindowTop),
                (s, l, t) => { s.ShoutToastWindowLeft = l; s.ShoutToastWindowTop = t; },
                (s, l, t) => ReflowToasts(s)),
            new("던전 알림", WindowPositionScope.SeparateToast,
                s => (s.DungeonCountDisplayWindowLeft, s.DungeonCountDisplayWindowTop),
                (s, l, t) => { s.DungeonCountDisplayWindowLeft = l; s.DungeonCountDisplayWindowTop = t; },
                (s, l, t) => ReflowToasts(s)),
            new("경험치 알림", WindowPositionScope.SeparateToast,
                s => (s.ExperienceLimitAlertWindowLeft, s.ExperienceLimitAlertWindowTop),
                (s, l, t) => { s.ExperienceLimitAlertWindowLeft = l; s.ExperienceLimitAlertWindowTop = t; },
                (s, l, t) => ReflowToasts(s)),
            new("아이템 알림", WindowPositionScope.SeparateToast,
                s => (s.ItemDropWindowLeft, s.ItemDropWindowTop),
                (s, l, t) => { s.ItemDropWindowLeft = l; s.ItemDropWindowTop = t; },
                (s, l, t) => { MoveStored<ItemDropHelperWindow>(s); ReflowToasts(s); }),
            new("필드 보스 알림", WindowPositionScope.SeparateToast,
                s => (s.BossAlertToastWindowLeft, s.BossAlertToastWindowTop),
                (s, l, t) => { s.BossAlertToastWindowLeft = l; s.BossAlertToastWindowTop = t; },
                (s, l, t) => ReflowToasts(s)),
        };

        // ===== 떠 있는 창 옮기기 =====

        /// <summary>그 종류로 떠 있는 오버레이 창을 저장된 자리로 옮긴다 — 창이 자기 설정 키를 알고 있다(OverlayWindowBase.ApplyStoredBounds).</summary>
        private static void MoveStored<T>(ChatSettings settings) where T : OverlayWindowBase
            => ForEach<T>(w => w.ApplyStoredBounds(settings));

        /// <summary>베이스를 쓰지 않는 일반 Window를 저장된 자리로 옮긴다.</summary>
        private static void Move<T>(double? left, double? top) where T : Window
            => ForEach<T>(w => WindowPlacement.ApplyStored(w, left, top));

        private static void ForEach<T>(Action<T> action) where T : Window
        {
            if (Application.Current == null)
                return;

            foreach (T window in Application.Current.Windows.OfType<T>().ToList())
            {
                try { action(window); }
                catch (Exception ex) { AppLogger.Warn($"Failed to move {typeof(T).Name}.", ex); }
            }
        }

        /// <summary>메인 채팅창. 스티키 서비스가 옛 자리로 되돌리지 않게 바뀐 위치를 알려 준다.</summary>
        private static void MoveMainWindow(double? left, double? top)
            => ForEach<MainWindow>(w =>
            {
                WindowPlacement.ApplyStored(w, left, top);
                w.NotifyPositionMovedExternally();
            });

        /// <summary>알림 창은 스택이 자리를 정하므로 앵커가 바뀌면 다시 늘어놓기만 하면 된다.</summary>
        private static void ReflowToasts(ChatSettings settings)
        {
            AppServices.Get<ToastStackService>().Reflow();
            AppServices.Get<ToastStackService>().RefreshPreviews(settings);
        }

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
