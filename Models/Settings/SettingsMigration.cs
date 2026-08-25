using System;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TWChatOverlay.Models
{
    /// <summary>
    /// 구버전(평면 구조, Version 키 없음) settings.json을 v2 스키마로 이관한다.
    /// 구버전의 키 이름은 ChatSettings의 호환 facade 프로퍼티 이름과 동일하므로,
    /// 리플렉션으로 facade에 값을 넣으면 검증(클램프)을 거쳐 섹션에 채워진다.
    /// </summary>
    public static class SettingsMigration
    {
        /// <summary>Version 키가 없으면 구버전(평면) 형식이다.</summary>
        public static bool IsLegacyFormat(JsonObject root) => !root.ContainsKey("Version");

        public static ChatSettings FromLegacy(JsonObject root, JsonSerializerOptions options)
        {
            var settings = new ChatSettings();

            foreach (var pair in root)
            {
                if (pair.Value == null)
                    continue;

                // 구버전의 통합 색상 동기화 → 항목별 동기화 토글 4개로 전개
                if (pair.Key == "DecorationColorSync")
                {
                    try
                    {
                        bool sync = pair.Value.GetValue<bool>();
                        settings.EtaLevelColorSync = sync;
                        settings.EtaCharacterColorSync = sync;
                        settings.TimestampColorSync = sync;
                        settings.IdTagColorSync = sync;
                    }
                    catch { }
                    continue;
                }

                PropertyInfo? property = typeof(ChatSettings).GetProperty(pair.Key, BindingFlags.Public | BindingFlags.Instance);
                if (property == null || !property.CanWrite)
                    continue;

                // 섹션/버전은 구버전 파일에 없어야 정상이며, 있어도 평면 이관 대상이 아니다
                if (property.Name is nameof(ChatSettings.Version)
                    or nameof(ChatSettings.Chat) or nameof(ChatSettings.Shout) or nameof(ChatSettings.Alerts)
                    or nameof(ChatSettings.Windows) or nameof(ChatSettings.Ui) or nameof(ChatSettings.Hotkeys)
                    or nameof(ChatSettings.SystemConfig) or nameof(ChatSettings.Presets))
                    continue;

                try
                {
                    object? value = pair.Value.Deserialize(property.PropertyType, options);
                    property.SetValue(settings, value);
                }
                catch
                {
                    // 개별 키 이관 실패는 무시하고 기본값을 유지한다
                }
            }

            return settings;
        }
    }
}
