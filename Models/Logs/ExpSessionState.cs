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
        private string _frozenTotalExpDisplay = string.Empty;

        public string LastGainedExpDisplay => _lastGainedExp > 0 ? $"+{FormatExp(_lastGainedExp)}" : string.Empty;

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
                OnPropertyChanged(nameof(TotalExpDisplay));
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
                OnPropertyChanged(nameof(TotalExpDisplay));
            }
        }

        public string TotalExpDisplay
        {
            get
            {
                if (_isFrozen)
                {
                    return string.IsNullOrWhiteSpace(_frozenTotalExpDisplay)
                        ? "측정 중지"
                        : _frozenTotalExpDisplay;
                }

                string currentExp = FormatExp(_totalExp);
                TimeSpan elapsed = DateTime.Now - _startTime;
                double hours = elapsed.TotalHours;

                if (_totalExp == 0 || elapsed.TotalSeconds < 30)
                {
                    return $"{currentExp} | -/h";
                }

                long expPerHour = (long)(_totalExp / hours);
                return $"{currentExp} | {FormatExp(expPerHour)}/h";
            }
        }

        public void ResetStartTime() => _startTime = DateTime.Now;

        public void FreezeTotalExpDisplay()
        {
            if (_isFrozen)
                return;

            _frozenTotalExpDisplay = BuildTotalExpDisplay();
            _isFrozen = true;
            OnPropertyChanged(nameof(TotalExpDisplay));
        }

        public void UnfreezeTotalExpDisplay()
        {
            if (!_isFrozen)
                return;

            _isFrozen = false;
            _frozenTotalExpDisplay = string.Empty;
            OnPropertyChanged(nameof(TotalExpDisplay));
        }

        public void Reset()
        {
            UnfreezeTotalExpDisplay();
            LastGainedExp = 0;
            TotalExp = 0;
            GainCount = 0;
            ResetStartTime();
        }

        public void RefreshDisplay() => OnPropertyChanged(nameof(TotalExpDisplay));

        private string BuildTotalExpDisplay()
        {
            return TotalExpDisplay;
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
