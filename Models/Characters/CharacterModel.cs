namespace TWChatOverlay.Models
{
    /// <summary>
    /// 캐릭터 이름과 이미지 경로를 표현하는 모델입니다.
    /// </summary>
    public class CharacterModel
    {
        public string Name { get; set; } = string.Empty;
        public string ImagePath => $"pack://application:,,,/TWChatOverlay;component/Data/images/char/{Name}.png";
    }
}