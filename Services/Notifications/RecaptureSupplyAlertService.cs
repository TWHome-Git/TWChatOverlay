using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    public static class RecaptureSupplyAlertService
    {
        private static readonly Regex TriggerRegex = new(
            @"경보\s*장치\s*4개를\s*모두\s*해제하고\s*보급품이\s*보관\s*되어\s*있는\s*막사를\s*찾으시오\.",
            RegexOptions.Compiled);
        private static readonly Regex CompletionRegex = new(
            @"보급품\s*탈환에\s*성공하였",
            RegexOptions.Compiled);

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        private static readonly string CacheDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ImageCache");
        private static readonly string CacheFilePath = Path.Combine(CacheDirectoryPath, "RecaptureSupplies.png");
        private static readonly string RemoteImageUrl = RemoteEndpoints.RecaptureSupplyImage;
        private static readonly object SyncRoot = new();

        private static Task<bool>? _preloadTask;
        private static RecaptureSupplyWindow? _window;

        public static Task<bool> PreloadAsync()
        {
            lock (SyncRoot)
            {
                if (IsCacheValid())
                {
                    _preloadTask = Task.FromResult(true);
                    return _preloadTask;
                }

                if (_preloadTask != null && _preloadTask.IsCompleted)
                {
                    _preloadTask = null;
                }

                if (_preloadTask != null)
                    return _preloadTask;

                _preloadTask = LoadOrDownloadImageAsync();
                return _preloadTask;
            }
        }

        public static void Observe(string formattedText)
        {
            if (string.IsNullOrWhiteSpace(formattedText) || !TriggerRegex.IsMatch(formattedText))
            {
                if (!string.IsNullOrWhiteSpace(formattedText) && CompletionRegex.IsMatch(formattedText))
                {
                    Close();
                }
                return;
            }

            _ = ShowAsync();
        }

        /// <summary>잠금 해제 모드에서 위치/크기를 조정할 수 있게 지도 창을 미리보기로 띄운다.</summary>
        public static void ShowPositionPreview(ChatSettings settings, bool force = false)
        {
            _ = ShowAsync(isPreview: true);
        }

        /// <summary>미리보기를 닫는다. (창의 Closed 처리에서 위치/크기가 저장된다)</summary>
        public static void ClosePositionPreview()
        {
            Close();
        }

        public static void Close()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _window?.Close();
                }
                catch { }
                finally
                {
                    _window = null;
                }
            }));
        }

        private static async Task ShowAsync(bool isPreview = false)
        {
            if (!await EnsureImageReadyAsync().ConfigureAwait(false))
                return;

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ChatSettings? settings = GetSharedSettings();

                // 사용자가 미니 지도 표시를 꺼둔 경우 (미리보기는 위치 조정을 위해 항상 표시)
                if (!isPreview && settings != null && !settings.ShowRecaptureSupplyMap)
                    return;

                if (_window == null || !_window.IsLoaded)
                {
                    _window = new RecaptureSupplyWindow(CacheFilePath);
                    RecaptureSupplyWindow createdWindow = _window;
                    _window.Closed += (_, _) =>
                    {
                        try
                        {
                            if (settings != null)
                            {
                                settings.RecaptureSupplyWindowLeft = createdWindow.Left;
                                settings.RecaptureSupplyWindowTop = createdWindow.Top;
                                if (createdWindow.ActualWidth > 0)
                                    settings.RecaptureSupplyWindowWidth = createdWindow.ActualWidth;
                                if (createdWindow.ActualHeight > 0)
                                    settings.RecaptureSupplyWindowHeight = createdWindow.ActualHeight;
                                ConfigService.SaveDeferred(settings);
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn("Failed to save Recapture supply window position on close.", ex);
                        }

                        if (ReferenceEquals(_window, createdWindow))
                            _window = null;
                    };
                }

                if (settings != null)
                {
                    if (settings.RecaptureSupplyWindowLeft.HasValue)
                        _window.Left = settings.RecaptureSupplyWindowLeft.Value;
                    if (settings.RecaptureSupplyWindowTop.HasValue)
                        _window.Top = settings.RecaptureSupplyWindowTop.Value;
                    if (settings.RecaptureSupplyWindowWidth.HasValue)
                        _window.Width = settings.RecaptureSupplyWindowWidth.Value;
                    if (settings.RecaptureSupplyWindowHeight.HasValue)
                        _window.Height = settings.RecaptureSupplyWindowHeight.Value;
                    if (!settings.RecaptureSupplyWindowLeft.HasValue || !settings.RecaptureSupplyWindowTop.HasValue)
                        _window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                    else
                        _window.WindowStartupLocation = WindowStartupLocation.Manual;
                }

                if (!_window.IsVisible)
                {
                    _window.Show();
                }

                TopmostWindowHelper.BringToTopmost(_window);
            });
        }

        private static ChatSettings? GetSharedSettings()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is Views.MainWindow main && main.DataContext is ChatSettings settings)
                    {
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to resolve shared settings for Recapture supply alert.", ex);
            }

            return null;
        }

        private static async Task<bool> EnsureImageReadyAsync()
        {
            if (IsCacheValid())
            {
                return true;
            }

            Task<bool> preload = PreloadAsync();
            bool ready = await preload.ConfigureAwait(false);
            return ready;
        }

        private static bool IsCacheValid()
        {
            try
            {
                return File.Exists(CacheFilePath) && new FileInfo(CacheFilePath).Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static async Task<bool> LoadOrDownloadImageAsync()
        {
            try
            {
                if (IsCacheValid())
                    return true;

                Directory.CreateDirectory(CacheDirectoryPath);

                using var response = await HttpClient.GetAsync(RemoteImageUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    AppLogger.Warn($"Recapture supply image was not available. Status={(int)response.StatusCode} {response.ReasonPhrase}");
                    return false;
                }

                await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var memory = new MemoryStream();
                await input.CopyToAsync(memory).ConfigureAwait(false);
                memory.Position = 0;

                BitmapDecoder decoder = BitmapDecoder.Create(
                    memory,
                    BitmapCreateOptions.PreservePixelFormat,
                    BitmapCacheOption.OnLoad);

                var pngEncoder = new PngBitmapEncoder();
                foreach (BitmapFrame frame in decoder.Frames)
                    pngEncoder.Frames.Add(frame);

                await using var output = new FileStream(CacheFilePath, FileMode.Create, FileAccess.Write, FileShare.Read);
                pngEncoder.Save(output);

                AppLogger.Info($"Recapture supply image cached at '{CacheFilePath}'.");
                return IsCacheValid();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to preload recapture supply image.", ex);
                return false;
            }
        }
    }
}
