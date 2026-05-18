using System;
using System.Windows;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 저장된 오버레이 좌표를 실제 창 위치로 적용합니다.
    /// </summary>
    public static class OverlayPresetPositionService
    {
        public static bool TryApplyByMargin(Window window, ChatSettings settings)
        {
            if (window == null) throw new ArgumentNullException(nameof(window));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            window.Left = settings.LineMarginLeft;
            window.Top = settings.LineMargin;
            return true;
        }
    }
}
