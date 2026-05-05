using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace TWChatOverlay.Services
{
    public static class IgnoredChatMessageService
    {
        private static readonly HashSet<string> NormalIgnoredContains = new(StringComparer.Ordinal)
        {
            "금화 주머니를 획득 했습니다.",
            "지역 보상상자를 열었습니다.",
            "이공간 보물상자를 열었습니다.",
            "포탈 전용 상자를 열었습니다.",
            "흉포한 라이코스",
            "메달의 사제, 티로로스",
            "서클릿의 사제, 마티아",
            "소매의 사제, 체리아",
            "선봉대장, 로카고스",
            "경보 장치",
            "신조",
            "붉은 프토마",
            "회색 프토마",
            "녹색 프토마",
            "푸른 프토마",
            "크라모르",
            "검의 사제, 셀리니아코스",
            "궤의 사제, 프로에드로스",
            "지팡이의 사제, 고이티아",
            "데스포이나",
            "키시니크",
            "Happy Birthday"
        };

        // [클럽 보스] only: controlled by ShowClubBoss checkbox in settings.
        private static readonly HashSet<string> ClubIgnoredContains = new(StringComparer.Ordinal)
        {
            "[클럽 보스]"
        };

        private static readonly Regex[] NormalIgnoredRegexes =
        {
            new(@"^(?:\[[^\]]+\]\s*)?(SP|MP|Fever|HP)가\s*\d+%\s*회복되었습니다\.?$", RegexOptions.Compiled),
            new(@"^(?:\[[^\]]+\]\s*)?체력이\s*\d+%\s*회복되었습니다\.?$", RegexOptions.Compiled)
        };

        public static bool IsIgnoredNormalMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            foreach (var token in NormalIgnoredContains)
            {
                if (message.Contains(token, StringComparison.Ordinal))
                    return true;
            }

            foreach (var regex in NormalIgnoredRegexes)
            {
                if (regex.IsMatch(message))
                    return true;
            }

            return false;
        }

        public static bool IsIgnoredClubMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            foreach (var token in ClubIgnoredContains)
            {
                if (message.Contains(token, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        // Kept for compatibility with existing startup call path.
        public static System.Threading.Tasks.Task EnsureLoadedAsync(bool forceRefresh = false)
            => System.Threading.Tasks.Task.CompletedTask;
    }
}
