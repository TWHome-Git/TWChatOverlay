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
        private DateTime _startTime = DateTime.Now;

        public string LastGainedExpDisplay => _lastGainedExp > 0 ? $"+{FormatExp(_lastGainedExp)}" : string.Empty;

        public bool HasLastExp => _lastGainedExp > 0;

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
                string currentExp = FormatExp(_totalExp);
                TimeSpan elapsed = DateTime.Now - _startTime;
                double hours = elapsed.TotalHours;

                if (_totalExp == 0 || elapsed.TotalSeconds < 30)
                {
                    return "   측정 대기 중...   ";
                }

                long expPerHour = (long)(_totalExp / hours);
                return $"{currentExp} | {FormatExp(expPerHour)}/h";
            }
        }

        public void ResetStartTime() => _startTime = DateTime.Now;

        public void Reset()
        {
            LastGainedExp = 0;
            TotalExp = 0;
            ResetStartTime();
        }

        public void RefreshDisplay() => OnPropertyChanged(nameof(TotalExpDisplay));

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
