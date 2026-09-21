using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace TWChatOverlay.Services
{
    /// <summary>아이디 태그 저장소. 구현은 <see cref="IdTagService"/>, 등록은 AppServices.</summary>
    public interface IIdTagService
    {
        /// <summary>파일이 다시 읽혀 태그 목록이 바뀌면 발생.</summary>
        event Action? IdTagsChanged;

        string FilePath { get; }
        int Count { get; }

        void Initialize();
        string GetRawText();
        void SaveRawText(string text);
        void Reload();
        bool TryGetTag(string? userId, out string tag);
    }

    /// <summary>
    /// 아이디 태그: 특정 아이디에 짧은 메모(태그)를 붙여 채팅 표시 시
    /// "아이디[에타레벨][캐릭터][태그]" 형태로 함께 보여줍니다.
    /// 실행 폴더의 idtag.txt를 읽으며, 형식은 blacklist.txt와 동일한 "아이디 - 태그" 입니다.
    /// 앱 전체에 하나만 두며(AppServices 싱글턴), 파일 감시와 태그 캐시가 인스턴스 상태다.
    /// </summary>
    public sealed class IdTagService : IIdTagService, IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly Dictionary<string, string> _tags = new(StringComparer.OrdinalIgnoreCase);
        private FileSystemWatcher? _watcher;
        private DateTime _lastReloadUtc = DateTime.MinValue;

        public event Action? IdTagsChanged;

        public string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "idtag.txt");

        public void Initialize()
        {
            EnsureFileExists();
            Reload();
            StartWatcher();
        }

        public string GetRawText()
        {
            EnsureFileExists();
            return File.ReadAllText(FilePath, Encoding.UTF8);
        }

        public void SaveRawText(string text)
        {
            EnsureFileExists();
            File.WriteAllText(FilePath, text ?? string.Empty, new UTF8Encoding(false));
            Reload();
        }

        public void Reload()
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

            lock (_syncRoot)
            {
                _tags.Clear();
                foreach (var pair in next)
                    _tags[pair.Key] = pair.Value;
            }

            _lastReloadUtc = DateTime.UtcNow;
            AppLogger.Info($"ID tags reloaded ({next.Count} entries).");
            IdTagsChanged?.Invoke();
        }

        public bool TryGetTag(string? userId, out string tag)
        {
            tag = string.Empty;
            if (string.IsNullOrWhiteSpace(userId))
                return false;

            lock (_syncRoot)
            {
                return _tags.TryGetValue(userId.Trim(), out tag!);
            }
        }

        public int Count
        {
            get { lock (_syncRoot) { return _tags.Count; } }
        }

        /// <summary>"아이디 - 태그" 한 줄을 해석한다. 상태가 없는 순수 함수라 정적으로 둔다.</summary>
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

        private void EnsureFileExists()
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
        private void StartWatcher()
        {
            try
            {
                string? dir = Path.GetDirectoryName(FilePath);
                if (string.IsNullOrWhiteSpace(dir)) return;

                _watcher?.Dispose();
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

        private void DebouncedReload()
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

        public void Dispose()
        {
            try { _watcher?.Dispose(); } catch { }
            _watcher = null;
        }
    }
}
