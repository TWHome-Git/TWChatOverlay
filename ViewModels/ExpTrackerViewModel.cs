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

        private string _totalExpDisplay = string.Empty;
        private string _lastGainedExpDisplay = string.Empty;
        private bool _hasLastExp;

        public ICommand ResetExpCommand { get; }

        public string TotalExpDisplay
        {
            get => _totalExpDisplay;
            set => SetProperty(ref _totalExpDisplay, value);
        }

        public string LastGainedExpDisplay
        {
            get => _lastGainedExpDisplay;
            set => SetProperty(ref _lastGainedExpDisplay, value);
        }

        public bool HasLastExp
        {
            get => _hasLastExp;
            set => SetProperty(ref _hasLastExp, value);
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
            TotalExpDisplay = _expService.SessionState.TotalExpDisplay;
            LastGainedExpDisplay = _expService.SessionState.LastGainedExpDisplay;
            HasLastExp = _expService.SessionState.HasLastExp;
        }
    }
}
