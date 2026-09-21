using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>던전 한 판의 구간별 소요 시간 기록. 던전·난이도별로 최근 3회를 파일에 남긴다.</summary>
    public sealed class DungeonRunRecord
    {
        public string DungeonKey { get; set; } = string.Empty;
        public DateTime StartedAt { get; set; }
        public DateTime EndedAt { get; set; }
        public string? Difficulty { get; set; }
        /// <summary>구간 이름 → 소요 초. 구간 순서는 던전 정의를 따른다.</summary>
        public Dictionary<string, double> Segments { get; set; } = new();
        public double TotalSeconds { get; set; }
        /// <summary>시작 대사가 나오기 전에 끝난 판. 소요 시간은 상한(TotalSeconds 이하)만 안다 — "30초 이하"로 보여준다.</summary>
        public bool Capped { get; set; }
    }

    /// <summary>타이머 창이 그릴 표 한 행. 열은 지지난 판 · 지난 판 · 이번 판.</summary>
    public sealed class TimerRow
    {
        public string Name { get; init; } = string.Empty;
        /// <summary>열별 값(초). null이면 "-".</summary>
        public double?[] Seconds { get; } = new double?[ContentTimerService.ColumnCount];
        /// <summary>열별로 값이 상한("N초 이하")인지.</summary>
        public bool[] Capped { get; } = new bool[ContentTimerService.ColumnCount];
        /// <summary>방금 끝난 구간 — 행 이름과 최근 판 값을 초록색으로 강조한다.</summary>
        public bool Highlight { get; set; }
        /// <summary>열별 판 시각("09.16 21:57"). 묶음 모드처럼 행마다 판이 다를 때 값 아래 작게 붙인다. null이면 안 붙인다.</summary>
        public string?[] Times { get; } = new string?[ContentTimerService.ColumnCount];
    }

    /// <summary>타이머 창이 그릴 내용 전체. 서비스가 만들고 창은 그리기만 한다.</summary>
    public sealed class TimerView
    {
        /// <summary>이 표가 보여주는 묶음 키 (창 왼쪽 목록에서 강조할 항목).</summary>
        public string GroupKey { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public List<TimerRow> Rows { get; } = new();
        /// <summary>합계 행 표시 여부 (한 판을 구간으로 나눈 던전만).</summary>
        public bool ShowTotal { get; init; }
        /// <summary>열 머리글 아래 작은 글씨 표시 여부 (열이 판 하나를 뜻할 때만: 지난주 판 수, 지난 판·이번 판 클리어 시각).</summary>
        public bool ShowColumnTimes { get; init; }
        /// <summary>열 머리글 아래 작은 글씨 ("7판", "06:52" 등). null이면 "-".</summary>
        public string?[] ColumnSub { get; } = new string?[ContentTimerService.ColumnCount];
        /// <summary>합계 행 열별 값.</summary>
        public double?[] Totals { get; } = new double?[ContentTimerService.ColumnCount];
        /// <summary>합계 행 열별로 값이 상한인지.</summary>
        public bool[] TotalsCapped { get; } = new bool[ContentTimerService.ColumnCount];
        /// <summary>가운데 열이 직전 판 대신 최고 기록인지 (머리글을 눌러 바꾼 상태).</summary>
        public bool PreviousIsBest { get; init; }
    }

    /// <summary>
    /// 던전 타이머: 던전 판의 소요 시간을 로그에 찍힌 시각으로 재고, 클리어되는 순간 지난주 평균 · 지난 판 · 이번 판을 나란히 보여준다.
    /// 진행 중에는 아무것도 표시하지 않는다 — 시작 문구와 끝 문구가 모두 잡혀 완성된 판만 기록·표시하므로,
    /// 잘못 열린 판(다른 컨텐츠 안에서 같은 대사가 찍힌 경우)은 끝 문구가 오지 않아 저절로 사라진다.
    /// 묶음(GroupKey)마다 판을 따로 추적하므로 아페티리아 판 도중 이클립스 보스 대사가 끼어들어도 아페티리아 판은 끊기지 않는다.
    /// 완료된 판은 던전별(난이도를 구분하는 던전은 난이도별)로 이번 주와 지난주, 주당 최대 7개를 파일에 남긴다 (주 경계는 일일/주간 창과 같은 월요일).
    /// 시작 시 지난주 월요일부터의 채팅 로그 파일을 읽어 두 주의 기록을 복원한다 (이미 있는 판은 건너뛴다).
    ///
    /// 완료 문구는 일일/주간 컨텐츠 창의 항목 문구(LogKeyword 상수)를 그대로 참조해 한 곳에서만 관리한다.
    /// 타이머에만 필요한 시작 문구·구간 대사만 여기서 더한다.
    ///
    /// 표시 방식은 두 가지다.
    ///  - 구간 모드 (아페티리아·신조·최후의 결전): 한 판을 구간으로 나눠 행으로, 열은 판 하나씩. 합계 행 있음.
    ///  - 묶음 모드 (어비스 심층·이클립스): 같은 묶음(GroupKey)의 던전들을 행으로, 각 행은 그 던전의 최근 3판. 합계 없음.
    /// 시작 대사는 같은데 끝 문구로 컨텐츠가 갈리는 경우(이클립스 보스 대사 → 토벌전 / 보스전)는 정의 하나에
    /// 변형(<see cref="RunVariant"/>)을 여럿 두고, 끝 문구가 어느 변형에 맞는지로 기록 대상을 정한다.
    /// 판정은 <see cref="RunTracker"/>가 하며, 실시간 로그와 과거 로그 파일 읽기가 같은 추적기를 쓴다.
    ///
    /// 아페티리아 (2026-09-16 로그 6판 기준, 매 판 빠짐없이 찍힘):
    ///   판 시작  : 1보스 시작 대사에서 판이 열린다. 진행 중에 이 대사가 다시 오면 새 판으로 시작한다.
    ///   1보스    : "검의 사제, 셀리니아코스 : 집행을 시작하겠소." ~ "검의 사제, 셀리니아코스 : 패배를 인정하오."
    ///   석상     : 1보스 끝 + 2초(연출) ~ "포탈에 도달한 혼령이 어떤 일을 불러올지는 아직 알 수 없습니다." (2보스 시작 5~6초 전)
    ///   2보스    : "지팡이의 사제, 고이티아 : 시작해볼까?" ~ "지팡이의 사제, 고이티아 : 감히…… 계획을 바꿔야겠어……"
    ///   3보스    : 2보스 끝 + 2초(연출) ~ "키시니크 : 이 굴레는 반복될 것이다……"
    ///   판 끝    : "아페티리아 클리어 횟수: [N회/7회]" (3보스 끝과 같은 초) — 일일/주간 문구
    ///   EX(변형) : 대사·구간이 같고 끝 문구만 "아페티리아(EX) 클리어 횟수:" — 그 경우 아페티리아 EX로 따로 기록한다
    ///   난이도   : 구분하지 않는다
    ///
    /// 어비스 - 심층Ⅰ/Ⅱ/Ⅲ (2026-09-14 로그 기준, 각 심층이 별개 판):
    ///   시작     : "어비스 - 심층Ⅰ(보스전)에 지옥 난이도로 입장하셨습니다." ← 난이도는 이 줄에서 읽는다
    ///   끝       : "미션을 완료했습니다. : 어비스 보스 처치(지옥)" (같은 초에 보상 줄 또는 크라운 시즌권 줄이 오기도 하므로 셋 다 끝으로 본다)
    ///
    /// 신조의 둥지 (2026-09-13 로그 7판 기준):
    ///   1보스    : "던전 초기화가 완료 되었습니다." ~ "이번 주 유마 프레키 보상을"
    ///   2보스    : "던전 초기화가 완료 되었습니다." ~ "이번 주 유마 올름 보상을"
    ///   3보스    : "던전 초기화가 완료 되었습니다." ~ "이번 주 신조 보상을" — 일일/주간 문구
    ///
    /// 최후의 결전 (2026-09-16 로그 기준, 단계 3개):
    ///   1단계  : "가짜 달여왕 군단의 지도 조각 10장을 테스모폴로스에게 건네주었습니다." ~ "다음 구역으로 이동할 수 있는 포탈이 생성되었습니다."
    ///   2단계  : 1단계 끝 ~ "차원의 문이 소환되고 있는 장소로 이동할 수 있는 포탈이 생성되었습니다."
    ///   3단계  : 2단계 끝 ~ "티로로스의 계략을 막아내었습니다. 잠시 후 기억의 숲 전초기지로 이동됩니다." — 일일/주간 문구
    ///
    /// 환희의 잔상 (2026-09-11·13·14 로그 기준, 단계 2개):
    ///   1단계  : "★ 1단계 미션 목표 : 제한 시간 내에 에너지 노드를 연결하여…" ~ 2단계 안내
    ///   2단계  : "★ 2단계 미션 목표 : 불안정한 마력의 근원 '환희의 레이티아'…" ~ "[환희의 레이티아 보상 상자] 아이템을 1개 획득하였습니다." — 일일/주간 문구
    ///
    /// 이클립스 보스 6종 (2026-09-11 로그 기준, 보스별 별개 판):
    ///   시작     : 보스 등장 대사 (로카고스 "그대들은 누구요?", 에토스 "너희들이 그 첩보 대상들이구나?", 체리아 "너희들은 뭐야?",
    ///              마티아 "또 다른 제물들이군요?", 티로로스 "이런, 이런…… 귀한 곳에", 라이코스 "'이를 드러내고 으르렁댄다'")
    ///   끝(변형) : "이클립스 보스 토벌전 클리어 횟수: [N/21]" → 토벌전 기록 / "이클립스 보스전(로카고스) 클리어 횟수:" → 보스전 기록
    ///              티로로스는 보스전에만 있다.
    /// </summary>
    public sealed class ContentTimerService
    {
        public const int ColumnCount = 3;
        public const int CurrentColumn = 2;

        /// <summary>
        /// 구간 하나. Start가 없으면 앞 구간이 끝날 때(첫 구간이면 판이 열릴 때) 시작하고, End가 없으면 다음 구간이 시작할 때(마지막이면 판이 끝날 때) 끝난다.
        /// StartDelaySeconds는 시작 지연 — 앞 구간 끝 대사 뒤 실제 국면이 시작될 때까지의 연출 시간만큼 늦춘다.
        /// </summary>
        public sealed record SegmentDefinition(
            string Name,
            Regex? Start,
            Regex? End,
            // 음수면 시작 문구가 실제 시작보다 늦게 찍히는 구간 (렐릭 키시니크: 대사가 전투 15초 뒤)
            double StartDelaySeconds = 0,
            // 이미 시작한 구간의 시작 문구가 또 왔을 때: 판을 새로 열지, 이 구간만 다시 재는지, 무시하는지
            SegmentStartRepeat Repeat = SegmentStartRepeat.RestartRun,
            // false면 이 구간의 시작 문구는 진행 중인 판 안에서만 뜻이 있다 (다른 던전에도 같은 대사가 있을 때)
            bool OpensRun = true,
            // true면 이 구간이 없어도 판을 기록한다 (렐릭 석상처럼 건너뛸 수 있는 구간). 시작만 하고 못 끝낸 경우도 빼고 기록한다.
            bool Optional = false,
            // true면 판 전체를 잰다 — 판이 열릴 때 시작하고 판이 끝날 때 끝나며, 다른 구간이 시작해도 끊기지 않는다 (렐릭 '전체')
            bool WholeRun = false);

        public enum SegmentStartRepeat
        {
            /// <summary>진행 중이던 판을 버리고 새로 센다 (아페티리아 1보스 대사가 다시 나옴).</summary>
            RestartRun,
            /// <summary>이 구간만 새 시각부터 다시 잰다 (렐릭 석상 미션 실패 후 재도전).</summary>
            RestartSegment,
            /// <summary>같은 판에서 반복되는 대사이므로 무시한다.</summary>
            Ignore,
        }

        /// <summary>
        /// 같은 시작 대사에서 열린 판이 끝 문구에 따라 어느 컨텐츠 기록이 되는지. Key/GroupKey/GroupName/진행도 항목은 변형마다 다르다.
        /// </summary>
        public sealed record RunVariant(
            string Key,
            string GroupKey,
            string GroupName,
            Regex End,
            string? ProgressItemName,
            bool ProgressCountedAtStart = false,
            // 묶음 표의 행 이름 (없으면 던전 이름 그대로)
            string? Name = null);

        /// <summary>
        /// 던전 하나의 정의.
        /// GroupKey/GroupName: 같은 묶음끼리 한 창에 행으로 나란히 보여주고, 판도 묶음 단위로 하나씩 추적한다. 묶음이 던전 하나면 구간 모드.
        /// RunStart: 판 시작 문구. 이름 그룹 diff가 있으면 난이도로 읽는다. 없으면 첫 구간 시작 대사에서 판이 열린다.
        /// RunEnd: 판 끝 문구 (Variants가 있으면 쓰지 않는다).
        /// HardMarker: 판 도중 보이면 난이도 HardLabel, 끝까지 없으면 NormalLabel (아페티리아처럼 구분 안 하면 null·빈 문자열).
        /// ProgressItemName: 일일/주간 컨텐츠 창에서 진행도(N/7)를 읽어 올 항목 이름 (일일/주간 창의 상수를 그대로 쓴다).
        /// ProgressCountedAtStart: 일일/주간 창이 그 항목을 입장 때 세면 true (이번 판 진행도 = 횟수), 클리어 때 세면 false (횟수 + 1).
        /// Variants: 끝 문구로 컨텐츠가 갈리는 경우의 변형 목록. 있으면 기록·표시는 변형 단위로 한다.
        /// StartOffsetSeconds: 판 시작 문구가 실제 입장보다 늦게 찍히는 던전은 음수로 그만큼 당긴다 (추종하는 환희: 대사가 입장 30초 뒤).
        /// Activity / RestartAfterQuietSeconds: 판 시작 문구가 판 도중에도 반복되는 던전용. Activity 줄이 RestartAfterQuietSeconds 안에
        /// 있었으면 반복으로 보고 무시하고, 그보다 오래 조용했으면(죽고 다시 들어옴) 새 판으로 센다.
        /// </summary>
        public sealed record DungeonDefinition(
            string Key,
            string Name,
            string GroupKey,
            string GroupName,
            Regex? RunStart,
            Regex RunEnd,
            Regex? HardMarker,
            string HardLabel,
            string NormalLabel,
            string? ProgressItemName,
            SegmentDefinition[] Segments,
            bool ProgressCountedAtStart = false,
            RunVariant[]? Variants = null,
            // 구간 모드에서 합계 행을 보여줄지 (아페티리아처럼 구간 합이 의미 없으면 끈다)
            bool ShowTotal = true,
            double StartOffsetSeconds = 0,
            Regex? Activity = null,
            double RestartAfterQuietSeconds = 0,
            // true면 이 던전의 판이 열릴 때 다른 던전의 진행 중인 판을 버린다. 시작 문구가 그 던전에서만 찍히는 시스템 줄일 때만 켠다
            // (아페티리아 안에서 라이코스 대사가 찍히듯, 대사로 시작하는 던전은 남의 판을 건드리면 안 된다). 판 끝(클리어 줄)은 항상 남의 판을 버린다.
            bool StartCancelsOthers = false);

        /// <summary>일일/주간 창의 문구 상수(부분 문자열 매칭)를 그대로 정규식으로 쓴다.</summary>
        private static Regex Keyword(string keyword) => new(Regex.Escape(keyword), RegexOptions.Compiled);

        // 어비스 판 끝: 클리어 순간 "미션을 완료했습니다. : 어비스 보스 처치(지옥)"가 항상 찍힌다.
        // 같은 초에 보상 줄(주간 횟수 소진 전)이나 크라운 시즌권 줄(소진 후)이 오기도 하므로 셋 다 끝으로 본다.
        private static readonly Regex AbyssRunEnd = new(
            @"미션을\s*완료했습니다\.?\s*:\s*어비스\s*보스\s*처치|어비스\s*던전\s*보상을\s*\d+\s*회\s*획득|크라운\s*시즌권이\s*사용되었습니다",
            RegexOptions.Compiled);

        /// <summary>
        /// 어비스 심층 하나. 일일/주간 창은 입장 줄("플레이를 이번 주에 7회 중 N회째")로 횟수를 세므로 진행도는 입장 때 이미 이번 판을 포함한다.
        /// 시작은 같은 초에 찍히는 "…에 지옥 난이도로 입장하셨습니다." 줄에서 난이도까지 읽는다.
        /// </summary>
        private static DungeonDefinition AbyssDepth(int depth, string roman, string dailyWeeklyItemName) => new(
            Key: $"abyss{depth}",
            Name: $"심층{depth}",
            GroupKey: "abyss",
            GroupName: "어비스 - 심층",
            RunStart: new Regex($@"어비스\s*-\s*심층{roman}\s*\(보스전\)\s*에\s*(?<diff>\S+)\s*난이도로\s*입장", RegexOptions.Compiled),
            RunEnd: AbyssRunEnd,
            HardMarker: null,
            HardLabel: string.Empty,
            NormalLabel: string.Empty,
            ProgressItemName: dailyWeeklyItemName,
            Segments: new[] { new SegmentDefinition($"심층{depth}", null, null) },
            ProgressCountedAtStart: true,
            StartCancelsOthers: true);

        // 신조의 둥지: 보스 방 세 곳이 모두 이 문구로 시작한다 (Definitions보다 먼저 초기화돼야 한다)
        private static readonly Regex ShinjoRoomStart = new(@"던전\s*초기화가\s*완료\s*되었습니다", RegexOptions.Compiled);

        /// <summary>
        /// 이클립스 보스 하나. 시작은 보스 등장 대사. 토벌전과 보스전이 같은 대사를 쓰므로 끝 문구로 갈린다:
        /// 토벌전 클리어 줄이면 토벌전 기록(묶음 N/21), 보스별 보스전 클리어 줄이면 보스전 기록(보스별 N/7).
        /// </summary>
        private static DungeonDefinition EclipseBoss(string key, string bossItemName, string bossRaidKeyword, string startPattern,
            string coreItemName, string coreKeyword, bool inSubjugation = true)
        {
            var variants = new List<RunVariant>();
            if (inSubjugation)
                variants.Add(new RunVariant($"eclipse_subj_{key}", "eclipse_subjugation", DailyWeeklyContentWindow.EclipseSubjugationItemName,
                    Keyword(DailyWeeklyContentWindow.EclipseSubjugationLogKeyword), DailyWeeklyContentWindow.EclipseSubjugationItemName));
            variants.Add(new RunVariant($"eclipse_raid_{key}", "eclipse_raid", DailyWeeklyContentWindow.EclipseBossRaidItemName,
                Keyword(bossRaidKeyword), bossItemName));
            // 코어 마스터도 같은 등장 대사로 시작하고 "N 코어 마스터 클리어 횟수" 줄로 끝난다 — "이클립스 코어" 창에 보스별 행으로 보여준다
            variants.Add(new RunVariant($"eclipse_core_{key}", "eclipse_core", "이클립스 코어",
                Keyword(coreKeyword), coreItemName));

            return new(
                Key: $"eclipse_{key}",
                Name: bossItemName,
                GroupKey: "eclipse",
                GroupName: "이클립스",
                RunStart: new Regex(startPattern, RegexOptions.Compiled),
                RunEnd: Keyword(DailyWeeklyContentWindow.EclipseSubjugationLogKeyword),
                HardMarker: null,
                HardLabel: string.Empty,
                NormalLabel: string.Empty,
                ProgressItemName: null,
                Segments: new[] { new SegmentDefinition(bossItemName, null, null) },
                Variants: variants.ToArray());
        }

        /// <summary>추적 단위 정의. 변형이 있는 던전은 하나로 추적하고 끝 문구에서 갈린다.</summary>
        public static readonly DungeonDefinition[] Definitions =
        {
            new(
                Key: "apetiria",
                Name: DailyWeeklyContentWindow.ApetiriaItemName,
                GroupKey: "apetiria",
                GroupName: DailyWeeklyContentWindow.ApetiriaItemName,
                // 따로 판 시작 문구를 보지 않는다 — 1보스 시작 대사에서 판이 열린다
                RunStart: null,
                // 판 끝 = 일일/주간 창이 아페티리아 횟수를 세는 문구 ("아페티리아 클리어 횟수:")
                RunEnd: Keyword(DailyWeeklyContentWindow.ApetiriaLogKeyword),
                // 아페티리아는 일반/어려움을 구분하지 않는다 — 기록도 하나로 합친다
                HardMarker: null,
                HardLabel: string.Empty,
                NormalLabel: string.Empty,
                ProgressItemName: DailyWeeklyContentWindow.ApetiriaItemName,
                ShowTotal: false,
                // 아페티리아 EX는 대사·구간이 같고 클리어 문구만 "아페티리아(EX) 클리어 횟수:"라서 끝 문구로 갈린다
                Variants: new[]
                {
                    new RunVariant("apetiria", "apetiria", DailyWeeklyContentWindow.ApetiriaItemName,
                        Keyword(DailyWeeklyContentWindow.ApetiriaLogKeyword), DailyWeeklyContentWindow.ApetiriaItemName),
                    new RunVariant("apetiria_ex", "apetiria_ex", DailyWeeklyContentWindow.ApetiriaExItemName,
                        Keyword(DailyWeeklyContentWindow.ApetiriaExLogKeyword), DailyWeeklyContentWindow.ApetiriaExItemName),
                },
                Segments: new[]
                {
                    new SegmentDefinition("1보스",
                        new Regex(@"셀리니아코스\s*:\s*집행을\s*시작하겠소", RegexOptions.Compiled),
                        new Regex(@"셀리니아코스\s*:\s*패배를\s*인정하오", RegexOptions.Compiled)),
                    // 석상은 1보스 패배 대사 뒤 연출 2초가 지나고 시작하고, 포탈 도달 안내(시스템 줄)로 끝난다
                    new SegmentDefinition("석상",
                        null,
                        new Regex(@"포탈에\s*도달한\s*혼령이\s*어떤\s*일을\s*불러올지는", RegexOptions.Compiled),
                        StartDelaySeconds: 2),
                    new SegmentDefinition("2보스",
                        new Regex(@"고이티아\s*:\s*시작해볼까", RegexOptions.Compiled),
                        new Regex(@"고이티아\s*:\s*감히", RegexOptions.Compiled)),
                    // 3보스는 2보스 끝 대사 뒤 연출 2초가 지나고 시작한다 (따로 시작 대사를 보지 않는다)
                    new SegmentDefinition("3보스",
                        null,
                        new Regex(@"키시니크\s*:\s*이\s*굴레는\s*반복될\s*것이다", RegexOptions.Compiled),
                        StartDelaySeconds: 2),
                }),
            AbyssDepth(1, "Ⅰ", DailyWeeklyContentWindow.AbyssDepthOneItemName),
            AbyssDepth(2, "Ⅱ", DailyWeeklyContentWindow.AbyssDepthTwoItemName),
            AbyssDepth(3, "Ⅲ", DailyWeeklyContentWindow.AbyssDepthThreeItemName),
            new(
                Key: "shinjo",
                Name: "신조의 둥지",
                GroupKey: "shinjo",
                GroupName: "신조의 둥지",
                // 따로 판 시작 문구가 없다 — 첫 보스 방의 초기화 완료에서 판이 열린다
                RunStart: null,
                // 판 끝 = 일일/주간 창이 신조 횟수를 세는 문구 ("이번 주 신조 보상을") = 3보스 끝
                RunEnd: Keyword(DailyWeeklyContentWindow.NestOfShinjoLogKeyword),
                HardMarker: null,
                HardLabel: string.Empty,
                NormalLabel: string.Empty,
                ProgressItemName: DailyWeeklyContentWindow.NestOfShinjoHardItemName,
                // 세 보스 방 모두 "던전 초기화가 완료 되었습니다."로 시작한다 — 추적기가 아직 시작 안 한 다음 구간에 붙인다
                Segments: new[]
                {
                    new SegmentDefinition("1보스", ShinjoRoomStart, new Regex(@"유마\s*프레키\s*보상을", RegexOptions.Compiled)),
                    new SegmentDefinition("2보스", ShinjoRoomStart, new Regex(@"유마\s*올름\s*보상을", RegexOptions.Compiled)),
                    new SegmentDefinition("3보스", ShinjoRoomStart, Keyword(DailyWeeklyContentWindow.NestOfShinjoLogKeyword)),
                    // 전체: 첫 방 초기화 ~ 신조 보상 줄 (방 사이 이동 포함). 합계 행 대신 보여준다.
                    new SegmentDefinition("전체", null, null, WholeRun: true),
                },
                ShowTotal: false),
            new(
                Key: "final_battle",
                Name: DailyWeeklyContentWindow.FinalBattleItemName,
                GroupKey: "final_battle",
                GroupName: DailyWeeklyContentWindow.FinalBattleItemName,
                // 입장 = 1단계 시작 대사에서 판이 열린다
                RunStart: null,
                // 판 끝 = 일일/주간 창이 최후의 결전 횟수를 세는 문구 (3단계 끝)
                RunEnd: Keyword(DailyWeeklyContentWindow.FinalBattleLogKeyword),
                HardMarker: null,
                HardLabel: string.Empty,
                NormalLabel: string.Empty,
                ProgressItemName: DailyWeeklyContentWindow.FinalBattleItemName,
                StartCancelsOthers: true,
                Segments: new[]
                {
                    new SegmentDefinition("1단계",
                        new Regex(@"지도\s*조각\s*10장을\s*테스모폴로스에게", RegexOptions.Compiled),
                        new Regex(@"다음\s*구역으로\s*이동할\s*수\s*있는\s*포탈이\s*생성", RegexOptions.Compiled)),
                    // 2·3단계는 앞 단계의 포탈 생성 문구에서 바로 이어진다
                    new SegmentDefinition("2단계",
                        null,
                        new Regex(@"차원의\s*문이\s*소환되고\s*있는\s*장소로\s*이동할\s*수\s*있는\s*포탈", RegexOptions.Compiled)),
                    new SegmentDefinition("3단계",
                        null,
                        Keyword(DailyWeeklyContentWindow.FinalBattleLogKeyword)),
                    // 전체: 1단계 시작 ~ 클리어 줄. 합계 행 대신 보여준다.
                    new SegmentDefinition("전체", null, null, WholeRun: true),
                },
                ShowTotal: false),
            new(
                Key: "afterimage_joy",
                Name: DailyWeeklyContentWindow.AfterimageOfJoyItemName,
                GroupKey: "afterimage_joy",
                GroupName: DailyWeeklyContentWindow.AfterimageOfJoyItemName,
                // 1단계 미션 안내에서 판이 열린다
                RunStart: null,
                // 판 끝 = 일일/주간 창이 환희의 잔상 완료를 세는 문구 (보상 상자 획득) = 2단계 끝
                RunEnd: Keyword(DailyWeeklyContentWindow.AfterimageOfJoyLogKeyword),
                HardMarker: null,
                HardLabel: string.Empty,
                NormalLabel: string.Empty,
                ProgressItemName: DailyWeeklyContentWindow.AfterimageOfJoyItemName,
                StartCancelsOthers: true,
                Segments: new[]
                {
                    // 1단계는 2단계 안내가 오는 순간 끝난다
                    new SegmentDefinition("1단계",
                        new Regex(@"1단계\s*미션\s*목표\s*:\s*제한\s*시간\s*내에\s*에너지\s*노드", RegexOptions.Compiled),
                        null),
                    new SegmentDefinition("2단계",
                        new Regex(@"2단계\s*미션\s*목표\s*:\s*불안정한\s*마력의\s*근원", RegexOptions.Compiled),
                        Keyword(DailyWeeklyContentWindow.AfterimageOfJoyLogKeyword)),
                    // 전체: 1단계 안내 ~ 보상 상자 줄. 합계 행 대신 보여준다.
                    new SegmentDefinition("전체", null, null, WholeRun: true),
                },
                ShowTotal: false),
            new(
                Key: "following_joy",
                Name: "추종하는 환희",
                GroupKey: "joy_sorrow",
                GroupName: "추종하는 환희",
                // 입장 줄이 없다. 레이티아의 '아름다운 꽃은…' 대사가 입장 30초 뒤에 오므로 그 시각에서 30초를 당겨 시작으로 본다.
                // 환희의 잔상의 '환희의 레이티아' 대사는 제외한다.
                RunStart: new Regex(@"(?<!환희의\s*)레이티아\s*:\s*아름다운\s*꽃은", RegexOptions.Compiled),
                StartOffsetSeconds: -30,
                // 같은 대사가 판 도중에도 다시 나온다 — 레이티아 대사가 45초 안에 있었으면 같은 판, 그보다 조용했으면 새 판
                Activity: new Regex(@"(?<!환희의\s*)레이티아\s*:", RegexOptions.Compiled),
                RestartAfterQuietSeconds: 45,
                RunEnd: Keyword(DailyWeeklyContentWindow.FollowingJoyNormalLogKeyword),
                HardMarker: null,
                HardLabel: string.Empty,
                NormalLabel: string.Empty,
                ProgressItemName: null,
                // 판 끝 = 일일/주간 창이 세는 보상 줄. 일반/어려움이 줄로 갈리므로 한 창에 행 둘로 보여준다.
                Variants: new[]
                {
                    // 행 이름은 짧게 "환희(일반)" — 진행도는 일일/주간 창의 항목에서 읽고, 그 창에서 꺼 둔 항목의 행은 숨긴다
                    new RunVariant("following_joy_normal", "joy_sorrow", "환희 · 슬픔",
                        Keyword(DailyWeeklyContentWindow.FollowingJoyNormalLogKeyword), DailyWeeklyContentWindow.FollowingJoyNormalItemName,
                        Name: "환희(일반)"),
                    new RunVariant("following_joy_hard", "joy_sorrow", "환희 · 슬픔",
                        Keyword(DailyWeeklyContentWindow.FollowingJoyHardLogKeyword), DailyWeeklyContentWindow.FollowingJoyHardItemName,
                        Name: "환희(어려움)"),
                },
                Segments: new[] { new SegmentDefinition("레이티아", null, null) }),
            new(
                Key: "gazing_sorrow",
                Name: "응시하는 슬픔",
                GroupKey: "joy_sorrow",
                GroupName: "응시하는 슬픔",
                // 입장 줄이 없다. 설계자의 '광란의 불꽃 안에서…' 대사가 입장 15초 뒤에 오므로 그 시각에서 15초를 당겨 시작으로 본다.
                RunStart: new Regex(@"설계자\s*:\s*광란의\s*불꽃\s*안에서", RegexOptions.Compiled),
                StartOffsetSeconds: -15,
                // 같은 대사가 판 도중 15~20초마다 다시 나온다 — 설계자 대사가 45초 안에 있었으면 같은 판
                Activity: new Regex(@"설계자\s*:", RegexOptions.Compiled),
                RestartAfterQuietSeconds: 45,
                RunEnd: Keyword(DailyWeeklyContentWindow.GazingSorrowNormalLogKeyword),
                HardMarker: null,
                HardLabel: string.Empty,
                NormalLabel: string.Empty,
                ProgressItemName: null,
                Variants: new[]
                {
                    new RunVariant("gazing_sorrow_normal", "joy_sorrow", "환희 · 슬픔",
                        Keyword(DailyWeeklyContentWindow.GazingSorrowNormalLogKeyword), DailyWeeklyContentWindow.GazingSorrowNormalItemName,
                        Name: "슬픔(일반)"),
                    new RunVariant("gazing_sorrow_hard", "joy_sorrow", "환희 · 슬픔",
                        Keyword(DailyWeeklyContentWindow.GazingSorrowHardLogKeyword), DailyWeeklyContentWindow.GazingSorrowHardItemName,
                        Name: "슬픔(어려움)"),
                },
                Segments: new[] { new SegmentDefinition("설계자", null, null) }),
            new(
                Key: "relic",
                Name: "고대 렐릭의 성소",
                GroupKey: "relic",
                GroupName: "고대 렐릭의 성소",
                // 입장 안내에서 판이 열린다 (석상 순서는 판마다 다를 수 있다)
                RunStart: new Regex(@"플레이\s*목표\s*:\s*제한\s*시간\s*내\s*보스와의\s*전투에서\s*승리", RegexOptions.Compiled),
                // 보스전에서 죽고 다시 들어오면 입장 안내가 또 찍힌다 — 렐릭 줄(보스 대사 포함)이 3분 안에 있었으면 같은 판으로 이어 잰다
                Activity: new Regex(@"플레이\s*목표\s*:|미션\s*목표\s*:|효과를\s*획득했습니다|키시니크\s*:", RegexOptions.Compiled),
                RestartAfterQuietSeconds: 180,
                StartCancelsOthers: true,
                // 판 끝 = 일일/주간 창이 렐릭 횟수를 세는 문구 ("고대 렐릭의 성소 - 주간 무료 클리어 횟수")
                RunEnd: Keyword(DailyWeeklyContentWindow.RelicLogKeyword),
                HardMarker: null,
                HardLabel: string.Empty,
                NormalLabel: string.Empty,
                ProgressItemName: DailyWeeklyContentWindow.RelicItemName,
                Segments: new[]
                {
                    // 석상 넷: 미션 안내 ~ 효과 획득. 건너뛸 수 있으므로 선택 구간이고, 미션에 실패해 안내가 다시 오면 그 석상만 새로 잰다.
                    new SegmentDefinition("갑옷의 석상",
                        new Regex(@"미션\s*목표\s*:\s*무리에서\s*행동이\s*다른\s*하나의\s*몬스터", RegexOptions.Compiled),
                        new Regex(@"수호의\s*가호.*효과를\s*획득", RegexOptions.Compiled),
                        Repeat: SegmentStartRepeat.RestartSegment, Optional: true),
                    new SegmentDefinition("보호의 석상",
                        new Regex(@"미션\s*목표\s*:\s*임의로\s*정해지는\s*1\s*~\s*50", RegexOptions.Compiled),
                        new Regex(@"활력의\s*기운.*효과를\s*획득", RegexOptions.Compiled),
                        Repeat: SegmentStartRepeat.RestartSegment, Optional: true),
                    new SegmentDefinition("생명의 석상",
                        new Regex(@"미션\s*목표\s*:\s*제한\s*시간\s*내에\s*모든\s*몬스터\s*처치", RegexOptions.Compiled),
                        new Regex(@"생명의\s*숨결.*효과를\s*획득", RegexOptions.Compiled),
                        Repeat: SegmentStartRepeat.RestartSegment, Optional: true),
                    new SegmentDefinition("검의 석상",
                        new Regex(@"몬스터의\s*정해진\s*숫자를\s*미션\s*숫자에서\s*차감", RegexOptions.Compiled),
                        new Regex(@"검의\s*은총.*효과를\s*획득", RegexOptions.Compiled),
                        Repeat: SegmentStartRepeat.RestartSegment, Optional: true),
                    // 전체: 입장 안내 ~ 주간 클리어 횟수 줄 (보스가 키시니크든 신조든 같다)
                    new SegmentDefinition("전체", null, null, WholeRun: true),
                },
                // 전체 행이 있으므로 구간 합계는 따로 보여주지 않는다
                ShowTotal: false),
            // 이클립스 보스 6종 (게임에서 도는 순서대로)
            EclipseBoss("lokagos", DailyWeeklyContentWindow.LokagosItemName, DailyWeeklyContentWindow.LokagosLogKeyword, @"로카고스\s*:\s*그대들은\s*누구요", DailyWeeklyContentWindow.LokagosCoreMasterItemName, DailyWeeklyContentWindow.LokagosCoreMasterLogKeyword),
            EclipseBoss("ethos", DailyWeeklyContentWindow.EthosItemName, DailyWeeklyContentWindow.EthosLogKeyword, @"에토스\s*:\s*너희들이\s*그\s*첩보", DailyWeeklyContentWindow.EthosCoreMasterItemName, DailyWeeklyContentWindow.EthosCoreMasterLogKeyword),
            EclipseBoss("cheria", DailyWeeklyContentWindow.CheriaItemName, DailyWeeklyContentWindow.CheriaLogKeyword, @"체리아\s*:\s*너희들은\s*뭐야", DailyWeeklyContentWindow.CheriaCoreMasterItemName, DailyWeeklyContentWindow.CheriaCoreMasterLogKeyword),
            EclipseBoss("matia", DailyWeeklyContentWindow.MatiaItemName, DailyWeeklyContentWindow.MatiaLogKeyword, @"마티아\s*:\s*또\s*다른\s*제물", DailyWeeklyContentWindow.MatiaCoreMasterItemName, DailyWeeklyContentWindow.MatiaCoreMasterLogKeyword),
            // 티로로스는 보스전에만 있고 토벌전에는 없다
            EclipseBoss("tyroros", DailyWeeklyContentWindow.TyrorosItemName, DailyWeeklyContentWindow.TyrorosLogKeyword, @"티로로스\s*:\s*이런,?\s*이런", DailyWeeklyContentWindow.TyrorosCoreMasterItemName, DailyWeeklyContentWindow.TyrorosCoreMasterLogKeyword, inSubjugation: false),
            EclipseBoss("lycos", DailyWeeklyContentWindow.LycosItemName, DailyWeeklyContentWindow.LycosLogKeyword, @"흉포한\s*라이코스\s*:\s*'?이를\s*드러내고", DailyWeeklyContentWindow.LycosCoreMasterItemName, DailyWeeklyContentWindow.LycosCoreMasterLogKeyword),
        };

        /// <summary>기록·표시 단위 정의. 변형이 있는 던전은 변형마다 하나씩 펼친다 (Definitions 다음에 초기화돼야 한다).</summary>
        public static readonly DungeonDefinition[] RecordDefinitions = Definitions
            .SelectMany(d => d.Variants is { Length: > 0 }
                ? d.Variants.Select(v => d with
                {
                    Key = v.Key,
                    Name = v.Name ?? d.Name,
                    GroupKey = v.GroupKey,
                    GroupName = v.GroupName,
                    RunEnd = v.End,
                    ProgressItemName = v.ProgressItemName,
                    ProgressCountedAtStart = v.ProgressCountedAtStart,
                    Variants = null,
                })
                : new[] { d })
            .ToArray();

        private DungeonDefinition? RecordDefinitionByKey(string key)
            => RecordDefinitions.FirstOrDefault(d => d.Key == key);

        /// <summary>추적 정의가 낳을 수 있는 기록 정의들 (변형이 없으면 자기 자신).</summary>
        private IEnumerable<DungeonDefinition> RecordDefinitionsOf(DungeonDefinition tracking)
            => tracking.Variants is { Length: > 0 }
                ? tracking.Variants.Select(v => RecordDefinitionByKey(v.Key)!)
                : new[] { RecordDefinitionByKey(tracking.Key) ?? tracking };

        /// <summary>난이도를 어디선가 읽는 던전인지 (판 시작 문구의 diff 그룹 또는 어려움 상자).</summary>
        private bool HasDifficulty(DungeonDefinition def)
            => def.HardMarker != null || (def.RunStart != null && def.RunStart.GetGroupNames().Contains("diff"));

        /// <summary>진행 중인 판의 구간 상태.</summary>
        public sealed class LiveRun
        {
            public DungeonDefinition Definition { get; }
            public DateTime StartedAt { get; }
            /// <summary>확정된 난이도(시작 문구나 어려움 상자에서). 확정 전엔 null.</summary>
            public string? Difficulty { get; set; }
            /// <summary>판 시작 때 읽어 둔 진행도 (기록 정의 키 → 일일/주간 창의 (횟수, 최대)). 클리어 후 표시에 쓴다.</summary>
            public Dictionary<string, (int Current, int Max)> ProgressAtStart { get; } = new(StringComparer.Ordinal);
            public DateTime?[] SegmentStart { get; }
            public DateTime?[] SegmentEnd { get; }
            /// <summary>이 판의 Activity 줄(또는 시작 문구)이 마지막으로 찍힌 로그 시각. 반복되는 시작 문구를 새 판과 구분하는 데 쓴다.</summary>
            public DateTime LastActivityAt { get; set; }

            public LiveRun(DungeonDefinition definition, DateTime startedAt)
            {
                Definition = definition;
                StartedAt = startedAt;
                LastActivityAt = startedAt;
                SegmentStart = new DateTime?[definition.Segments.Length];
                SegmentEnd = new DateTime?[definition.Segments.Length];
            }
        }

        /// <summary>
        /// 로그 줄을 순서대로 넣으면 판·구간 상태를 갱신하는 순수 추적기. UI나 파일을 모르므로
        /// 실시간 로그와 과거 로그 파일 읽기에서 같은 판정을 쓴다. 스레드 안전하지 않다 — 호출자가 잠근다.
        /// 판은 묶음(GroupKey)마다 하나씩 따로 추적한다. 시작 문구와 끝 문구가 모두 잡힌 판만 기록이 되고,
        /// 끝 문구가 오지 않은 판은 같은 묶음의 다음 시작 문구에서 조용히 버려진다.
        /// </summary>
        public sealed class RunTracker
        {
            // 판 시작 문구가 같은 초에 여러 줄 찍히므로 이 시간 안의 재시작은 같은 판으로 본다
            private static readonly TimeSpan StartDebounce = TimeSpan.FromSeconds(5);
            // 너무 오래된 미완성 판이 뒤늦은 끝 문구로 기록되지 않게 하는 한도 (가장 긴 판인 아페티리아 ~5분보다 넉넉히)
            public static readonly TimeSpan RunSafetyLimit = TimeSpan.FromMinutes(30);

            private readonly DungeonDefinition[] _definitions;
            // 묶음 키 → 진행 중인 판
            private readonly Dictionary<string, LiveRun> _runs = new(StringComparer.Ordinal);

            /// <summary>새 판이 열렸다.</summary>
            public event Action<LiveRun>? RunStarted;
            /// <summary>판이 끝났다. 기록의 DungeonKey는 기록 정의(변형) 키다.</summary>
            public event Action<LiveRun, DungeonRunRecord>? RunFinished;
            /// <summary>판은 끝났지만 구간 하나라도 완성되지 않아 기록하지 않는다. 두 번째 인자는 빠진 구간 이름 또는 사유.</summary>
            public event Action<LiveRun, string>? RunDiscarded;
            /// <summary>진행 중인 판의 구간 하나가 끝났다 (구간 인덱스). 판 끝과 같은 줄에서 함께 올 수 있다.</summary>
            public event Action<LiveRun, int>? SegmentFinished;

            public RunTracker(DungeonDefinition[] definitions)
            {
                _definitions = definitions;
            }

            /// <summary>묶음의 진행 중인 판. 안전 한도를 넘었으면 null.</summary>
            public LiveRun? GetCurrent(string groupKey, DateTime now)
                => _runs.TryGetValue(groupKey, out LiveRun? run) && now - run.StartedAt < RunSafetyLimit ? run : null;

            /// <summary>진행 중인 판이 하나라도 있으면 true.</summary>
            public bool HasActiveRun(DateTime now)
                => _runs.Values.Any(run => now - run.StartedAt < RunSafetyLimit);

            /// <summary>진행 중이던 판을 모두 기록 없이 버린다 (사용자가 타이머 창을 닫음 등). 버린 게 있으면 true.</summary>
            /// <summary>한 묶음을 뺀 나머지 묶음의 진행 중인 판을 기록 없이 버린다 (다른 던전의 시작·클리어 줄이 왔을 때).</summary>
            private void CancelOthers(string keepGroupKey, string reason)
            {
                var cancelled = _runs.Where(p => p.Key != keepGroupKey).Select(p => p.Value).ToList();
                foreach (LiveRun run in cancelled)
                {
                    _runs.Remove(run.Definition.GroupKey);
                    RunDiscarded?.Invoke(run, reason);
                }
            }

            public bool CancelAll(string reason)
            {
                if (_runs.Count == 0)
                    return false;
                var cancelled = _runs.Values.ToList();
                _runs.Clear();
                foreach (LiveRun run in cancelled)
                    RunDiscarded?.Invoke(run, reason);
                return true;
            }

            /// <summary>로그 줄 하나를 넣는다. at은 로그에 찍힌 시각, now는 안전 한도 판단용 현재 시각.</summary>
            public void Feed(string text, DateTime at, DateTime now)
            {
                if (string.IsNullOrWhiteSpace(text))
                    return;

                // 판 시작 문구 (있는 던전만). 같은 묶음의 진행 중이던 판은 버리고 새로 센다.
                foreach (DungeonDefinition def in _definitions)
                {
                    if (def.RunStart == null)
                        continue;
                    Match m = def.RunStart.Match(text);
                    if (!m.Success)
                        continue;

                    LiveRun? existing = GetCurrent(def.GroupKey, now);
                    if (existing != null && ReferenceEquals(existing.Definition, def))
                    {
                        if (at - existing.StartedAt < StartDebounce)
                            return; // 같은 판의 나머지 안내 줄
                        // 판 도중 반복되는 시작 문구 — 최근까지 그 던전의 줄이 이어졌으면 같은 판이다.
                        // 보스 대사가 반복되는 경우(추종하는 환희)는 그냥 무시하고, 사망 후 재입장 안내(렐릭)는 끝낸 구간은 두고
                        // 진행 중이던 구간(자기 시작 문구가 있는 것)만 비워 재입장 뒤 다시 재게 한다.
                        if (def.Activity != null && at - existing.LastActivityAt <= TimeSpan.FromSeconds(def.RestartAfterQuietSeconds))
                        {
                            existing.LastActivityAt = at;
                            for (int i = 0; i < def.Segments.Length; i++)
                            {
                                if (def.Segments[i].Start != null && existing.SegmentStart[i].HasValue && !existing.SegmentEnd[i].HasValue)
                                    existing.SegmentStart[i] = null;
                            }
                            return;
                        }
                    }
                    DateTime startedAt = at.AddSeconds(def.StartOffsetSeconds);
                    var run = new LiveRun(def, startedAt) { LastActivityAt = at };
                    _runs[def.GroupKey] = run;
                    if (def.StartCancelsOthers)
                        CancelOthers(def.GroupKey, "다른 던전 시작");
                    Group diff = m.Groups["diff"];
                    if (diff.Success && !string.IsNullOrWhiteSpace(diff.Value))
                        run.Difficulty = diff.Value.Trim();
                    // 첫 구간에 시작 문구가 없으면 판이 열릴 때 시작한다. 판 전체 구간도 판이 열릴 때 시작한다.
                    for (int i = 0; i < def.Segments.Length; i++)
                    {
                        if (def.Segments[i].WholeRun || (i == 0 && def.Segments[i].Start == null))
                            MarkSegmentStart(run, i, startedAt);
                    }
                    RunStarted?.Invoke(run);
                    return;
                }

                // 구간 시작 문구 — 판 시작 문구가 없는 던전은 첫 구간에서 판을 연다.
                // 여러 구간이 같은 시작 문구를 쓰면(신조의 둥지 보스 방 초기화) 아직 시작 안 한 다음 구간에 붙인다.
                foreach (DungeonDefinition def in _definitions)
                {
                    LiveRun? existing = GetCurrent(def.GroupKey, now);
                    bool sameRun = existing != null && ReferenceEquals(existing.Definition, def);
                    for (int i = 0; i < def.Segments.Length; i++)
                    {
                        SegmentDefinition segDef = def.Segments[i];
                        Regex? start = segDef.Start;
                        if (start == null || !start.IsMatch(text))
                            continue;
                        // 판 안에서만 뜻이 있는 대사(렐릭 키시니크)가 판 밖에서 왔다 — 아페티리아 3보스의 같은 대사 등. 이 던전은 건너뛴다.
                        if (!segDef.OpensRun && !sameRun)
                            break;
                        bool restart = false;
                        if (sameRun && existing!.SegmentStart[i].HasValue)
                        {
                            // 이미 시작한 구간의 시작 문구가 또 왔다. 같은 문구를 쓰는 미시작 구간이 뒤에 있으면(신조 보스 방) 그쪽에 붙이고,
                            // 없으면 구간 정의에 따라: 판을 새로 열거나(아페티리아 1보스), 이 구간만 다시 재거나(렐릭 석상 재도전), 무시한다(반복 대사).
                            bool laterSameStartPending = false;
                            for (int j = i + 1; j < def.Segments.Length; j++)
                            {
                                if (ReferenceEquals(def.Segments[j].Start, start) && !existing.SegmentStart[j].HasValue)
                                {
                                    laterSameStartPending = true;
                                    break;
                                }
                            }
                            if (laterSameStartPending)
                                continue;
                            if (segDef.Repeat == SegmentStartRepeat.Ignore)
                                return;
                            if (segDef.Repeat == SegmentStartRepeat.RestartSegment)
                            {
                                existing.SegmentStart[i] = null;
                                existing.SegmentEnd[i] = null;
                                MarkSegmentStart(existing, i, at);
                                return;
                            }
                            restart = true;
                        }

                        LiveRun run;
                        bool opened = false;
                        if (!sameRun || restart)
                        {
                            run = new LiveRun(def, at);
                            _runs[def.GroupKey] = run;
                            opened = true;
                            if (def.StartCancelsOthers)
                                CancelOthers(def.GroupKey, "다른 던전 시작");
                            // 판 전체 구간은 판이 열리는 순간 시작한다 (구간 시작보다 먼저 표시해야 그 구간 시작에 끊기지 않는다)
                            for (int j = 0; j < def.Segments.Length; j++)
                            {
                                if (def.Segments[j].WholeRun)
                                    MarkSegmentStart(run, j, at);
                            }
                        }
                        else
                        {
                            run = existing!;
                        }
                        MarkSegmentStart(run, i, at);
                        if (opened)
                            RunStarted?.Invoke(run);
                        return;
                    }
                }

                // 진행 중인 판들의 난이도·구간 끝·판 끝 문구. 묶음마다 따로 본다.
                var activeGroups = new HashSet<string>(StringComparer.Ordinal);
                foreach (LiveRun run in _runs.Values.ToList())
                {
                    // 앞 던전의 판 끝이 이 판을 버렸을 수 있다
                    if (!_runs.TryGetValue(run.Definition.GroupKey, out LiveRun? still) || !ReferenceEquals(still, run))
                        continue;
                    if (now - run.StartedAt >= RunSafetyLimit)
                        continue;
                    activeGroups.Add(run.Definition.GroupKey);
                    DungeonDefinition active = run.Definition;

                    if (active.Activity != null && active.Activity.IsMatch(text))
                        run.LastActivityAt = at;

                    if (active.HardMarker != null && active.HardMarker.IsMatch(text))
                    {
                        run.Difficulty = active.HardLabel;
                        continue;
                    }

                    for (int i = 0; i < active.Segments.Length; i++)
                    {
                        Regex? end = active.Segments[i].End;
                        if (end == null || !end.IsMatch(text))
                            continue;
                        MarkSegmentEnd(run, i, at);
                        break; // 끝 문구는 판 끝 문구와 같은 초에 올 수 있으니 아래 판 끝 검사도 계속한다
                    }

                    // 판 끝 — 변형이 있으면 어느 변형의 끝 문구인지로 기록 대상이 정해진다
                    if (active.Variants is { Length: > 0 })
                    {
                        foreach (RunVariant variant in active.Variants)
                        {
                            if (variant.End.IsMatch(text))
                            {
                                FinishRun(run, at, variant.Key);
                                break;
                            }
                        }
                    }
                    else if (active.RunEnd.IsMatch(text))
                    {
                        FinishRun(run, at, active.Key);
                    }
                }

                // 시작 대사가 입장보다 늦게 찍히는 던전(추종하는 환희·응시하는 슬픔)은 대사가 나오기 전에 잡으면 판이 열리지 않은 채 끝 문구가 온다.
                // 그런 판은 소요 시간이 보정 초 이하라는 것만 알 수 있으므로 상한 기록("30초 이하")으로 남긴다.
                foreach (DungeonDefinition def in _definitions)
                {
                    if (def.StartOffsetSeconds >= 0 || activeGroups.Contains(def.GroupKey))
                        continue;
                    string? recordKey = null;
                    if (def.Variants is { Length: > 0 })
                        recordKey = def.Variants.FirstOrDefault(v => v.End.IsMatch(text))?.Key;
                    else if (def.RunEnd.IsMatch(text))
                        recordKey = def.Key;
                    if (recordKey == null)
                        continue;

                    var run = new LiveRun(def, at.AddSeconds(def.StartOffsetSeconds)) { LastActivityAt = at };
                    for (int i = 0; i < def.Segments.Length; i++)
                        run.SegmentStart[i] = run.StartedAt;
                    RunStarted?.Invoke(run);
                    FinishRun(run, at, recordKey, capped: true);
                }
            }

            /// <summary>구간 i 시작(시작 지연 적용). 앞 구간 중 아직 안 끝난 것은 대사 시각에 끝난 것으로 본다.</summary>
            private void MarkSegmentStart(LiveRun run, int i, DateTime at)
            {
                if (run.SegmentStart[i].HasValue)
                    return;

                run.SegmentStart[i] = at.AddSeconds(run.Definition.Segments[i].StartDelaySeconds);
                for (int j = 0; j < i; j++)
                {
                    if (run.Definition.Segments[j].WholeRun)
                        continue; // 판 전체 구간은 판 끝에서만 끝난다
                    if (run.SegmentStart[j].HasValue && !run.SegmentEnd[j].HasValue)
                    {
                        run.SegmentEnd[j] = at;
                        SegmentFinished?.Invoke(run, j);
                    }
                }
            }

            /// <summary>구간 i 끝. 다음 구간이 시작 문구가 없는 구간(석상·3보스)이면 그 구간을 이 시각 + 시작 지연에 시작한다.</summary>
            private void MarkSegmentEnd(LiveRun run, int i, DateTime at)
            {
                if (!run.SegmentStart[i].HasValue || run.SegmentEnd[i].HasValue)
                    return;

                run.SegmentEnd[i] = at;
                SegmentFinished?.Invoke(run, i);
                int next = i + 1;
                if (next < run.Definition.Segments.Length &&
                    run.Definition.Segments[next].Start == null &&
                    !run.SegmentStart[next].HasValue)
                {
                    run.SegmentStart[next] = at.AddSeconds(run.Definition.Segments[next].StartDelaySeconds);
                }
            }

            private void FinishRun(LiveRun run, DateTime at, string recordKey, bool capped = false)
            {
                _runs.Remove(run.Definition.GroupKey);
                // 어느 던전이든 클리어 줄이 왔다면 다른 던전의 진행 중이던 판은 이미 버려진 것이다
                CancelOthers(run.Definition.GroupKey, "다른 던전 클리어");

                // 모든 기록은 완료 기준 — 시작이 안 잡혔거나, 끝 문구가 있는 구간이 안 끝났으면 저장하지 않는다.
                // 끝 문구가 없는 구간(마지막 구간 등)은 판 끝이 곧 그 구간의 끝이다.
                for (int i = 0; i < run.SegmentStart.Length; i++)
                {
                    bool optional = run.Definition.Segments[i].Optional;
                    if (!run.SegmentStart[i].HasValue)
                    {
                        if (optional)
                            continue;
                        RunDiscarded?.Invoke(run, run.Definition.Segments[i].Name);
                        return;
                    }
                    if (!run.SegmentEnd[i].HasValue)
                    {
                        if (run.Definition.Segments[i].End != null)
                        {
                            if (optional)
                            {
                                run.SegmentStart[i] = null; // 시작만 하고 못 끝낸 선택 구간(석상 미션 실패)은 빼고 기록한다
                                continue;
                            }
                            RunDiscarded?.Invoke(run, run.Definition.Segments[i].Name);
                            return;
                        }
                        run.SegmentEnd[i] = at;
                    }
                }

                // 난이도를 구분하는 던전만: 끝까지 어려움 상자가 없었으면 일반
                string difficulty = run.Difficulty ?? (run.Definition.HardMarker == null ? string.Empty : run.Definition.NormalLabel);
                var record = new DungeonRunRecord
                {
                    DungeonKey = recordKey,
                    StartedAt = run.StartedAt,
                    EndedAt = at,
                    Difficulty = difficulty,
                    Capped = capped,
                };
                for (int i = 0; i < run.Definition.Segments.Length; i++)
                {
                    if (run.SegmentStart[i] is DateTime s && run.SegmentEnd[i] is DateTime e)
                        record.Segments[run.Definition.Segments[i].Name] = Math.Max(0, (e - s).TotalSeconds);
                }
                // 합계는 구간 시간의 합 (판 전체 경과 시간이 아니다 — 구간 사이 연출·이동 시간은 뺀다).
                // 판 전체 구간이 있는 던전(렐릭)은 그 값이 곧 합계다.
                SegmentDefinition? whole = run.Definition.Segments.FirstOrDefault(s => s.WholeRun);
                record.TotalSeconds = whole != null && record.Segments.TryGetValue(whole.Name, out double wholeSeconds)
                    ? wholeSeconds
                    : record.Segments.Values.Sum();

                RunFinished?.Invoke(run, record);
            }
        }

        private const int DefaultResultSeconds = 30;
        /// <summary>던전·난이도마다 한 주에 보관하는 완료 판 수. 이번 주와 지난주만 남긴다.</summary>
        public const int PerWeekDepth = 7;

        /// <summary>이번 주 시작(월요일 0시). 일일/주간 창과 같은 기준.</summary>
        private static DateTime ThisWeekStart(DateTime now) => DailyWeeklyContentWindow.GetWeeklyResetKey(now);
        /// <summary>지난주 시작(월요일 0시). 이보다 오래된 기록은 버린다.</summary>
        private static DateTime LastWeekStart(DateTime now) => ThisWeekStart(now).AddDays(-7);

        // FormattedText 앞의 로그 시각 "[ 6시 39분 58초]" — 실제 찍힌 시각으로 재야 초 단위가 정확하다
        private static readonly Regex LogTimeRegex = new(@"^\[\s*(\d{1,2})시\s*(\d{1,2})분\s*(\d{1,2})초\s*\]", RegexOptions.Compiled);
        private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRunRegex = new(@"\s+", RegexOptions.Compiled);

        private static readonly string HistoryFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "DungeonTimerHistory.json");
        // 최고 기록은 두 주만 남는 기록 파일과 따로 둔다 — 오래된 판이 지워져도 기록은 남아야 한다
        private static readonly string BestFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "DungeonTimerBest.json");
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        };

        private readonly object SyncRoot = new();
        private readonly RunTracker LiveTracker;

        public ContentTimerService()
        {
            // 추적기 콜백이 이 인스턴스의 이력·창 상태를 쓰므로 생성자에서 만든다 (필드 초기화식에서는 this 사용 불가)
            LiveTracker = CreateLiveTracker();
        }
        // 키 = "기록키/난이도"
        private Dictionary<string, List<DungeonRunRecord>>? _history;
        // 키 = "기록키/난이도" → 그 던전에서 가장 빨랐던 판
        private Dictionary<string, DungeonRunRecord>? _best;
        private bool _bestDirty;

        // ===== 전체 기록 아카이브 =====
        // 두 주만 남는 기록 파일과 달리 지우지 않는다. 월별 파일(yyyy-MM.jsonl)에 판 하나를 한 줄로 덧붙이므로
        // 기록이 수만 건이 돼도 판이 끝날 때 쓰는 양은 한 줄이다.
        private static readonly string ArchiveDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "DungeonTimerArchive");
        private static readonly string ArchiveScanMarkerPath = Path.Combine(ArchiveDirectoryPath, "_fullscan.json");
        /// <summary>던전 정의가 크게 바뀌어 과거 로그를 다시 뽑아야 할 때만 올린다.</summary>
        private const int ArchiveScanVersion = 1;
        private static readonly JsonSerializerOptions ArchiveJsonOptions = new()
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        };
        // 기록키/난이도 → 이미 보관한 판의 끝난 시각(초). 같은 판을 두 번 적지 않기 위한 색인
        private Dictionary<string, HashSet<long>>? _archiveIndex;
        private bool _archiveAppended;

        /// <summary>아카이브에 판이 더해지면 발생 (실시간 클리어, 과거 로그 추출). 기록 추이 창이 다시 그린다.</summary>
        public event Action? ArchiveChanged;

        /// <summary>과거 로그 전체에서 기록을 뽑는 1회성 작업이 도는 중인지.</summary>
        public bool IsArchiveScanRunning { get; private set; }
        /// <summary>가운데 열을 최고 기록으로 보여주는 중인지 ("직전 판" 머리글을 누르면 바뀐다).</summary>
        private bool _showBest;
        /// <summary>마지막으로 그린 표의 재료 — 머리글을 눌러 같은 내용을 다시 그릴 때 쓴다.</summary>
        private (DungeonDefinition Def, string Difficulty, string Status, int? Current, int? Max,
            DungeonRunRecord? InProgress, string? Highlight)? _lastViewArgs;
        private ContentTimerWindow? _window;
        private bool _backfillStarted;
        // 대기·미리보기 때 보여줄 묶음 — 마지막으로 기록이 있었던 묶음
        private string _lastGroupKey = RecordDefinitions[0].GroupKey;

        private RunTracker CreateLiveTracker()
        {
            var tracker = new RunTracker(Definitions);
            tracker.RunStarted += run =>
            {
                AppLogger.Info($"Dungeon timer started. Dungeon='{run.Definition.Name}'");
                // 진행도는 판 시작 때 읽어 둔다 — 클리어 줄을 일일/주간 창이 먼저 셌는지에 따라 값이 흔들리지 않게.
                // 변형이 있는 던전은 갈릴 수 있는 기록 정의 전부를 읽어 둔다.
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    foreach (DungeonDefinition recordDef in RecordDefinitionsOf(run.Definition))
                    {
                        if (TryReadProgress(recordDef, out int cur, out int max))
                        {
                            lock (SyncRoot)
                                run.ProgressAtStart[recordDef.Key] = (cur, max);
                        }
                    }
                }));
            };
            tracker.RunDiscarded += (run, reason) =>
                AppLogger.Info($"Dungeon timer run discarded ({reason}). Dungeon='{run.Definition.Name}'");
            // 구간이 끝날 때마다(1보스 처치, 석상 완료…) 진행 중인 판을 끝난 구간까지 보여주고 방금 끝난 구간을 강조한다.
            // 단일 구간 던전(어비스·이클립스·환희/슬픔)은 구간 끝이 곧 판 끝이라 아래 RunFinished에서만 보여준다.
            tracker.SegmentFinished += (run, index) =>
            {
                DungeonDefinition def = run.Definition;
                if (_priming || def.Segments.Length <= 1 || def.Segments[index].WholeRun)
                    return;
                if (AppServices.Get<TrayAllWindowsService>().IsTrayed)
                    return;

                var partial = new DungeonRunRecord
                {
                    DungeonKey = def.Key,
                    StartedAt = run.StartedAt,
                    EndedAt = DateTime.Now,
                    Difficulty = run.Difficulty ?? string.Empty,
                };
                for (int i = 0; i < def.Segments.Length; i++)
                {
                    if (run.SegmentStart[i] is DateTime s && run.SegmentEnd[i] is DateTime e)
                        partial.Segments[def.Segments[i].Name] = Math.Max(0, (e - s).TotalSeconds);
                }
                string segmentName = def.Segments[index].Name;
                // 끝 문구로 갈리는 던전(아페티리아/EX)은 아직 어느 쪽인지 모르므로 첫 변형 기준으로 보여준다
                DungeonDefinition recordDef = RecordDefinitionsOf(def).First();

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ChatSettings? settings = GetSharedSettings();
                    if (settings != null && !settings.ShowContentTimer)
                        return;
                    int seconds = settings?.ContentTimerResultSeconds ?? DefaultResultSeconds;
                    var (cur, max) = ReadProgressOrNull(recordDef);
                    if (cur.HasValue && !recordDef.ProgressCountedAtStart)
                        cur += 1; // 클리어 때 세는 항목이면 이번 판은 아직 안 세어져 있다
                    TimerView view = BuildView(recordDef, partial.Difficulty, "진행 중", cur, max, partial, segmentName);
                    EnsureWindow(settings).ShowResult(view, seconds);
                }));
            };
            tracker.RunFinished += (run, record) =>
            {
                AppendHistory(record);
                DungeonDefinition recordDef = RecordDefinitionByKey(record.DungeonKey) ?? run.Definition;
                // 결과에서도 마지막으로 끝난 구간을 강조한다
                string? lastSegment = null;
                for (int i = 0; i < run.Definition.Segments.Length; i++)
                {
                    if (!run.Definition.Segments[i].WholeRun && run.SegmentEnd[i].HasValue)
                        lastSegment = run.Definition.Segments[i].Name;
                }
                lock (SyncRoot)
                    _lastGroupKey = recordDef.GroupKey;
                AppLogger.Info($"Dungeon timer finished. Dungeon='{recordDef.GroupName}/{recordDef.Name}' Difficulty='{record.Difficulty}' Total={record.TotalSeconds:0}s");
                if (_priming)
                    return; // 시작 때 오늘 로그를 넣는 중 — 기록만 남기고 화면에는 띄우지 않는다
                if (AppServices.Get<TrayAllWindowsService>().IsTrayed)
                    return; // 트레이 최소화 중에는 창을 띄우지 않는다 (기록은 남는다)

                int? progressCurrent = null, progressMax = null;
                lock (SyncRoot)
                {
                    if (run.ProgressAtStart.TryGetValue(record.DungeonKey, out var p))
                    {
                        // 이번 판 = 시작 때 횟수 (+1: 클리어 때 세는 항목이면 이 판이 아직 안 세어져 있었다)
                        progressCurrent = p.Current + (recordDef.ProgressCountedAtStart ? 0 : 1);
                        progressMax = p.Max;
                    }
                }

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    ChatSettings? settings = GetSharedSettings();
                    if (settings != null && !settings.ShowContentTimer)
                        return;
                    int seconds = settings?.ContentTimerResultSeconds ?? DefaultResultSeconds;
                    if (!progressCurrent.HasValue)
                        (progressCurrent, progressMax) = ReadProgressOrNull(recordDef);
                    TimerView view = BuildView(recordDef, record.Difficulty ?? string.Empty, "클리어", progressCurrent, progressMax,
                        highlightSegment: lastSegment);
                    EnsureWindow(settings).ShowResult(view, seconds);
                }));
            };
            return tracker;
        }

        public bool IsRunning
        {
            get
            {
                lock (SyncRoot)
                    return LiveTracker.HasActiveRun(DateTime.Now);
            }
        }

        /// <summary>사용자가 타이머 창을 닫았다 — 진행 중이던 판은 기록 없이 버린다.</summary>
        public void CancelCurrentRun()
        {
            bool cancelled;
            lock (SyncRoot)
                cancelled = LiveTracker.CancelAll("창 닫음");
            if (cancelled)
                AppLogger.Info("Dungeon timer runs cancelled by closing the window.");
        }

        // ===== 실시간 로그 =====

        public void Observe(string formattedText)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
                return;

            DateTime now = DateTime.Now;
            DateTime at = ResolveLogTime(formattedText, now.Date, now);
            lock (SyncRoot)
            {
                // 시작 때 오늘 로그로 이미 넣은 줄이면 건너뛴다 (같은 초의 줄은 내용으로 가린다)
                if (at < _primedUntil || (at == _primedUntil && _primedLastSecondTexts.Contains(formattedText)))
                    return;
                LiveTracker.Feed(formattedText, at, now);
            }
        }

        // 시작 때 오늘 로그를 실시간 추적기에 넣는 중인지 (그동안 화면 표시는 하지 않는다) / 어디까지 넣었는지
        private volatile bool _priming;
        private DateTime _primedUntil = DateTime.MinValue;
        private HashSet<string> _primedLastSecondTexts = new(StringComparer.Ordinal);

        /// <summary>
        /// 오늘 로그 파일을 실시간 추적기에 순서대로 넣어, 앱을 켜기 전에 이미 진행 중이던 판의 구간 상태를 복원한다.
        /// 완료된 판은 기록에 들어가고(백필과 겹치면 같은 판으로 걸러진다), 30분 넘게 지난 판은 안전 한도에 걸려 무시된다.
        /// </summary>
        private void PrimeLiveTracker(string path, DateTime day)
        {
            List<string> lines = ReadLogLines(path).ToList();
            DateTime now = DateTime.Now;
            lock (SyncRoot)
            {
                _priming = true;
                try
                {
                    DateTime lastAt = DateTime.MinValue;
                    var lastSecond = new HashSet<string>(StringComparer.Ordinal);
                    foreach (string line in lines)
                    {
                        DateTime at = ResolveLogTime(line, day, now);
                        LiveTracker.Feed(line, at, now);
                        if (at > lastAt)
                        {
                            lastAt = at;
                            lastSecond.Clear();
                        }
                        if (at == lastAt)
                            lastSecond.Add(line);
                    }
                    _primedUntil = lastAt;
                    _primedLastSecondTexts = lastSecond;
                }
                finally
                {
                    _priming = false;
                }
            }
            AppLogger.Info($"Dungeon timer primed from today's log. Lines={lines.Count}, active={LiveTracker.HasActiveRun(DateTime.Now)}");
        }

        // ===== 표 내용 만들기 =====

        /// <summary>
        /// 창이 그릴 표를 만든다. 묶음에 기록 정의가 하나면 구간 모드(행 = 구간, 열 = 판), 여럿이면 묶음 모드(행 = 던전, 열 = 그 던전의 판).
        /// 이번 판 열은 가장 최근에 완료한 판이다. def는 기록 정의여야 한다.
        /// </summary>
        private TimerView BuildView(DungeonDefinition def, string difficulty, string status, int? progressCurrent, int? progressMax,
            DungeonRunRecord? inProgress = null, string? highlightSegment = null)
        {
            DungeonDefinition[] group = RecordDefinitions.Where(d => d.GroupKey == def.GroupKey).ToArray();
            bool segmentMode = group.Length == 1;
            // 묶음의 던전들이 같은 일일/주간 항목을 쓰면(토벌전 N/21) 진행도는 제목에, 각자 다르면(어비스 심층별 N/7) 행 이름에 붙인다
            bool sharedProgressItem = group.All(m => m.ProgressItemName == group[0].ProgressItemName);
            string title = BuildTitle(def.GroupName, difficulty,
                sharedProgressItem ? progressCurrent : null, sharedProgressItem ? progressMax : null, segmentMode);

            // 머리글을 눌러 다시 그릴 수 있게 이번에 그린 내용을 기억해 둔다
            _lastViewArgs = (def, difficulty, status, progressCurrent, progressMax, inProgress, highlightSegment);

            var view = new TimerView
            {
                GroupKey = def.GroupKey,
                Title = title,
                Status = status,
                ShowTotal = segmentMode && def.ShowTotal,
                ShowColumnTimes = true,
                PreviousIsBest = _showBest,
            };

            if (segmentMode)
            {
                Columns cols = PickColumns(GetHistory(def, difficulty));
                // 진행 중인 판이 있으면 최근 판 자리에 그 판(끝난 구간만)을 놓고, 직전 판은 가장 최근 완료 기록이 된다
                if (inProgress != null)
                    cols = cols with { Previous = cols.Latest, Latest = inProgress };
                // 가운데 열: 평소에는 직전 판, 머리글을 눌러 Best로 바꾸면 가장 빨랐던 판
                DungeonRunRecord? middle = _showBest ? GetBest(def, difficulty) : cols.Previous;
                view.ColumnSub[0] = cols.LastWeek.Count > 0 ? $"{cols.LastWeek.Count}판 평균" : null;
                view.ColumnSub[1] = FormatWhen(middle?.EndedAt);
                view.ColumnSub[2] = inProgress != null ? "진행 중" : FormatWhen(cols.Latest?.EndedAt);
                view.Totals[0] = Average(cols.LastWeek.Select(r => r.TotalSeconds));
                view.Totals[1] = middle?.TotalSeconds;
                view.Totals[2] = cols.Latest?.TotalSeconds;
                view.TotalsCapped[1] = middle?.Capped == true;
                view.TotalsCapped[2] = cols.Latest?.Capped == true;
                foreach (SegmentDefinition segment in def.Segments)
                {
                    var row = new TimerRow { Name = segment.Name, Highlight = highlightSegment != null && segment.Name == highlightSegment };
                    row.Seconds[0] = Average(cols.LastWeek.Where(r => r.Segments.ContainsKey(segment.Name)).Select(r => r.Segments[segment.Name]));
                    row.Seconds[1] = SegmentSeconds(middle, segment.Name);
                    row.Seconds[2] = SegmentSeconds(cols.Latest, segment.Name);
                    row.Capped[1] = middle?.Capped == true;
                    row.Capped[2] = cols.Latest?.Capped == true;
                    view.Rows.Add(row);
                }
            }
            else
            {
                // 묶음 표: 열 하나에 던전이 여럿이라 부제는 행들을 합쳐 적는다 — 지난주 판 수 합계, 열에서 가장 최근에 끝난 판의 시각
                int lastWeekTotalRuns = 0;
                DateTime? previousLatest = null, latestLatest = null;
                foreach (DungeonDefinition member in group)
                {
                    // 일일/주간 창에서 꺼 둔 항목(추종하는 환희(어려움) 등)은 행도 보여주지 않는다
                    if (!sharedProgressItem && !IsProgressItemEnabled(member))
                        continue;
                    string memberDifficulty = string.IsNullOrEmpty(difficulty) ? GetLastDifficulty(member) : difficulty;
                    Columns cols = PickColumns(GetHistory(member, memberDifficulty));
                    DungeonRunRecord? middle = _showBest ? GetBest(member, memberDifficulty) : cols.Previous;
                    lastWeekTotalRuns += cols.LastWeek.Count;
                    previousLatest = MaxDate(previousLatest, cols.Previous?.EndedAt);
                    latestLatest = MaxDate(latestLatest, cols.Latest?.EndedAt);
                    // 던전마다 항목이 다르면 행 이름에 그 던전의 진행도를 붙인다 — "심층1 (8/7)"
                    string rowName = member.Name;
                    if (!sharedProgressItem)
                    {
                        var (rowCur, rowMax) = ReadProgressOrNull(member);
                        if (rowCur.HasValue && rowMax.HasValue && rowMax.Value > 0)
                            rowName = $"{member.Name} ({rowCur.Value}/{rowMax.Value})";
                    }
                    var row = new TimerRow { Name = rowName };
                    row.Seconds[0] = Average(cols.LastWeek.Select(r => r.TotalSeconds));
                    row.Seconds[1] = middle?.TotalSeconds;
                    row.Seconds[2] = cols.Latest?.TotalSeconds;
                    row.Capped[1] = middle?.Capped == true;
                    row.Capped[2] = cols.Latest?.Capped == true;
                    // 행마다 판이 다르므로 판 시각은 머리글이 아니라 각 값 아래에 붙인다
                    row.Times[1] = FormatWhen(middle?.EndedAt);
                    row.Times[2] = FormatWhen(cols.Latest?.EndedAt);
                    view.Rows.Add(row);
                }
                view.ColumnSub[0] = lastWeekTotalRuns > 0 ? $"{lastWeekTotalRuns}판 평균" : null;
                view.ColumnSub[1] = string.Empty;
                view.ColumnSub[2] = string.Empty;
            }

            return view;
        }

        /// <summary>머리글 아래 시각 — 어느 날인지 헷갈리지 않게 날짜를 붙인다 ("09.14 21:47"). 구분자는 로캘과 무관하게 고정한다.</summary>
        private string? FormatWhen(DateTime? at) => at?.ToString("MM'.'dd HH':'mm", System.Globalization.CultureInfo.InvariantCulture);

        private DateTime? MaxDate(DateTime? a, DateTime? b)
            => !a.HasValue ? b : !b.HasValue ? a : (a.Value >= b.Value ? a : b);

        /// <summary>열에 놓을 기록: 지난주 판 전부(평균용), 지난 판, 이번 판(가장 최근).</summary>
        private sealed record Columns(List<DungeonRunRecord> LastWeek, DungeonRunRecord? Previous, DungeonRunRecord? Latest);

        /// <summary>
        /// 완료 기록(오래된 것 → 최근 순)을 열에 배치한다. 이번 판 = 가장 최근, 지난 판 = 그 직전(주와 무관),
        /// 지난주 평균 = 지난주 월요일 0시 ~ 이번 주 월요일 0시 사이에 끝난 판들.
        /// </summary>
        private Columns PickColumns(IReadOnlyList<DungeonRunRecord> history)
        {
            DateTime now = DateTime.Now;
            DateTime thisWeek = ThisWeekStart(now);
            DateTime lastWeek = LastWeekStart(now);
            var lastWeekRuns = history.Where(r => r.EndedAt >= lastWeek && r.EndedAt < thisWeek).ToList();
            DungeonRunRecord? latest = history.Count >= 1 ? history[history.Count - 1] : null;
            DungeonRunRecord? previous = history.Count >= 2 ? history[history.Count - 2] : null;
            return new Columns(lastWeekRuns, previous, latest);
        }

        private double? Average(IEnumerable<double> values)
        {
            var list = values.ToList();
            return list.Count > 0 ? list.Average() : null;
        }

        private double? SegmentSeconds(DungeonRunRecord? record, string segmentName)
            => record != null && record.Segments.TryGetValue(segmentName, out double s) ? s : null;

        /// <summary>"아페티리아 (3/7)" · "어비스 - 심층(지옥)" · "이클립스 토벌전 (5/21)" 꼴.</summary>
        private string BuildTitle(string groupName, string difficulty, int? progressCurrent, int? progressMax, bool segmentMode)
        {
            string title = groupName;
            if (!string.IsNullOrEmpty(difficulty))
                title += segmentMode ? $" · {difficulty}" : $"({difficulty})";
            if (progressCurrent.HasValue && progressMax.HasValue && progressMax.Value > 0)
                title += $" ({progressCurrent.Value}/{progressMax.Value})";
            return title;
        }

        /// <summary>일일/주간 컨텐츠 창의 항목에서 현재 횟수/최대 횟수를 읽는다. UI 스레드에서 부른다.</summary>
        private bool TryReadProgress(DungeonDefinition def, out int current, out int max)
        {
            current = 0;
            max = 0;
            if (string.IsNullOrEmpty(def.ProgressItemName))
                return false;

            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow main)
                        return main.TryGetDailyWeeklyItemProgress(def.ProgressItemName, out current, out max);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to read dungeon progress for timer.", ex);
            }
            return false;
        }

        private (int? Current, int? Max) ReadProgressOrNull(DungeonDefinition def)
            => TryReadProgress(def, out int cur, out int max) ? (cur, max) : (null, null);

        /// <summary>진행도 항목이 일일/주간 창에서 켜져 있는지. 항목이 없거나 읽지 못하면 켜진 것으로 본다. UI 스레드에서 부른다.</summary>
        private bool IsProgressItemEnabled(DungeonDefinition def)
        {
            if (string.IsNullOrEmpty(def.ProgressItemName))
                return true;
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow main)
                        return main.IsDailyWeeklyItemEnabled(def.ProgressItemName) ?? true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to read dungeon item state for timer.", ex);
            }
            return true;
        }

        /// <summary>"[ 6시 39분 58초] …" 앞의 시각을 날짜와 합친다. 못 읽으면 fallback.</summary>
        private DateTime ResolveLogTime(string text, DateTime date, DateTime fallback)
        {
            Match m = LogTimeRegex.Match(text);
            if (!m.Success)
                return fallback;

            try
            {
                int h = int.Parse(m.Groups[1].Value);
                int mi = int.Parse(m.Groups[2].Value);
                int s = int.Parse(m.Groups[3].Value);
                if (h > 23 || mi > 59 || s > 59)
                    return fallback;
                DateTime at = date.AddHours(h).AddMinutes(mi).AddSeconds(s);
                if (at > fallback.AddMinutes(1) && date == fallback.Date)
                    at = at.AddDays(-1); // 자정 직후 어제 시각
                return at;
            }
            catch
            {
                return fallback;
            }
        }

        // ===== 과거 로그 파일에서 기록 복원 =====

        /// <summary>
        /// 지난주 월요일부터 오늘까지의 채팅 로그 파일(TWChatLog_yyyy_MM_dd.html)을 읽어 완료된 판을 기록에 합친다.
        /// 이미 있는 판(시작 시각이 5초 안에 같은 것)은 건너뛴다. 앱 실행 중 한 번만 돈다.
        /// </summary>
        public Task BackfillFromLogsAsync(string? chatLogFolder)
        {
            lock (SyncRoot)
            {
                if (_backfillStarted)
                    return Task.CompletedTask;
                _backfillStarted = true;
            }

            if (string.IsNullOrWhiteSpace(chatLogFolder) || !Directory.Exists(chatLogFolder))
                return Task.CompletedTask;

            string folder = chatLogFolder;
            return Task.Run(() =>
            {
                try
                {
                    // 평소에는 지난주 월요일부터만 읽는다 (앱이 꺼져 있던 동안의 판 복원).
                    // 전체 기록 아카이브를 아직 만든 적이 없으면 이번 한 번만 남아 있는 로그 전부에서 기록을 뽑고, 끝나면 표시를 남겨 다시 읽지 않는다.
                    bool fullScan = !IsArchiveFullScanDone();
                    IsArchiveScanRunning = fullScan;
                    DateTime oldest = fullScan ? DateTime.MinValue : LastWeekStart(DateTime.Now);
                    int foundTotal = 0, addedTotal = 0, filesRead = 0;
                    var files = Directory.EnumerateFiles(folder, "TWChatLog_*.html")
                        .Select(path => (Path: path, Day: ParseLogFileDate(path)))
                        .Where(f => f.Day.HasValue && f.Day.Value >= oldest)
                        .OrderBy(f => f.Day!.Value)
                        .ToList();
                    // 오늘 파일은 실시간 추적기에도 먼저 넣는다 — 앱을 판 도중에 켰어도 이미 지난 구간(1보스·석상 등)이 복원된다.
                    // 넣는 동안은 화면에 띄우지 않고, 이미 넣은 줄이 실시간으로 또 들어오면 Observe에서 걸러낸다.
                    var todayFile = files.FirstOrDefault(f => f.Day!.Value.Date == DateTime.Today);
                    if (todayFile.Path != null)
                        PrimeLiveTracker(todayFile.Path, todayFile.Day!.Value);

                    foreach (var (path, dayNullable) in files)
                    {
                        DateTime day = dayNullable!.Value;

                        // 파일 하나는 시간순으로 넣는다. 자정을 넘기는 판은 드물어 파일마다 추적기를 새로 쓴다.
                        var found = new List<DungeonRunRecord>();
                        var tracker = new RunTracker(Definitions);
                        tracker.RunFinished += (_, record) => found.Add(record);
                        foreach (string line in ReadLogLines(path))
                        {
                            DateTime at = ResolveLogTime(line, day, day);
                            tracker.Feed(line, at, at);
                        }
                        filesRead++;
                        foundTotal += found.Count;
                        addedTotal += MergeHistory(found);
                        NotifyArchiveChangedIfAppended();

                        // 전체 추출은 파일이 많다 — 게임과 채팅 표시를 방해하지 않게 파일 사이에 숨을 돌린다
                        if (fullScan)
                            Thread.Sleep(20);
                    }

                    if (fullScan)
                    {
                        MarkArchiveFullScanDone(filesRead, foundTotal);
                        IsArchiveScanRunning = false;
                        try { ArchiveChanged?.Invoke(); } catch { }
                    }

                    AppLogger.Info($"Dungeon timer backfill finished. FullScan={fullScan}, Files={filesRead}, runs found={foundTotal}, added={addedTotal}.");
                }
                catch (Exception ex)
                {
                    IsArchiveScanRunning = false;
                    AppLogger.Warn("Dungeon timer backfill failed.", ex);
                }
            });
        }

        /// <summary>"TWChatLog_2026_09_16.html" 파일 이름에서 날짜를 읽는다. 형식이 다르면 null.</summary>
        private DateTime? ParseLogFileDate(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            const string prefix = "TWChatLog_";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return null;
            return DateTime.TryParseExact(name.Substring(prefix.Length), "yyyy_MM_dd",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime day)
                ? day
                : null;
        }

        /// <summary>게임 채팅 로그(CP949 HTML)를 태그 없는 줄로 읽는다. "[ 6시 39분 58초] 내용" 꼴.</summary>
        private IEnumerable<string> ReadLogLines(string path)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            Encoding encoding = Encoding.GetEncoding(949);

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
            string? raw;
            while ((raw = reader.ReadLine()) != null)
            {
                if (raw.Length == 0)
                    continue;
                string text = HtmlTagRegex.Replace(raw, " ");
                text = WebUtility.HtmlDecode(text).Replace("&nbsp", " ");
                text = WhitespaceRunRegex.Replace(text, " ").Trim();
                if (text.Length > 0)
                    yield return text;
            }
        }

        // ===== 기록 파일 =====

        private string HistoryKey(string dungeonKey, string difficulty) => $"{dungeonKey}/{difficulty}";

        /// <summary>기록 정의·난이도의 완료된 판 기록(오래된 것 → 최근 순, 최대 3개).</summary>
        public IReadOnlyList<DungeonRunRecord> GetHistory(DungeonDefinition def, string difficulty)
        {
            lock (SyncRoot)
            {
                EnsureHistoryLoaded();
                return _history!.TryGetValue(HistoryKey(def.Key, difficulty), out var list) ? list.ToList() : new List<DungeonRunRecord>();
            }
        }

        /// <summary>이 기록 정의에서 가장 최근에 완료한 판. 없으면 null.</summary>
        private DungeonRunRecord? LatestRecordUnlocked(string recordKey)
        {
            DungeonRunRecord? latest = null;
            foreach (var pair in _history!)
            {
                if (!pair.Key.StartsWith(recordKey + "/", StringComparison.Ordinal))
                    continue;
                foreach (var record in pair.Value)
                {
                    if (latest == null || record.EndedAt > latest.EndedAt)
                        latest = record;
                }
            }
            return latest;
        }

        /// <summary>이 던전에서 가장 최근에 완료한 판의 난이도. 난이도를 구분하지 않는 던전은 빈 문자열, 기록이 없으면 HardLabel.</summary>
        public string GetLastDifficulty(DungeonDefinition def)
        {
            if (!HasDifficulty(def))
                return string.Empty; // 난이도를 어디서도 읽지 않는 던전

            lock (SyncRoot)
            {
                EnsureHistoryLoaded();
                return LatestRecordUnlocked(def.Key)?.Difficulty ?? def.HardLabel;
            }
        }

        private void AppendHistory(DungeonRunRecord record)
        {
            lock (SyncRoot)
            {
                EnsureHistoryLoaded();
                AddRecordUnlocked(record);
                SaveHistory();
                SaveBestIfDirty();
            }
            NotifyArchiveChangedIfAppended();
        }

        /// <summary>과거 로그에서 찾은 판들을 합친다. 새로 들어간 개수를 돌려준다.</summary>
        private int MergeHistory(IEnumerable<DungeonRunRecord> records)
        {
            lock (SyncRoot)
            {
                EnsureHistoryLoaded();
                int added = 0;
                foreach (var record in records)
                {
                    if (AddRecordUnlocked(record))
                        added++;
                }
                if (added > 0)
                    SaveHistory();
                SaveBestIfDirty();
                return added;
            }
        }

        /// <summary>
        /// 같은 판(시작 시각 5초 이내)이 이미 있으면 넣지 않는다. 시각순 정렬 후 지난주 월요일 이전 기록을 버리고,
        /// 한 주에 7개가 넘으면 그 주의 오래된 것부터 지운다.
        /// </summary>
        private bool AddRecordUnlocked(DungeonRunRecord record)
        {
            // 합계는 언제나 구간 합 — 옛 파일(판 전체 경과 시간으로 저장)도 여기서 맞춘다
            if (record.Segments.Count > 0)
                record.TotalSeconds = record.Segments.Values.Sum();

            // 두 주 기록의 중복 판정·정리와 무관하게, 처음 보는 판이면 전체 기록에 남긴다
            ArchiveAppendIfNewUnlocked(record);

            string key = HistoryKey(record.DungeonKey, record.Difficulty ?? string.Empty);
            if (!_history!.TryGetValue(key, out var list))
            {
                list = new List<DungeonRunRecord>();
                _history[key] = list;
            }
            // 같은 판 판정은 끝난 시각으로 한다 — 시작 문구 정의가 바뀌어도 클리어 문구 시각은 그대로라 옛 기록과 겹치지 않는다
            if (list.Any(r => Math.Abs((r.EndedAt - record.EndedAt).TotalSeconds) < 5))
                return false;

            list.Add(record);
            TrimUnlocked(list);
            UpdateBestUnlocked(key, record);
            return true;
        }

        // ===== 전체 기록 아카이브 =====

        /// <summary>난이도를 구분하는 던전인지 (기록 추이 창의 난이도 선택용).</summary>
        public bool DefinitionHasDifficulty(DungeonDefinition def) => HasDifficulty(def);

        /// <summary>묶음에 속한 기록 정의들 (어비스 심층Ⅰ·Ⅱ·Ⅲ, 이클립스 보스 6종 등).</summary>
        public IReadOnlyList<DungeonDefinition> GetGroupMembers(string groupKey)
            => RecordDefinitions.Where(d => d.GroupKey == groupKey).ToList();

        /// <summary>이 던전의 전체 기록 (모든 난이도, 오래된 것 → 최근 순). 파일을 읽으므로 UI 스레드 밖에서 부른다.</summary>
        public IReadOnlyList<DungeonRunRecord> GetArchive(DungeonDefinition def)
        {
            var result = new List<DungeonRunRecord>();
            lock (SyncRoot)
            {
                EnsureHistoryLoaded(); // 두 주 기록 파일에만 있던 판도 이때 아카이브로 옮겨진다
                foreach (DungeonRunRecord record in ReadArchiveUnlocked())
                {
                    if (record.DungeonKey != def.Key)
                        continue;
                    if (!HasDifficulty(def))
                        record.Difficulty = string.Empty;
                    else
                        record.Difficulty ??= def.NormalLabel;
                    result.Add(record);
                }
            }
            result.Sort((a, b) => a.EndedAt.CompareTo(b.EndedAt));
            return result;
        }

        private IEnumerable<DungeonRunRecord> ReadArchiveUnlocked()
        {
            if (!Directory.Exists(ArchiveDirectoryPath))
                yield break;

            foreach (string file in Directory.EnumerateFiles(ArchiveDirectoryPath, "*.jsonl").OrderBy(f => f, StringComparer.Ordinal))
            {
                IEnumerable<string> lines;
                try { lines = File.ReadAllLines(file, Encoding.UTF8); }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to read dungeon timer archive '{file}'.", ex);
                    continue;
                }

                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    DungeonRunRecord? record = null;
                    try { record = JsonSerializer.Deserialize<DungeonRunRecord>(line, ArchiveJsonOptions); }
                    catch { /* 깨진 줄은 건너뛴다 */ }
                    if (record != null && !string.IsNullOrEmpty(record.DungeonKey))
                        yield return record;
                }
            }
        }

        private void EnsureArchiveIndexUnlocked()
        {
            if (_archiveIndex != null)
                return;

            _archiveIndex = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
            foreach (DungeonRunRecord record in ReadArchiveUnlocked())
                ArchiveIndexSet(record).Add(ToUnixSeconds(record.EndedAt));
        }

        private HashSet<long> ArchiveIndexSet(DungeonRunRecord record)
        {
            string key = HistoryKey(record.DungeonKey, record.Difficulty ?? string.Empty);
            if (!_archiveIndex!.TryGetValue(key, out var set))
            {
                set = new HashSet<long>();
                _archiveIndex[key] = set;
            }
            return set;
        }

        private static long ToUnixSeconds(DateTime at) => (long)(at - DateTime.UnixEpoch).TotalSeconds;

        /// <summary>
        /// 처음 보는 판이면 그 달 파일 끝에 한 줄로 덧붙인다. 같은 판 판정은 두 주 기록과 같이 끝난 시각 5초 이내.
        /// SyncRoot를 쥔 채로 부른다.
        /// </summary>
        private void ArchiveAppendIfNewUnlocked(DungeonRunRecord record)
        {
            try
            {
                if (record.EndedAt == default || string.IsNullOrEmpty(record.DungeonKey))
                    return;

                EnsureArchiveIndexUnlocked();
                HashSet<long> seen = ArchiveIndexSet(record);
                long at = ToUnixSeconds(record.EndedAt);
                for (long t = at - 4; t <= at + 4; t++)
                {
                    if (seen.Contains(t))
                        return;
                }

                Directory.CreateDirectory(ArchiveDirectoryPath);
                string file = Path.Combine(ArchiveDirectoryPath, record.EndedAt.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture) + ".jsonl");
                File.AppendAllText(file, JsonSerializer.Serialize(record, ArchiveJsonOptions) + Environment.NewLine, new UTF8Encoding(false));
                seen.Add(at);
                _archiveAppended = true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to append dungeon timer archive.", ex);
            }
        }

        private void NotifyArchiveChangedIfAppended()
        {
            bool appended;
            lock (SyncRoot)
            {
                appended = _archiveAppended;
                _archiveAppended = false;
            }
            if (!appended)
                return;
            try { ArchiveChanged?.Invoke(); } catch { }
        }

        /// <summary>과거 로그 전체 추출을 이미 마쳤는지. 표시 파일이 없거나 추출 버전이 낮으면 아직이다.</summary>
        private bool IsArchiveFullScanDone()
        {
            try
            {
                if (!File.Exists(ArchiveScanMarkerPath))
                    return false;
                using var doc = JsonDocument.Parse(File.ReadAllText(ArchiveScanMarkerPath));
                return doc.RootElement.TryGetProperty("Version", out var v) && v.GetInt32() >= ArchiveScanVersion;
            }
            catch
            {
                return false;
            }
        }

        private void MarkArchiveFullScanDone(int files, int runs)
        {
            try
            {
                Directory.CreateDirectory(ArchiveDirectoryPath);
                var marker = new { Version = ArchiveScanVersion, CompletedAt = DateTime.Now, Files = files, Runs = runs };
                File.WriteAllText(ArchiveScanMarkerPath, JsonSerializer.Serialize(marker, JsonOptions), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to write dungeon timer archive scan marker.", ex);
            }
        }

        // ===== 최고 기록 =====

        /// <summary>이 기록 정의·난이도에서 가장 빨랐던 판. 없으면 null.</summary>
        public DungeonRunRecord? GetBest(DungeonDefinition def, string difficulty)
        {
            lock (SyncRoot)
            {
                EnsureHistoryLoaded(); // 기록을 읽으면서 최고 기록도 함께 채워진다
                return _best!.TryGetValue(HistoryKey(def.Key, difficulty), out var best) ? best : null;
            }
        }

        /// <summary>
        /// 더 빠른 판이면 최고 기록을 바꾼다. 상한 기록("30초 이하")은 실제 시간을 모르므로 최고 기록으로 삼지 않는다.
        /// </summary>
        private void UpdateBestUnlocked(string key, DungeonRunRecord record)
        {
            EnsureBestLoaded();
            if (record.Capped || record.TotalSeconds <= 0)
                return;
            if (_best!.TryGetValue(key, out var best) && best.TotalSeconds <= record.TotalSeconds)
                return;
            _best[key] = record;
            _bestDirty = true;
        }

        private void EnsureBestLoaded()
        {
            if (_best != null)
                return;

            _best = new Dictionary<string, DungeonRunRecord>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(BestFilePath))
                    return;
                var loaded = JsonSerializer.Deserialize<Dictionary<string, DungeonRunRecord>>(File.ReadAllText(BestFilePath), JsonOptions);
                if (loaded == null)
                    return;
                foreach (var pair in loaded)
                {
                    // 정의가 사라진 던전은 버린다
                    if (pair.Value == null || RecordDefinitionByKey(pair.Value.DungeonKey) == null)
                        continue;
                    _best[pair.Key] = pair.Value;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load dungeon timer best records.", ex);
            }
        }

        private void SaveBestIfDirty()
        {
            if (!_bestDirty)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(BestFilePath)!);
                File.WriteAllText(BestFilePath, JsonSerializer.Serialize(_best, JsonOptions), new UTF8Encoding(false));
                _bestDirty = false;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save dungeon timer best records.", ex);
            }
        }

        /// <summary>시각순으로 정렬하고 지난주 월요일 이전은 버리며, 주마다 최근 7개만 남긴다.</summary>
        private void TrimUnlocked(List<DungeonRunRecord> list)
        {
            DateTime now = DateTime.Now;
            DateTime oldest = LastWeekStart(now);
            list.Sort((a, b) => a.EndedAt.CompareTo(b.EndedAt));
            list.RemoveAll(r => r.EndedAt < oldest);

            foreach (var week in list.GroupBy(r => ThisWeekStart(r.EndedAt)).ToList())
            {
                var runs = week.ToList();
                for (int i = 0; i < runs.Count - PerWeekDepth; i++)
                    list.Remove(runs[i]);
            }
        }

        private void EnsureHistoryLoaded()
        {
            if (_history != null)
                return;

            _history = new Dictionary<string, List<DungeonRunRecord>>(StringComparer.Ordinal);
            try
            {
                if (!File.Exists(HistoryFilePath))
                    return;
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<DungeonRunRecord>>>(File.ReadAllText(HistoryFilePath), JsonOptions);
                if (loaded == null)
                    return;
                foreach (var pair in loaded)
                {
                    foreach (var record in pair.Value ?? new List<DungeonRunRecord>())
                    {
                        // 난이도를 어려움 상자로만 구분하던 옛 기록: 그 던전이 이제 구분하지 않으면 한 묶음으로
                        DungeonDefinition? def = RecordDefinitionByKey(record.DungeonKey);
                        if (def == null)
                            continue; // 정의가 사라진 던전
                        if (!HasDifficulty(def))
                            record.Difficulty = string.Empty;
                        else
                            record.Difficulty ??= def.NormalLabel;
                        // 구간 이름이 바뀐 옛 기록("N페이지"·"N페이즈" → "N단계")을 현재 정의 이름으로 맞춘다
                        foreach (string oldName in record.Segments.Keys.Where(k => k.Contains("페이지") || k.Contains("페이즈")).ToList())
                        {
                            double value = record.Segments[oldName];
                            record.Segments.Remove(oldName);
                            record.Segments[oldName.Replace("페이지", "단계").Replace("페이즈", "단계")] = value;
                        }
                        // '전체' 구간이 생기기 전의 옛 기록(신조·최후의 결전·환희의 잔상): 판 시작~끝 시각으로 채운다
                        SegmentDefinition? whole = def.Segments.FirstOrDefault(s => s.WholeRun);
                        if (whole != null && !record.Segments.ContainsKey(whole.Name))
                        {
                            double wholeSeconds = Math.Max(0, (record.EndedAt - record.StartedAt).TotalSeconds);
                            record.Segments[whole.Name] = wholeSeconds;
                            record.TotalSeconds = wholeSeconds;
                        }
                        AddRecordUnlocked(record);
                    }
                }
                // 최고 기록 파일이 없던 때(또는 새 던전)는 남아 있는 기록에서 채워 둔다
                SaveBestIfDirty();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load dungeon timer history.", ex);
            }
        }

        private void SaveHistory()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(HistoryFilePath)!);
                File.WriteAllText(HistoryFilePath, JsonSerializer.Serialize(_history, JsonOptions), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save dungeon timer history.", ex);
            }
        }

        // ===== 창 =====

        private DungeonDefinition IdleDefinition()
        {
            lock (SyncRoot)
                return RecordDefinitions.FirstOrDefault(d => d.GroupKey == _lastGroupKey) ?? RecordDefinitions[0];
        }

        /// <summary>잠금 해제 모드에서 위치를 잡을 수 있게 띄운다. 제목의 진행도는 일일/주간 창의 현재 횟수다.</summary>
        public void ShowPositionPreview(ChatSettings settings)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                DungeonDefinition def = IdleDefinition();
                string difficulty = GetLastDifficulty(def);
                var (cur, max) = ReadProgressOrNull(def);
                EnsureWindow(settings).ShowPreview(BuildView(def, difficulty, "위치 조정 미리보기", cur, max), settings.ContentTimerCompact);
            }));
        }

        /// <summary>미리보기를 닫는다. 디버그 상시 표시면 대기 상태로 되돌린다.</summary>
        public void ClosePositionPreview()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(HideOrIdle));
        }

        /// <summary>디버그 빌드용: 창을 항상 띄워 둔다. 대기 상태로 기록만 보여주고, 결과가 끝나도 닫지 않는다.</summary>
        public void EnsureVisibleForDebug(ChatSettings settings)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                EnsureWindow(settings).KeepOpen = true;
                ShowIdle();
            }));
        }

        /// <summary>창이 떠 있으면 대기 상태(기록만 표시)로 바꾼다.</summary>
        public void ShowIdle()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _window;
                if (window == null || !window.IsLoaded)
                    return;
                ShowGroupOn(window, _lastGroupKey, "대기 중");
            }));
        }

        /// <summary>타이머 기록 창이 열리거나 닫힐 때 (메뉴 바 버튼 표시용).</summary>
        public event Action<bool>? WindowVisibilityChanged;

        /// <summary>메뉴 바 버튼: 기록 창이 떠 있으면 닫고, 없으면 마지막 묶음의 기록을 띄운다. 저절로 닫히지 않는다.</summary>
        public void ToggleManualWindow(ChatSettings settings)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _window;
                if (window != null && window.IsLoaded)
                {
                    window.KeepOpen = false;
                    try { window.Close(); } catch { }
                    return;
                }
                // 마지막에 보던 모드(창의 작게/크게 버튼으로 바꾼 값)로 연다
                ShowGroupOn(EnsureWindow(settings), _lastGroupKey, "기록", settings.ContentTimerCompact);
            }));
        }

        /// <summary>창 왼쪽 목록에 보여줄 묶음들 (정의 순서: 아페티리아 → 어비스 → 신조 → 최후의 결전 → 토벌전 → 보스전).</summary>
        /// <summary>창 왼쪽 목록과 &lt; &gt; 넘김 순서. 여기 없는 묶음은 정의 순서대로 뒤에 붙는다.</summary>
        private static readonly string[] GroupOrder =
        {
            "shinjo", "abyss", "eclipse_raid", "eclipse_core", "eclipse_subjugation",
            "apetiria", "apetiria_ex", "final_battle", "joy_sorrow", "afterimage_joy", "relic",
        };

        public readonly IReadOnlyList<(string Key, string Name)> Groups = RecordDefinitions
            .GroupBy(d => d.GroupKey)
            .Select(g => (Key: g.Key, Name: g.First().GroupName))
            .OrderBy(g => { int i = Array.IndexOf(GroupOrder, g.Key); return i < 0 ? int.MaxValue : i; })
            .ToList();

        /// <summary>
        /// 어느 던전을 보든 표에 놓일 수 있는 행 이름 전부. 창이 행 이름 열의 너비를 가장 긴 이름에 맞춰 고정하는 데 쓴다
        /// (묶음 모드 행에는 진행도가 붙을 수 있으므로 자리를 미리 잡아 둔다).
        /// </summary>
        public IEnumerable<string> AllRowNames()
        {
            foreach (var group in RecordDefinitions.GroupBy(d => d.GroupKey))
            {
                DungeonDefinition[] members = group.ToArray();
                if (members.Length == 1)
                {
                    foreach (SegmentDefinition segment in members[0].Segments)
                        yield return segment.Name;
                }
                else
                {
                    bool sharedProgressItem = members.All(m => m.ProgressItemName == members[0].ProgressItemName);
                    foreach (DungeonDefinition member in members)
                        yield return sharedProgressItem ? member.Name : member.Name + " (0/0)";
                }
            }
        }

        /// <summary>창의 &lt; &gt; 버튼: 묶음을 앞뒤로 넘긴다. 자동 닫힘은 멈춘다.</summary>
        public void ShowNextGroup(int direction)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _window;
                if (window == null || !window.IsLoaded)
                    return;
                int index = Groups.ToList().FindIndex(g => g.Key == _lastGroupKey);
                int next = ((index < 0 ? 0 : index) + direction + Groups.Count) % Groups.Count;
                ShowGroupOn(window, Groups[next].Key, "기록");
            }));
        }

        /// <summary>
        /// "직전 판" 머리글 클릭: 가운데 열을 최고 기록(Best)과 번갈아 보여준다.
        /// 지금 보고 있는 표를 그대로(작게/크게 모드, 진행 중인 판까지) 다시 그린다.
        /// </summary>
        public void TogglePreviousColumn()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _window;
                if (window == null || !window.IsLoaded || _lastViewArgs == null)
                    return;
                _showBest = !_showBest;
                var (def, difficulty, status, cur, max, inProgress, highlight) = _lastViewArgs.Value;
                window.RefreshView(BuildView(def, difficulty, status, cur, max, inProgress, highlight));
            }));
        }

        /// <summary>창 왼쪽 목록 클릭: 그 묶음으로 바로 간다. 자동 닫힘은 멈춘다.</summary>
        public void ShowGroup(string groupKey)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _window;
                if (window == null || !window.IsLoaded)
                    return;
                ShowGroupOn(window, groupKey, "기록");
            }));
        }

        /// <summary>묶음의 기록을 창에 그린다. UI 스레드에서 부른다.</summary>
        private void ShowGroupOn(ContentTimerWindow window, string groupKey, string status, bool compact = false)
        {
            DungeonDefinition def = RecordDefinitions.FirstOrDefault(d => d.GroupKey == groupKey) ?? RecordDefinitions[0];
            lock (SyncRoot)
                _lastGroupKey = def.GroupKey;
            string difficulty = GetLastDifficulty(def);
            var (cur, max) = ReadProgressOrNull(def);
            window.ShowIdle(BuildView(def, difficulty, status, cur, max), compact);
        }

        /// <summary>디버그 상시 표시면 대기 상태로, 아니면 닫는다. UI 스레드에서 부른다.</summary>
        private void HideOrIdle()
        {
            var window = _window;
            if (window == null || !window.IsLoaded)
                return;
            if (window.KeepOpen)
            {
                ShowIdle();
                return;
            }
            try { window.Close(); } catch { }
        }

        public void Close()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try { _window?.Close(); } catch { }
                finally { _window = null; }
            }));
        }

        private ContentTimerWindow EnsureWindow(ChatSettings? settings)
        {
            if (_window != null && _window.IsLoaded)
                return _window;

            var window = new ContentTimerWindow(settings);
            _window = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                    _window = null;
                try { WindowVisibilityChanged?.Invoke(false); } catch { }
            };

            if (settings != null)
                ApplyStoredPosition(window, settings);

            try { WindowVisibilityChanged?.Invoke(true); } catch { }
            return window;
        }

        /// <summary>타이머 창이 떠 있으면 설정에 저장된 위치로 옮긴다. 설정 전체 교체(프로필 불러오기) 뒤에 쓴다.</summary>
        public void ApplyStoredBounds(ChatSettings settings)
        {
            if (settings == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _window;
                if (window == null || !window.IsLoaded)
                    return;
                ApplyStoredPosition(window, settings);
            }));
        }

        // 창 크기는 글자 크기 설정에 맞춰 내용대로 잡히므로(SizeToContent) 위치만 적용한다
        private void ApplyStoredPosition(ContentTimerWindow window, ChatSettings settings)
        {
            if (settings.ContentTimerWindowLeft.HasValue && settings.ContentTimerWindowTop.HasValue)
                window.WindowStartupLocation = WindowStartupLocation.Manual;
            WindowPlacement.ApplyStored(window, settings.ContentTimerWindowLeft, settings.ContentTimerWindowTop);
        }

        private ChatSettings? GetSharedSettings()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow main && main.DataContext is ChatSettings settings)
                        return settings;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to resolve shared settings for dungeon timer.", ex);
            }

            return null;
        }
    }
}
