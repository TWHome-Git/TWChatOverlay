using System;
using System.Globalization;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 주간 누적 경험치 수동 갱신 상태를 관리합니다.
    /// </summary>
    internal static class ExperienceWeeklyRefreshService
    {
        public static bool ShouldPromptOnMondayFirstLogin(ChatSettings settings, DateTime nowLocal)
        {
            if (settings == null)
                return false;

            if (nowLocal.DayOfWeek != DayOfWeek.Monday)
                return false;

            string weekKey = GetIsoWeekKey(nowLocal.Date);
            if (string.Equals(settings.ExperienceLimitLastRefreshWeekKey, weekKey, StringComparison.Ordinal))
                return false;

            return !string.Equals(settings.ExperienceLimitWeeklyPromptShownWeekKey, weekKey, StringComparison.Ordinal);
        }

        public static void MarkPromptShownForCurrentWeek(ChatSettings settings, DateTime nowLocal)
        {
            if (settings == null)
                return;

            settings.ExperienceLimitWeeklyPromptShownWeekKey = GetIsoWeekKey(nowLocal.Date);
            ConfigService.SaveDeferred(settings);
        }

        public static void MarkCurrentWeekRefreshed(ChatSettings settings, DateTime nowLocal)
        {
            if (settings == null)
                return;

            string weekKey = GetIsoWeekKey(nowLocal.Date);
            settings.ExperienceLimitLastRefreshWeekKey = weekKey;
            settings.ExperienceLimitWeeklyPromptShownWeekKey = weekKey;
            ConfigService.SaveDeferred(settings);
        }

        public static void ApplyRefreshedExperience(ChatSettings settings, long experienceValue, DateTime nowLocal)
        {
            if (settings == null)
                return;

            long normalized = Math.Max(0, experienceValue);
            settings.ExperienceLimitTotalExp = normalized;
            settings.ExperienceLimitStateInitialized = true;
            MarkCurrentWeekRefreshed(settings, nowLocal);

            _ = ExperienceAlertWindowService.ApplyStateSnapshot(new ExperienceAlertStateSnapshot
            {
                TotalExp = normalized
            });
        }

        private static string GetIsoWeekKey(DateTime date)
        {
            int isoYear = ISOWeek.GetYear(date);
            int isoWeek = ISOWeek.GetWeekOfYear(date);
            return $"{isoYear}-W{isoWeek:00}";
        }
    }
}
