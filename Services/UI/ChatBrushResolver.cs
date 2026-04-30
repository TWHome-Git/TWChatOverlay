using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 채팅 카테고리와 설정 색상 문자열을 WPF 브러시로 변환합니다.
    /// </summary>
    public static class ChatBrushResolver
    {
        public static SolidColorBrush Resolve(ChatSettings settings, ChatCategory category)
        {
            string hex = category switch
            {
                ChatCategory.System or ChatCategory.System2 or ChatCategory.System3 => settings.SystemColor,
                ChatCategory.Team => settings.TeamColor,
                ChatCategory.Club => settings.ClubColor,
                ChatCategory.Shout => settings.ShoutColor,
                _ => settings.NormalColor
            };

            try
            {
                return new BrushConverter().ConvertFromString(hex) as SolidColorBrush ?? Brushes.White;
            }
            catch
            {
                return Brushes.White;
            }
        }
    }
}
