using System;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.ViewModels
{
    public sealed class BossAlarmCardViewModel : ViewModelBase
    {
        private readonly ChatSettings _settings;
        private readonly BossAlertConfig _config;

        public BossAlarmCardViewModel(ChatSettings settings, BossTimerService.BossTimerDefinition boss)
        {
            _settings = settings;
            BossId = boss.Id;
            Name = boss.Name;
            ScheduleText = BossTimerService.BuildScheduleText(boss);
            _config = _settings.GetOrCreateBossAlertConfig(BossId);
        }

        public string BossId { get; }

        public string Name { get; }

        public string ScheduleText { get; private set; }

        public bool Alert3MinutesBefore
        {
            get => _config.Alert3MinutesBefore;
            set
            {
                if (_config.Alert3MinutesBefore == value)
                    return;

                _config.Alert3MinutesBefore = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public bool Alert1MinuteBefore
        {
            get => _config.Alert1MinuteBefore;
            set
            {
                if (_config.Alert1MinuteBefore == value)
                    return;

                _config.Alert1MinuteBefore = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public bool AlertAtSpawn
        {
            get => _config.AlertAtSpawn;
            set
            {
                if (_config.AlertAtSpawn == value)
                    return;

                _config.AlertAtSpawn = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        private bool IsConfusedLand => string.Equals(BossId, "Confused Land", StringComparison.OrdinalIgnoreCase);
        private bool IsOriginOfDoom => string.Equals(BossId, "Origin of Doom", StringComparison.OrdinalIgnoreCase);

        /// <summary>입장 시간 카운트 토글을 노출할 보스(혼란한 대지·파멸의 기원)인지.</summary>
        public bool HasEntryCountdown => IsConfusedLand || IsOriginOfDoom;

        /// <summary>등장 후 입장 가능 시간(혼란한 대지 3분, 파멸의 기원 6분)을 팝업으로 카운트다운.</summary>
        public bool EntryCountdown
        {
            get => IsOriginOfDoom
                ? _settings.BossAlertOriginOfDoomEntryCountdown
                : _settings.BossAlertConfusedLandEntryCountdown;
            set
            {
                if (EntryCountdown == value)
                    return;

                if (IsOriginOfDoom)
                    _settings.BossAlertOriginOfDoomEntryCountdown = value;
                else
                    _settings.BossAlertConfusedLandEntryCountdown = value;
                OnPropertyChanged();
                SaveSettings();
            }
        }

        public void UpdateScheduleText(string value)
        {
            if (string.Equals(ScheduleText, value, StringComparison.Ordinal))
                return;

            ScheduleText = value;
            OnPropertyChanged(nameof(ScheduleText));
        }

        private void SaveSettings()
        {
            ConfigService.SaveDeferred(_settings);
        }
    }
}
