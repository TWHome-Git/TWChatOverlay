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
    /// <summary>
    /// GitHub Releases를 통해 프로그램 버전을 확인하고 자동 업데이트를 수행하는 서비스입니다.
    /// </summary>
    public static class UpdateService
    {
        private const string GitHubOwner = "TWHome-Git";
        private const string GitHubRepo = "TWChatOverlay";
        private static readonly string LatestReleaseUrl =
            $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";

        /// <summary>
        /// 현재 어셈블리의 버전을 반환합니다.
        /// </summary>
        public static Version GetCurrentVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        }

        /// <summary>
        /// GitHub에서 최신 릴리스를 확인하고, 새 버전이 있으면 사용자에게 업데이트를 안내합니다.
        /// </summary>
        public static async Task CheckForUpdateAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue("TWChatOverlay", GetCurrentVersion().ToString()));
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                var response = await client.GetAsync(LatestReleaseUrl);
                if (!response.IsSuccessStatusCode)
                    return;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var tagName = root.GetProperty("tag_name").GetString();
                if (string.IsNullOrEmpty(tagName))
                    return;

                var latestVersion = ParseVersion(tagName);
                var currentVersion = GetCurrentVersion();

                if (latestVersion <= currentVersion)
                    return;

                var releaseBody = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
                var downloadUrl = FindZipAssetUrl(root);
                var htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() : null;

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    var result = await InvokeOnUIAsync(() => MessageBox.Show(
                        $"새로운 버전이 있습니다!\n\n현재: {currentVersion.ToString(3)}\n최신: {tagName}\n\n{TruncateBody(releaseBody)}\n\nGitHub 릴리스 페이지를 열겠습니까?",
                        "업데이트 알림",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information));

                    if (result == MessageBoxResult.Yes && !string.IsNullOrEmpty(htmlUrl))
                        Process.Start(new ProcessStartInfo(htmlUrl) { UseShellExecute = true });
                    return;
                }

                var updateResult = await InvokeOnUIAsync(() => MessageBox.Show(
                    $"새로운 버전이 있습니다!\n\n현재: {currentVersion.ToString(3)}\n최신: {tagName}\n\n{TruncateBody(releaseBody)}\n\n지금 업데이트하시겠습니까?",
                    "업데이트 알림",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information));

                if (updateResult == MessageBoxResult.Yes)
                {
                    await DownloadAndApplyUpdateAsync(client, downloadUrl);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"업데이트 확인 중 오류 발생: {ex.Message}");
            }
        }

        /// <summary>
        /// 릴리스 에셋에서 zip 파일의 다운로드 URL을 찾습니다.
        /// </summary>
        private static string? FindZipAssetUrl(JsonElement root)
        {
            if (!root.TryGetProperty("assets", out var assets))
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    return asset.GetProperty("browser_download_url").GetString();
                }
            }
            return null;
        }

        /// <summary>
        /// 업데이트 zip을 다운로드하고 배치 스크립트를 통해 적용합니다.
        /// </summary>
        private static async Task DownloadAndApplyUpdateAsync(HttpClient client, string downloadUrl)
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            var tempDir = Path.Combine(Path.GetTempPath(), "TWChatOverlay_Update");
            var zipPath = Path.Combine(tempDir, "update.zip");
            var extractDir = Path.Combine(tempDir, "extracted");

            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
                Directory.CreateDirectory(tempDir);

                await InvokeOnUIAsync(() =>
                {
                    MessageBox.Show(
                        "업데이트 파일을 다운로드합니다.\n확인을 누르면 다운로드가 시작됩니다.\n\n완료될 때까지 잠시 기다려주세요.",
                        "업데이트 다운로드",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return true;
                });

                using (var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    await contentStream.CopyToAsync(fileStream);
                }

                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                var subDirs = Directory.GetDirectories(extractDir);
                var sourceDir = extractDir;
                if (subDirs.Length == 1 && Directory.GetFiles(extractDir).Length == 0)
                    sourceDir = subDirs[0];

                var batPath = Path.Combine(tempDir, "update.bat");
                var processName = Process.GetCurrentProcess().ProcessName;
                var exeName = Path.GetFileName(Environment.ProcessPath ?? $"{processName}.exe");
                var exePath = Path.Combine(appDir, exeName);

                var batContent =
                    "@echo off\r\n" +
                    "chcp 65001 >nul\r\n" +
                    "echo 업데이트를 적용하고 있습니다...\r\n" +
                    "timeout /t 2 /nobreak >nul\r\n" +
                    $"taskkill /f /im \"{exeName}\" >nul 2>&1\r\n" +
                    "timeout /t 2 /nobreak >nul\r\n" +
                    $"xcopy /s /y /q \"{sourceDir}\\*\" \"{appDir}\\\" >nul\r\n" +
                    $"if errorlevel 1 (\r\n" +
                    $"  echo xcopy 실패. robocopy로 재시도합니다...\r\n" +
                    $"  robocopy \"{sourceDir}\" \"{appDir}\" /s /is /it >nul\r\n" +
                    $")\r\n" +
                    "echo 업데이트가 완료되었습니다. 프로그램을 재시작합니다...\r\n" +
                    $"start \"\" \"{exePath}\"\r\n" +
                    "del \"%~f0\"\r\n";

                File.WriteAllText(batPath, batContent, new System.Text.UTF8Encoding(false));

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = tempDir
                });

                await InvokeOnUIAsync(() => { Application.Current.Shutdown(); return true; });
            }
            catch (Exception ex)
            {
                await InvokeOnUIAsync(() =>
                {
                    MessageBox.Show(
                        $"업데이트 다운로드 중 오류가 발생했습니다.\n{ex.Message}\n\n{ex.StackTrace}",
                        "업데이트 오류",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return true;
                });
            }
        }

        /// <summary>
        /// 태그 문자열에서 버전을 파싱합니다.
        /// </summary>
        private static Version ParseVersion(string tag)
        {
            var cleaned = tag.TrimStart('v', 'V');
            return Version.TryParse(cleaned, out var version) ? version : new Version(0, 0, 0);
        }

        /// <summary>
        /// UI 스레드에서 함수를 실행하고 결과를 반환합니다.
        /// </summary>
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

        /// <summary>
        /// 릴리스 노트를 최대 200자로 자릅니다.
        /// </summary>
        private static string TruncateBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return "";
            body = body.Trim();
            return body.Length > 200 ? body.Substring(0, 200) + "..." : body;
        }
    }
}
