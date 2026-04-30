using System;
using System.Net;
using System.Text.RegularExpressions;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    public static class CharacterProfileLogRouter
    {
        private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhiteSpaceRegex = new(@"\s+", RegexOptions.Compiled);

        public static int GetNextProfileSlot(int currentProfileSlot, string? rawLog, ChatSettings? settings)
        {
            int safeCurrent = currentProfileSlot is 1 or 2 ? currentProfileSlot : 1;
            if (settings == null || string.IsNullOrWhiteSpace(rawLog))
                return safeCurrent;

            string text = Normalize(rawLog);
            if (string.IsNullOrWhiteSpace(text))
                return safeCurrent;

            string profile1Pattern = Normalize(settings.Profile1SwitchLog);
            string profile2Pattern = Normalize(settings.Profile2SwitchLog);

            int idx1 = string.IsNullOrWhiteSpace(profile1Pattern) ? -1 : text.IndexOf(profile1Pattern, StringComparison.Ordinal);
            int idx2 = string.IsNullOrWhiteSpace(profile2Pattern) ? -1 : text.IndexOf(profile2Pattern, StringComparison.Ordinal);

            if (idx1 < 0 && idx2 < 0)
                return safeCurrent;

            if (idx1 >= 0 && idx2 >= 0)
                return idx1 <= idx2 ? 1 : 2;

            return idx1 >= 0 ? 1 : 2;
        }

        public static string Normalize(string? rawLog)
        {
            if (string.IsNullOrWhiteSpace(rawLog))
                return string.Empty;

            string decoded = WebUtility.HtmlDecode(rawLog).Replace("&nbsp", " ");
            decoded = HtmlTagRegex.Replace(decoded, " ");
            decoded = WhiteSpaceRegex.Replace(decoded, string.Empty);
            return decoded.Trim();
        }
    }
}
