using System;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.ViewModels
{
    /// <summary>
    /// 채팅 로그 표시 및 탭 관리 ViewModel
    /// </summary>
    public class ChatViewModel : ViewModelBase
    {
        private readonly LogService _logService;
        private readonly ExperienceService _expService;
        private ChatSettings _settings;
        private readonly LogAnalysisService _logAnalysisService;
        private readonly ExperienceEssenceAlertService _experienceEssenceAlertService;
        private readonly LogTabBufferStore _tabLogBuffers;
        private readonly LogDocumentRenderer _logDocumentRenderer;

        private string _currentTabTag = "Basic";
        private double _fontSize;
        private FontFamily _fontFamily = new FontFamily("Segoe UI");
        private FlowDocument _logDocument = new FlowDocument();

        public ICommand TabClickCommand { get; }

        public string CurrentTabTag
        {
            get => _currentTabTag;
            set => SetProperty(ref _currentTabTag, value);
        }

        public double FontSize
        {
            get => _fontSize;
            set => SetProperty(ref _fontSize, value);
        }

        public FontFamily FontFamily
        {
            get => _fontFamily;
            set => SetProperty(ref _fontFamily, value);
        }

        public FlowDocument LogDocument
        {
            get => _logDocument;
            set => SetProperty(ref _logDocument, value);
        }

        public ChatViewModel(LogService logService, ExperienceService expService, ChatSettings settings)
        {
            _logService = logService ?? throw new ArgumentNullException(nameof(logService));
            _expService = expService ?? throw new ArgumentNullException(nameof(expService));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logAnalysisService = new LogAnalysisService(_settings);
            _experienceEssenceAlertService = new ExperienceEssenceAlertService(_settings);
            _tabLogBuffers = new LogTabBufferStore(200);
            _logDocumentRenderer = new LogDocumentRenderer(200);

            TabClickCommand = new RelayCommand<string?>(ExecuteTabClick);

            FontSize = _settings.FontSize;
            FontFamily = FontService.GetFont(_settings.FontFamily);

            LogDocument = new FlowDocument();
        }

        /// <summary>
        /// 탭 클릭 시 실행되는 명령어
        /// </summary>
        private void ExecuteTabClick(string? tabTag)
        {
            if (string.IsNullOrEmpty(tabTag)) return;

            CurrentTabTag = tabTag;
            RefreshLogDisplay();
        }

        /// <summary>
        /// 새 로그를 버퍼에 추가
        /// </summary>
        public void AddToBuffer(string tabName, LogParser.ParseResult log)
        {
            _tabLogBuffers.Add(tabName, log);
        }

        /// <summary>
        /// UI에 로그 추가
        /// </summary>
        public void AddToUI(LogParser.ParseResult log, bool isRealTime = false)
        {
            if (LogDocument == null) return;

            _logDocumentRenderer.AddLog(LogDocument, log, _settings, FontFamily, FontSize, isRealTime, _expService.IsReady);
        }

        /// <summary>
        /// 로그 디스플레이 새로고침
        /// </summary>
        public void RefreshLogDisplay()
        {
            LogDocument.Blocks.Clear();

            var logs = _tabLogBuffers.GetLogs(CurrentTabTag);
            foreach (var log in logs)
            {
                AddToUI(log);
            }
        }

        /// <summary>
        /// 로그 처리 (파싱 후 버퍼/UI에 추가)
        /// </summary>
        public void ProcessNewLog(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return;

            var analysis = _logAnalysisService.Analyze(html, isRealTime: true);
            if (!analysis.IsSuccess) return;
            var parseResult = analysis.Parsed;

            if (analysis.HasExperienceGain)
                _expService.AddExp(parseResult.GainedExp);
            _experienceEssenceAlertService.Process(analysis);

            foreach (string tabName in analysis.BufferTabs)
                AddToBuffer(tabName, parseResult);

            if (_logAnalysisService.ShouldRenderToTab(parseResult, CurrentTabTag))
                AddToUI(parseResult, isRealTime: true);
        }

        /// <summary>
        /// 색상 변경 후 로그 재표시
        /// </summary>
        public void UpdateAllLogColors()
        {
            _tabLogBuffers.UpdateAllBrushes(category => ChatBrushResolver.Resolve(_settings, category));
            RefreshLogDisplay();
        }

        /// <summary>
        /// 설정 변경 시 처리
        /// </summary>
        public void OnSettingsFontChanged()
        {
            FontSize = _settings.FontSize;
            FontFamily = FontService.GetFont(_settings.FontFamily);
            RefreshLogDisplay();
        }

        /// <summary>
        /// 필터 설정 변경 시 처리
        /// </summary>
        public void OnSettingsFilterChanged()
        {
            RefreshLogDisplay();
        }
    }
}
