using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace TWChatOverlay.Services
{
    public enum UpdateCheckResult
    {
        NoUpdate,
        UpdateDeclined,
        UpdateApplied,
        Failed
    }

    public static class UpdateService
    {
        private const string GitHubOwner = "TWHome-Git";
        private const string GitHubRepo = "TWChatOverlay";
        private static readonly string LatestReleaseUrl = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

        public static Version GetCurrentVersion()
            => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

        public static Task<UpdateCheckResult> CheckForUpdateAsync()
            => CheckForUpdateAsync(forceInstallLatest: false, showNoUpdateMessage: false);

        public static async Task<UpdateCheckResult> CheckForUpdateAsync(bool forceInstallLatest, bool showNoUpdateMessage)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TWChatOverlay", GetCurrentVersion().ToString()));
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                var response = await client.GetAsync(LatestReleaseUrl).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                    return UpdateCheckResult.Failed;

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string? tagName = root.GetProperty("tag_name").GetString();
                if (string.IsNullOrWhiteSpace(tagName))
                    return UpdateCheckResult.Failed;

                Version latestVersion = ParseVersion(tagName);
                Version currentVersion = GetCurrentVersion();
                bool hasNewer = latestVersion > currentVersion;
                bool shouldPrompt = forceInstallLatest || hasNewer;

                if (!shouldPrompt)
                {
                    if (showNoUpdateMessage)
                    {
                        await InvokeOnUIAsync(() =>
                        {
                            MessageBox.Show($"현재 버전이 최신입니다.\n현재: {currentVersion:0.0.0}", "수동 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);
                            return true;
                        }).ConfigureAwait(false);
                    }
                    return UpdateCheckResult.NoUpdate;
                }

                string releaseBody = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? string.Empty : string.Empty;
                string? downloadUrl = FindZipAssetUrl(root);
                string title = forceInstallLatest ? "수동 업데이트" : "업데이트 알림";
                string question = forceInstallLatest
                    ? $"최신 빌드를 다시 설치합니다.\n현재: {currentVersion:0.0.0}\n최신: {tagName}\n\n진행할까요?"
                    : $"새 버전이 있습니다.\n현재: {currentVersion:0.0.0}\n최신: {tagName}\n\n{TruncateBody(releaseBody)}\n\n지금 업데이트할까요?";

                MessageBoxResult choice = await InvokeOnUIAsync(() =>
                    MessageBox.Show(question, title, MessageBoxButton.YesNo, MessageBoxImage.Information)).ConfigureAwait(false);

                if (choice != MessageBoxResult.Yes)
                    return UpdateCheckResult.UpdateDeclined;

                if (string.IsNullOrWhiteSpace(downloadUrl))
                    return UpdateCheckResult.Failed;

                bool applied = await DownloadAndApplyUpdateAsync(client, downloadUrl).ConfigureAwait(false);
                return applied ? UpdateCheckResult.UpdateApplied : UpdateCheckResult.Failed;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"업데이트 확인 오류: {ex.Message}");
                return UpdateCheckResult.Failed;
            }
        }

        private static string? FindZipAssetUrl(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out var assets))
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    return asset.GetProperty("browser_download_url").GetString();
            }

            return null;
        }

        private static async Task<bool> DownloadAndApplyUpdateAsync(HttpClient client, string downloadUrl)
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            string tempDir = Path.Combine(Path.GetTempPath(), "TWChatOverlay_Update");
            string zipPath = Path.Combine(tempDir, "update.zip");
            string extractDir = Path.Combine(tempDir, "extracted");

            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    await using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    await contentStream.CopyToAsync(fileStream).ConfigureAwait(false);
                }

                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                string[] subDirs = Directory.GetDirectories(extractDir);
                string sourceDir = subDirs.Length == 1 && Directory.GetFiles(extractDir).Length == 0 ? subDirs[0] : extractDir;
                string exeName = Path.GetFileName(Environment.ProcessPath ?? $"{Process.GetCurrentProcess().ProcessName}.exe");
                string exePath = Path.Combine(appDir, exeName);
                string batPath = Path.Combine(tempDir, "update.bat");

                string batContent =
                    "@echo off\r\n" +
                    "chcp 65001 >nul\r\n" +
                    "timeout /t 2 /nobreak >nul\r\n" +
                    $"taskkill /f /im \"{exeName}\" >nul 2>&1\r\n" +
                    "timeout /t 2 /nobreak >nul\r\n" +
                    $"xcopy /s /y /q \"{sourceDir}\\*\" \"{appDir}\\\" >nul\r\n" +
                    $"start \"\" \"{exePath}\"\r\n" +
                    "del \"%~f0\"\r\n";

                File.WriteAllText(batPath, batContent);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = tempDir
                });

                await InvokeOnUIAsync(() =>
                {
                    Application.Current.Shutdown();
                    return true;
                }).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                await InvokeOnUIAsync(() =>
                {
                    MessageBox.Show($"업데이트 적용 중 오류가 발생했습니다.\n{ex.Message}", "업데이트 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return true;
                }).ConfigureAwait(false);
                return false;
            }
        }

        private static Version ParseVersion(string tag)
        {
            string cleaned = tag.TrimStart('v', 'V');
            return Version.TryParse(cleaned, out var version) ? version : new Version(0, 0, 0);
        }

        private static Task<T> InvokeOnUIAsync<T>(Func<T> func)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess())
                return Task.FromResult(func());

            var tcs = new TaskCompletionSource<T>();
            dispatcher.InvokeAsync(() =>
            {
                try { tcs.SetResult(func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        private static string TruncateBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return string.Empty;
            body = body.Trim();
            return body.Length > 200 ? body.Substring(0, 200) + "..." : body;
        }
    }
}
