using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace TWChatOverlay.Models
{
    /// <summary>
    /// 던전 클리어 추적 항목 — 단순 토글, 카운트, 그룹(하위 항목 포함)을 통합 처리
    /// </summary>
    public class DailyWeeklyContentLog : INotifyPropertyChanged
    {
        private bool _isCleared;
        private bool _isHidden;
        private int _currentCount;
        private IReadOnlyList<DailyWeeklyContentLog>? _children;

        public string Name { get; init; } = "";
        public bool IsSubItem { get; init; } = false;
        public bool IsRegionGroup { get; init; } = false;
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
                OnPropertyChanged(nameof(ProgressForeground));
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
                            if (e.PropertyName == nameof(IsCleared) ||
                                e.PropertyName == nameof(IsEnabled))
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
                if (HasChildren)
                    return _children!.Where(c => c.IsEnabled).All(c => c.IsCleared);
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
                OnPropertyChanged(nameof(ProgressForeground));
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
                OnPropertyChanged(nameof(ProgressForeground));
            }
        }

        /// <summary>상위 묶음 이름. 진행중 섹션은 묶음 헤더 없이 항목만 모으므로 어디 소속인지 여기서 보여준다.</summary>
        public string? ParentName { get; set; }

        /// <summary>진행중 섹션에서 쓰는 이름 — "묶음 · 항목" 형태.</summary>
        public string InProgressDisplayName
        {
            get
            {
                string name = DisplayName;
                if (string.IsNullOrWhiteSpace(ParentName)) return name;

                string prefix = TrimSharedTail(ParentName!, name);
                return string.IsNullOrWhiteSpace(prefix) ? name : $"{prefix} · {name}";
            }
        }

        /// <summary>
        /// 묶음 이름에서 항목 이름과 겹치는 뒷말을 덜어낸다.
        /// ("이클립스 코어 마스터" + "로카고스 코어 마스터" -> "이클립스")
        /// 좁은 창에서 같은 말이 두 번 나와 이름이 잘리는 것을 막는다.
        /// </summary>
        private static string TrimSharedTail(string parent, string child)
        {
            string[] p = parent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            string[] c = child.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            int shared = 0;
            while (shared < p.Length - 1 && shared < c.Length &&
                   string.Equals(p[p.Length - 1 - shared], c[c.Length - 1 - shared], StringComparison.Ordinal))
                shared++;

            return string.Join(' ', p.Take(p.Length - shared));
        }

        public string CountDisplay => $"{_currentCount}/{MaxCount}";

        public string VisualCountDisplay => HasCount ? CountDisplay : (IsCleared ? "1/1" : "0/1");

        public bool ShowVisualCount => !HasChildren;

        public Brush ProgressForeground
        {
            get
            {
                if (HasChildren)
                    return Brushes.White;

                if (IsCleared)
                    return new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

                double ratio;
                if (HasCount)
                {
                    int threshold = ClearThreshold > 0 ? ClearThreshold : Math.Max(1, MaxCount);
                    ratio = Math.Clamp((double)_currentCount / threshold, 0d, 1d);
                }
                else
                {
                    ratio = _isCleared ? 1d : 0d;
                }

                byte startR = 0xC9, startG = 0xD1, startB = 0xD9;
                byte endR = 0x81, endG = 0xE6, endB = 0x93;
                byte r = (byte)Math.Round(startR + (endR - startR) * ratio);
                byte g = (byte)Math.Round(startG + (endG - startG) * ratio);
                byte b = (byte)Math.Round(startB + (endB - startB) * ratio);
                return new SolidColorBrush(Color.FromRgb(r, g, b));
            }
        }

        public void Mark()
        {
            if (HasCount)
            {
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
                OnPropertyChanged(nameof(ProgressForeground));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
