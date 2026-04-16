namespace TWChatOverlay.Models
{
    #region Enum

    /// <summary>
    /// 로그 색상/형태에 따라 분류되는 채팅 카테고리입니다.
    /// </summary>
    public enum ChatCategory
    {
        NormalSelf, // 일반(자신)
        Normal,     // 일반(타인)
        Shout,      // 외치기
        Club,       // 클럽
        Team,       // 팀
        System,     // 시스템
        System2,    // 시스템2
        System3,    // 시스템3
        Unknown     // 기타
    }
    #endregion
}