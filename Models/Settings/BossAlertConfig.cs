using System.Text.Json.Serialization;

namespace TWChatOverlay.Models
{
    public class BossAlertConfig
    {
        [JsonPropertyOrder(1)]
        public bool Alert3MinutesBefore { get; set; }

        [JsonPropertyOrder(2)]
        public bool Alert1MinuteBefore { get; set; }

        [JsonPropertyOrder(3)]
        public bool AlertAtSpawn { get; set; }

        /// <summary>등장 후 입장 가능 시간 카운트다운 (JSON에 entryMinutes가 있는 보스). null이면 켜짐.</summary>
        [JsonPropertyOrder(4)]
        public bool? EntryCountdown { get; set; }
    }
}
