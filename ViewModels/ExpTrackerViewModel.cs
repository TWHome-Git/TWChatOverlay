using System;
using System.Windows.Input;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.ViewModels
{
    /// <summary>
    /// 경험치 추적기 ViewModel
    /// </summary>
    public class ExpTrackerViewModel : ViewModelBase
    {
        private readonly ExperienceService _expService;

        private string _totalExpValueDisplay = string.Empty;
        private string _expPerHourDisplay = string.Empty;
        private string _lastGainedExpDisplay = string.Empty;
        private string _gainCountDisplay = string.Empty;
        private bool _hasLastExp;
        private bool _isMeasurementStopped;

        public ICommand ResetExpCommand { get; }

        /// <summary>현재까지 획득한 누적 경험치</summary>
        public string TotalExpValueDisplay
        {
            get => _totalExpValueDisplay;
            set => SetProperty(ref _totalExpValueDisplay, value);
        }

        /// <summary>시간당 획득 경험치</summary>
        public string ExpPerHourDisplay
        {
            get => _expPerHourDisplay;
            set => SetProperty(ref _expPerHourDisplay, value);
        }

        /// <summary>최근 획득한 경험치</summary>
        public string LastGainedExpDisplay
        {
            get => _lastGainedExpDisplay;
            set => SetProperty(ref _lastGainedExpDisplay, value);
        }

        /// <summary>현재까지 잡은 마리수</summary>
        public string GainCountDisplay
        {
            get => _gainCountDisplay;
            set
            {
                if (!SetProperty(ref _gainCountDisplay, value))
                    return;

                OnPropertyChanged(nameof(ShowGainCountDisplay));
            }
        }

        public bool ShowGainCountDisplay => !string.IsNullOrWhiteSpace(_gainCountDisplay);

        public bool HasLastExp
        {
            get => _hasLastExp;
            set => SetProperty(ref _hasLastExp, value);
        }

        /// <summary>비활동으로 측정이 멈춘 상태</summary>
        public bool IsMeasurementStopped
        {
            get => _isMeasurementStopped;
            set => SetProperty(ref _isMeasurementStopped, value);
        }

        public ExpTrackerViewModel(ExperienceService expService, ChatSettings settings)
        {
            _expService = expService ?? throw new ArgumentNullException(nameof(expService));
            _ = settings ?? throw new ArgumentNullException(nameof(settings));

            ResetExpCommand = new RelayCommand<object?>(_ => ExecuteResetExp());

            UpdateDisplay();
        }

        /// <summary>
        /// 경험치 초기화
        /// </summary>
        private void ExecuteResetExp()
        {
            _expService.Reset();
            UpdateDisplay();
        }

        /// <summary>
        /// 디스플레이 업데이트
        /// </summary>
        public void UpdateDisplay()
        {
            ExpSessionState state = _expService.SessionState;

            TotalExpValueDisplay = state.TotalExpValueDisplay;
            ExpPerHourDisplay = state.ExpPerHourDisplay;
            LastGainedExpDisplay = state.LastGainedExpValueDisplay;
            GainCountDisplay = state.GainCountDisplay;
            HasLastExp = state.HasLastExp;
            IsMeasurementStopped = state.IsMeasurementStopped;
        }
    }
}
