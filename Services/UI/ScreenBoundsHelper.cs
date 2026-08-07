using System;
using System.Windows;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 저장된 창 좌표를 화면 안으로 보정하는 헬퍼.
    /// 기존 코드는 주 모니터 <see cref="SystemParameters.WorkArea"/>만 기준으로 클램프해,
    /// 보조 모니터에 있던 창이 주 모니터로 끌려오는 문제가 있었다. 이 헬퍼는 모든 모니터를 포함하는
    /// 가상 데스크톱 경계(WPF가 DIP 단위로 제공)를 기준으로 하여 멀티모니터에서도 위치를 보존한다.
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

        /// <summary>
        /// (left, top, width, height) 창이 화면 안에 최소한 일부라도 보이도록 좌표를 보정한다.
        /// 창의 일정 비율(minVisible) 이상이 가상 데스크톱과 겹치면 그대로 두고, 그렇지 않으면
        /// 가상 데스크톱 경계 안으로 이동시킨다. 순수 함수(테스트 가능).
        /// </summary>
        public static (double Left, double Top) EnsureVisible(
            double left, double top, double width, double height, Rect desktop, double minVisible = 0.15)
        {
            if (width <= 0 || height <= 0 || desktop.Width <= 0 || desktop.Height <= 0)
                return (left, top);

            var windowRect = new Rect(left, top, width, height);
            Rect overlap = Rect.Intersect(windowRect, desktop);

            double windowArea = width * height;
            double visibleArea = overlap.IsEmpty ? 0 : overlap.Width * overlap.Height;

            // 충분히 보이면 위치를 건드리지 않는다(보조 모니터 위치 보존).
            if (windowArea > 0 && visibleArea / windowArea >= minVisible)
                return (left, top);

            // 그렇지 않으면 창 전체가 데스크톱 안에 들어오도록 클램프한다.
            double clampedLeft = left;
            double clampedTop = top;

            if (width <= desktop.Width)
                clampedLeft = Math.Max(desktop.Left, Math.Min(left, desktop.Right - width));
            else
                clampedLeft = desktop.Left;

            if (height <= desktop.Height)
                clampedTop = Math.Max(desktop.Top, Math.Min(top, desktop.Bottom - height));
            else
                clampedTop = desktop.Top;

            return (clampedLeft, clampedTop);
        }

        /// <summary>현재 가상 데스크톱 기준으로 <see cref="EnsureVisible(double,double,double,double,Rect,double)"/>를 적용.</summary>
        public static (double Left, double Top) EnsureVisible(double left, double top, double width, double height)
            => EnsureVisible(left, top, width, height, GetVirtualDesktop());
    }
}
