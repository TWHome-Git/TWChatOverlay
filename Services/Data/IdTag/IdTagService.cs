using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 아이디 태그: 특정 아이디에 짧은 메모(태그)를 붙여 채팅 표시 시
    /// "아이디[에타레벨][캐릭터][태그]" 형태로 함께 보여줍니다.
    /// 실행 폴더의 idtag.txt를 읽으며, 형식은 blacklist.txt와 동일한 "아이디 - 태그" 입니다.
    /// </summary>
    public static class IdTagService
    {
        private static readonly object SyncRoot = new();
        private static readonly Dictionary<string, string> Tags = new(StringComparer.OrdinalIgnoreCase);
        private static FileSystemWatcher? _watcher;
        private static DateTime _lastReloadUtc = DateTime.MinValue;

        public static event Action? IdTagsChanged;

        public static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "idtag.txt");

        public static void Initialize()
        {
            EnsureFileExists();
            Reload();
            StartWatcher();
        }

        public static string GetRawText()
        {
            EnsureFileExists();
            return File.ReadAllText(FilePath, Encoding.UTF8);
        }

        public static void SaveRawText(string text)
        {
            EnsureFileExists();
            File.WriteAllText(FilePath, text ?? string.Empty, new UTF8Encoding(false));
            Reload();
        }

        public static void Reload()
        {
            EnsureFileExists();

            var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines;
            try
            {
                lines = File.ReadAllLines(FilePath, Encoding.UTF8);
            }
            catch (IOException)
            {
                // 외부 편집기가 저장 중일 수 있음 — 다음 변경 알림에서 다시 시도
                return;
            }

            foreach (string rawLine in lines)
            {
                if (TryParseEntry(rawLine, out string userId, out string tag))
                    next[userId] = tag;
            }

            lock (SyncRoot)
            {
                Tags.Clear();
                foreach (var pair in next)
                    Tags[pair.Key] = pair.Value;
            }

            _lastReloadUtc = DateTime.UtcNow;
            AppLogger.Info($"ID tags reloaded ({next.Count} entries).");
            IdTagsChanged?.Invoke();
        }

        public static bool TryGetTag(string? userId, out string tag)
        {
            tag = string.Empty;
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            lock (SyncRoot)
            {
                return Tags.TryGetValue(userId.Trim(), out tag!);
            }
        }

        public static int Count
        {
            get { lock (SyncRoot) { return Tags.Count; } }
        }

        public static bool TryParseEntry(string? line, out string userId, out string tag)
        {
            userId = string.Empty;
            tag = string.Empty;

            if (IsIgnorableLine(line))
                return false;

            string trimmed = line!.Trim();
            int separatorIndex = trimmed.IndexOf(" - ", StringComparison.Ordinal);
            int separatorLength = 3;
            if (separatorIndex < 0)
            {
                separatorIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
                separatorLength = 1;
            }

            if (separatorIndex <= 0 || separatorIndex + separatorLength >= trimmed.Length)
                return false;

            userId = trimmed.Substring(0, separatorIndex).Trim();
            tag = trimmed.Substring(separatorIndex + separatorLength).Trim();

            return !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(tag);
        }

        private static bool IsIgnorableLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return true;

            string trimmed = line.Trim();
            return trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal);
        }

        private static void EnsureFileExists()
        {
            string path = FilePath;
            string? directoryPath = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directoryPath) && !Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            if (!File.Exists(path))
            {
                string template =
                    "# 아이디 태그 (ID Tag)" + Environment.NewLine +
                    "# 채팅에서 아이디 뒤에 [태그]를 붙여 표시합니다. 예) 뜨뜨해[1][아나이스][드드해] : 안녕" + Environment.NewLine +
                    "# 형식: 아이디 - 태그" + Environment.NewLine +
                    "# 예시: 뜨뜨해 - 드드해" + Environment.NewLine +
                    "# '#'으로 시작하는 줄은 무시됩니다. 파일을 저장하면 즉시 반영됩니다." + Environment.NewLine;
                File.WriteAllText(path, template, new UTF8Encoding(false));
            }
        }

        /// <summary>메모장 등 외부 편집기로 저장해도 즉시 반영되도록 파일 변경을 감시합니다.</summary>
        private static void StartWatcher()
        {
            try
            {
                string? dir = Path.GetDirectoryName(FilePath);
                if (string.IsNullOrWhiteSpace(dir)) return;

                _watcher = new FileSystemWatcher(dir, Path.GetFileName(FilePath))
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                    EnableRaisingEvents = true
                };
                FileSystemEventHandler handler = (_, _) => DebouncedReload();
                _watcher.Changed += handler;
                _watcher.Created += handler;
                _watcher.Renamed += (_, _) => DebouncedReload();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("ID tag file watcher could not be started.", ex);
            }
        }

        private static void DebouncedReload()
        {
            // 편집기가 저장 시 여러 이벤트를 연달아 보내므로 300ms 이내 중복은 무시
            if ((DateTime.UtcNow - _lastReloadUtc).TotalMilliseconds < 300)
                return;

            System.Threading.Tasks.Task.Delay(150).ContinueWith(_ =>
            {
                try { Reload(); }
                catch (Exception ex) { AppLogger.Warn("ID tag reload after file change failed.", ex); }
            });
        }
    }
}
