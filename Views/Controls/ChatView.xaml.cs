using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 채팅 로그 표시 영역 컨트롤입니다.
    /// </summary>
    public partial class ChatView : UserControl
    {
        private ScrollViewer? _scrollViewer;

        public static readonly DependencyProperty IsAutoScrollEnabledProperty =
            DependencyProperty.Register(nameof(IsAutoScrollEnabled), typeof(bool), typeof(ChatView), new PropertyMetadata(true));

        public ChatView()
        {
            InitializeComponent();
            Loaded += ChatView_Loaded;
        }

        public bool IsAutoScrollEnabled
        {
            get => (bool)GetValue(IsAutoScrollEnabledProperty);
            private set => SetValue(IsAutoScrollEnabledProperty, value);
        }

        public RichTextBox LogDisplayControl => LogDisplay;

        private void ChatView_Loaded(object sender, RoutedEventArgs e)
        {
            if (_scrollViewer != null) return;

            _scrollViewer = FindDescendant<ScrollViewer>(LogDisplay);
            if (_scrollViewer == null) return;

            _scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
            UpdateAutoScrollState();
        }

        private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateAutoScrollState();
        }

        private void UpdateAutoScrollState()
        {
            if (_scrollViewer == null) return;

            if (_scrollViewer.ScrollableHeight <= 0)
            {
                IsAutoScrollEnabled = true;
                return;
            }

            IsAutoScrollEnabled = Math.Abs(_scrollViewer.VerticalOffset - _scrollViewer.ScrollableHeight) < 1.0;
        }

        /// <summary>
        /// 창이 클릭 통과 상태라 휠이 직접 들어오지 않을 때, 전역 훅이 받은 휠로 대신 스크롤한다.
        /// delta는 120 단위(양수 = 위로). ScrollViewer가 휠을 직접 받을 때와 같은 양(시스템 휠 줄 수 × LineUp/LineDown)만큼 움직인다.
        /// </summary>
        public void ScrollByWheelDelta(int delta)
        {
            _scrollViewer ??= FindDescendant<ScrollViewer>(LogDisplay);
            if (_scrollViewer == null || delta == 0)
                return;

            int lines = SystemParameters.WheelScrollLines;
            if (lines <= 0)
                lines = 3;

            int notches = Math.Max(1, Math.Abs(delta) / 120);
            int steps = notches * lines;
            for (int i = 0; i < steps; i++)
            {
                if (delta > 0)
                    _scrollViewer.LineUp();
                else
                    _scrollViewer.LineDown();
            }
        }

        private void ScrollToBottom_Click(object sender, RoutedEventArgs e)
        {
            LogDisplay.ScrollToEnd();
            UpdateAutoScrollState();
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                    return typed;

                var result = FindDescendant<T>(child);
                if (result != null)
                    return result;
            }

            return null;
        }
    }
}
