using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using TWChatOverlay.Views;

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
        private const string LatestReleaseUrl = RemoteEndpoints.LatestReleaseApi;
        private static readonly TimeSpan UpdateMetadataTimeout = TimeSpan.FromSeconds(8);
        private static readonly TimeSpan UpdateDownloadTimeout = TimeSpan.FromMinutes(15);

        private sealed record ReleaseInfo(string TagName, Version LatestVersion, string ReleaseBody, string? DownloadUrl, bool IsPrerelease = false);

        public static Version GetCurrentVersion()
            => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

        public static Task<UpdateCheckResult> CheckForUpdateAsync()
            => CheckForUpdateAsync(forceInstallLatest: false, showNoUpdateMessage: false);

        public static async Task<UpdateCheckResult> CheckForUpdateAsync(bool forceInstallLatest, bool showNoUpdateMessage)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                AppLogger.Info($"Starting update check. ForceInstallLatest={forceInstallLatest}, ShowNoUpdateMessage={showNoUpdateMessage}");
                using var metadataClient = CreateClient(UpdateMetadataTimeout);

                // 수동 업데이트는 베타(프리릴리즈)를 포함한 최신 릴리즈를, 자동 확인은 정식 릴리즈만 본다
                ReleaseInfo? release = await GetLatestReleaseInfoAsync(metadataClient, includePrerelease: forceInstallLatest).ConfigureAwait(false);
                if (release == null)
                {
                    AppLogger.Warn($"Update check returned no release metadata after {stopwatch.ElapsedMilliseconds} ms.");
                    return UpdateCheckResult.Failed;
                }

                Version currentVersion = GetCurrentVersion();
                bool hasNewer = release.LatestVersion > currentVersion;
                bool shouldPrompt = forceInstallLatest || hasNewer;
#if DEBUG
                // 디버그 빌드: 창 디자인 확인용으로 최신 버전이어도 업데이트 창을 항상 띄운다
                shouldPrompt = true;
#endif
                AppLogger.Info($"Update metadata loaded in {stopwatch.ElapsedMilliseconds} ms. Current={FormatVersion(currentVersion)}, Latest={release.TagName}, HasNewer={hasNewer}");

                if (!shouldPrompt)
                {
                    if (showNoUpdateMessage)
                    {
                        await InvokeOnUIAsync(() =>
                        {
                            DialogService.ShowTopmost(
                                null,
                                $"현재 버전이 최신입니다.\n현재: {FormatVersion(currentVersion)}",
                                "업데이트 알림",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                            return true;
                        }).ConfigureAwait(false);
                    }

                    AppLogger.Info($"Update check completed with no update after {stopwatch.ElapsedMilliseconds} ms.");
                    return UpdateCheckResult.NoUpdate;
                }

                bool confirmed = await ShowUpdatePromptAsync(currentVersion, release, forceInstallLatest).ConfigureAwait(false);
                if (!confirmed)
                {
                    AppLogger.Info($"Update prompt was declined after {stopwatch.ElapsedMilliseconds} ms.");
                    return UpdateCheckResult.UpdateDeclined;
                }

                if (string.IsNullOrWhiteSpace(release.DownloadUrl))
                {
                    AppLogger.Warn("Update metadata did not include a downloadable zip asset.");
                    return UpdateCheckResult.Failed;
                }

                using var downloadClient = CreateClient(UpdateDownloadTimeout);
                bool applied = await DownloadAndApplyUpdateAsync(downloadClient, release.DownloadUrl).ConfigureAwait(false);
                AppLogger.Info($"Update apply finished with result={applied} after {stopwatch.ElapsedMilliseconds} ms.");
                return applied ? UpdateCheckResult.UpdateApplied : UpdateCheckResult.Failed;
            }
            catch (OperationCanceledException ex)
            {
                AppLogger.Warn($"Update check timed out after {stopwatch.ElapsedMilliseconds} ms.", ex);
                return UpdateCheckResult.Failed;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Update check failed after {stopwatch.ElapsedMilliseconds} ms.", ex);
                return UpdateCheckResult.Failed;
            }
        }

        private static async Task<ReleaseInfo?> GetLatestReleaseInfoAsync(HttpClient client, bool includePrerelease = false)
        {
            using var cts = new CancellationTokenSource(UpdateMetadataTimeout);
            string url = includePrerelease ? RemoteEndpoints.ReleaseListApi : LatestReleaseUrl;
            using var response = await client.GetAsync(url, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Warn($"Update metadata request returned HTTP {(int)response.StatusCode}.");
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!includePrerelease)
                return ParseReleaseElement(doc.RootElement);

            // 목록은 생성일 내림차순 — 초안을 제외한 가장 최근 릴리즈(베타 포함)를 고른다
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                bool isDraft = element.TryGetProperty("draft", out var draftEl) && draftEl.GetBoolean();
                if (isDraft)
                    continue;
                return ParseReleaseElement(element);
            }

            return null;
        }

        private static ReleaseInfo? ParseReleaseElement(JsonElement root)
        {
            string? tagName = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrWhiteSpace(tagName))
                return null;

            Version latestVersion = ParseVersion(tagName);
            string releaseBody = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? string.Empty : string.Empty;
            string? downloadUrl = FindZipAssetUrl(root);
            bool isPrerelease = root.TryGetProperty("prerelease", out var preEl) && preEl.GetBoolean();

            return new ReleaseInfo(tagName.Trim(), latestVersion, releaseBody.Trim(), downloadUrl, isPrerelease);
        }

        private static HttpClient CreateClient(TimeSpan timeout)
        {
            var client = new HttpClient
            {
                Timeout = timeout
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TWChatOverlay", GetCurrentVersion().ToString()));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        private static Task<bool> ShowUpdatePromptAsync(Version currentVersion, ReleaseInfo release, bool forceInstallLatest)
        {
            string windowTitle = forceInstallLatest ? "수동 업데이트" : "업데이트 알림";
            string headline = forceInstallLatest
                ? (release.IsPrerelease ? "베타 버전을 설치합니다." : "최신 버전을 다시 설치합니다.")
                : "업데이트 내역";
            string footerHint = forceInstallLatest
                ? "업데이트를 시작하면 앱이 잠시 종료되고 최신 파일이 다시 설치됩니다."
                : "업데이트를 시작하면 앱이 잠시 종료되고 최신 파일이 적용됩니다.";
            string confirmText = forceInstallLatest ? "다시 설치" : "업데이트";
            string currentText = $"v{FormatVersion(currentVersion)}";
            string latestText = release.TagName.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? release.TagName
                : $"v{release.TagName}";
            if (release.IsPrerelease)
                latestText += " (베타)";

            return InvokeOnUIAsync(() =>
            {
                var dialog = new UpdateDialogWindow(
                    windowTitle,
                    headline,
                    currentText,
                    latestText,
                    release.ReleaseBody,
                    footerHint,
                    confirmText);

                Window? owner = ResolveOwnerWindow();
                if (owner != null)
                {
                    dialog.Owner = owner;
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                else
                {
                    dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }

                dialog.Topmost = true;
                return dialog.ShowDialog() == true;
            });
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

                await DownloadZipWithRetryAsync(client, downloadUrl, zipPath).ConfigureAwait(false);

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
                    ChatWindowHub.BeginShutdown();
                    Application.Current.Shutdown();
                    return true;
                }).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                await InvokeOnUIAsync(() =>
                {
                    DialogService.ShowTopmost(
                        null,
                        $"업데이트 적용 중 오류가 발생했습니다.\n{ex.Message}",
                        "업데이트 오류",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return true;
                }).ConfigureAwait(false);
                return false;
            }
        }

        /// <summary>
        /// 업데이트 zip을 다운로드합니다. 네트워크 오류 시 최대 3회 재시도(1s, 2s 백오프).
        /// 타임아웃(OperationCanceledException)은 재시도하지 않습니다.
        /// </summary>
        private static async Task DownloadZipWithRetryAsync(HttpClient client, string downloadUrl, string zipPath)
        {
            const int maxAttempts = 3;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    using var response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    await using var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                    await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                    await contentStream.CopyToAsync(fileStream).ConfigureAwait(false);
                    if (attempt > 1)
                        AppLogger.Info($"Update download succeeded on attempt {attempt}/{maxAttempts}.");
                    return;
                }
                catch (Exception ex) when (attempt < maxAttempts && ex is not OperationCanceledException)
                {
                    int delayMs = 1000 * (1 << (attempt - 1)); // 1s, 2s
                    AppLogger.Warn($"Update download attempt {attempt}/{maxAttempts} failed; retrying in {delayMs} ms.", ex);
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }
            }
        }

        private static Version ParseVersion(string tag)
        {
            string cleaned = tag.TrimStart('v', 'V');
            // 베타 태그(v5.1.0-beta 등)는 접미사를 떼고 버전만 파싱한다
            int suffixIndex = cleaned.IndexOf('-');
            if (suffixIndex > 0)
                cleaned = cleaned[..suffixIndex];
            return Version.TryParse(cleaned, out var version) ? version : new Version(0, 0, 0);
        }

        private static string FormatVersion(Version version)
            => version.Build >= 0 ? version.ToString(3) : version.ToString(2);

        private static Window? ResolveOwnerWindow()
        {
            return Application.Current?.Windows
                .OfType<Window>()
                .Where(window => window.IsVisible)
                .OrderByDescending(window => window.IsActive)
                .FirstOrDefault();
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
    }
}
