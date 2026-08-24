using System;
using System.Media;
using System.Windows.Threading;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 경험치 세션 상태를 관리하고 알림 조건을 처리합니다.
    /// </summary>
    public class ExperienceService
    {
        private static readonly TimeSpan InactivityTimeout = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan HideAfterStopTimeout = TimeSpan.FromMinutes(1);
        private readonly ChatSettings _settings;
        private readonly DispatcherTimer _expTimer;
        private readonly DispatcherTimer _inactivityTimer;
        private DateTime _lastAlarmTime = DateTime.MinValue;
        private readonly DateTime _startTime = DateTime.Now;
        private DateTime? _lastExpAt;
        private DateTime? _expiredAt;
        private bool _isReady = false;
        private bool _isSessionExpired = false;
        private bool _isTrackerActive = false;
        private readonly bool _suppressAlert;
        public ExpSessionState SessionState { get; } = new();
        public bool IsReady => _isReady;

        /// <summary>경험치 획득 활동이 있어 추적창을 표시해야 하는 상태.
        /// 시작 시에는 꺼져 있고, 실시간 획득 시 켜지며 [중단] 후 1분이 지나면 다시 꺼진다.</summary>
        public bool IsTrackerActive => _isTrackerActive;

        /// <summary>IsTrackerActive가 바뀔 때(표시↔숨김 전환 필요 시) 발생.</summary>
        public event Action? TrackerActiveChanged;

        /// <summary>
        /// 경험치 추적 서비스 인스턴스를 생성합니다.
        /// </summary>
        public ExperienceService(ChatSettings settings, bool suppressAlert = false)
        {
            _settings = settings;
            _suppressAlert = suppressAlert;
            _expTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
            _expTimer.Tick += (s, e) => SessionState.RefreshDisplay();
            _inactivityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _inactivityTimer.Tick += (s, e) => CheckInactivityTimeout();
        }

        public void Start()
        {
            _expTimer.Start();
            _inactivityTimer.Start();
        }

        public void Stop()
        {
            _expTimer.Stop();
            _inactivityTimer.Stop();
        }
        public void SetReady() => _isReady = true;

        /// <summary>
        /// 경험치를 추가하고 UI에 반영합니다.
        /// </summary>
        public void AddExp(long gained)
        {
            if (gained <= 0) return;

            if (_isSessionExpired)
            {
                SessionState.Reset();
                _isSessionExpired = false;
            }

            SessionState.LastGainedExp = gained;
            SessionState.TotalExp += gained;
            SessionState.GainCount += 1;
            _lastExpAt = DateTime.Now;
            _expiredAt = null;

            if (!_isReady || (DateTime.Now - _startTime).TotalSeconds < 5)
            {
                return;
            }

            // 실시간 획득이 확인된 시점부터 추적창을 표시한다 (시작 직후 로그 백로그는 제외)
            if (!_isTrackerActive)
            {
                _isTrackerActive = true;
                TrackerActiveChanged?.Invoke();
            }

            if (!_suppressAlert && _isReady && _settings.IsExpAlarmEnabled && gained < _settings.ExpAlarmThreshold)
            {
                if ((DateTime.Now - _lastAlarmTime).TotalSeconds >= 3)
                {
                    NotificationService.PlayAlert("EXPBuffCheck.wav");
                    _lastAlarmTime = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// 경험치와 시작 시간을 초기화합니다.
        /// </summary>
        public void Reset()
        {
            SessionState.Reset();
            _lastExpAt = null;
            _expiredAt = null;
            _isSessionExpired = false;
        }

        private void CheckInactivityTimeout()
        {
            if (!_lastExpAt.HasValue)
                return;

            if (!_isSessionExpired)
            {
                if (DateTime.Now - _lastExpAt.Value < InactivityTimeout)
                    return;

                SessionState.FreezeTotalExpDisplay();
                _isSessionExpired = true;
                _expiredAt = DateTime.Now;
                return;
            }

            // [중단] 표시 후 1분이 더 지나면 추적창을 숨긴다
            if (_isTrackerActive && _expiredAt.HasValue &&
                DateTime.Now - _expiredAt.Value >= HideAfterStopTimeout)
            {
                _isTrackerActive = false;
                TrackerActiveChanged?.Invoke();
            }
        }
    }
}
