namespace TWChatOverlay.Models
{
    /// <summary>
    /// 창 위치 프리셋 데이터 클래스
    /// </summary>
    public class WindowPositionPreset
    {
        /// <summary>
        /// 프리셋 이름
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyOrder(1)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 창의 X 좌표 (좌측)
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyOrder(3)]
        public double Left { get; set; }

        /// <summary>
        /// 창의 Y 좌표 (상단)
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyOrder(4)]
        public double Top { get; set; }

        /// <summary>
        /// 게임 창 기준 X 오프셋(LineMarginLeft)
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyOrder(5)]
        public double LineMarginLeft { get; set; }

        /// <summary>
        /// 게임 창 하단 기준 Y 오프셋(LineMargin)
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyOrder(6)]
        public double LineMargin { get; set; }

        /// <summary>
        /// 오프셋 데이터 저장 여부
        /// </summary>
        [System.Text.Json.Serialization.JsonPropertyOrder(2)]
        public bool HasMarginData { get; set; }

        public WindowPositionPreset() { }

        public WindowPositionPreset(string name, double left, double top)
        {
            Name = name;
            Left = left;
            Top = top;
        }

        public WindowPositionPreset(string name, double left, double top, double lineMarginLeft, double lineMargin)
        {
            Name = name;
            Left = left;
            Top = top;
            LineMarginLeft = lineMarginLeft;
            LineMargin = lineMargin;
            HasMarginData = true;
        }
    }
}
