using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 외부 윈도우(게임 창)의 정보 탐색 및 위치 계산을 위한 클래스
    /// </summary>
    public static class OverlayHelper
    {
        #region Structures & Constants

        /// <summary>
        /// 사각형의 왼쪽 상단과 오른쪽 하단 좌표를 정의하는 구조체
        /// </summary>
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

        #endregion

        #region DllImports

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

        #endregion

        #region Methods

        /// <summary>
        /// 일반 GetWindowRect의 그림자 영역 오차방지
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

        /// <summary>
        /// 테일즈위버 창 찾기
        /// </summary>
        public static IntPtr FindTalesWeaverWindow()
        {
            IntPtr foundHandle = IntPtr.Zero;
            EnumWindows((hWnd, lParam) =>
            {
                string title = GetWindowTitle(hWnd);

                if (title.Contains("Talesweaver", StringComparison.OrdinalIgnoreCase) && IsWindowVisible(hWnd))
                {
                    foundHandle = hWnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return foundHandle;
        }

        public static string GetWindowTitle(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero)
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool IsForegroundTalesWeaverWindow()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            string title = GetWindowTitle(foreground);
            return title.Contains("Talesweaver", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsForegroundAllowedOverlayWindow()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return false;
            }

            string title = GetWindowTitle(foreground);
            if (title.Contains("Talesweaver", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                GetWindowThreadProcessId(foreground, out uint processId);
                uint currentProcessId = (uint)Process.GetCurrentProcess().Id;
                if (processId != currentProcessId)
                {
                    return false;
                }

                return string.Equals(title, "설정", StringComparison.Ordinal) ||
                       string.Equals(title, "Menu", StringComparison.Ordinal) ||
                       string.Equals(title, "TW Chat Overlay", StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
