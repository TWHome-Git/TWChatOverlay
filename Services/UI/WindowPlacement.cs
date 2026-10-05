using System.Windows;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 설정에 저장된 위치/크기를 창에 적용하는 공용 함수.
    /// 값을 인자로 먼저 받으므로, Left를 바꾸는 순간 LocationChanged → 설정 저장이 동기로 일어나
    /// (새 Left, 옛 Top)이 설정에 덮어써져도 Top은 이미 읽어 둔 값으로 옮겨진다.
    /// 설정 프로퍼티를 Left 대입 뒤에 다시 읽는 방식으로 쓰면 이 보호가 사라지니 반드시 인자로 넘길 것.
    /// </summary>
    public static class WindowPlacement
    {
        /// <summary>창을 잡을 수 있으려면 이만큼은 화면 안에 들어와 있어야 한다.</summary>
        private const double MinVisible = 80;

        /// <summary>
        /// 저장된 위치가 있으면 그 자리로 옮긴다. 없는 축은 그대로 둔다.
        /// 저장된 자리가 지금 화면 밖이면(보조 모니터를 떼거나 해상도·배율이 바뀐 경우)
        /// 0,0으로 끌어온다 — 그대로 두면 창이 보이지 않아 되찾을 길이 없다.
        /// </summary>
        public static void ApplyStored(Window window, double? left, double? top)
        {
            if (window == null)
                return;
            if (!left.HasValue && !top.HasValue)
                return;

            double targetLeft = left ?? window.Left;
            double targetTop = top ?? window.Top;

            if (!IsOnScreen(targetLeft, targetTop))
            {
                // 한 축만 저장돼 있어도 화면 밖이면 둘 다 되돌린다 (한쪽만 고치면 여전히 안 보인다)
                window.Left = 0;
                window.Top = 0;
                return;
            }

            if (left.HasValue)
                window.Left = targetLeft;
            if (top.HasValue)
                window.Top = targetTop;
        }

        /// <summary>그 자리에 창을 두면 화면에서 잡을 수 있는지. 모니터가 여럿이면 전체를 합친 영역으로 본다.</summary>
        private static bool IsOnScreen(double left, double top)
        {
            if (double.IsNaN(left) || double.IsNaN(top) ||
                double.IsInfinity(left) || double.IsInfinity(top))
                return false;

            double screenLeft = SystemParameters.VirtualScreenLeft;
            double screenTop = SystemParameters.VirtualScreenTop;
            double screenRight = screenLeft + SystemParameters.VirtualScreenWidth;
            double screenBottom = screenTop + SystemParameters.VirtualScreenHeight;

            // 왼쪽은 조금 걸쳐 있어도 되지만, 위로 넘어가면 제목줄을 잡을 수 없다
            return left >= screenLeft - MinVisible
                && left <= screenRight - MinVisible
                && top >= screenTop
                && top <= screenBottom - MinVisible;
        }

        /// <summary>저장된 위치/크기를 적용한다. 크기는 창의 최소 크기보다 작으면 무시한다.</summary>
        public static void ApplyStoredBounds(Window window, double? left, double? top, double? width, double? height)
        {
            if (window == null)
                return;

            ApplyStored(window, left, top);

            if (width.HasValue && width.Value > 0 && width.Value >= window.MinWidth)
                window.Width = width.Value;
            if (height.HasValue && height.Value > 0 && height.Value >= window.MinHeight)
                window.Height = height.Value;
        }
    }
}
