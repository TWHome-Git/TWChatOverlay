using System;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    internal static class ExperienceWeeklyRefreshService
    {
        public static void MarkCurrentWeekRefreshed(ChatSettings settings, DateTime nowLocal)
        {
            if (settings == null)
                return;

            string weekKey = LogTextClassifier.GetIsoWeekKey(nowLocal.Date);
            settings.ExperienceLimitLastRefreshWeekKey = weekKey;
            settings.ExperienceLimitWeeklyPromptShownWeekKey = weekKey;
            ConfigService.SaveDeferred(settings);
        }
    }
}
