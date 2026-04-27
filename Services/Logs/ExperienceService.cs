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
        private readonly ChatSettings _settings;
        private readonly DispatcherTimer _expTimer;
        private DateTime _lastAlarmTime = DateTime.MinValue;
        private readonly DateTime _startTime = DateTime.Now;
        private bool _isReady = false;
        public ExpSessionState SessionState { get; } = new();
        public bool IsReady => _isReady;

        /// <summary>
        /// 경험치 추적 서비스 인스턴스를 생성합니다.
        /// </summary>
        public ExperienceService(ChatSettings settings)
        {
            _settings = settings;
            _expTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3000) };
            _expTimer.Tick += (s, e) => SessionState.RefreshDisplay();
        }

        public void Start() => _expTimer.Start();
        public void Stop() => _expTimer.Stop();
        public void SetReady() => _isReady = true;

        /// <summary>
        /// 경험치를 추가하고 UI에 반영합니다.
        /// </summary>
        public void AddExp(long gained)
        {
            if (gained <= 0) return;
            SessionState.LastGainedExp = gained;
            SessionState.TotalExp += gained;

            if (!_isReady || (DateTime.Now - _startTime).TotalSeconds < 5)
            {
                return;
            }

            if (_isReady && _settings.IsExpAlarmEnabled && gained < _settings.ExpAlarmThreshold)
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
        }
    }
}