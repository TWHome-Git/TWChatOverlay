namespace TWChatOverlay.Services
{
    /// <summary>
    /// 경험치 누적 알림 창에 표시/적용할 상태 스냅샷입니다.
    /// </summary>
    public sealed class ExperienceAlertStateSnapshot
    {
        public long TotalExp { get; set; }

        public bool IsVisible { get; set; }
    }
}
