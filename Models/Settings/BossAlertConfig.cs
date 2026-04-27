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
    }
}
