using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TWChatOverlay.Models
{
    /// <summary>
    /// 경험치 세션의 누적/표시 상태를 관리합니다.
    /// </summary>
    public class ExpSessionState : INotifyPropertyChanged
    {
        private long _lastGainedExp;
        private long _totalExp;
        private int _gainCount;
        /// <summary>첫 경험치가 들어온 시각. null이면 아직 재기 시작하지 않은 것 — 리셋만 하고 두면 시계가 돌지 않는다.</summary>
        private DateTime? _startedAt;
        private bool _isFrozen;
        private DateTime _frozenAt;
        private string _frozenTotalValueDisplay = string.Empty;
        private string _frozenExpPerHourDisplay = string.Empty;

        public string LastGainedExpDisplay => _lastGainedExp > 0 ? $"+{FormatExp(_lastGainedExp)}" : string.Empty;

        /// <summary>최근 획득 경험치 — 값이 없으면 자리 표시자("-").</summary>
        public string LastGainedExpValueDisplay => _lastGainedExp > 0 ? FormatExp(_lastGainedExp) : "-";

        public bool HasLastExp => _lastGainedExp > 0;

        public int GainCount
        {
            get => _gainCount;
            set
            {
                if (_gainCount == value) return;
                _gainCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(GainCountDisplay));
                RaiseTotalDisplayChanged();
            }
        }

        public string GainCountDisplay => $"{GainCount:N0}마리";

        public long LastGainedExp
        {
            get => _lastGainedExp;
            set
            {
                if (_lastGainedExp == value) return;
                _lastGainedExp = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LastGainedExpDisplay));
                OnPropertyChanged(nameof(LastGainedExpValueDisplay));
                OnPropertyChanged(nameof(HasLastExp));
            }
        }

        public long TotalExp
        {
            get => _totalExp;
            set
            {
                if (_totalExp == value) return;
                _totalExp = value;
                OnPropertyChanged();
                RaiseTotalDisplayChanged();
            }
        }

        /// <summary>현재까지 획득한 누적 경험치.</summary>
        public string TotalExpValueDisplay
        {
            get
            {
                if (_isFrozen)
                {
                    return string.IsNullOrWhiteSpace(_frozenTotalValueDisplay)
                        ? FormatExp(_totalExp)
                        : _frozenTotalValueDisplay;
                }

                return FormatExp(_totalExp);
            }
        }

        /// <summary>1시간 예상 획득 경험치(단위 없음). 표본이 부족하면 "-".</summary>
        public string ExpPerHourDisplay
        {
            get
            {
                if (_isFrozen)
                {
                    return string.IsNullOrWhiteSpace(_frozenExpPerHourDisplay)
                        ? "-"
                        : _frozenExpPerHourDisplay;
                }

                TimeSpan elapsed = Elapsed;
                double hours = elapsed.TotalHours;

                if (_totalExp == 0 || elapsed.TotalSeconds < 30 || hours <= 0)
                    return "-";

                long expPerHour = (long)(_totalExp / hours);
                return FormatExp(expPerHour);
            }
        }

        /// <summary>
        /// 측정 중이 아닌 상태인지 여부 — 비활동으로 멈췄거나, 리셋 뒤 아직 경험치가 안 들어와 시작 전이거나.
        /// "경험치가 들어오면 측정 시작"이므로 시작 전은 측정 중이 아니다.
        /// </summary>
        public bool IsMeasurementStopped => _isFrozen || _startedAt is null;

        /// <summary>
        /// 측정을 시작한 뒤 지난 시간. 멈춘 뒤에는 멈춘 시각에서 더 가지 않는다 (1시간 예상과 같은 기준 시각).
        /// 경험치가 한 번도 안 들어왔으면 0 — 리셋을 누르고 사냥을 안 해도 시간이 가는 일이 없다.
        /// </summary>
        public TimeSpan Elapsed
        {
            get
            {
                if (_startedAt is not DateTime start)
                    return TimeSpan.Zero;

                DateTime until = _isFrozen ? _frozenAt : DateTime.Now;
                TimeSpan span = until - start;
                return span > TimeSpan.Zero ? span : TimeSpan.Zero;
            }
        }

        /// <summary>측정 시간 표시 — 분 단위로 "12분", 한 시간이 넘으면 "1시간 2분".</summary>
        public string ElapsedDisplay
        {
            get
            {
                TimeSpan elapsed = Elapsed;
                int minutes = elapsed > TimeSpan.Zero ? (int)elapsed.TotalMinutes : 0;
                return minutes >= 60 ? $"{minutes / 60}시간 {minutes % 60}분" : $"{minutes}분";
            }
        }

        public string TotalExpDisplay => $"{TotalExpValueDisplay} | {ExpPerHourDisplay}/h";

        /// <summary>측정 시작 시각을 비운다. 다음 경험치가 들어올 때 다시 잡는다.</summary>
        public void ResetStartTime() => _startedAt = null;

        /// <summary>경험치가 들어왔다 — 아직 시작 전이면 지금을 시작 시각으로 잡는다.</summary>
        public void MarkStarted() => _startedAt ??= DateTime.Now;

        public void FreezeTotalExpDisplay()
        {
            if (_isFrozen)
                return;

            _frozenTotalValueDisplay = TotalExpValueDisplay;
            _frozenExpPerHourDisplay = ExpPerHourDisplay;
            _frozenAt = DateTime.Now;
            _isFrozen = true;
            RaiseTotalDisplayChanged();
        }

        public void UnfreezeTotalExpDisplay()
        {
            if (!_isFrozen)
                return;

            _isFrozen = false;
            _frozenTotalValueDisplay = string.Empty;
            _frozenExpPerHourDisplay = string.Empty;
            RaiseTotalDisplayChanged();
        }

        public void Reset()
        {
            UnfreezeTotalExpDisplay();
            LastGainedExp = 0;
            TotalExp = 0;
            GainCount = 0;
            ResetStartTime();
            RaiseTotalDisplayChanged();
        }

        public void RefreshDisplay() => RaiseTotalDisplayChanged();

        private void RaiseTotalDisplayChanged()
        {
            OnPropertyChanged(nameof(TotalExpValueDisplay));
            OnPropertyChanged(nameof(ExpPerHourDisplay));
            OnPropertyChanged(nameof(IsMeasurementStopped));
            OnPropertyChanged(nameof(ElapsedDisplay));
            OnPropertyChanged(nameof(TotalExpDisplay));
        }

        private static string FormatExp(long value)
        {
            if (value >= 1_000_000_000_000)
            {
                long jo = value / 1_000_000_000_000;
                double eok = (value % 1_000_000_000_000) / 100_000_000.0;
                return $"{jo}조 {Math.Floor(eok * 10) / 10.0:F1}억";
            }
            if (value >= 100_000_000)
            {
                double eok = value / 100_000_000.0;
                return $"{Math.Floor(eok * 10) / 10.0:F1}억";
            }
            if (value >= 10_000)
            {
                double man = (double)value / 10_000;
                return $"{man:N1}만";
            }

            return value.ToString("N0");
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
