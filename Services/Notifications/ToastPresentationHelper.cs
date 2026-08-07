using System;
using System.Windows;
using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 토스트/알림 오버레이 창들이 공통으로 쓰던 폰트 해석·기준 위치 계산 로직을 한 곳으로 모은 헬퍼.
    /// (기존에 ItemDrop/Shout/Dungeon/Messenger 토스트 서비스에 거의 동일하게 복제되어 있던 코드)
    /// </summary>
    internal static class ToastPresentationHelper
    {
        /// <summary>
        /// 토스트 폰트를 3단계로 해석한다: MainWindow.CurrentFont → 설정 FontFamily → "Malgun Gothic".
        /// </summary>
        public static FontFamily ResolveToastFont()
        {
            try
            {
                FontFamily? hostFont = MainWindowHost.Current?.CurrentFont;
                if (hostFont != null && !string.IsNullOrWhiteSpace(hostFont.Source))
                    return hostFont;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to resolve toast font from MainWindow host.", ex);
            }

            try
            {
                ChatSettings settings = ConfigService.Load();
                FontFamily configured = FontService.GetFont(settings.FontFamily);
                if (configured != null && !string.IsNullOrWhiteSpace(configured.Source))
                    return configured;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to resolve toast font from settings.", ex);
            }

            return new FontFamily("Malgun Gothic");
        }

        /// <summary>
        /// 저장된 좌표가 모두 있으면 그 위치를, 없으면 작업 영역 가로 중앙 + defaultTop을 반환한다.
        /// </summary>
        public static (double Left, double Top) ResolveBasePosition(double? savedLeft, double? savedTop, double toastWidth, double defaultTop)
        {
            if (savedLeft.HasValue && savedTop.HasValue)
                return (savedLeft.Value, savedTop.Value);

            var area = SystemParameters.WorkArea;
            return (area.Left + (area.Width - toastWidth) / 2, defaultTop);
        }

        /// <summary>
        /// 현재 열려 있는 MainWindow의 DataContext(공유 ChatSettings)를 찾는다. 없으면 null.
        /// </summary>
        public static ChatSettings? FindSharedSettings()
        {
            try
            {
                return MainWindowHost.Current?.HostSettings;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to resolve shared settings from MainWindow host.", ex);
                return null;
            }
        }
    }
}
