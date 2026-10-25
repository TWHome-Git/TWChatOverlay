using System;
using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 알림 사운드가 어느 설정 볼륨을 따르는지 나타내는 채널.
    /// 호출자가 채널을 넘기면 파일 이름과 무관하게 해당 볼륨이 적용된다.
    /// </summary>
    public enum AlertChannel
    {
        /// <summary>볼륨 설정 없음 — 최대 볼륨.</summary>
        Default,
        ItemDrop,
        Highlight,
        MagicCircle,
        Reflection,
        ExpBuff,
        BuffTracker,
        Boss
    }

    /// <summary>
    /// 앱 알림 사운드를 재생하는 유틸리티 서비스입니다.
    /// </summary>
    public sealed class NotificationService
    {
        private const string SoundExtension = ".mp3";

        private readonly List<MediaPlayer> _activeMp3Players = new();
        private readonly object _mp3Lock = new();
        private readonly ConcurrentDictionary<string, string> _mp3CachePaths = new(StringComparer.OrdinalIgnoreCase);

        // 채널을 지정하지 않은 호출(파일 이름만 넘기는 기존 경로)용 파일 → 채널 표.
        // 키는 확장자와 보스용 _before1/_before3 접미사를 뺀 이름.
        private static readonly Dictionary<string, AlertChannel> ChannelByBaseName = new(StringComparer.OrdinalIgnoreCase)
        {
            ["drop"] = AlertChannel.ItemDrop,
            ["drop_low"] = AlertChannel.ItemDrop,
            ["highlight"] = AlertChannel.Highlight,
            ["wave"] = AlertChannel.MagicCircle,
            ["reflection"] = AlertChannel.Reflection,
            ["reflectionend"] = AlertChannel.Reflection,
            ["expbuffcheck"] = AlertChannel.ExpBuff,
            ["buffcheck"] = AlertChannel.BuffTracker,
            ["arkan"] = AlertChannel.Boss,
            ["scherzendo"] = AlertChannel.Boss,
            ["originofdoom"] = AlertChannel.Boss,
            ["confusedland"] = AlertChannel.Boss,
            ["event"] = AlertChannel.Boss
        };

        /// <summary>
        /// 리소스 사운드 파일을 재생합니다. 채널은 파일 이름에서 추정한다.
        /// </summary>
        public void PlayAlert(string fileName)
            => PlayAlert(fileName, ResolveChannel(fileName));

        /// <summary>
        /// 리소스 사운드 파일을 지정한 채널의 설정 볼륨으로 재생합니다.
        /// </summary>
        public void PlayAlert(string fileName, AlertChannel channel)
        {
            try
            {
                PlayAudioResource(NormalizeFileName(fileName), channel);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to play alert '{fileName}'. Falling back to system sound.", ex);
            }
        }

        /// <summary>
        /// 번들 사운드는 전부 mp3다. 확장자가 없거나 옛 .wav로 요청한 이름(원격 BossTimer.json의 sounds 등)도 mp3로 맞춘다.
        /// </summary>
        private static string NormalizeFileName(string fileName)
        {
            string ext = Path.GetExtension(fileName);
            if (string.IsNullOrEmpty(ext))
                return fileName + SoundExtension;

            return string.Equals(ext, ".wav", StringComparison.OrdinalIgnoreCase)
                ? Path.ChangeExtension(fileName, SoundExtension)
                : fileName;
        }

        private static AlertChannel ResolveChannel(string fileName)
        {
            string baseName = Path.GetFileNameWithoutExtension(fileName);
            if (baseName.EndsWith("_before1", StringComparison.OrdinalIgnoreCase) ||
                baseName.EndsWith("_before3", StringComparison.OrdinalIgnoreCase))
            {
                baseName = baseName[..^"_before1".Length];
            }

            return ChannelByBaseName.TryGetValue(baseName, out var channel) ? channel : AlertChannel.Default;
        }

        private void PlayAudioResource(string fileName, AlertChannel channel)
        {
            string tempFilePath = EnsureAudioTempFile(fileName);
            var player = new MediaPlayer();

            player.MediaOpened += (_, _) =>
            {
                player.Volume = ResolveAudioVolume(channel);
                if (AppLogger.IsLevelEnabled(LogLevel.INFO))
                    AppLogger.Info($"Audio resource opened successfully. File='{fileName}', Channel={channel}, Volume={player.Volume:0.##}, Path='{tempFilePath}'");
                player.Play();
            };

            player.MediaEnded += (_, _) =>
            {
                if (AppLogger.IsLevelEnabled(LogLevel.DEBUG))
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

            if (AppLogger.IsLevelEnabled(LogLevel.INFO))
                AppLogger.Info($"Opening audio resource. File='{fileName}', Path='{tempFilePath}'");
            player.Open(new Uri(tempFilePath, UriKind.Absolute));
        }

        private void ReleaseAudioPlayer(MediaPlayer player)
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

        /// <summary>채널 → 설정 볼륨. 아이템 드랍은 0~0.1, 나머지는 0~1로 자른다.</summary>
        private static double ResolveAudioVolume(AlertChannel channel)
        {
            try
            {
                if (AppServices.TryGet<Models.ChatSettings>() is Models.ChatSettings settings)
                {
                    return channel switch
                    {
                        AlertChannel.ItemDrop => Math.Clamp(settings.ItemDropAlertVolume, 0.0, 0.1),
                        AlertChannel.Highlight => Math.Clamp(settings.HighlightAlertVolume, 0.0, 1.0),
                        AlertChannel.MagicCircle => Math.Clamp(settings.MagicCircleAlertVolume, 0.0, 1.0),
                        AlertChannel.Reflection => Math.Clamp(settings.ReflectionPatternAlertVolume, 0.0, 1.0),
                        AlertChannel.ExpBuff => Math.Clamp(settings.ExpBuffAlertVolume, 0.0, 1.0),
                        AlertChannel.BuffTracker => Math.Clamp(settings.BuffTrackerEndSoundVolume, 0.0, 1.0),
                        AlertChannel.Boss => Math.Clamp(settings.BossAlertVolume, 0.0, 1.0),
                        _ => 1.0
                    };
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to resolve audio volume from settings. Channel={channel}", ex);
            }

            return channel == AlertChannel.ItemDrop ? 0.1 : 1.0;
        }

        private string EnsureAudioTempFile(string fileName)
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
            if (AppLogger.IsLevelEnabled(LogLevel.INFO))
                AppLogger.Info($"Audio resource extracted to temp file. File='{fileName}', Path='{tempFilePath}'");
            return tempFilePath;
        }

        public void DeleteCachedAudioFiles()
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
