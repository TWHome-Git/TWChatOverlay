namespace TWChatOverlay.Models
{
    public partial class ChatSettings
    {
        public void UpdateColor(string tag, string hex)
        {
            switch (tag)
            {
                case "Normal": NormalColor = hex; break;
                case "System": SystemColor = hex; break;
                case "Team": TeamColor = hex; break;
                case "Club": ClubColor = hex; break;
                case "Shout": ShoutColor = hex; break;
                case "EtaCharacter": EtaCharacterColor = hex; break;
                case "Timestamp": TimestampColor = hex; break;
                case "IdTag": IdTagColor = hex; break;
                case "SenderId": SenderIdColor = hex; break;
                case "EtaLevelRange1": EtaLevelRange1Color = hex; break;
                case "EtaLevelRange2": EtaLevelRange2Color = hex; break;
                case "EtaLevelRange3": EtaLevelRange3Color = hex; break;
                case "EtaLevelRange4": EtaLevelRange4Color = hex; break;
                case "EtaLevelRange5": EtaLevelRange5Color = hex; break;
            }
        }

        public BossAlertConfig GetOrCreateBossAlertConfig(string bossId)
        {
            if (string.IsNullOrWhiteSpace(bossId))
            {
                return new BossAlertConfig();
            }

            if (!BossAlertConfigs.TryGetValue(bossId, out BossAlertConfig? config))
            {
                config = new BossAlertConfig();
                BossAlertConfigs[bossId] = config;
            }

            return config;
        }
    }
}
