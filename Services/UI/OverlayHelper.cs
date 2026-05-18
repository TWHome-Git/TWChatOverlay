using System;
using System.Runtime.InteropServices;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 오버레이 좌표 계산에 사용하는 Win32 RECT 유틸리티입니다.
    /// </summary>
    public static class OverlayHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        /// <summary>
        /// 일반 GetWindowRect의 그림자 영역 오차를 보정한 실제 창 RECT를 반환합니다.
        /// </summary>
        public static RECT GetActualRect(IntPtr hwnd)
        {
            int result = DwmGetWindowAttribute(
                hwnd,
                DWMWA_EXTENDED_FRAME_BOUNDS,
                out RECT rect,
                Marshal.SizeOf(typeof(RECT)));

            if (result != 0)
            {
                GetWindowRect(hwnd, out rect);
            }

            return rect;
        }
    }
}
