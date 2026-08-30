using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 설정 프로필: 현재 settings.json 전체 상태를 이름 붙여 저장하고 불러온다.
    /// 프로필은 프로그램 폴더의 Profiles/&lt;이름&gt;.json 파일 하나씩이며,
    /// 기본 프로필 2개("프로필 1", "프로필 2")는 파일이 없어도 항상 목록에 나타난다.
    /// </summary>
    public static class SettingsProfileService
    {
        public static readonly string[] DefaultProfileNames = { "Profile 1", "Profile 2" };

        // 구버전 한글 기본 프로필 파일명 → 영어 이름으로 이관
        private static readonly (string Old, string New)[] LegacyRenames =
        {
            ("프로필 1", "Profile 1"),
            ("프로필 2", "Profile 2"),
        };

        private static readonly string ProfilesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Profiles");
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        };

        /// <summary>기본 2개 + 추가로 저장된 프로필 이름 목록 (기본이 항상 앞).</summary>
        public static List<string> GetProfileNames()
        {
            MigrateLegacyNames();
            var names = new List<string>(DefaultProfileNames);
            try
            {
                if (Directory.Exists(ProfilesDir))
                {
                    var extras = Directory.EnumerateFiles(ProfilesDir, "*.json")
                        .Select(Path.GetFileNameWithoutExtension)
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Select(name => name!)
                        .Where(name => !names.Contains(name, StringComparer.Ordinal))
                        .OrderBy(name => name, StringComparer.Ordinal);
                    names.AddRange(extras);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to enumerate settings profiles.", ex);
            }

            return names;
        }

        public static bool Exists(string name)
        {
            try { return File.Exists(PathFor(name)); }
            catch { return false; }
        }

        public static bool IsDefaultProfile(string name)
            => Array.IndexOf(DefaultProfileNames, name) >= 0;

        /// <summary>현재 설정 전체를 프로필로 저장한다.</summary>
        public static bool Save(string name, ChatSettings settings)
        {
            if (string.IsNullOrWhiteSpace(name) || settings == null)
                return false;

            try
            {
                Directory.CreateDirectory(ProfilesDir);
                string json = JsonSerializer.Serialize(settings, Options);
                File.WriteAllText(PathFor(name), json, new UTF8Encoding(false));
                AppLogger.Info($"Settings profile saved. Name='{name}'");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to save settings profile '{name}'.", ex);
                return false;
            }
        }

        /// <summary>프로필 파일을 읽어 설정 인스턴스로 돌려준다.</summary>
        public static bool TryLoad(string name, out ChatSettings loaded)
            => TryLoadFile(PathFor(name), out loaded);

        /// <summary>임의의 프로필(.json) 파일을 읽어 설정 인스턴스로 돌려준다. (파일에서 불러오기)</summary>
        public static bool TryLoadFile(string path, out ChatSettings loaded)
        {
            loaded = null!;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                    return false;

                var settings = JsonSerializer.Deserialize<ChatSettings>(File.ReadAllText(path), Options);
                if (settings == null)
                    return false;

                settings.EnsureLoadedDefaults();
                loaded = settings;
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to load settings profile file '{path}'.", ex);
                return false;
            }
        }

        /// <summary>현 시점 설정 전체를 지정한 파일로 내보낸다. (파일로 저장)</summary>
        public static bool ExportToFile(string path, ChatSettings settings)
        {
            if (string.IsNullOrWhiteSpace(path) || settings == null)
                return false;

            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(settings, Options);
                File.WriteAllText(path, json, new UTF8Encoding(false));
                AppLogger.Info($"Settings profile exported to '{path}'.");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to export settings profile to '{path}'.", ex);
                return false;
            }
        }

        /// <summary>저장된 프로필의 이름을 바꾼다(파일 이름 변경). 실패 사유는 error로 돌려준다.</summary>
        public static bool Rename(string oldName, string newName, out string? error)
        {
            error = null;
            newName = (newName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(newName))
            {
                error = "이름을 입력해 주세요.";
                return false;
            }
            if (string.Equals(oldName, newName, StringComparison.Ordinal))
                return true;

            try
            {
                string oldPath = PathFor(oldName);
                string newPath = PathFor(newName);
                if (!File.Exists(oldPath))
                {
                    error = "저장된 프로필이 없습니다.";
                    return false;
                }
                if (File.Exists(newPath) || GetProfileNames().Contains(newName, StringComparer.Ordinal))
                {
                    error = "같은 이름의 프로필이 이미 있습니다.";
                    return false;
                }

                File.Move(oldPath, newPath);
                AppLogger.Info($"Settings profile renamed. '{oldName}' -> '{newName}'");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to rename settings profile '{oldName}' -> '{newName}'.", ex);
                error = "이름을 바꾸지 못했습니다.";
                return false;
            }
        }

        /// <summary>추가 프로필 삭제. 기본 프로필은 파일만 지워지고 목록에는 남는다.</summary>
        public static bool Delete(string name)
        {
            try
            {
                string path = PathFor(name);
                if (!File.Exists(path))
                    return false;
                File.Delete(path);
                AppLogger.Info($"Settings profile deleted. Name='{name}'");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to delete settings profile '{name}'.", ex);
                return false;
            }
        }

        /// <summary>"Profile 3"부터 비어 있는 새 이름을 제안한다.</summary>
        public static string SuggestNewName()
        {
            var taken = new HashSet<string>(GetProfileNames(), StringComparer.Ordinal);
            for (int i = 3; i < 100; i++)
            {
                string candidate = $"Profile {i}";
                if (!taken.Contains(candidate))
                    return candidate;
            }
            return $"Profile {DateTime.Now:HHmmss}";
        }

        /// <summary>구버전 한글 기본 프로필 파일("프로필 1.json" 등)을 영어 이름으로 이관한다.</summary>
        private static void MigrateLegacyNames()
        {
            try
            {
                foreach (var (oldName, newName) in LegacyRenames)
                {
                    string oldPath = PathFor(oldName);
                    string newPath = PathFor(newName);
                    if (File.Exists(oldPath) && !File.Exists(newPath))
                    {
                        File.Move(oldPath, newPath);
                        AppLogger.Info($"Settings profile renamed. '{oldName}' -> '{newName}'");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to migrate legacy profile names.", ex);
            }
        }

        private static string PathFor(string name)
        {
            // 파일명에 못 쓰는 문자는 _로 치환
            var safe = new StringBuilder(name.Length);
            foreach (char c in name)
                safe.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            return Path.Combine(ProfilesDir, safe + ".json");
        }
    }
}
