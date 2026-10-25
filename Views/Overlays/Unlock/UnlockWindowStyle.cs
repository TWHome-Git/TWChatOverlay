using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Unlock
{
    /// <summary>잠금 해제 보조 창(격자·하이라이트)이 함께 쓰는 네이티브 스타일·브러시 도우미.</summary>
    internal static class UnlockWindowStyle
    {
        /// <summary>WS_EX_TRANSPARENT + WS_EX_NOACTIVATE + WS_EX_TOOLWINDOW: 모든 마우스 입력이 아래 창으로 통과한다.</summary>
        public static void ApplyClickThrough(Window window)
        {
            try
            {
                var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                int ex = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(handle, NativeMethods.GWL_EXSTYLE,
                    ex | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Click-through setup failed.", ex);
            }
        }

        /// <summary>Freeze된 단색 브러시. 하이라이트·인스펙터 창은 선택할 때마다 새로 만들어지므로 브러시는 공유한다.</summary>
        public static SolidColorBrush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
