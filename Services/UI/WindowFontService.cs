using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    public static class WindowFontService
    {
        public static FontFamily ResolveCurrentFont()
        {
            FontFamily? hostFont = MainWindowHost.Current?.CurrentFont;
            if (hostFont != null)
                return hostFont;

            try
            {
                ChatSettings settings = ConfigService.Load();
                return FontService.GetFont(settings.FontFamily);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to resolve current font from settings.", ex);
            }

            return new FontFamily("Malgun Gothic");
        }

        public static void Apply(Window window)
        {
            if (window == null)
                return;

            window.FontFamily = ResolveCurrentFont();
            // 잠금 해제 인스펙터에서 지정한 창별 투명도 복원
            UiLockService.ApplyStoredOpacity(window);
        }

        public static void Apply(FrameworkElement element)
        {
            if (element == null)
                return;

            element.SetCurrentValue(TextElement.FontFamilyProperty, ResolveCurrentFont());
        }
    }
}
