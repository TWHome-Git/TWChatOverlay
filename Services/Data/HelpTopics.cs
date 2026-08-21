using System.Collections.Generic;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 설정 화면 [?] 버튼의 도움말 내용 저장소.
    /// 항목을 채우려면 아래 Topics 딕셔너리에 키별로 (제목, 본문)을 적으면 된다.
    /// 본문은 여러 줄 가능("\n" 또는 raw string 사용).
    /// </summary>
    public static class HelpTopics
    {
        private const string PlaceholderBody = "설명이 준비 중입니다.";

        public static (string Title, string Body) Get(string key)
        {
            if (!string.IsNullOrWhiteSpace(key) && Topics.TryGetValue(key, out var topic))
                return topic;

            return ("도움말", PlaceholderBody);
        }

        // ===== 채팅 =====
        private static readonly Dictionary<string, (string Title, string Body)> Topics = new()
        {
            // 기본 채팅 필터
            ["chat.filter.normal"] = ("일반 채팅", PlaceholderBody),
            ["chat.filter.team"] = ("팀 채팅", PlaceholderBody),
            ["chat.filter.club"] = ("클럽 채팅", PlaceholderBody),
            ["chat.filter.shout"] = ("외치기", PlaceholderBody),
            ["chat.filter.system"] = ("시스템 메시지", PlaceholderBody),

            // 아이디 표시
            ["chat.id.eta_level"] = ("에타 레벨 표시", PlaceholderBody),
            ["chat.id.character"] = ("캐릭터 표시", PlaceholderBody),
            ["chat.id.id_tag"] = ("아이디 태그", PlaceholderBody),
            ["chat.id.club_boss"] = ("클럽 보스 메시지", PlaceholderBody),
            ["chat.id.timestamp"] = ("타임 스탬프", PlaceholderBody),

            // 폰트
            ["chat.font.family"] = ("폰트 종류", PlaceholderBody),
            ["chat.font.size"] = ("폰트 크기", PlaceholderBody),

            // 서브 채팅창 1
            ["chat.clone1.enabled"] = ("서브 채팅창 1 사용", PlaceholderBody),
            ["chat.clone1.follow"] = ("메인 폰트 따라가기", PlaceholderBody),
            ["chat.clone1.font"] = ("서브 채팅창 1 폰트", PlaceholderBody),
            ["chat.clone1.size"] = ("서브 채팅창 1 크기", PlaceholderBody),

            // 서브 채팅창 2
            ["chat.clone2.enabled"] = ("서브 채팅창 2 사용", PlaceholderBody),
            ["chat.clone2.follow"] = ("메인 폰트 따라가기", PlaceholderBody),
            ["chat.clone2.font"] = ("서브 채팅창 2 폰트", PlaceholderBody),
            ["chat.clone2.size"] = ("서브 채팅창 2 크기", PlaceholderBody),
        };
    }
}
