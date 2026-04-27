using System;

namespace TWChatOverlay.Models
{
    /// <summary>
    /// 던전 숙제 항목별 사용자 설정 (활성화 여부 및 필요 횟수)
    /// </summary>
    public class DungeonItemConfig
    {
        [System.Text.Json.Serialization.JsonPropertyOrder(1)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 완료로 인정할 총 횟수. 0 이면 항목 기본값 사용.
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyOrder(2)]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
        public int RequiredCount { get; set; } = 0;

        [System.Text.Json.Serialization.JsonIgnore]
        [System.Text.Json.Serialization.JsonPropertyOrder(3)]
        public int CurrentCount { get; set; } = 0;
        [System.Text.Json.Serialization.JsonIgnore]
        [System.Text.Json.Serialization.JsonPropertyOrder(4)]
        public bool IsCleared { get; set; } = false;
        [System.Text.Json.Serialization.JsonIgnore]
        [System.Text.Json.Serialization.JsonPropertyOrder(5)]
        public DateTime SavedAt { get; set; } = DateTime.MinValue;
    }
}
