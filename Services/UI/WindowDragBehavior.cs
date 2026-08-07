using System;
using System.Windows;
using System.Windows.Input;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 창 드래그 이동 로직을 한 곳으로 모은 헬퍼 + Attached Behavior.
    /// 기존에는 20여 개 창이 각자 <c>try { DragMove(); } catch { }</c> 형태를 복제하고 있었다.
    /// </summary>
    public static class WindowDragBehavior
    {
        /// <summary>
        /// 왼쪽 버튼이 눌린 상태면 소속 창을 드래그 이동시킨다. 드래그 시작 조건이 아니면 조용히 무시.
        /// 코드비하인드의 MouseLeftButtonDown 핸들러에서 호출한다.
        /// </summary>
        public static void BeginDrag(Window? window, MouseButtonEventArgs e)
        {
            if (window == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            try
            {
                window.DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove는 왼쪽 버튼 다운 처리 중에만 유효하다. 그 외 상황이면 무시.
            }
        }

        // === XAML에서 선언적으로 쓰기 위한 Attached Behavior ===
        // 사용 예: <Border local:WindowDragBehavior.IsDragEnabled="True" .../>

        public static readonly DependencyProperty IsDragEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsDragEnabled",
                typeof(bool),
                typeof(WindowDragBehavior),
                new PropertyMetadata(false, OnIsDragEnabledChanged));

        public static bool GetIsDragEnabled(DependencyObject obj) => (bool)obj.GetValue(IsDragEnabledProperty);
        public static void SetIsDragEnabled(DependencyObject obj, bool value) => obj.SetValue(IsDragEnabledProperty, value);

        private static void OnIsDragEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element)
                return;

            if (e.NewValue is true)
                element.MouseLeftButtonDown += Element_MouseLeftButtonDown;
            else
                element.MouseLeftButtonDown -= Element_MouseLeftButtonDown;
        }

        private static void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DependencyObject dep)
                BeginDrag(Window.GetWindow(dep), e);
        }
    }
}
