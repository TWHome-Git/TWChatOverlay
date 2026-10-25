using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay
{
    /// <summary>
    /// Backward-compatible facade for log analysis helpers.
    /// </summary>
    public static class LogParser
    {
        public class ParseResult
        {
            public string FormattedText { get; set; } = "";
            public SolidColorBrush Brush { get; set; } = Brushes.White;
            public ChatCategory Category { get; set; } = ChatCategory.Unknown;
            /// <summary>외치기 줄의 세부 종류(무료/유료/공지). Category가 Shout일 때만 뜻이 있다.</summary>
            public ShoutKind ShoutKind { get; set; } = ShoutKind.Notice;
            public bool IsSuccess { get; set; } = false;
            public bool IsHighlight { get; set; } = false;
            public bool IsMagicCircleAlert { get; set; } = false;
            public bool IsReflectionPatternAlert { get; set; } = false;
            /// <summary>상태이상 [방전] 줄 — 어비스에서 방전에 걸린 순간.</summary>
            public bool IsDischargeAlert { get; set; } = false;
            /// <summary>"방전상태가 풀렸습니다" 줄 — 방전이 풀린 순간.</summary>
            public bool IsDischargeCleared { get; set; } = false;
            public long GainedExp { get; set; } = 0;
            /// <summary>몬스터 처치로 얻은 경험치 줄인지 (보상 경험치가 아님). 경험치 추적창·사냥 기록은 이것만 센다.</summary>
            public bool IsHuntingExp { get; set; } = false;
            public string? EtosImagePath { get; set; } = null;
            public bool IsTrackedItemDrop { get; set; } = false;
            public string? TrackedItemName { get; set; } = null;
            public ItemDropGrade TrackedItemGrade { get; set; } = ItemDropGrade.Normal;
            public int TrackedItemCount { get; set; } = 1;
            public string? SenderId { get; set; } = null;
            public string? RawSenderId { get; set; } = null;
            public bool HasLeadingBodyWhitespace { get; set; } = false;
            /// <summary>클럽 보스 공지 줄인지 ([클럽 보스] 포함) — 표시 여부·개별 색상에 사용.</summary>
            public bool IsClubBossMessage { get; set; } = false;
            /// <summary>
            /// 일반(흰색) 줄 중 보스·몬스터 대사(문구 또는 화자 일치) — "보스 대사" 토글이 꺼져 있을 때만 숨긴다.
            /// 설정과 무관한 매치 결과만 담으므로 분석 시점에 한 번 계산하고, 토글 결합은 소비 시점에 한다.
            /// </summary>
            public bool IsBossDialogueLine { get; set; } = false;
            /// <summary>일반(흰색) 줄 중 토글과 무관하게 늘 숨기는 안내문(상자 획득, 회복, 등장 안내 등).</summary>
            public bool IsIgnoredNoticeLine { get; set; } = false;
            /// <summary>
            /// 설정과 무관하게 채팅 오버레이(메인·서브 창 모두)에 그리지 않는 줄 — 반복 안내(오를리 남은 공격 횟수·자동 퇴장),
            /// 에토스 방향 암호 줄(방향 오버레이만 띄움), 수색대장 에토스 화자 대사. 분석 시점에 한 번 계산한다.
            /// 설정에 따르는 숨김(보스 대사·클럽 보스)은 <see cref="LogAnalysisService.IsHiddenByChatFilter"/>가 표시 시점마다 따로 본다.
            /// </summary>
            public bool IsDisplaySuppressed { get; set; } = false;

            /// <summary>얕은 복사본. 참조 필드는 문자열과 Frozen 브러시뿐이라 얕은 복사로 원본과 독립된다.</summary>
            public ParseResult Clone() => (ParseResult)MemberwiseClone();
        }

        public static bool IsMatchTab(ParseResult log, string tabTag, ChatSettings settings)
        {
            return tabTag switch
            {
                "Basic" => IsVisible(log, settings),
                "General" => log.Category is ChatCategory.NormalSelf or ChatCategory.Normal && settings.ShowNormal,
                "Team" => log.Category == ChatCategory.Team,
                "Club" => log.Category == ChatCategory.Club,
                "Shout" => log.Category == ChatCategory.Shout,
                "System" => log.Category is ChatCategory.System or ChatCategory.System2 or ChatCategory.System3,
                _ => false
            };
        }

        /// <summary>줄 단위 표시 여부. 외치기는 무료/유료/공지 종류별 설정을 따른다.</summary>
        public static bool IsVisible(ParseResult log, ChatSettings settings)
        {
            if (log.Category == ChatCategory.Shout)
            {
                return log.ShoutKind switch
                {
                    ShoutKind.Free => settings.ShowFreeShout,
                    ShoutKind.Paid => settings.ShowPaidShout,
                    _ => settings.ShowNoticeShout,
                };
            }

            return IsVisible(log.Category, settings);
        }

        public static bool IsVisible(ChatCategory category, ChatSettings settings)
        {
            return category switch
            {
                ChatCategory.NormalSelf or ChatCategory.Normal => settings.ShowNormal,
                ChatCategory.Club => settings.ShowClub,
                ChatCategory.Team => settings.ShowTeam,
                ChatCategory.System or ChatCategory.System2 or ChatCategory.System3 => settings.ShowSystem,
                _ => true
            };
        }
    }
}
