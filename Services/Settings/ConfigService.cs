using System;
using System.IO;
using System.Collections.Generic;
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
        private static readonly string BackupFilePath = FilePath + ".bak";
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
                    File.Copy(FilePath, BackupFilePath, overwrite: true);
                    File.Replace(tempPath, FilePath, null);
                }
                else
                {
                    File.Move(tempPath, FilePath);
                    File.Copy(FilePath, BackupFilePath, overwrite: true);
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
                    AppLogger.IsEnabled = settings.EnableDebugLogging;

                    string normalizedJson = JsonSerializer.Serialize(settings, _options);
                    bool removedObsoleteKeys = TryRemoveObsoleteKeys(json, normalizedJson, out string? cleanedJson);
                    if (removedObsoleteKeys && cleanedJson != null)
                    {
                        settings = JsonSerializer.Deserialize<ChatSettings>(cleanedJson, _options) ?? settings;
                        normalizedJson = JsonSerializer.Serialize(settings, _options);
                    }

                    if (!string.Equals(json, normalizedJson, StringComparison.Ordinal))
                    {
                        Save(settings);
                        AppLogger.Info("Settings were normalized to the current schema.");
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

        private static bool TryRemoveObsoleteKeys(string currentJson, string normalizedJson, out string? cleanedJson)
        {
            cleanedJson = null;
            try
            {
                JsonNode? current = JsonNode.Parse(currentJson);
                JsonNode? normalized = JsonNode.Parse(normalizedJson);
                if (current is not JsonObject currentObj || normalized is not JsonObject normalizedObj)
                    return false;

                bool changed = RemoveObsoleteKeysRecursive(currentObj, normalizedObj);
                if (!changed)
                    return false;

                cleanedJson = currentObj.ToJsonString(_options);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to remove obsolete nested settings keys.", ex);
                return false;
            }
        }

        private static bool RemoveObsoleteKeysRecursive(JsonObject current, JsonObject reference)
        {
            bool changed = false;
            var keys = new List<string>();
            foreach (var pair in current)
                keys.Add(pair.Key);

            foreach (string key in keys)
            {
                if (!reference.ContainsKey(key))
                {
                    current.Remove(key);
                    changed = true;
                    continue;
                }

                JsonNode? currentNode = current[key];
                JsonNode? referenceNode = reference[key];
                if (currentNode is JsonObject currentChildObj && referenceNode is JsonObject referenceChildObj)
                {
                    if (RemoveObsoleteKeysRecursive(currentChildObj, referenceChildObj))
                        changed = true;
                }
            }

            return changed;
        }
    }
}
