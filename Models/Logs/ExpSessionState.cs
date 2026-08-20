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
        private DateTime _startTime = DateTime.Now;
        private bool _isFrozen;
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

                TimeSpan elapsed = DateTime.Now - _startTime;
                double hours = elapsed.TotalHours;

                if (_totalExp == 0 || elapsed.TotalSeconds < 30 || hours <= 0)
                    return "-";

                long expPerHour = (long)(_totalExp / hours);
                return FormatExp(expPerHour);
            }
        }

        /// <summary>비활동으로 측정이 멈춘 상태인지 여부.</summary>
        public bool IsMeasurementStopped => _isFrozen;

        public string TotalExpDisplay => $"{TotalExpValueDisplay} | {ExpPerHourDisplay}/h";

        public void ResetStartTime() => _startTime = DateTime.Now;

        public void FreezeTotalExpDisplay()
        {
            if (_isFrozen)
                return;

            _frozenTotalValueDisplay = TotalExpValueDisplay;
            _frozenExpPerHourDisplay = ExpPerHourDisplay;
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
