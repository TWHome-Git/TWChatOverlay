namespace TWChatOverlay.Services
{
    /// <summary>
    /// 경험치 누적 알림 창에 표시/적용할 상태 스냅샷입니다.
    /// </summary>
    public sealed class ExperienceAlertStateSnapshot
    {
        public bool IsProfileMode { get; set; }

        public long TotalExp { get; set; }

        public long Profile1Exp { get; set; }

        public long Profile2Exp { get; set; }

        public string Profile1Label { get; set; } = "프로필1";

        public string Profile2Label { get; set; } = "프로필2";

        public bool IsVisible { get; set; }
    }
}
