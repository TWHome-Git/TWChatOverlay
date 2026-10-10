using System;
using System.Globalization;
using System.Windows.Input;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.ViewModels
{
    /// <summary>
    /// 설정 화면 "창 위치" 목록의 한 줄. 지금 저장된 좌표를 보여주고, 그 창만 0,0으로 되돌린다.
    /// 잠금 해제 모드에서 창을 화면 밖으로 끌고 나가 다시 잡을 수 없게 됐을 때 쓰는 구제 수단이다.
    /// </summary>
    public sealed class WindowPositionRowViewModel : ViewModelBase
    {
        private readonly ChatSettings _settings;
        private readonly WindowPositionEntry _entry;
        private readonly Action _onReset;

        public WindowPositionRowViewModel(ChatSettings settings, WindowPositionEntry entry, Action onReset)
        {
            _settings = settings;
            _entry = entry;
            _onReset = onReset;
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
                if (!left.HasValue && !top.HasValue)
                    return "미설정";

                return string.Format(CultureInfo.InvariantCulture, "{0:F0}, {1:F0}", left ?? 0, top ?? 0);
            }
        }

        /// <summary>저장된 좌표가 바뀌었을 수 있으니 표시를 다시 읽는다.</summary>
        public void Refresh() => OnPropertyChanged(nameof(Coordinates));

        private void Reset()
        {
            _entry.Write(_settings, 0, 0);
            Refresh();
            _onReset();
        }
    }
}
