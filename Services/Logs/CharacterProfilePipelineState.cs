using System;
using System.Collections.Generic;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 캐릭터 프로필별 런타임 상태(활성 슬롯/경험치/버프)를 단일 객체로 관리합니다.
    /// </summary>
    public sealed class CharacterProfilePipelineState : IDisposable
    {
        private readonly ChatSettings _settings;
        private readonly Dictionary<int, ExperienceService> _profileExperienceServices = new();
        private readonly Dictionary<int, BuffTrackerService> _profileBuffTrackerServices = new();

        public CharacterProfilePipelineState(ChatSettings settings, IEnumerable<int>? profileSlots = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            foreach (int slot in profileSlots ?? new[] { 1, 2 })
            {
                _profileExperienceServices[slot] = new ExperienceService(_settings, suppressAlert: true);
                _profileBuffTrackerServices[slot] = new BuffTrackerService(_settings, suppressEndSound: true);
            }
        }

        public int ActiveProfileSlot { get; private set; } = 1;

        public bool IsProfileEnabled => _settings.EnableCharacterProfiles;

        public int EffectiveProfileSlot => ActiveProfileSlot;

        public void AdvanceProfileSlot(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return;

            ActiveProfileSlot = CharacterProfileLogRouter.GetNextProfileSlot(ActiveProfileSlot, html, _settings);
        }

        public void ProcessProfileBuff(LogAnalysisResult analysis)
        {
            if (!IsProfileEnabled)
                return;

            if (_profileBuffTrackerServices.TryGetValue(ActiveProfileSlot, out var profileBuffTracker))
            {
                profileBuffTracker.ProcessLog(analysis);
            }
        }

        public void ProcessProfileExperienceGain(long gainedExp)
        {
            if (gainedExp <= 0)
                return;

            if (_profileExperienceServices.TryGetValue(ActiveProfileSlot, out var profileExperience))
            {
                profileExperience.AddExp(gainedExp);
            }
        }

        public void StartExperienceTrackers()
        {
            foreach (var experienceService in _profileExperienceServices.Values)
            {
                experienceService.Start();
            }
        }

        public void StopExperienceTrackers()
        {
            foreach (var experienceService in _profileExperienceServices.Values)
            {
                experienceService.Stop();
            }
        }

        public void Dispose()
        {
            foreach (var buffTracker in _profileBuffTrackerServices.Values)
            {
                buffTracker.Dispose();
            }
        }
    }
}
