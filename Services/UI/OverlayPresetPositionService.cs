using System;
using System.Windows;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 게임 창 기준의 오버레이 프리셋 위치를 실제 창 위치로 적용합니다.
    /// </summary>
    public static class OverlayPresetPositionService
    {
        public static bool TryApplyByMargin(Window window, ChatSettings settings)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            IntPtr gameHwnd = OverlayHelper.FindTalesWeaverWindow();
            if (gameHwnd == IntPtr.Zero) return false;

            var rect = OverlayHelper.GetActualRect(gameHwnd);
            var source = PresentationSource.FromVisual(window);

            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            if (dpiX <= 0) dpiX = 1.0;
            if (dpiY <= 0) dpiY = 1.0;

            double gameWidth = (rect.Right - rect.Left) / dpiX;
            double gameHeight = (rect.Bottom - rect.Top) / dpiY;
            double gameLeft = rect.Left / dpiX;
            double gameTop = rect.Top / dpiY;

            double windowWidth = window.ActualWidth > 0 ? window.ActualWidth : settings.WindowWidth;
            double windowHeight = window.ActualHeight > 0 ? window.ActualHeight : settings.WindowHeight;

            window.Left = gameLeft + (gameWidth / 2.0) - (windowWidth / 2.0) + (settings.LineMarginLeft / dpiX);
            window.Top = gameTop + gameHeight - windowHeight - (settings.LineMargin / dpiY);
            return true;
        }
    }
}
