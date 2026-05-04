using System;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 로그 1건 처리에 필요한 입력/중간 결과 컨텍스트를 단일 객체로 전달합니다.
    /// </summary>
    public sealed class UnifiedLogPipelineContext
    {
        public UnifiedLogPipelineContext(string rawHtml, bool isRealTime, bool deferUiScroll, int effectiveSourceSlot, bool isSourceSplitEnabled)
        {
            RawHtml = rawHtml ?? string.Empty;
            IsRealTime = isRealTime;
            DeferUiScroll = deferUiScroll;
            EffectiveSourceSlot = effectiveSourceSlot;
            IsSourceSplitEnabled = isSourceSplitEnabled;
        }

        public string RawHtml { get; }

        public bool IsRealTime { get; }

        public bool DeferUiScroll { get; }

        public bool IsSourceSplitEnabled { get; }

        public int EffectiveSourceSlot { get; }

        public bool HandledDailyWeeklyCountLog { get; set; }

        public MainLogPipelineAnalysis? PipelineAnalysis { get; set; }

        public bool HasSucceededAnalysis => PipelineAnalysis?.Primary.IsSuccess == true;

        public LogAnalysisResult PrimaryAnalysis =>
            PipelineAnalysis?.Primary ?? LogAnalysisResult.Empty(RawHtml, IsRealTime);
    }
}
