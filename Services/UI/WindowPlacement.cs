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
        /// <summary>저장된 위치가 있으면 그 자리로 옮긴다. 없는 축은 그대로 둔다.</summary>
        public static void ApplyStored(Window window, double? left, double? top)
        {
            if (window == null)
                return;

            if (left.HasValue)
                window.Left = left.Value;
            if (top.HasValue)
                window.Top = top.Value;
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
