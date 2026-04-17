using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace TWChatOverlay.Models
{
    /// <summary>
    /// 던전 클리어 추적 항목 — 단순 토글, 카운트, 그룹(하위 항목 포함)을 통합 처리합니다.
    /// </summary>
    public class DailyWeeklyContentLog : INotifyPropertyChanged
    {
        private bool _isCleared;
        private bool _isHidden;
        private int _currentCount;
        private IReadOnlyList<DailyWeeklyContentLog>? _children;

        public string Name { get; init; } = "";
        public bool IsSubItem { get; init; } = false;
        public bool IsWeekly { get; init; } = false;
        public bool AllowCountOverMax { get; init; } = false;
        public int DefaultMaxCount { get; init; } = 0;
        public int ClearThreshold { get; init; } = 0;
        public string? LogKeyword { get; init; }
        public string? LogKeyword2 { get; init; }

        private string? _detail;
        public string? Detail
        {
            get => _detail;
            set
            {
                if (_detail == value) return;
                _detail = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
            }
        }

        public bool IsHidden
        {
            get => _isHidden;
            set
            {
                if (_isHidden == value) return;
                _isHidden = value;
                OnPropertyChanged();
            }
        }

        public void SetCount(int value)
        {
            if (!HasCount) return;
            int next = AllowCountOverMax ? Math.Max(0, value) : Math.Clamp(value, 0, MaxCount);
            if (_currentCount == next) return;
            CurrentCount = next;
        }

        public string DisplayName => string.IsNullOrWhiteSpace(Detail) ? Name : $"{Name} ({Detail})";

        private int _maxCount;
        public int MaxCount
        {
            get => _maxCount;
            set
            {
                if (_maxCount == value) return;
                _maxCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCount));
                OnPropertyChanged(nameof(EffectiveRequiredCount));
                OnPropertyChanged(nameof(IsCleared));
                OnPropertyChanged(nameof(CountDisplay));
                OnPropertyChanged(nameof(VisualCountDisplay));
            }
        }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_isEnabled == value) return;
                _isEnabled = value;
                OnPropertyChanged();
                if (HasChildren)
                    foreach (var child in _children!)
                        child.IsEnabled = value;
            }
        }

        public bool HasCount => MaxCount > 0;
        public bool HasChildren => _children?.Count > 0;
        public int EffectiveRequiredCount => MaxCount > 0 ? MaxCount : 1;

        public IReadOnlyList<DailyWeeklyContentLog>? Children
        {
            get => _children;
            init
            {
                _children = value;
                if (_children is not null)
                    foreach (var child in _children)
                        child.PropertyChanged += (_, e) =>
                        {
                            if (e.PropertyName == nameof(IsCleared))
                            {
                                OnPropertyChanged(nameof(IsCleared));
                            }
                        };
            }
        }

        public bool IsCleared
        {
            get
            {
                if (HasChildren) return _children!.All(c => c.IsCleared);
                if (HasCount)
                {
                    int threshold = ClearThreshold > 0 ? ClearThreshold : MaxCount;
                    return _currentCount >= threshold;
                }
                return _isCleared;
            }
            set
            {
                if (HasChildren)
                {
                    foreach (var child in _children!)
                        child.IsCleared = value;
                    return;
                }
                if (HasCount) return;
                if (_isCleared == value) return;
                _isCleared = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VisualCountDisplay));
            }
        }

        public int CurrentCount
        {
            get => _currentCount;
            private set
            {
                if (_currentCount == value) return;
                _currentCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsCleared));
                OnPropertyChanged(nameof(CountDisplay));
                OnPropertyChanged(nameof(VisualCountDisplay));
            }
        }

        public string CountDisplay => $"{_currentCount}/{MaxCount}";

        public string VisualCountDisplay => HasCount ? CountDisplay : (IsCleared ? "1/1" : "0/1");

        public bool ShowVisualCount => !HasChildren;

        public void Mark()
        {
            if (HasCount)
            {
                if (AllowCountOverMax || _currentCount < MaxCount)
                    CurrentCount++;
            }
            else if (!HasChildren)
            {
                IsCleared = true;
            }
        }

        public void Reset()
        {
            if (HasChildren)
            {
                foreach (var child in _children!)
                    child.Reset();
            }
            else
            {
                _isCleared = false;
                _currentCount = 0;
                OnPropertyChanged(nameof(IsCleared));
                OnPropertyChanged(nameof(CurrentCount));
                OnPropertyChanged(nameof(CountDisplay));
                OnPropertyChanged(nameof(VisualCountDisplay));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
