using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Threading;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    public sealed class BossAlarmSchedulerService
    {
        private readonly ChatSettings _settings;
        private readonly DispatcherTimer _timer;
        private readonly HashSet<string> _firedKeys = new(StringComparer.Ordinal);

        public BossAlarmSchedulerService(ChatSettings settings)
        {
            _settings = settings;
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += Timer_Tick;
        }

        public void Start()
        {
            _ = BossTimerService.EnsureLoadedAsync();
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
            _firedKeys.Clear();
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            try
            {
                DateTime now = DateTime.Now;
                bool triggered = false;

                foreach (var boss in BossTimerService.GetBosses())
                {
                    BossAlertConfig config = _settings.GetOrCreateBossAlertConfig(boss.Id);
                    if (CheckAlarm(boss, now, config.Alert3MinutesBefore, TimeSpan.FromMinutes(3), "3분 전") ||
                        CheckAlarm(boss, now, config.Alert1MinuteBefore, TimeSpan.FromMinutes(1), "1분 전") ||
                        CheckAlarm(boss, now, config.AlertAtSpawn, TimeSpan.FromSeconds(5), "5초 전"))
                    {
                        triggered = true;
                        break;
                    }
                }

                if (triggered)
                {
                    AppLogger.Debug("Boss alarm tick ended after highest-priority match.");
                }

                CleanupFiredKeys(now.AddHours(-2));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Boss alarm timer tick failed.", ex);
            }
        }

        private bool CheckAlarm(BossTimerService.BossTimerDefinition boss, DateTime now, bool isEnabled, TimeSpan offsetBefore, string label)
        {
            if (!isEnabled)
                return false;

            DateTime nowSecond = new(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);

            foreach (DateTime date in new[] { now.Date.AddDays(-1), now.Date, now.Date.AddDays(1) })
            {
                foreach (DateTime occurrence in BossTimerService.GetOccurrences(boss, date))
                {
                    DateTime triggerTime = occurrence.Subtract(offsetBefore);
                    if (triggerTime != nowSecond)
                        continue;

                    string fireKey = $"{boss.Id}|{occurrence:yyyyMMddHHmmss}|{(int)offsetBefore.TotalSeconds}";
                    if (!_firedKeys.Add(fireKey))
                        continue;

                    AppLogger.Info($"Boss alarm triggered. Boss='{boss.Name}', Trigger='{label}', Occurrence='{occurrence:yyyy-MM-dd HH:mm:ss}'");
                    NotificationService.PlayAlert(ResolveSoundFile(boss.Id, offsetBefore));
                    return true;
                }
            }

            return false;
        }

        private static string ResolveSoundFile(string bossId, TimeSpan offsetBefore)
        {
            string baseName = bossId switch
            {
                "Arkan" => "Arkan",
                "Scherzendo" => "Scherzendo",
                "Origin of Doom" => "OriginofDoom",
                "Confused Land" => "ConfusedLand",
                "event" => "event",
                _ => "Highlight"
            };

            if (string.Equals(baseName, "Highlight", StringComparison.Ordinal))
            {
                return "Highlight.wav";
            }

            return offsetBefore.TotalSeconds switch
            {
                180 => $"{baseName}_before3.wav",
                60 => $"{baseName}_before1.wav",
                _ => $"{baseName}.wav"
            };
        }

        private void CleanupFiredKeys(DateTime threshold)
        {
            var expired = _firedKeys
                .Where(key => TryParseOccurrence(key, out DateTime occurrence) && occurrence < threshold)
                .ToList();

            foreach (string key in expired)
            {
                _firedKeys.Remove(key);
            }
        }

        private static bool TryParseOccurrence(string key, out DateTime occurrence)
        {
            occurrence = DateTime.MinValue;
            string[] parts = key.Split('|');
            if (parts.Length < 2)
                return false;

            return DateTime.TryParseExact(parts[1], "yyyyMMddHHmmss", null, System.Globalization.DateTimeStyles.None, out occurrence);
        }
    }
}
