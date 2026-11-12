using System.Windows;
using System.Windows.Controls;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 채팅 로그 표시 영역 컨트롤입니다.
    /// </summary>
    public partial class ChatView : UserControl
    {
        public static readonly DependencyProperty IsAutoScrollEnabledProperty =
            DependencyProperty.Register(nameof(IsAutoScrollEnabled), typeof(bool), typeof(ChatView), new PropertyMetadata(true));

        public ChatView()
        {
            InitializeComponent();
            // 스크롤 막대는 스크롤 뷰어가 맞춘다. 여기서는 맨 아래를 보고 있는지만 따라간다 (▼ 버튼 표시, 새 줄 따라가기)
            LogDisplay.ScrollMetricsChanged += (_, _) =>
            {
                bool atEnd = LogDisplay.IsAtEnd;
                if (IsAutoScrollEnabled != atEnd)
                    IsAutoScrollEnabled = atEnd;
            };
        }

        /// <summary>맨 아래를 보고 있는지. 위로 올려 보는 중이면 새 줄이 와도 따라 내려가지 않는다.</summary>
        public bool IsAutoScrollEnabled
        {
            get => (bool)GetValue(IsAutoScrollEnabledProperty);
            private set => SetValue(IsAutoScrollEnabledProperty, value);
        }

        public ChatLineDisplay LogDisplayControl => LogDisplay;

        private void ScrollToBottom_Click(object sender, RoutedEventArgs e)
        {
            LogDisplay.ScrollToEnd();
        }
    }
}
