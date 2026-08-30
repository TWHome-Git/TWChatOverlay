using System.Collections.Generic;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 설정 화면 [?] 버튼의 도움말 내용 저장소.
    /// 항목을 채우려면 아래 Topics 딕셔너리에 키별로 (제목, 본문, 이미지)를 적으면 된다.
    /// 본문은 여러 줄 가능("\n" 사용). 이미지는 Data/images/Help/ 아래 파일명 (없으면 null).
    /// 이미지는 `TWChatOverlay.exe --render-help-shots 폴더` 로 다시 생성할 수 있다.
    /// </summary>
    public static class HelpTopics
    {
        private const string PlaceholderBody = "설명이 준비 중입니다.";

        /// <summary>동기화·색 버튼 공통 안내 (사용법 그림은 '아이디' [?] 도움말에 있다)</summary>
        private const string ColorSyncGuide =
            "\n\n'동기화'를 끄면 색 버튼으로 색을 따로 지정할 수 있습니다.\n" +
            "자세한 방법은 [채팅 표시 > 아이디]의 [?] 도움말 그림을 참고하세요.";

        public static (string Title, string Body, string[]? Frames) Get(string key)
        {
            if (!string.IsNullOrWhiteSpace(key) && Topics.TryGetValue(key, out var topic))
                return topic;

            return ("도움말", PlaceholderBody, null);
        }

        private static readonly Dictionary<string, (string Title, string Body, string[]? Frames)> Topics = new()
        {
            // ===== 채팅 =====

            ["chat.filter"] = (
                "기본 채팅 필터",
                "게임 채팅을 종류별로 켜고 끕니다. 끈 종류는 채팅창에 표시되지 않습니다.\n" +
                "각 종류 왼쪽의 색 버튼으로 해당 채팅의 글자 색을 바꿀 수 있습니다.",
                new[] { "chat_filter.png" }),

            ["chat.id.sender"] = (
                "아이디 색상",
                "채팅 줄에서 보낸 사람 아이디 부분의 색을 지정합니다.\n" +
                "그림처럼 '동기화'가 민트색(켜짐)이면 줄 색을 그대로 따르고,\n" +
                "'동기화'를 눌러 끄면 색 버튼이 활성화되어 원하는 색을 고를 수 있습니다.\n" +
                "다른 표기(에타 레벨/캐릭터/태그/타임 스탬프)의 동기화·색 버튼도 같은 방식입니다.",
                new[] { "chat_id_sender_sync_on.png", "chat_id_sender_sync_off.png" }),

            ["chat.id.eta_level"] = (
                "에타 레벨 표시",
                "말한 사람의 에타 레벨을 아이디 뒤에 [레벨]로 붙여 보여줍니다.\n" +
                "레벨 정보는 TW DB 에타 랭킹에서 가져오며, 하루 단위로 갱신됩니다.\n" +
                "랭킹에 없거나 에타 정보가 없는(레벨 0) 아이디는 표시하지 않습니다.\n" +
                "아래 '레벨별 색상'을 켜면 레벨 구간에 따라 색을 다르게 칠할 수 있습니다." + ColorSyncGuide,
                new[] { "chat_id_eta_level_off.png", "chat_id_eta_level_on.png" }),

            ["chat.id.eta_range"] = (
                "레벨별 색상",
                "에타 레벨 구간에 따라 [레벨] 표기 색을 다르게 칠합니다.\n" +
                "구간: 1~20 / 21~40 / 41~60 / 61~80 / 81 이상 — 각 구간의 색 버튼으로 바꿀 수 있습니다.\n" +
                "켜면 '동기화'나 개별 색 설정보다 우선 적용됩니다.\n" +
                "에타 정보가 없는(레벨 0) 아이디는 레벨 자체가 표시되지 않습니다.",
                new[] { "chat_id_eta_range_off.png", "chat_id_eta_range_on.png" }),

            ["chat.id.character"] = (
                "캐릭터 표시",
                "말한 사람의 캐릭터(직업) 이름을 아이디 뒤에 [캐릭터]로 붙여 보여줍니다.\n" +
                "에타 레벨과 함께 쓰면 [레벨][캐릭터] 순서로 표시됩니다.\n" +
                "캐릭터 정보가 없는 아이디는 표시되지 않습니다." + ColorSyncGuide,
                new[] { "chat_id_character_off.png", "chat_id_character_on.png" }),

            ["chat.id.id_tag"] = (
                "아이디 태그",
                "idtag.txt에 적어 둔 메모를 해당 아이디 뒤에 [태그]로 보여줍니다.\n" +
                "[편집]을 눌러 메모장을 열고 \"아이디=태그\" 형식으로 한 줄씩 적으면 됩니다.\n" +
                "예) 모비딕=상인  →  채팅에서 모비딕[상인]으로 표시\n" +
                "태그를 지우려면 idtag.txt에서 해당 줄을 삭제하면 됩니다." + ColorSyncGuide,
                new[] { "chat_id_tag_off.png", "chat_id_tag_on.png" }),

            ["chat.id.club_boss"] = (
                "클럽 보스 메시지",
                "클럽 보스 관련 공지(생성/참가/퇴장/문 닫힘 등)를 채팅창에 표시할지 정합니다.\n" +
                "끄면 해당 공지가 채팅창에서 숨겨집니다.",
                new[] { "chat_id_club_boss_off.png", "chat_id_club_boss_on.png" }),

            ["chat.id.timestamp"] = (
                "타임 스탬프",
                "각 채팅 줄 앞에 게임 로그의 시각([N시 N분 N초])을 표시합니다.\n" +
                "끄면 시각 없이 내용만 표시됩니다." + ColorSyncGuide,
                new[] { "chat_id_timestamp_off.png", "chat_id_timestamp_on.png" }),

            ["chat.font"] = (
                "폰트",
                "채팅창 글꼴과 크기를 바꿉니다. 모든 채팅 창(메인/서브)에 함께 적용됩니다.\n" +
                "'사용자 설정'을 고르면 프로그램 폴더의 UserDefine.ttf(.otf/.ttc) 파일을 사용합니다.\n" +
                "줄 간격·왼쪽 여백도 이 탭에서 조절할 수 있습니다.",
                new[] { "chat_font_13.png", "chat_font_17.png" }),

            ["chat.clone1"] = (
                "서브 채팅창 1",
                "메인 채팅창과 별도로 띄우는 추가 채팅창입니다. 최대 2개까지 열 수 있습니다.\n" +
                "탭을 눌러 표시할 채팅 종류(기본/일반/팀/클럽/외치기/시스템/아이템)를 고를 수 있어\n" +
                "예를 들어 메인은 대화, 서브는 외치기 전용으로 나눠 쓰기 좋습니다.\n" +
                "위치·크기·폰트는 창마다 따로 저장됩니다.",
                new[] { "chat_clone_basic.png", "chat_clone_shout.png" }),

            ["chat.clone2"] = (
                "서브 채팅창 2",
                "메인 채팅창과 별도로 띄우는 추가 채팅창입니다. 최대 2개까지 열 수 있습니다.\n" +
                "탭을 눌러 표시할 채팅 종류(기본/일반/팀/클럽/외치기/시스템/아이템)를 고를 수 있어\n" +
                "예를 들어 메인은 대화, 서브는 외치기 전용으로 나눠 쓰기 좋습니다.\n" +
                "위치·크기·폰트는 창마다 따로 저장됩니다.",
                new[] { "chat_clone_basic.png", "chat_clone_shout.png" }),

            ["chat.filter.category_prefix"] = (
                "종류 말머리",
                "각 채팅 줄 앞에 [일반]/[팀]/[클럽]/[외치기]/[시스템] 종류를 붙여 한눈에 구분합니다.\n" +
                "외치기는 \"외치기 : 내용 [보낸이]\" 원문을 \"[외치기] 보낸이 : 내용\" 형태로 정리해 보여줍니다.",
                new[] { "chat_category_prefix_off.png", "chat_category_prefix_on.png" }),

            // ===== 외치기 =====

            ["shout.toast"] = (
                "외치기 팝업",
                "외치기가 올라오면 화면에 토스트 알림으로 띄워 줍니다.\n" +
                "'유지 시간(초)'으로 토스트가 떠 있는 시간을 조절합니다.\n" +
                "트레이 최소화 중에는 토스트가 뜨지 않으며,\n" +
                "놓친 외치기는 메뉴의 외치기 다시 보기에서 확인할 수 있습니다.",
                new[] { "shout_toast_off.png", "shout_toast_on.png" }),

            ["shout.autocopy"] = (
                "닉네임 자동복사",
                "외치기 끝의 [닉네임]을 자동으로 클립보드에 복사합니다.\n" +
                "외치기를 보고 바로 Ctrl+V로 1:1 대화나 팀 초대에 붙여넣기 좋습니다.",
                new[] { "shout_autocopy_off.png", "shout_autocopy_on.png" }),

            ["shout.toast_font"] = (
                "토스트 글자 크기",
                "외치기 토스트 팝업의 글자 크기를 조절합니다 (10~40).",
                new[] { "shout_toast_font_13.png", "shout_toast_font_20.png" }),

            // ===== 화면 =====

            ["display.opacity"] = (
                "투명도",
                "오버레이 창 배경의 투명도를 조절합니다 (20~100%).\n" +
                "배경만 투명해지고 글자는 항상 선명하게 유지됩니다.",
                new[] { "display_opacity_100.png", "display_opacity_50.png" }),

            ["display.unlock"] = (
                "잠금 해제 모드",
                "창을 옮기거나 크기를 바꿀 수 있는 편집 모드입니다.\n" +
                "각 창을 드래그해 이동하고, 가로·세로 크기와 X·Y 좌표를 직접 입력할 수도 있습니다.\n" +
                "이동 중에는 좌표·크기 안내 창이 최상단에 표시됩니다.\n" +
                "다시 누르면 잠금 상태로 돌아갑니다.",
                new[] { "display_unlock_off.png", "display_unlock_on.png" }),

            ["display.menu_horizontal"] = (
                "메뉴 바 가로형",
                "메뉴 아이콘 바를 가로형으로 바꿉니다. 끄면 세로형(기본)입니다.",
                new[] { "display_menu_h_off.png", "display_menu_h_on.png" }),

            ["display.always_on_top"] = (
                "항상 위",
                "켜면 다른 앱이 앞으로 와도 오버레이 창들을 계속 맨 위로 유지합니다.\n" +
                "끄면 z-순서를 건드리지 않아 처음 실행 상태 그대로 OS에 맡깁니다.\n" +
                "메뉴 바는 이 설정과 무관하게 항상 맨 위에 유지됩니다.",
                new[] { "display_ontop_off.png", "display_ontop_on.png" }),

            // ===== 키워드 알림 =====

            ["keyword.color"] = (
                "색상 강조",
                "등록한 키워드가 포함된 채팅 줄을 지정한 색으로 강조합니다.\n" +
                "키워드는 '키워드' 탭에서 @단어1 @단어2 형식으로 등록합니다.",
                new[] { "keyword_color_off.png", "keyword_color_on.png" }),

            // ===== 경험치 추적 =====

            ["exp.tracker"] = (
                "경험치 추적",
                "사냥 중 획득 경험치를 실시간으로 추적하는 창을 띄웁니다.\n" +
                "누적 경험치 / 1시간 예상 / 획득 경험치 / 처치 수를 보여주며\n" +
                "[중지]와 [리셋] 버튼으로 측정을 제어합니다.",
                new[] { "exp_tracker_off.png", "exp_tracker_on.png" }),

            ["exp.cumulative_alert"] = (
                "경험치 누적 알림",
                "획득 경험치를 계속 누적해 알림 창으로 보여줍니다.\n" +
                "아래 '현재 누적 경험치(억)'에 현재 값을 입력해 두면 그 값부터 이어서 계산합니다.\n" +
                "알림 창의 [설정] 버튼을 누르면 설정 화면에 들어가지 않고도\n" +
                "누적 알림 ON/OFF와 저장된 누적 경험치를 바로 고칠 수 있습니다.",
                new[] { "exp_cum_off.png", "exp_cum_on.png" }),

            ["exp.current_total"] = (
                "현재 누적 경험치(억)",
                "누적 경험치 계산의 시작값을 억 단위로 입력하고 [적용]을 누르세요.\n" +
                "게임에서 확인한 현재 누적 경험치와 맞춰 두면 이후 획득분이 더해집니다.",
                null),

            ["exp.loweff"] = (
                "저효율 알림",
                "경험치 획득 효율이 기준치보다 낮아지면 알림을 띄웁니다.\n" +
                "버프가 꺼졌거나 사냥 효율이 떨어졌을 때 빨리 알아차릴 수 있습니다.",
                new[] { "exp_loweff_off.png", "exp_loweff_on.png" }),

            ["exp.loweff_threshold"] = (
                "기준치(만 EXP 미만)",
                "이 값(만 단위 경험치) 미만으로 획득하면 저효율로 판단해 알림을 띄웁니다.",
                null),

            ["exp.loweff_font"] = (
                "누적 알림 폰트 크기",
                "알림 창의 글자 크기를 조절합니다.",
                null),

            // ===== 던전 도우미 =====

            ["dungeon.wave_end"] = (
                "웨이브 종료 알림",
                "룬 경험치·테시스 코어에서 웨이브가 끝나면 알림을 띄웁니다.",
                new[] { "dungeon_wave_off.png", "dungeon_wave_on.png" }),

            ["dungeon.abyss_reflect"] = (
                "반사 패턴 알림",
                "어비스에서 반사 패턴이 시작되면 알림을 띄워\n" +
                "공격을 멈출 타이밍을 알려줍니다.",
                new[] { "dungeon_reflect_off.png", "dungeon_reflect_on.png" }),

            ["dungeon.etos_direction"] = (
                "에토스 방향 알림",
                "이클립스에서 에토스가 나타날 방향을 그림으로 알려줍니다.\n" +
                "아래는 북동(NE) 방향 안내 예시입니다.",
                new[] { "dungeon_etos_ne.jpg" }),

            ["dungeon.recapture_map"] = (
                "보급품 탈환 미니 지도",
                "보급품 탈환에 진입하면 아래처럼 보급품 위치(별 표시)가 그려진\n" +
                "미니 지도 창을 자동으로 띄웁니다. 던전에서 나가면 창도 함께 닫힙니다.",
                new[] { "dungeon_recapture_map.png" }),

            ["dungeon.abandon_count"] = (
                "어밴던로드 입장 횟수",
                "어밴던로드 입장을 감지해 입장 횟수를 세고 알림 창으로 보여줍니다.\n" +
                "알림 창 폰트 크기와 지속 시간은 아래에서 조절합니다.",
                new[] { "dungeon_abandon_count_off.png", "dungeon_abandon_count_on.png" }),

            ["dungeon.abandon_gold"] = (
                "어밴던로드 통계",
                "어밴던로드 상황판 창을 보여줍니다.\n" +
                "주간 합계 금액과 마정석(하급~최상급) 획득 내역을 한눈에 볼 수 있습니다.",
                new[] { "dungeon_abandon_gold_off.png", "dungeon_abandon_gold_on.png" }),

            ["dungeon.craving_count"] = (
                "갈망하는 즐거움 입장 횟수",
                "갈망하는 즐거움 입장을 감지해 입장 횟수를 알림 창으로 보여줍니다.",
                new[] { "dungeon_craving_count_off.png", "dungeon_craving_count_on.png" }),

            // ===== 아이템 알림 =====

            ["item.drop_alert"] = (
                "아이템 획득 알림",
                "채팅 로그에서 아이템 획득을 감지해 알림을 띄웁니다.\n" +
                "'알림 필터' 탭에서 알림 받을 아이템을 고를 수 있습니다.",
                new[] { "item_drop_off.png", "item_drop_on.png" }),

            ["item.filter"] = (
                "알림 필터",
                "알림 받을 아이템 목록을 관리합니다.\n" +
                "'기본' 목록은 자동으로 업데이트되는 전체 목록이고,\n" +
                "'사용자 정의'를 켜면 기본 목록에서 원하는 항목만 옮겨 담아\n" +
                "그 목록에 있는 아이템만 알림을 받습니다.",
                null),

            // ===== 버프 추적 =====

            ["buff.tracker"] = (
                "버프 추적",
                "채팅 로그에서 버프 사용을 감지해 남은 시간을 추적하는 창을 띄웁니다.\n" +
                "버프가 곧 끝나면 종료 사운드로 알려주며, 볼륨은 아래에서 조절합니다.",
                new[] { "buff_tracker_off.png", "buff_tracker_on.png" }),

            // ===== 필드 보스 =====

            ["boss.alert"] = (
                "필드 보스 알림",
                "필드 보스 등장 시간표에 맞춰 3분 전 / 1분 전 / 5초 전에 알림을 띄웁니다.\n" +
                "보스별로 받을 알림을 따로 켜고 끌 수 있고, 알림 볼륨도 조절할 수 있습니다.",
                new[] { "boss_alert_3min.png", "boss_alert_spawn.png" }),

            // ===== 시스템 =====

            ["system.log_folder"] = (
                "채팅 로그 폴더",
                "테일즈위버 채팅 로그(ChatLog) 폴더 경로입니다.\n" +
                "게임 설치 위치가 다르면 직접 경로를 입력해 주세요.\n" +
                "이 폴더의 로그 파일을 읽어 채팅창을 채웁니다.",
                null),

            ["system.debug_log"] = (
                "Debug.log 활성화",
                "문제 진단용 상세 로그를 프로그램 폴더의 Debug.log 파일에 기록합니다.\n" +
                "오류 제보 시에만 켜고, 평소에는 꺼 두는 것을 권장합니다.",
                null),

            ["system.manual_update"] = (
                "수동 업데이트",
                "프로그램이 쓰는 원격 데이터(아이템 목록 등)를 지금 바로 새로 내려받습니다.\n" +
                "보통은 자동으로 갱신되므로 데이터가 오래되어 보일 때만 사용하면 됩니다.",
                null),

            ["system.log_reload"] = (
                "로그 다시 읽기",
                "채팅 로그 파일을 처음부터 다시 읽어 채팅창을 다시 채웁니다.\n" +
                "채팅 표시가 꼬였거나 누락이 의심될 때 사용하세요.",
                null),

            ["system.reset"] = (
                "설정 초기화",
                "모든 설정을 기본값으로 되돌립니다.\n" +
                "되돌릴 수 없으니 주의해서 사용하세요.",
                null),
        };
    }
}
