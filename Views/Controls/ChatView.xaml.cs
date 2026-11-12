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
        private bool _isSyncingScrollBar;

        public static readonly DependencyProperty IsAutoScrollEnabledProperty =
            DependencyProperty.Register(nameof(IsAutoScrollEnabled), typeof(bool), typeof(ChatView), new PropertyMetadata(true));

        public ChatView()
        {
            InitializeComponent();
            LogDisplay.ScrollMetricsChanged += (_, _) => SyncScrollState();
        }

        /// <summary>맨 아래를 보고 있는지. 위로 올려 보는 중이면 새 줄이 와도 따라 내려가지 않는다.</summary>
        public bool IsAutoScrollEnabled
        {
            get => (bool)GetValue(IsAutoScrollEnabledProperty);
            private set => SetValue(IsAutoScrollEnabledProperty, value);
        }

        public ChatLineDisplay LogDisplayControl => LogDisplay;

        /// <summary>표시 영역의 스크롤 범위·위치를 스크롤 막대와 자동 스크롤 표시에 옮긴다.</summary>
        private void SyncScrollState()
        {
            double scrollable = LogDisplay.ScrollableHeight;
            _isSyncingScrollBar = true;
            try
            {
                LogScrollBar.Maximum = scrollable;
                LogScrollBar.ViewportSize = LogDisplay.ViewportHeight;
                LogScrollBar.LargeChange = Math.Max(1, LogDisplay.ViewportHeight);
                LogScrollBar.SmallChange = 16;
                LogScrollBar.Value = LogDisplay.VerticalOffset;
                // 막대가 나타나면 표시 영역이 좁아져 줄바꿈을 다시 잡는다 — 넘칠 때 한 번만 일어난다
                LogScrollBar.Visibility = scrollable > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            finally
            {
                _isSyncingScrollBar = false;
            }

            bool atEnd = LogDisplay.IsAtEnd;
            if (IsAutoScrollEnabled != atEnd)
                IsAutoScrollEnabled = atEnd;
        }

        private void LogScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isSyncingScrollBar)
                return;
            LogDisplay.ScrollToVerticalOffset(e.NewValue);
        }

        private void ScrollToBottom_Click(object sender, RoutedEventArgs e)
        {
            LogDisplay.ScrollToEnd();
        }
    }
}
