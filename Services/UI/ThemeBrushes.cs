using System.Windows;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    /// <summary>테마 팔레트 브러시를 코드비하인드에서 조회합니다.</summary>
    public static class ThemeBrushes
    {
        public static Brush Get(string key, Brush? fallback = null)
            => Application.Current?.TryFindResource(key) as Brush ?? fallback ?? Brushes.Gray;
    }
}
