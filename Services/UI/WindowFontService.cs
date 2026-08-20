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
            // 채팅창과 동일한 렌더링 모드로 통일 — 같은 폰트(특히 굴림)가
            // 창마다 다르게(비트맵/외곽선) 보이는 문제 방지
            TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        }

        public static void Apply(FrameworkElement element)
        {
            if (element == null)
                return;

            element.SetCurrentValue(TextElement.FontFamilyProperty, ResolveCurrentFont());
            TextOptions.SetTextFormattingMode(element, TextFormattingMode.Display);
        }
    }
}
