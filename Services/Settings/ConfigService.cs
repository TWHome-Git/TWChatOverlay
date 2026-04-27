using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using System.Threading;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 프로그램 설정을 JSON 파일로 저장하고 불러오는 기능
    /// </summary>
    public static class ConfigService
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private static readonly object _saveLock = new();
        private static readonly TimeSpan SaveDebounce = TimeSpan.FromMilliseconds(250);
        private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };
        private static Timer? _saveTimer;
        private static ChatSettings? _pendingSettings;
        private static string? _lastSavedJson;

        static ConfigService()
        {
            _options.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals;
        }


        /// <summary>
        /// 설정 객체를 파일로 저장
        /// </summary>
        public static void Save(ChatSettings settings)
        {
            lock (_saveLock)
            {
                _pendingSettings = null;
                _saveTimer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                SaveInternal(settings);
            }
        }

        public static void SaveDeferred(ChatSettings settings)
        {
            if (settings == null)
            {
                AppLogger.Warn("Deferred settings save skipped because settings instance was null.");
                return;
            }

            lock (_saveLock)
            {
                _pendingSettings = settings;
                _saveTimer ??= new Timer(_ => FlushPendingSave(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _saveTimer.Change(SaveDebounce, Timeout.InfiniteTimeSpan);
            }
        }

        private static void FlushPendingSave()
        {
            lock (_saveLock)
            {
                ChatSettings? pending = _pendingSettings;
                _pendingSettings = null;
                if (pending != null)
                {
                    SaveInternal(pending);
                }
            }
        }

        private static void SaveInternal(ChatSettings settings)
        {
            if (settings == null)
            {
                AppLogger.Warn("Settings save skipped because settings instance was null.");
                return;
            }

            try
            {
                string json = JsonSerializer.Serialize(settings, _options);
                if (string.Equals(_lastSavedJson, json, StringComparison.Ordinal))
                {
                    AppLogger.Debug("Settings save skipped because there were no changes.");
                    return;
                }

                string directory = Path.GetDirectoryName(FilePath) ?? AppDomain.CurrentDomain.BaseDirectory;
                Directory.CreateDirectory(directory);

                string tempPath = FilePath + ".tmp";
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));

                if (File.Exists(FilePath))
                {
                    File.Replace(tempPath, FilePath, null);
                }
                else
                {
                    File.Move(tempPath, FilePath);
                }

                _lastSavedJson = json;
                AppLogger.Info($"Settings saved to {FilePath}.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to save settings.", ex);
                try
                {
                    string tempPath = FilePath + ".tmp";
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// 파일로부터 설정을 불러옴. 파일이 없거나 오류 발생 시 기본 설정을 반환
        /// </summary>
        public static ChatSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    _lastSavedJson = json;
                    AppLogger.Debug($"Settings loaded from {FilePath}.");
                    var settings = JsonSerializer.Deserialize<ChatSettings>(json, _options) ?? new ChatSettings();
                    bool cleaned = CleanupLegacyDungeonProgress(json, settings);
                    bool removedLegacyDebugLogging = CleanupLegacyDebugLoggingSetting(json);
                    AppLogger.IsEnabled = settings.EnableDebugLogging;
                    if (cleaned || removedLegacyDebugLogging)
                    {
                        Save(settings);
                        AppLogger.Info("Legacy settings were migrated to the current schema.");
                    }
                    return settings;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to load settings.", ex);
            }

            AppLogger.Warn("Settings file was missing or unreadable. Default settings will be used.");
            var defaultSettings = new ChatSettings();
            AppLogger.IsEnabled = defaultSettings.EnableDebugLogging;
            Save(defaultSettings);
            return defaultSettings;
        }

        private static bool CleanupLegacyDungeonProgress(string json, ChatSettings settings)
        {
            bool changed = false;

            foreach (var config in settings.DungeonItemConfigs.Values)
            {
                if (config.CurrentCount != 0 || config.IsCleared || config.SavedAt != DateTime.MinValue)
                {
                    config.CurrentCount = 0;
                    config.IsCleared = false;
                    config.SavedAt = DateTime.MinValue;
                    changed = true;
                }
            }

            try
            {
                JsonNode? rootNode = JsonNode.Parse(json);
                if (rootNode is not JsonObject rootObject)
                {
                    return changed;
                }

                if (rootObject[nameof(ChatSettings.DungeonItemConfigs)] is not JsonObject configRoot)
                {
                    return changed;
                }

                foreach (var pair in configRoot)
                {
                    if (pair.Value is not JsonObject configObject)
                    {
                        continue;
                    }

                    if (configObject.ContainsKey(nameof(DungeonItemConfig.CurrentCount)) ||
                        configObject.ContainsKey(nameof(DungeonItemConfig.IsCleared)) ||
                        configObject.ContainsKey(nameof(DungeonItemConfig.SavedAt)))
                    {
                        changed = true;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Legacy dungeon progress cleanup check failed.", ex);
            }

            return changed;
        }

        private static bool CleanupLegacyDebugLoggingSetting(string json)
        {
            try
            {
                JsonNode? rootNode = JsonNode.Parse(json);
                if (rootNode is not JsonObject rootObject)
                {
                    return false;
                }

                return rootObject.ContainsKey(nameof(ChatSettings.EnableDebugLogging));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Legacy debug logging setting cleanup check failed.", ex);
                return false;
            }
        }
    }
}
