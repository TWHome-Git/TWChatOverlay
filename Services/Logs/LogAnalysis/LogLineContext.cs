using TWChatOverlay.Models;

namespace TWChatOverlay.Services.LogAnalysis
{
    public sealed class LogLineContext
    {
        public LogLineContext(string rawHtml, ChatSettings settings)
            : this(rawHtml, settings, new LogParser.ParseResult())
        {
        }

        /// <summary>이미 분석된 결과(의 복사본)에 분석기 하나만 다시 돌릴 때.</summary>
        public LogLineContext(string rawHtml, ChatSettings settings, LogParser.ParseResult result)
        {
            RawHtml = rawHtml;
            Settings = settings;
            Result = result;
        }

        public string RawHtml { get; }
        public ChatSettings Settings { get; }
        public string ChatContent { get; set; } = string.Empty;
        public string MessageOnly { get; set; } = string.Empty;
        public LogParser.ParseResult Result { get; }

        public bool IsSuccess
        {
            get => Result.IsSuccess;
            set => Result.IsSuccess = value;
        }

        public bool IsSystemLog => Result.Category is ChatCategory.System or ChatCategory.System2 or ChatCategory.System3;
    }
}
