using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TWChatOverlay.Services
{
    public static class RemoteResourceManifestService
    {
        private const string ManifestUrl = "https://raw.githubusercontent.com/TWHome-Git/TWHomeDB/main/manifest.json";
        private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly RemoteJsonCacheClient ManifestCacheClient = new(
            "RemoteResourceManifestService.Manifest",
            ManifestUrl,
            TimeSpan.FromMinutes(10),
            HttpClient,
            forceRemoteCheckOnFirstCall: true);

        private static readonly string LocalVersionPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Cache",
            "resource_manifest_versions.json");

        public static async Task<bool> ShouldForceRefreshAsync(string resourceName)
        {
            if (string.IsNullOrWhiteSpace(resourceName))
                return false;

            string? json = await ManifestCacheClient.GetJsonAsync(forceRefresh: true).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return false;

            var manifest = JsonSerializer.Deserialize<ResourceManifest>(json);
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

            string? json = await ManifestCacheClient.GetJsonAsync(forceRefresh: false).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var manifest = JsonSerializer.Deserialize<ResourceManifest>(json);
            if (manifest?.Resources == null || !manifest.Resources.TryGetValue(resourceName, out int remoteVersion))
                return;

            var local = ReadLocalVersions();
            local[resourceName] = remoteVersion;
            WriteLocalVersions(local);
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
