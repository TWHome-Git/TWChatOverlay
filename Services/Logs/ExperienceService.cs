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
        public void AddExp(long gained, DateTime? logTime = null)
        {
            if (gained <= 0) return;

            // 공백 판정은 "로그에 찍힌 시각"으로 한다.
            // 처리 시각(벽시계)으로 재면 로그가 몰려 늦게 처리될 때 10초 간격도 1분으로 보여 엉뚱하게 리셋된다.
            DateTime at = logTime ?? DateTime.Now;

            // 1분 넘게 경험치가 없었으면 새 사냥으로 보고 처음부터 다시 잰다.
            // 비활동 점검(30초 간격)이 공백 사이에 돌았는지와 무관하게 같은 기준으로 판정해
            // 같은 길이의 공백인데 리셋이 되기도 하고 안 되기도 하는 일이 없게 한다.
            bool longGap = _lastExpAt is DateTime lastAt && at - lastAt >= InactivityTimeout;
            if (longGap)
            {
                SessionState.Reset();
                SessionState.UnfreezeTotalExpDisplay();
            }
            else if (_isSessionExpired)
            {
                // 처리만 늦었을 뿐 실제로는 계속 사냥 중이었다 — 표시만 되돌리고 값은 이어 간다
                SessionState.UnfreezeTotalExpDisplay();
            }

            _isSessionExpired = false;
            _expiredAt = null;

            SessionState.LastGainedExp = gained;
            SessionState.TotalExp += gained;
            SessionState.GainCount += 1;
            _lastExpAt = at;

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
                    AppServices.Get<NotificationService>().PlayAlert("EXPBuffCheck.wav");
                    _lastAlarmTime = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// 경험치와 시작 시간을 초기화합니다.
        /// </summary>
        public void Reset()
        {
            // 누가 초기화했는지 남긴다 — 사냥 중에 값이 0이 되는 일이 또 생기면 여기서 찾는다
            AppLogger.Info($"Exp session reset. Total={SessionState.TotalExp:N0}, Count={SessionState.GainCount}");
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
                _expiredAt = _lastExpAt;   // 마지막 획득 시점부터 쉰 것으로 친다
                return;
            }

            // 측정이 끝나도 창은 그대로 둔다 — 마지막 판의 누적·마리수·측정 시간을 계속 볼 수 있어야 한다.
            // (새 경험치가 들어오면 그때 초기화하고 처음부터 다시 잰다)
        }
    }
}
