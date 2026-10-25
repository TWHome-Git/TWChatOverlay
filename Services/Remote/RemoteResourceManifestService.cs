using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TWChatOverlay.Services
{
    public static class RemoteResourceManifestService
    {
        private const string ManifestUrl = RemoteEndpoints.Manifest;
        private static readonly RemoteJsonCacheClient ManifestCacheClient = new(
            "RemoteResourceManifestService.Manifest",
            ManifestUrl,
            TimeSpan.FromMinutes(10),
            RemoteEndpoints.SharedHttpClient,
            forceRemoteCheckOnFirstCall: true);

        private static readonly string LocalVersionPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Cache",
            "resource_manifest_versions.json");

        // 마지막으로 읽은 manifest. ShouldForceRefreshAsync 직후의 MarkResourceVersionAppliedAsync가 캐시 파일을 다시 읽지 않도록 넘겨 준다.
        private static ResourceManifest? _lastManifest;

        /// <summary>
        /// 원격 manifest의 리소스 버전이 마지막으로 적용한 버전과 다른지. manifest는 캐시 TTL(10분)을 따르므로
        /// (프로세스 첫 호출만 원격 확인) 원격 갱신은 최대 10분 늦게 감지된다.
        /// </summary>
        public static async Task<bool> ShouldForceRefreshAsync(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
                return false;

            var manifest = await LoadManifestAsync().ConfigureAwait(false);
            if (manifest?.Resources == null || !manifest.Resources.TryGetValue(resourceName, out int remoteVersion))
                return false;

            var local = ReadLocalVersions();
            int localVersion = local.TryGetValue(resourceName, out int value) ? value : -1;
            return remoteVersion != localVersion;
        }

        public static async Task MarkResourceVersionAppliedAsync(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
                return;

            var manifest = _lastManifest ?? await LoadManifestAsync().ConfigureAwait(false);
            if (manifest?.Resources == null || !manifest.Resources.TryGetValue(resourceName, out int remoteVersion))
                return;

            var local = ReadLocalVersions();
            local[resourceName] = remoteVersion;
            WriteLocalVersions(local);
        }

        private static async Task<ResourceManifest?> LoadManifestAsync()
        {
            string? json = await ManifestCacheClient.GetJsonAsync(forceRefresh: false).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var manifest = JsonSerializer.Deserialize<ResourceManifest>(json);
            if (manifest != null)
                _lastManifest = manifest;
            return manifest;
        }

        private static Dictionary<string, int> ReadLocalVersions()
        {
            try
            {
                if (!File.Exists(LocalVersionPath))
                    return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                string json = File.ReadAllText(LocalVersionPath);
                return JsonSerializer.Deserialize<Dictionary<string, int>>(json)
                       ?? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void WriteLocalVersions(Dictionary<string, int> versions)
        {
            string? dir = Path.GetDirectoryName(LocalVersionPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(LocalVersionPath, JsonSerializer.Serialize(versions));
        }

        private sealed class ResourceManifest
        {
            [JsonPropertyName("version")]
            public int Version { get; set; }

            [JsonPropertyName("resources")]
            public Dictionary<string, int> Resources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        }
    }
}
