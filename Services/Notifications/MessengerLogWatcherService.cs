using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;

namespace TWChatOverlay.Services
{
    public sealed class MessengerLogWatcherService : IDisposable
    {
        private static readonly Encoding KoreanEncoding = Encoding.GetEncoding(949);
        private static readonly Regex TargetLineRegex = new(
            @"<font[^>]*color\s*=\s*[""']#ffffff[""'][^>]*>\s*(?<name>[^:<]+)\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private FileSystemWatcher? _watcher;
        private bool _disposed;
        private readonly Models.ChatSettings _settings;
        private readonly ConcurrentDictionary<string, byte> _processing = new(StringComparer.OrdinalIgnoreCase);

        public MessengerLogWatcherService(Models.ChatSettings settings)
        {
            _settings = settings;
        }

        public void Start()
        {
            string messengerDirectory = ResolveMessengerLogDirectory();
            if (_watcher != null)
                return;

            if (!Directory.Exists(messengerDirectory))
            {
                AppLogger.Warn($"Messenger log directory not found: {messengerDirectory}");
                return;
            }

            _watcher = new FileSystemWatcher(messengerDirectory)
            {
                Filter = "*.*",
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
            _watcher.Renamed += OnRenamed;
            AppLogger.Info($"Messenger log watcher started. Path={messengerDirectory}");
        }

        public void Stop()
        {
            if (_watcher == null)
                return;

            _watcher.Created -= OnFileEvent;
            _watcher.Changed -= OnFileEvent;
            _watcher.Renamed -= OnRenamed;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
            AppLogger.Info("Messenger log watcher stopped.");
        }

        private void OnRenamed(object sender, RenamedEventArgs e) => QueueProcess(e.FullPath);

        private void OnFileEvent(object sender, FileSystemEventArgs e) => QueueProcess(e.FullPath);

        private void QueueProcess(string fullPath)
        {
            string ext = Path.GetExtension(fullPath);
            if (!ext.Equals(".html", StringComparison.OrdinalIgnoreCase) &&
                !ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
                return;

            if (!_processing.TryAdd(fullPath, 0))
                return;

            _ = Task.Run(async () =>
            {
                try
                {
                    IReadOnlyList<string> targetIds = await TryExtractPartnerIdsAsync(fullPath).ConfigureAwait(false);
                    if (targetIds.Count == 0)
                    {
                        AppLogger.Debug($"Messenger parse result empty: {fullPath}");
                        return;
                    }

                    var entries = new List<string>(targetIds.Count);
                    foreach (string targetId in targetIds)
                    {
                        string levelText = "정보 없음";
                        if (EtaProfileResolver.TryGetProfile(targetId, out var profile))
                            levelText = profile.Level.ToString();
                        entries.Add($"{targetId} [{levelText}]");
                    }

                    AppLogger.Info($"Messenger toast dispatch start. File={fullPath}, Targets={entries.Count}");
                    MessengerEtaToastService.ShowForFile(fullPath, entries, _settings);
                    AppLogger.Info($"Messenger toast dispatch end. File={fullPath}, Targets={entries.Count}");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to process messenger log create event.", ex);
                }
                finally
                {
                    _processing.TryRemove(fullPath, out _);
                }
            });
        }

        private string ResolveMessengerLogDirectory()
        {
            string chatLogPath = _settings.ChatLogFolderPath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(chatLogPath))
                return @"C:\Nexon\TalesWeaver\MsgerLog";

            string normalized = chatLogPath.TrimEnd('\\', '/');
            if (normalized.EndsWith("ChatLog", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Path.GetDirectoryName(normalized) ?? normalized, "MsgerLog");

            return Path.Combine(normalized, "..", "MsgerLog");
        }

        private static async Task<IReadOnlyList<string>> TryExtractPartnerIdsAsync(string path)
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    if (!File.Exists(path))
                        return Array.Empty<string>();

                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, KoreanEncoding, detectEncodingFromByteOrderMarks: true);
                    string html = await reader.ReadToEndAsync().ConfigureAwait(false);
                    var result = new List<string>();
                    foreach (Match match in TargetLineRegex.Matches(html))
                    {
                        string id = match.Groups["name"].Value.Trim();
                        if (string.IsNullOrWhiteSpace(id))
                            continue;
                        if (!result.Contains(id, StringComparer.Ordinal))
                            result.Add(id);
                    }
                    return result;
                }
                catch (IOException)
                {
                    await Task.Delay(120).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException)
                {
                    await Task.Delay(120).ConfigureAwait(false);
                }
                catch
                {
                    return Array.Empty<string>();
                }
            }

            return Array.Empty<string>();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
