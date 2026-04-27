using TWChatOverlay.Models;

namespace TWChatOverlay.Services.LogAnalysis
{
    public sealed class LogLineContext
    {
        public LogLineContext(string rawHtml, ChatSettings settings)
        {
            RawHtml = rawHtml;
            Settings = settings;
        }

        public string RawHtml { get; }
        public ChatSettings Settings { get; }
        public string ChatContent { get; set; } = string.Empty;
        public string MessageOnly { get; set; } = string.Empty;
        public LogParser.ParseResult Result { get; } = new();

        public bool IsSuccess
        {
            get => Result.IsSuccess;
            set => Result.IsSuccess = value;
        }

        public bool IsSystemLog => Result.Category is ChatCategory.System or ChatCategory.System2 or ChatCategory.System3;
    }
}
