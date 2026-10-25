using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    public static class WindowFontService
    {
        /// <summary>
        /// 현재 폰트를 3단계로 해석한다: MainWindow.CurrentFont → 설정 FontFamily → "Malgun Gothic".
        /// 설정은 DI 싱글턴을 쓰고, 컨테이너가 없는 도움말 이미지 생성 모드에서만 디스크에서 읽는다.
        /// </summary>
        public static FontFamily ResolveCurrentFont()
        {
            try
            {
                FontFamily? hostFont = MainWindowHost.Current?.CurrentFont;
                if (hostFont != null && !string.IsNullOrWhiteSpace(hostFont.Source))
                    return hostFont;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to resolve current font from MainWindow host.", ex);
            }

            try
            {
                ChatSettings settings = AppServices.TryGet<ChatSettings>() ?? ConfigService.Load();
                FontFamily configured = FontService.GetFont(settings.FontFamily);
                if (configured != null && !string.IsNullOrWhiteSpace(configured.Source))
                    return configured;
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
