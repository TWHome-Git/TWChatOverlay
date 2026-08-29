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

        /// <summary>동기화·색 버튼 공통 설명 (색상 지정 가능한 표기 항목 뒤에 붙인다)</summary>
        private const string ColorSyncGuide =
            "\n\n■ 색상 동기화와 색 지정\n" +
            "'동기화'가 민트색이면 켜진 상태로, 이 표기는 줄(채팅 종류)의 색을 그대로 따릅니다.\n" +
            "'동기화'를 눌러 회색(꺼짐)으로 바꾸면 옆의 색 버튼이 활성화되고,\n" +
            "색 버튼을 눌러 원하는 색을 고르면 이 표기에만 그 색이 적용됩니다.";

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
                "채팅 줄에서 보낸 사람 아이디 부분의 색을 지정합니다." + ColorSyncGuide,
                new[] { "chat_id_sender_sync_on.png", "chat_id_sender_sync_off.png" }),

            ["chat.id.eta_level"] = (
                "에타 레벨 표시",
                "말한 사람의 에타 레벨을 아이디 뒤에 [레벨]로 붙여 보여줍니다.\n" +
                "레벨 정보는 TW DB 에타 랭킹에서 가져오며, 랭킹에 없는 아이디는 표시되지 않습니다.\n" +
                "레벨 정보는 하루 단위로 갱신됩니다." + ColorSyncGuide,
                new[] { "chat_id_eta_level_off.png", "chat_id_eta_level_on.png" }),

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
                "각 채팅 줄 앞에 [일반]/[팀]/[클럽]/[시스템] 종류를 붙여 어떤 채팅인지 한눈에 구분합니다.\n" +
                "외치기는 원문에 이미 '외치기 :'가 있어 말머리를 붙이지 않습니다.",
                new[] { "chat_category_prefix_off.png", "chat_category_prefix_on.png" }),

            // ===== 던전 도우미 =====

            ["dungeon.recapture_map"] = (
                "보급품 탈환 미니 지도",
                "보급품 탈환에 진입하면 보급품 위치가 표시된 미니 지도 창을 자동으로 띄웁니다.\n" +
                "던전에서 나가면 창도 함께 닫힙니다.",
                null),
        };
    }
}
