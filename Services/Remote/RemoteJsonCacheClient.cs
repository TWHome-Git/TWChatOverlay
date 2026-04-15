using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 원격 JSON을 TTL + ETag + 로컬 fallback 전략으로 가져오는 공용 캐시 클라이언트.
    /// </summary>
    public sealed class RemoteJsonCacheClient
    {
        private readonly string _name;
        private readonly string _url;
        private readonly TimeSpan _ttl;
        private readonly HttpClient _httpClient;
        private readonly int _refreshAnchorHourLocal;
        private readonly bool _forceRemoteCheckOnFirstCall;
        private bool _hasTriedRemoteInThisProcess;
        private DateTime _nextRemoteRetryUtc;
        private static readonly TimeSpan RemoteFailureBackoff = TimeSpan.FromMinutes(3);
        private static readonly object CacheFileLock = new();
        private static readonly string SharedCacheFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Cache",
            "remote_json_cache.json");

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
                        TryWriteCache(cache);
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
                        TryWriteCache(next);
                        return json;
                    }

                    _nextRemoteRetryUtc = now.Add(RemoteFailureBackoff);
                    Debug.WriteLine($"[{_name}] Remote responded {(int)response.StatusCode}. Using fallback cache when available.");
                }
                catch (Exception ex)
                {
                    _nextRemoteRetryUtc = now.Add(RemoteFailureBackoff);
                    Debug.WriteLine($"[{_name}] Remote fetch failed: {ex.Message}");
                }
            }

            if (cache != null && !string.IsNullOrWhiteSpace(cache.Json))
            {
                return cache.Json;
            }

            return null;
        }

        public void DeleteCache()
        {
            try
            {
                lock (CacheFileLock)
                {
                    var file = ReadSharedCacheFile();
                    if (file.Entries.Remove(_name))
                    {
                        WriteSharedCacheFile(file);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{_name}] Cache delete failed: {ex.Message}");
            }
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
                    var file = ReadSharedCacheFile();
                    if (file.Entries.TryGetValue(_name, out var sharedPayload) &&
                        !string.IsNullOrWhiteSpace(sharedPayload.Json))
                    {
                        return sharedPayload;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{_name}] Cache read failed: {ex.Message}");
                return null;
            }
        }

        private void TryWriteCache(JsonCachePayload payload)
        {
            try
            {
                lock (CacheFileLock)
                {
                    var file = ReadSharedCacheFile();
                    file.Entries[_name] = payload;
                    WriteSharedCacheFile(file);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[{_name}] Cache write failed: {ex.Message}");
            }
        }

        private static SharedJsonCacheFile ReadSharedCacheFile()
        {
            try
            {
                if (!File.Exists(SharedCacheFilePath))
                    return new SharedJsonCacheFile();

                string text = File.ReadAllText(SharedCacheFilePath);
                return JsonSerializer.Deserialize<SharedJsonCacheFile>(text) ?? new SharedJsonCacheFile();
            }
            catch
            {
                return new SharedJsonCacheFile();
            }
        }

        private static void WriteSharedCacheFile(SharedJsonCacheFile file)
        {
            string? directory = Path.GetDirectoryName(SharedCacheFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(SharedCacheFilePath, JsonSerializer.Serialize(file));
        }

        private sealed class SharedJsonCacheFile
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
