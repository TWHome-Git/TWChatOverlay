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
        // 설정은 Config 폴더에 저장한다. 아이템 알림 관련(Alerts.ItemDrop)은 item.json으로 분리.
        private static readonly string ConfigDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config");
        private static readonly string FilePath = Path.Combine(ConfigDir, "settings.json");
        private static readonly string ItemFilePath = Path.Combine(ConfigDir, "item.json");
        private static readonly string LegacyFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
        private static readonly string FactoryDefaultsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Defaults", "DefaultSettings.json");
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

        /// <summary>설정 저장 폴더에 실제로 쓸 수 있는지 검사한다. (권한 문제 조기 감지)</summary>
        public static bool VerifyWritable(out string? error)
        {
            try
            {
                string probePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".write_probe.tmp");
                File.WriteAllText(probePath, "probe");
                File.Delete(probePath);
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool SettingsFileExists()
        {
            try
            {
                return File.Exists(FilePath) || File.Exists(LegacyFilePath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>구버전 위치(프로그램 폴더 루트)의 settings.json을 Config 폴더로 옮긴다.</summary>
        private static void MigrateFileLocation()
        {
            try
            {
                if (File.Exists(FilePath) || !File.Exists(LegacyFilePath))
                    return;

                Directory.CreateDirectory(ConfigDir);
                File.Move(LegacyFilePath, FilePath);
                AppLogger.Info($"Settings file moved to '{FilePath}'.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to move settings file into Config folder.", ex);
            }
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

        private static bool _saveFailureNotified;

        private static void SaveInternal(ChatSettings settings)
        {
            if (settings == null)
            {
                AppLogger.Warn("Settings save skipped because settings instance was null.");
                return;
            }

            string json;
            try
            {
                json = JsonSerializer.Serialize(settings, _options);
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to serialize settings.", ex);
                return;
            }

            if (string.Equals(_lastSavedJson, json, StringComparison.Ordinal))
            {
                AppLogger.Debug("Settings save skipped because there were no changes.");
                return;
            }

            // 아이템 알림(Alerts.ItemDrop)은 item.json으로 분리해 저장한다
            string settingsJson = json;
            string itemJson = "{}";
            try
            {
                if (JsonNode.Parse(json) is JsonObject root)
                {
                    if (root["Alerts"] is JsonObject alerts && alerts["ItemDrop"] is JsonNode itemNode)
                    {
                        alerts.Remove("ItemDrop");
                        itemJson = itemNode.ToJsonString(_options);
                    }
                    settingsJson = root.ToJsonString(_options);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to split item settings; saving combined file.", ex);
                settingsJson = json;
                itemJson = "{}";
            }

            // 백신/동기화 도구의 일시적 파일 잠금에 대비해 짧게 재시도한다
            const int maxAttempts = 3;
            Exception? lastError = null;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    WriteJsonFile(FilePath, settingsJson);
                    WriteJsonFile(ItemFilePath, itemJson);
                    _lastSavedJson = json;
                    _saveFailureNotified = false;
                    AppLogger.Info($"Settings saved to {FilePath}.");
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    lastError = ex;
                    if (attempt < maxAttempts)
                        Thread.Sleep(100);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    break;
                }
            }

            AppLogger.Error("Failed to save settings.", lastError ?? new IOException("unknown"));
            CleanupTempFile();
            NotifySaveFailureOnce(lastError);
        }

        private static void WriteJsonFile(string path, string json)
        {
            string directory = Path.GetDirectoryName(path) ?? AppDomain.CurrentDomain.BaseDirectory;
            Directory.CreateDirectory(directory);

            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json, new UTF8Encoding(false));

            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }

        private static void CleanupTempFile()
        {
            try
            {
                foreach (string path in new[] { FilePath + ".tmp", ItemFilePath + ".tmp" })
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
            catch
            {
            }
        }

        /// <summary>저장이 계속 실패하면 사용자에게 세션당 1회 알린다. (조용한 설정 유실 방지)</summary>
        private static void NotifySaveFailureOnce(Exception? error)
        {
            if (_saveFailureNotified)
                return;
            _saveFailureNotified = true;

            try
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    System.Windows.MessageBox.Show(
                        $"설정을 저장하지 못했습니다.\n\n경로: {FilePath}\n원인: {error?.Message}\n\n" +
                        "프로그램 폴더에 쓰기 권한이 있는지 확인하거나, 폴더를 다른 위치로 옮긴 뒤 다시 실행해 주세요.",
                        "설정 저장 실패",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }));
            }
            catch
            {
            }
        }

        /// <summary>
        /// 파일로부터 설정을 불러옴. 파일이 없거나 오류 발생 시 기본 설정을 반환
        /// </summary>
        public static ChatSettings Load()
        {
            MigrateFileLocation();

            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    AppLogger.Debug($"Settings loaded from {FilePath}.");

                    ChatSettings settings;
                    bool migratedFromLegacy = false;
                    bool itemFileExisted = File.Exists(ItemFilePath);
                    JsonObject? rootObj = JsonNode.Parse(json) as JsonObject;
                    if (rootObj != null && SettingsMigration.IsLegacyFormat(rootObj))
                    {
                        // 구버전(평면) 파일: v2로 이관하고 원본은 .v1.bak으로 보존
                        settings = SettingsMigration.FromLegacy(rootObj, _options);
                        migratedFromLegacy = true;
                        try { File.Copy(FilePath, FilePath + ".v1.bak", overwrite: true); } catch { }
                        AppLogger.Info("Legacy settings format detected; migrating to v2 schema.");
                    }
                    else
                    {
                        // item.json(Alerts.ItemDrop 분리 저장분)을 병합해 하나의 설정으로 읽는다
                        if (rootObj != null && itemFileExisted)
                        {
                            try
                            {
                                if (JsonNode.Parse(File.ReadAllText(ItemFilePath)) is JsonNode itemNode)
                                {
                                    if (rootObj["Alerts"] is not JsonObject alerts)
                                    {
                                        alerts = new JsonObject();
                                        rootObj["Alerts"] = alerts;
                                    }
                                    alerts["ItemDrop"] = itemNode;
                                    json = rootObj.ToJsonString(_options);
                                }
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Warn("Failed to merge item.json; item settings fall back to defaults.", ex);
                            }
                        }

                        settings = JsonSerializer.Deserialize<ChatSettings>(json, _options) ?? new ChatSettings();
                    }

                    settings.EnsureLoadedDefaults();
                    MigrateDungeonItemConfigKeys(settings);
                    AppLogger.IsEnabled = settings.EnableDebugLogging;
                    //AppLogger.IsEnabled = true;

                    if (migratedFromLegacy)
                    {
                        _lastSavedJson = null;
                        Save(settings);
                        AppLogger.Info("Settings were migrated to the v2 schema.");
                        return settings;
                    }

                    string normalizedJson = JsonSerializer.Serialize(settings, _options);
                    bool removedObsoleteKeys = TryRemoveObsoleteKeys(json, normalizedJson, out string? cleanedJson);
                    if (removedObsoleteKeys && cleanedJson != null)
                    {
                        settings = JsonSerializer.Deserialize<ChatSettings>(cleanedJson, _options) ?? settings;
                        normalizedJson = JsonSerializer.Serialize(settings, _options);
                    }

                    _lastSavedJson = normalizedJson;
                    if (removedObsoleteKeys || !itemFileExisted)
                    {
                        _lastSavedJson = null;
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
            var defaultSettings = TryLoadFactoryDefaults() ?? new ChatSettings();
            AppLogger.IsEnabled = defaultSettings.EnableDebugLogging;
            Save(defaultSettings);
            return defaultSettings;
        }

        /// <summary>
        /// 배포에 동봉된 공장 기본 설정(Defaults\DefaultSettings.json)을 읽는다.
        /// 설정 초기화와 설정 파일이 없는 최초 실행에서 기본값으로 쓰인다. 없거나 손상되면 null.
        /// </summary>
        public static ChatSettings? TryLoadFactoryDefaults()
        {
            try
            {
                if (!File.Exists(FactoryDefaultsPath))
                    return null;

                var settings = JsonSerializer.Deserialize<ChatSettings>(File.ReadAllText(FactoryDefaultsPath), _options);
                if (settings == null)
                    return null;

                settings.EnsureLoadedDefaults();
                MigrateDungeonItemConfigKeys(settings);
                AppLogger.Info("Factory default settings loaded.");
                return settings;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load factory default settings.", ex);
                return null;
            }
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

        /// <summary>옛 던전 설정 키를 현재 표기로 개명한다. 설정·공장 기본값·프로필 로드 경로 모두에서 호출된다.</summary>
        internal static void MigrateDungeonItemConfigKeys(ChatSettings settings)
        {
            try
            {
                if (settings.DungeonItemConfigs == null)
                    return;

                // 띄어쓰기 표기 통일 (표시 텍스트가 저장 키를 겸하므로 키도 함께 개명)
                RenameDungeonItemConfigKey(settings, "코어던전", "코어 던전");
                RenameDungeonItemConfigKey(settings, "기타지역", "기타 지역");

                bool hasUnified = settings.DungeonItemConfigs.TryGetValue("아페티리아", out var unifiedCfg);
                bool hasNormal = settings.DungeonItemConfigs.TryGetValue("아페티리아 일반", out var normalCfg);
                bool hasHard = settings.DungeonItemConfigs.TryGetValue("아페티리아 어려움", out var hardCfg);

                if (!hasUnified && (hasNormal || hasHard))
                {
                    // Prefer hard-mode state when both exist, otherwise fallback to normal-mode state.
                    settings.DungeonItemConfigs["아페티리아"] = hasHard ? hardCfg! : normalCfg!;
                }

                if (hasNormal)
                    settings.DungeonItemConfigs.Remove("아페티리아 일반");
                if (hasHard)
                    settings.DungeonItemConfigs.Remove("아페티리아 어려움");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to migrate dungeon item config keys.", ex);
            }
        }

        /// <summary>옛 키의 값을 새 키로 옮긴다. 새 키가 이미 있으면 기존 값을 유지하고 옛 키만 지운다.</summary>
        private static void RenameDungeonItemConfigKey(ChatSettings settings, string oldKey, string newKey)
        {
            if (!settings.DungeonItemConfigs.TryGetValue(oldKey, out var oldCfg))
                return;

            if (!settings.DungeonItemConfigs.ContainsKey(newKey))
                settings.DungeonItemConfigs[newKey] = oldCfg;
            settings.DungeonItemConfigs.Remove(oldKey);
        }
    }
}
