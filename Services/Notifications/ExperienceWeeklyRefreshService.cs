using System;
using System.Globalization;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    internal static class ExperienceWeeklyRefreshService
    {
        public static void MarkCurrentWeekRefreshed(ChatSettings settings, DateTime nowLocal)
        {
            if (settings == null)
                return;

            string weekKey = GetIsoWeekKey(nowLocal.Date);
            settings.ExperienceLimitLastRefreshWeekKey = weekKey;
            settings.ExperienceLimitWeeklyPromptShownWeekKey = weekKey;
            ConfigService.SaveDeferred(settings);
        }

        private static string GetIsoWeekKey(DateTime date)
        {
            int isoYear = ISOWeek.GetYear(date);
            int isoWeek = ISOWeek.GetWeekOfYear(date);
            return $"{isoYear}-W{isoWeek:00}";
        }
    }
}
