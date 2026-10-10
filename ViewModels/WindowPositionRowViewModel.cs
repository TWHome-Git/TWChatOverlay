using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.ViewModels
{
    /// <summary>
    /// 설정 화면 "창 위치" 목록의 한 줄. 지금 저장된 좌표를 보여주고, 그 창만 0,0으로 되돌리거나
    /// 좌표를 더블클릭해 원하는 자리로 직접 보낸다.
    /// 잠금 해제 모드에서 창을 화면 밖으로 끌고 나가 다시 잡을 수 없게 됐을 때 쓰는 구제 수단이다.
    /// </summary>
    public sealed class WindowPositionRowViewModel : ViewModelBase
    {
        /// <summary>받아들이는 입력 — "1920, 480" 처럼 쉼표로 나뉜 정수 두 개만.</summary>
        private static readonly Regex PairRegex = new(@"^\s*(-?\d+)\s*,\s*(-?\d+)\s*$", RegexOptions.Compiled);

        private readonly ChatSettings _settings;
        private readonly WindowPositionEntry _entry;
        private readonly Action _onChanged;

        private bool _isEditing;
        private string _editText = string.Empty;

        public WindowPositionRowViewModel(ChatSettings settings, WindowPositionEntry entry, Action onChanged)
        {
            _settings = settings;
            _entry = entry;
            _onChanged = onChanged;
            ResetCommand = new RelayCommand<object?>(_ => Reset());
        }

        public string Name => _entry.Name;

        public ICommand ResetCommand { get; }

        /// <summary>"120, 480" 또는 아직 한 번도 옮기지 않았으면 "미설정".</summary>
        public string Coordinates
        {
            get
            {
                (double? left, double? top) = _entry.Read(_settings);
                return left.HasValue || top.HasValue ? Format(left ?? 0, top ?? 0) : "미설정";
            }
        }

        /// <summary>좌표를 직접 고치는 중인지. 켜지면 입력칸이, 꺼지면 좌표 글씨가 보인다.</summary>
        public bool IsEditing
        {
            get => _isEditing;
            private set
            {
                if (SetProperty(ref _isEditing, value))
                    OnPropertyChanged(nameof(IsNotEditing));
            }
        }

        public bool IsNotEditing => !_isEditing;

        /// <summary>입력칸에 적힌 글. 확정할 때 "숫자, 숫자" 꼴인지 본다.</summary>
        public string EditText
        {
            get => _editText;
            set => SetProperty(ref _editText, value);
        }

        /// <summary>저장된 좌표가 바뀌었을 수 있으니 표시를 다시 읽는다.</summary>
        public void Refresh() => OnPropertyChanged(nameof(Coordinates));

        /// <summary>좌표를 더블클릭했을 때. 지금 값을 입력칸에 담는다 (미설정이면 빈칸).</summary>
        public void BeginEdit()
        {
            (double? left, double? top) = _entry.Read(_settings);
            EditText = left.HasValue || top.HasValue ? Format(left ?? 0, top ?? 0) : string.Empty;
            IsEditing = true;
        }

        public void CancelEdit() => IsEditing = false;

        /// <summary>
        /// 입력한 좌표를 적용한다. "숫자, 숫자" 꼴이 아니면 (빈칸·쉼표 없음·숫자 아님)
        /// 아무것도 바꾸지 않고 원래 값으로 돌아간다.
        /// </summary>
        public void CommitEdit()
        {
            if (!IsEditing)
                return;

            IsEditing = false;

            Match match = PairRegex.Match(EditText ?? string.Empty);
            if (!match.Success)
            {
                Refresh(); // 입력칸을 접고 저장된 값을 다시 보여준다
                return;
            }

            double left = double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            double top = double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            Apply(left, top);
        }

        private void Reset() => Apply(0, 0);

        private void Apply(double left, double top)
        {
            _entry.Write(_settings, left, top);
            Refresh();
            // 이 창만 새 자리로 옮긴다 — 다른 창(설정 창 포함)은 건드리지 않는다
            _entry.MoveToStored(_settings);
            _onChanged();
        }

        private static string Format(double left, double top)
            => string.Format(CultureInfo.InvariantCulture, "{0:F0}, {1:F0}", left, top);
    }
}
