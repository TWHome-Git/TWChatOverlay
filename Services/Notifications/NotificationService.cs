using System;
using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 앱 알림 사운드를 재생하는 유틸리티 서비스입니다.
    /// </summary>
    public static class NotificationService
    {
        private static readonly List<MediaPlayer> _activeMp3Players = new();
        private static readonly object _mp3Lock = new();
        private static readonly ConcurrentDictionary<string, string> _mp3CachePaths = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 리소스 사운드 파일을 재생합니다.
        /// </summary>
        public static void PlayAlert(string fileName)
        {
            try
            {
                string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name
                    ?? "TWChatOverlay";

                Uri uri = new Uri($"/{assemblyName};component/Sound/{fileName}", UriKind.Relative);

                PlayAudioResource(fileName);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to play alert '{fileName}'. Falling back to system sound.", ex);
            }
        }

        private static void PlayAudioResource(string fileName)
        {
            string tempFilePath = EnsureAudioTempFile(fileName);
            var player = new MediaPlayer();

            player.MediaOpened += (_, _) =>
            {
                player.Volume = ResolveAudioVolume(fileName);
                AppLogger.Info($"Audio resource opened successfully. File='{fileName}', Volume={player.Volume:0.##}, Path='{tempFilePath}'");
                player.Play();
            };

            player.MediaEnded += (_, _) =>
            {
                AppLogger.Debug($"Audio playback completed. File='{fileName}'");
                ReleaseAudioPlayer(player);
            };

            player.MediaFailed += (_, e) =>
            {
                AppLogger.Warn($"Audio playback failed. File='{fileName}', Path='{tempFilePath}', Error='{e.ErrorException?.Message}'");
                ReleaseAudioPlayer(player);
            };

            lock (_mp3Lock)
            {
                _activeMp3Players.Add(player);
            }

            AppLogger.Info($"Opening audio resource. File='{fileName}', Path='{tempFilePath}'");
            player.Open(new Uri(tempFilePath, UriKind.Absolute));
        }

        private static void ReleaseAudioPlayer(MediaPlayer player)
        {
            try
            {
                player.Stop();
                player.Close();
            }
            catch { }

            lock (_mp3Lock)
            {
                _activeMp3Players.Remove(player);
            }
        }

        private static double ResolveAudioVolume(string fileName)
        {
            try
            {
                foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
                {
                    if (window is Views.MainWindow mainWindow && mainWindow.DataContext is Models.ChatSettings settings)
                    {
                        return fileName.ToLowerInvariant() switch
                        {
                            "drop.mp3" => Math.Max(0.0, Math.Min(0.1, settings.ItemDropAlertVolume)),
                            "drop_low.mp3" => Math.Max(0.0, Math.Min(0.1, settings.ItemDropAlertVolume)),
                            "highlight.wav" => Math.Max(0.0, Math.Min(1.0, settings.HighlightAlertVolume)),
                            "magiccircle.wav" => Math.Max(0.0, Math.Min(1.0, settings.MagicCircleAlertVolume)),
                            "expbuffcheck.wav" => Math.Max(0.0, Math.Min(1.0, settings.ExpBuffAlertVolume)),
                            "expcheck.wav" => Math.Max(0.0, Math.Min(1.0, settings.ExpBuffAlertVolume)),
                            "buffcheck.wav" => Math.Max(0.0, Math.Min(1.0, settings.BuffTrackerEndSoundVolume)),
                            "arkan.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "arkan_before1.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "arkan_before3.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "scherzendo.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "scherzendo_before1.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "scherzendo_before3.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "originofdoom.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "originofdoom_before1.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "originofdoom_before3.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "confusedland.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "confusedland_before1.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "confusedland_before3.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "event.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "event_before1.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            "event_before3.wav" => Math.Max(0.0, Math.Min(1.0, settings.BossAlertVolume)),
                            _ => 1.0
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to resolve audio volume from settings. File='{fileName}'", ex);
            }

            return string.Equals(fileName, "drop.mp3", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, "drop_low.mp3", StringComparison.OrdinalIgnoreCase)
                ? 0.1
                : 1.0;
        }

        private static string EnsureAudioTempFile(string fileName)
        {
            if (_mp3CachePaths.TryGetValue(fileName, out string? existingPath) && File.Exists(existingPath))
            {
                return existingPath;
            }

            string assemblyName = System.Reflection.Assembly.GetExecutingAssembly().GetName().Name
                ?? "TWChatOverlay";
            Uri resourceUri = new Uri($"/{assemblyName};component/Sound/{fileName}", UriKind.Relative);
            var resourceStream = System.Windows.Application.GetResourceStream(resourceUri);
            if (resourceStream == null)
            {
                throw new FileNotFoundException($"Embedded audio resource was not found: {fileName}");
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), "TWChatOverlay", "SoundCache");
            Directory.CreateDirectory(tempDirectory);
            string tempFilePath = Path.Combine(tempDirectory, fileName);

            using (var input = resourceStream.Stream)
            using (var output = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                input.CopyTo(output);
            }

            _mp3CachePaths[fileName] = tempFilePath;
            AppLogger.Info($"Audio resource extracted to temp file. File='{fileName}', Path='{tempFilePath}'");
            return tempFilePath;
        }

        public static void DeleteCachedAudioFiles()
        {
            try
            {
                lock (_mp3Lock)
                {
                    foreach (var player in _activeMp3Players.ToArray())
                    {
                        try
                        {
                            player.Stop();
                            player.Close();
                        }
                        catch { }
                    }

                    _activeMp3Players.Clear();
                }

                foreach (var path in _mp3CachePaths.Values)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Failed to delete cached MP3 file. Path='{path}'", ex);
                    }
                }

                _mp3CachePaths.Clear();

                string tempDirectory = Path.Combine(Path.GetTempPath(), "TWChatOverlay", "SoundCache");
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }

                AppLogger.Info("Cached MP3 audio files deleted.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to delete cached MP3 audio files.", ex);
            }
        }
    }
}
