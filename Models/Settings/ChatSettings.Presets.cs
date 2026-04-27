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
            }
        }

        public void SavePreset(int presetNumber, double left, double top, double? lineMarginLeft = null, double? lineMargin = null)
        {
            double savedLineMarginLeft = lineMarginLeft ?? LineMarginLeft;
            double savedLineMargin = lineMargin ?? LineMargin;

            switch (presetNumber)
            {
                case 1:
                    Preset1 = new WindowPositionPreset($"프리셋 1 (X: {savedLineMarginLeft:F0}, Y: {savedLineMargin:F0})", left, top, savedLineMarginLeft, savedLineMargin);
                    break;
                case 2:
                    Preset2 = new WindowPositionPreset($"프리셋 2 (X: {savedLineMarginLeft:F0}, Y: {savedLineMargin:F0})", left, top, savedLineMarginLeft, savedLineMargin);
                    break;
                case 3:
                    Preset3 = new WindowPositionPreset($"프리셋 3 (X: {savedLineMarginLeft:F0}, Y: {savedLineMargin:F0})", left, top, savedLineMarginLeft, savedLineMargin);
                    break;
            }
        }

        public WindowPositionPreset? GetPreset(int presetNumber)
        {
            return presetNumber switch
            {
                1 => Preset1,
                2 => Preset2,
                3 => Preset3,
                _ => null
            };
        }

        public WindowPositionPreset? GetLastSelectedPreset()
        {
            return GetPreset(LastSelectedPresetNumber);
        }

        public void UpdatePositionDisplay(double xOffset, double yOffset)
        {
            CurrentPositionDisplay = $"X: {xOffset:F0}, Y: {yOffset:F0}";
            OnPropertyChanged(nameof(CurrentPositionDisplay));
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
