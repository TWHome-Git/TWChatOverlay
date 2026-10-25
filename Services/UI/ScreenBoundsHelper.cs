using System;
using System.Collections.Generic;
using System.Windows;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 화면 경계 판정·보정을 한 곳에 모은 헬퍼. 좌표는 모두 WPF DIP(SystemParameters와 같은 단위).
    ///
    /// 모니터가 여럿이고 크기·배치가 들쭉날쭉하면 "모든 모니터를 합친 사각형"(가상 화면) 안이어도
    /// 실제로는 어느 모니터에도 걸치지 않는 빈틈이 생긴다. 그 빈틈에 놓인 창은 보이지 않는데 가상 화면 기준으로는
    /// 멀쩡해 보여 되찾을 길이 없었다. 그래서 '화면 안인지'는 모니터 하나하나의 작업 영역으로 판정한다.
    /// 모니터 열거에 실패하면 가상 화면 하나를 모니터로 본다.
    /// </summary>
    public static class ScreenBoundsHelper
    {
        /// <summary>창을 잡을 수 있으려면 이만큼은 어느 모니터 안에 들어와 있어야 한다.</summary>
        public const double DefaultMinVisible = 80;

        /// <summary>현재 가상 데스크톱(모든 모니터를 포함하는 경계) 사각형을 DIP 단위로 반환.</summary>
        public static Rect GetVirtualDesktop()
        {
            return new Rect(
                SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth,
                SystemParameters.VirtualScreenHeight);
        }

        /// <summary>
        /// 모니터별 작업 영역(작업 표시줄 제외)을 DIP로. 열거가 안 되면 가상 데스크톱 하나.
        /// 장치 픽셀 → DIP 변환은 WPF가 SystemParameters에 쓰는 것과 같은 비율(가상 화면 폭 기준)을 쓴다.
        /// </summary>
        public static IReadOnlyList<Rect> GetMonitorWorkAreas()
        {
            var result = new List<Rect>();
            try
            {
                int deviceLeft = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
                int deviceTop = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
                int deviceWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
                Rect virtualDip = GetVirtualDesktop();
                double scale = deviceWidth > 0 ? virtualDip.Width / deviceWidth : 1.0;

                bool OnMonitor(IntPtr hMonitor, IntPtr hdc, ref NativeMethods.RECT rcMonitor, IntPtr data)
                {
                    var info = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
                    if (NativeMethods.GetMonitorInfo(hMonitor, ref info))
                    {
                        var r = info.rcWork;
                        result.Add(new Rect(
                            virtualDip.Left + (r.Left - deviceLeft) * scale,
                            virtualDip.Top + (r.Top - deviceTop) * scale,
                            Math.Max(0, (r.Right - r.Left) * scale),
                            Math.Max(0, (r.Bottom - r.Top) * scale)));
                    }
                    return true;
                }

                NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, OnMonitor, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to enumerate monitors; falling back to the virtual screen.", ex);
                result.Clear();
            }

            if (result.Count == 0)
                result.Add(GetVirtualDesktop());
            return result;
        }

        /// <summary>
        /// (left, top)에 창을 두면 어느 모니터에서든 잡을 수 있는지.
        /// 왼쪽은 조금 걸쳐 있어도 되지만, 위로 넘어가면 제목줄을 잡을 수 없다.
        /// </summary>
        public static bool IsOnAnyScreen(double left, double top, double minVisible = DefaultMinVisible)
        {
            if (double.IsNaN(left) || double.IsNaN(top) || double.IsInfinity(left) || double.IsInfinity(top))
                return false;

            foreach (Rect monitor in GetMonitorWorkAreas())
            {
                if (left >= monitor.Left - minVisible
                    && left <= monitor.Right - minVisible
                    && top >= monitor.Top
                    && top <= monitor.Bottom - minVisible)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// 창이 화면 밖으로 완전히 사라지지 않게 가상 화면 안으로 끌어온다 — 적어도 <paramref name="visibleMargin"/>px는 남긴다.
        /// 크기를 모르면(0) 여백만큼을 크기로 본다. (잠금 해제 인스펙터의 X/Y 입력 보정)
        /// </summary>
        public static (double Left, double Top) ClampToVirtualScreen(double left, double top, double width, double height, double visibleMargin = 40)
        {
            Rect area = GetVirtualDesktop();
            return (
                ClampAxis(left, area.Left, area.Width, width, visibleMargin),
                ClampAxis(top, area.Top, area.Height, height, visibleMargin));
        }

        /// <summary>한 축만 보정한다. <paramref name="screenLength"/> 안에서 <paramref name="visibleMargin"/>px는 반드시 보이게.</summary>
        public static double ClampAxis(double value, double screenStart, double screenLength, double windowLength, double visibleMargin = 40)
        {
            double size = windowLength > 0 ? windowLength : visibleMargin;
            double lower = screenStart - Math.Max(0, size - visibleMargin);
            double upper = screenStart + screenLength - visibleMargin;
            return upper < lower ? lower : Math.Max(lower, Math.Min(upper, value));
        }
    }
}
