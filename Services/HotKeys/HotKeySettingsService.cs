using System;
using System.Collections.Generic;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 설정 화면의 단축키 값 충돌과 초기화를 관리합니다.
    /// </summary>
    public static class HotKeySettingsService
    {
        public static IReadOnlyList<string> PropertyNames { get; } = new[]
        {
            nameof(ChatSettings.ExitHotKey),
            nameof(ChatSettings.ToggleOverlayHotKey),
            nameof(ChatSettings.ToggleAddonHotKey),
            nameof(ChatSettings.ToggleAlwaysVisibleHotKey),
            nameof(ChatSettings.ToggleDailyWeeklyContentHotKey),
            nameof(ChatSettings.ToggleEtaRankingHotKey),
            nameof(ChatSettings.ToggleCoefficientHotKey),
            nameof(ChatSettings.ToggleEquipmentDbHotKey),
            nameof(ChatSettings.ToggleEncryptHotKey),
            nameof(ChatSettings.ToggleSettingsHotKey)
        };

        public static void ResolveConflict(ChatSettings settings, string targetPropertyName, string? hotKeyValue, Action<string>? onPropertyCleared)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            string normalizedValue = hotKeyValue?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(normalizedValue))
            {
                return;
            }

            foreach (var propertyName in PropertyNames)
            {
                if (string.Equals(propertyName, targetPropertyName, StringComparison.Ordinal))
                {
                    continue;
                }

                string existingValue = GetValue(settings, propertyName);
                if (!string.Equals(existingValue, normalizedValue, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SetValue(settings, propertyName, string.Empty);
                onPropertyCleared?.Invoke(propertyName);
                AppLogger.Warn($"Duplicate hotkey '{normalizedValue}' removed from '{propertyName}' in favor of '{targetPropertyName}'.");
            }
        }

        public static void NormalizeDuplicates(ChatSettings settings, Action<string>? onPropertyCleared)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var propertyName in PropertyNames)
            {
                string value = GetValue(settings, propertyName);
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (seen.TryGetValue(value, out var existingPropertyName))
                {
                    SetValue(settings, existingPropertyName, string.Empty);
                    onPropertyCleared?.Invoke(existingPropertyName);
                    seen[value] = propertyName;
                    AppLogger.Warn($"Duplicate hotkey '{value}' reassigned from '{existingPropertyName}' to '{propertyName}'.");
                    continue;
                }

                seen[value] = propertyName;
            }
        }

        public static void ClearAll(ChatSettings settings, Action<string>? onPropertyChanged)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            foreach (var propertyName in PropertyNames)
            {
                SetValue(settings, propertyName, string.Empty);
                onPropertyChanged?.Invoke(propertyName);
            }
        }

        private static string GetValue(ChatSettings settings, string propertyName) => propertyName switch
        {
            nameof(ChatSettings.ExitHotKey) => settings.ExitHotKey,
            nameof(ChatSettings.ToggleOverlayHotKey) => settings.ToggleOverlayHotKey,
            nameof(ChatSettings.ToggleAddonHotKey) => settings.ToggleAddonHotKey,
            nameof(ChatSettings.ToggleAlwaysVisibleHotKey) => settings.ToggleAlwaysVisibleHotKey,
            nameof(ChatSettings.ToggleDailyWeeklyContentHotKey) => settings.ToggleDailyWeeklyContentHotKey,
            nameof(ChatSettings.ToggleEtaRankingHotKey) => settings.ToggleEtaRankingHotKey,
            nameof(ChatSettings.ToggleCoefficientHotKey) => settings.ToggleCoefficientHotKey,
            nameof(ChatSettings.ToggleEquipmentDbHotKey) => settings.ToggleEquipmentDbHotKey,
            nameof(ChatSettings.ToggleEncryptHotKey) => settings.ToggleEncryptHotKey,
            nameof(ChatSettings.ToggleSettingsHotKey) => settings.ToggleSettingsHotKey,
            _ => string.Empty
        };

        private static void SetValue(ChatSettings settings, string propertyName, string value)
        {
            switch (propertyName)
            {
                case nameof(ChatSettings.ExitHotKey):
                    settings.ExitHotKey = value;
                    break;
                case nameof(ChatSettings.ToggleOverlayHotKey):
                    settings.ToggleOverlayHotKey = value;
                    break;
                case nameof(ChatSettings.ToggleAddonHotKey):
                    settings.ToggleAddonHotKey = value;
                    break;
                case nameof(ChatSettings.ToggleAlwaysVisibleHotKey):
                    settings.ToggleAlwaysVisibleHotKey = value;
                    break;
                case nameof(ChatSettings.ToggleDailyWeeklyContentHotKey):
                    settings.ToggleDailyWeeklyContentHotKey = value;
                    break;
                case nameof(ChatSettings.ToggleEtaRankingHotKey):
                    settings.ToggleEtaRankingHotKey = value;
                    break;
                case nameof(ChatSettings.ToggleCoefficientHotKey):
                    settings.ToggleCoefficientHotKey = value;
                    break;
                case nameof(ChatSettings.ToggleEquipmentDbHotKey):
                    settings.ToggleEquipmentDbHotKey = value;
                    break;
                case nameof(ChatSettings.ToggleEncryptHotKey):
                    settings.ToggleEncryptHotKey = value;
                    break;
                case nameof(ChatSettings.ToggleSettingsHotKey):
                    settings.ToggleSettingsHotKey = value;
                    break;
            }
        }
    }
}
