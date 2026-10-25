using System.Windows;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 모든 모니터를 포함하는 가상 데스크톱 경계(WPF가 DIP 단위로 제공)를 구하는 헬퍼.
    /// </summary>
    public static class ScreenBoundsHelper
    {
        /// <summary>현재 가상 데스크톱(모든 모니터를 포함하는 경계) 사각형을 DIP 단위로 반환.</summary>
        public static Rect GetVirtualDesktop()
        {
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
        }
    }
}
