using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 원격 JSON을 TTL + ETag + 로컬 fallback 전략으로 가져오는 공용 캐시 클라이언트.
    /// 캐시는 리소스마다 Cache/remote/{name}.json(본문) + {name}.meta.json(ETag·시각)으로 나눠 두어
    /// 작은 리소스를 읽을 때 큰 리소스(ETA 랭킹 1.7MB)를 함께 파싱하지 않고, 304 응답에서는 메타만 다시 쓴다.
    /// </summary>
    public sealed class RemoteJsonCacheClient
    {
        private readonly string _name;
        private readonly string _url;
        private readonly TimeSpan _ttl;
        private readonly HttpClient _httpClient;
        private readonly int _refreshAnchorHourLocal;
        private readonly bool _forceRemoteCheckOnFirstCall;
        private readonly string _bodyPath;
        private readonly string _metaPath;
        private bool _hasTriedRemoteInThisProcess;
        private DateTime _nextRemoteRetryUtc;
        private static readonly TimeSpan RemoteFailureBackoff = TimeSpan.FromMinutes(3);
        private static readonly object CacheFileLock = new();
        private static readonly string CacheDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "remote");
        // 구버전: 모든 리소스를 한 파일에 담았다. 첫 접근 때 리소스별 파일로 나누고 지운다.
        private static readonly string LegacySharedCacheFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "remote_json_cache.json");
        private static int _legacyMigrated;

        public RemoteJsonCacheClient(
            string name,
            string url,
            TimeSpan ttl,
            HttpClient httpClient,
            int refreshAnchorHourLocal = -1,
            bool forceRemoteCheckOnFirstCall = false)
        {
            _name = name;
            _url = url;
            _ttl = ttl;
            _httpClient = httpClient;
            _refreshAnchorHourLocal = refreshAnchorHourLocal;
            _forceRemoteCheckOnFirstCall = forceRemoteCheckOnFirstCall;
            _bodyPath = GetBodyPath(name);
            _metaPath = GetMetaPath(name);
        }

        public async Task<string?> GetJsonAsync(bool forceRefresh = false)
        {
            var cache = TryReadCache();
            var now = DateTime.UtcNow;
            bool shouldTryRemote = forceRefresh || ShouldTryRemote(cache, now);
            if (!forceRefresh && now < _nextRemoteRetryUtc)
            {
                shouldTryRemote = false;
            }

            if (shouldTryRemote)
            {
                _hasTriedRemoteInThisProcess = true;
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, _url);
                    if (!string.IsNullOrWhiteSpace(cache?.ETag))
                    {
                        request.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(cache.ETag));
                    }

                    using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.NotModified && cache != null && !string.IsNullOrWhiteSpace(cache.Json))
                    {
                        _nextRemoteRetryUtc = DateTime.MinValue;
                        cache.LastCheckedUtc = now;
                        TryWriteCache(cache, writeBody: false);
                        return cache.Json;
                    }

                    if (response.IsSuccessStatusCode)
                    {
                        _nextRemoteRetryUtc = DateTime.MinValue;
                        string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        var next = new JsonCachePayload
                        {
                            Json = json,
                            ETag = response.Headers.ETag?.Tag ?? cache?.ETag,
                            LastFetchedUtc = now,
                            LastCheckedUtc = now
                        };
                        TryWriteCache(next, writeBody: true);
                        return json;
                    }

                    _nextRemoteRetryUtc = now.Add(RemoteFailureBackoff);
                    AppLogger.Warn($"[{_name}] Remote responded {(int)response.StatusCode}. Using fallback cache when available.");
                }
                catch (Exception ex)
                {
                    _nextRemoteRetryUtc = now.Add(RemoteFailureBackoff);
                    AppLogger.Warn($"[{_name}] Remote fetch failed.", ex);
                }
            }

            if (cache != null && !string.IsNullOrWhiteSpace(cache.Json))
            {
                return cache.Json;
            }

            return null;
        }

        private bool ShouldTryRemote(JsonCachePayload? cache, DateTime nowUtc)
        {
            if (_forceRemoteCheckOnFirstCall && !_hasTriedRemoteInThisProcess)
                return true;

            if (cache == null || string.IsNullOrWhiteSpace(cache.Json))
                return true;

            if (_refreshAnchorHourLocal >= 0 && _refreshAnchorHourLocal <= 23)
            {
                var lastCheckedLocal = cache.LastCheckedUtc.ToLocalTime();
                return IsAfterDailyRefreshAnchor(lastCheckedLocal, nowUtc.ToLocalTime(), _refreshAnchorHourLocal);
            }

            if (cache.LastCheckedUtc == default)
                return true;

            return (nowUtc - cache.LastCheckedUtc) >= _ttl;
        }

        private static bool IsAfterDailyRefreshAnchor(DateTime lastCheckedLocal, DateTime nowLocal, int anchorHour)
        {
            DateTime cycleStart = GetCurrentCycleStartLocal(nowLocal, anchorHour);
            return lastCheckedLocal < cycleStart;
        }

        private static DateTime GetCurrentCycleStartLocal(DateTime nowLocal, int anchorHour)
        {
            DateTime todayAnchor = new DateTime(nowLocal.Year, nowLocal.Month, nowLocal.Day, anchorHour, 0, 0, nowLocal.Kind);
            return nowLocal >= todayAnchor ? todayAnchor : todayAnchor.AddDays(-1);
        }

        private JsonCachePayload? TryReadCache()
        {
            try
            {
                lock (CacheFileLock)
                {
                    MigrateLegacySharedFileIfNeeded();

                    if (!File.Exists(_bodyPath))
                        return null;

                    string json = File.ReadAllText(_bodyPath);
                    if (string.IsNullOrWhiteSpace(json))
                        return null;

                    var meta = File.Exists(_metaPath)
                        ? JsonSerializer.Deserialize<CacheMeta>(File.ReadAllText(_metaPath))
                        : null;

                    return new JsonCachePayload
                    {
                        Json = json,
                        ETag = meta?.ETag,
                        LastFetchedUtc = meta?.LastFetchedUtc ?? default,
                        LastCheckedUtc = meta?.LastCheckedUtc ?? default
                    };
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[{_name}] Cache read failed.", ex);
                return null;
            }
        }

        /// <param name="writeBody">false면 메타(ETag·시각)만 기록한다 — 304 Not Modified 경로.</param>
        private void TryWriteCache(JsonCachePayload payload, bool writeBody)
        {
            try
            {
                lock (CacheFileLock)
                {
                    WriteEntry(_bodyPath, _metaPath, payload, writeBody);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[{_name}] Cache write failed.", ex);
            }
        }

        private static void WriteEntry(string bodyPath, string metaPath, JsonCachePayload payload, bool writeBody)
        {
            Directory.CreateDirectory(CacheDirectory);

            if (writeBody)
                WriteAtomic(bodyPath, payload.Json);

            var meta = new CacheMeta
            {
                LastFetchedUtc = payload.LastFetchedUtc,
                LastCheckedUtc = payload.LastCheckedUtc,
                ETag = payload.ETag
            };
            WriteAtomic(metaPath, JsonSerializer.Serialize(meta));
        }

        private static void WriteAtomic(string path, string content)
        {
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, path, overwrite: true);
        }

        /// <summary>
        /// 구버전 공유 캐시 파일(remote_json_cache.json)을 리소스별 파일로 나눈다. 프로세스당 1회, CacheFileLock 안에서 호출.
        /// 이미 리소스별 파일이 있는 항목은 건너뛰고, 끝나면 공유 파일을 지운다 (파싱 실패 시에도 — 어차피 쓸 수 없는 파일).
        /// </summary>
        private static void MigrateLegacySharedFileIfNeeded()
        {
            if (Interlocked.Exchange(ref _legacyMigrated, 1) != 0)
                return;

            try
            {
                if (!File.Exists(LegacySharedCacheFilePath))
                    return;

                var legacy = JsonSerializer.Deserialize<LegacySharedCacheFile>(File.ReadAllText(LegacySharedCacheFilePath));
                int migrated = 0;
                if (legacy?.Entries != null)
                {
                    foreach (var (name, payload) in legacy.Entries)
                    {
                        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(payload?.Json))
                            continue;

                        string bodyPath = GetBodyPath(name);
                        if (File.Exists(bodyPath))
                            continue;

                        WriteEntry(bodyPath, GetMetaPath(name), payload!, writeBody: true);
                        migrated++;
                    }
                }

                File.Delete(LegacySharedCacheFilePath);
                AppLogger.Info($"Legacy shared remote cache split into per-resource files. Migrated={migrated}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Legacy shared remote cache migration failed.", ex);
                try { File.Delete(LegacySharedCacheFilePath); } catch { }
            }
        }

        private static string GetBodyPath(string name) => Path.Combine(CacheDirectory, SanitizeFileName(name) + ".json");
        private static string GetMetaPath(string name) => Path.Combine(CacheDirectory, SanitizeFileName(name) + ".meta.json");

        private static string SanitizeFileName(string name)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = name.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (Array.IndexOf(invalid, chars[i]) >= 0)
                    chars[i] = '_';
            }

            return new string(chars);
        }

        private sealed class CacheMeta
        {
            public DateTime LastFetchedUtc { get; set; }
            public DateTime LastCheckedUtc { get; set; }
            public string? ETag { get; set; }
        }

        private sealed class LegacySharedCacheFile
        {
            public Dictionary<string, JsonCachePayload> Entries { get; set; } = new(StringComparer.Ordinal);
        }

        private sealed class JsonCachePayload
        {
            public DateTime LastFetchedUtc { get; set; }
            public DateTime LastCheckedUtc { get; set; }
            public string? ETag { get; set; }
            public string Json { get; set; } = string.Empty;
        }
    }
}
