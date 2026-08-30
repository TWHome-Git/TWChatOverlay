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

        // 출현 시각은 하루 단위로 캐시한다 (매초 스케줄 재계산·문자열 파싱 방지)
        private readonly Dictionary<string, DateTime[]> _occurrenceCache = new(StringComparer.Ordinal);
        private DateTime _occurrenceCacheDate = DateTime.MinValue;

        /// <summary>틱이 늦게 와도 이 시간 안이면 알람을 놓치지 않고 울린다.</summary>
        private static readonly TimeSpan LateFireWindow = TimeSpan.FromSeconds(2);

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
                        CheckAlarm(boss, now, config.AlertAtSpawn, TimeSpan.FromSeconds(5), "5초 전") ||
                        CheckEntryCountdownStart(boss, now))
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

            foreach (DateTime occurrence in GetOccurrencesCached(boss, now))
            {
                DateTime triggerTime = occurrence.Subtract(offsetBefore);

                // 트리거 시각을 지났고, 지난 지 얼마 안 됐을 때만 (틱 지연으로 정확한 초를 놓쳐도 울린다)
                TimeSpan elapsed = now - triggerTime;
                if (elapsed < TimeSpan.Zero || elapsed > LateFireWindow)
                    continue;

                string fireKey = $"{boss.Id}|{occurrence:yyyyMMddHHmmss}|{(int)offsetBefore.TotalSeconds}";
                if (!_firedKeys.Add(fireKey))
                    continue;

                AppLogger.Info($"Boss alarm triggered. Boss='{boss.Name}', Trigger='{label}', Occurrence='{occurrence:yyyy-MM-dd HH:mm:ss}'");
                NotificationService.PlayAlert(ResolveSoundFile(boss.Id, offsetBefore));
                // 팝업 알림: 보스 출현 5초 후(입장 카운트다운은 종료 후) 자동으로 닫힌다
                Views.BossAlertToastWindow.ShowAlert(boss.Name, label, occurrence, _settings, GetEntryWindow(boss.Id, _settings));
                return true;
            }

            return false;
        }

        /// <summary>
        /// 입장 시간 카운트가 켜진 보스(혼란한 대지 3분, 파멸의 기원 6분): 등장 시각에 카운트다운 팝업을 띄운다.
        /// 사전 알림(1분/5초 전)이 꺼져 있어도 이 토글이 켜져 있으면 등장 시점에 팝업이 열린다. (사운드 없음)
        /// </summary>
        private bool CheckEntryCountdownStart(BossTimerService.BossTimerDefinition boss, DateTime now)
        {
            TimeSpan? entryWindow = GetEntryWindow(boss.Id, _settings);
            if (entryWindow == null)
                return false;

            foreach (DateTime occurrence in GetOccurrencesCached(boss, now))
            {
                TimeSpan elapsed = now - occurrence;
                if (elapsed < TimeSpan.Zero || elapsed > LateFireWindow)
                    continue;

                string fireKey = $"{boss.Id}|{occurrence:yyyyMMddHHmmss}|entry";
                if (!_firedKeys.Add(fireKey))
                    continue;

                AppLogger.Info($"Boss entry countdown started. Boss='{boss.Name}', Occurrence='{occurrence:yyyy-MM-dd HH:mm:ss}'");
                Views.BossAlertToastWindow.ShowAlert(boss.Name, "등장", occurrence, _settings, entryWindow);
                return true;
            }

            return false;
        }

        /// <summary>입장 시간 카운트가 켜진 보스의 입장 가능 시간 — 혼란한 대지 3분, 파멸의 기원 6분.</summary>
        internal static TimeSpan? GetEntryWindow(string bossId, ChatSettings? settings)
        {
            if (string.Equals(bossId, "Confused Land", StringComparison.OrdinalIgnoreCase)
                && settings?.BossAlertConfusedLandEntryCountdown == true)
                return TimeSpan.FromMinutes(3);

            if (string.Equals(bossId, "Origin of Doom", StringComparison.OrdinalIgnoreCase)
                && settings?.BossAlertOriginOfDoomEntryCountdown == true)
                return TimeSpan.FromMinutes(6);

            return null;
        }

        /// <summary>어제~내일 출현 시각을 보스별로 캐시해 반환한다. 날짜가 바뀌면 갱신.</summary>
        private DateTime[] GetOccurrencesCached(BossTimerService.BossTimerDefinition boss, DateTime now)
        {
            DateTime today = now.Date;
            if (_occurrenceCacheDate != today)
            {
                _occurrenceCache.Clear();
                _occurrenceCacheDate = today;
            }

            if (!_occurrenceCache.TryGetValue(boss.Id, out DateTime[]? occurrences))
            {
                var list = new List<DateTime>();
                foreach (DateTime date in new[] { today.AddDays(-1), today, today.AddDays(1) })
                {
                    list.AddRange(BossTimerService.GetOccurrences(boss, date));
                }

                occurrences = list.ToArray();
                _occurrenceCache[boss.Id] = occurrences;
            }

            return occurrences;
        }

        /// <summary>디버그용 테스트 발사: 실제 알림 흐름(사운드+팝업) 그대로 울린다.</summary>
        public static void FireTestAlert(string bossId, string bossName, string label, ChatSettings? settings)
        {
            TimeSpan offset = label switch
            {
                "3분 전" => TimeSpan.FromMinutes(3),
                "1분 전" => TimeSpan.FromMinutes(1),
                _ => TimeSpan.FromSeconds(5),
            };

            AppLogger.Info($"Boss alarm TEST fired. Boss='{bossName}', Trigger='{label}'");
            NotificationService.PlayAlert(ResolveSoundFile(bossId, offset));
            Views.BossAlertToastWindow.ShowAlert(bossName, label, DateTime.Now.Add(offset), settings, GetEntryWindow(bossId, settings));
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
