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
        private readonly ChatSettings _settings;
        private readonly DispatcherTimer _expTimer;
        private readonly DispatcherTimer _inactivityTimer;
        private DateTime _lastAlarmTime = DateTime.MinValue;
        private readonly DateTime _startTime = DateTime.Now;
        private DateTime? _lastExpAt;
        private bool _isReady = false;
        private bool _isSessionExpired = false;
        private readonly bool _suppressAlert;
        public ExpSessionState SessionState { get; } = new();
        public bool IsReady => _isReady;

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
            _lastExpAt = DateTime.Now;

            if (!_isReady || (DateTime.Now - _startTime).TotalSeconds < 5)
            {
                return;
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
            _isSessionExpired = false;
        }

        private void CheckInactivityTimeout()
        {
            if (_isSessionExpired || !_lastExpAt.HasValue)
                return;

            if (DateTime.Now - _lastExpAt.Value < InactivityTimeout)
                return;

            SessionState.FreezeTotalExpDisplay();
            _isSessionExpired = true;
        }
    }
}
