using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using TWChatOverlay.Views;
using Xunit;

namespace TWChatOverlay.Tests
{
    /// <summary>
    /// 채팅창(ChatView): 표시 영역이 스크롤 뷰어와 맞물려 스크롤 막대가 나타나고 사라지는지,
    /// 맨 아래를 보고 있는지(새 줄 따라가기, ▼ 버튼) 표시가 맞는지.
    /// </summary>
    public class ChatViewTests
    {
        private static ChatLineContent Line(int index)
            => new(new (string Text, Brush? Brush)[] { ($"줄 {index:000} 가나다 abc", null) }, Brushes.White);

        private static ChatView Create(out ChatLineDisplay display, out ScrollViewer scrollViewer, out ScrollBar scrollBar)
        {
            var view = new ChatView();
            display = view.LogDisplayControl;
            // 창이 없어 창의 글꼴 값에 묶이지 않는다 — 직접 정한다
            display.FontFamily = new FontFamily("Malgun Gothic");
            display.FontSize = 13;
            Wpf.Layout(view, 650, 250);
            scrollViewer = Wpf.FindDescendant<ScrollViewer>(view)!;
            scrollBar = Wpf.FindDescendant<ScrollBar>(view)!;
            return view;
        }

        private static void AddLines(ChatView view, ChatLineDisplay display, int from, int count)
        {
            display.BeginUpdate();
            for (int i = from; i < from + count; i++)
                display.AddLine(Line(i));
            display.EndUpdate();
            view.UpdateLayout();
        }

        [Fact(DisplayName = "스크롤 막대: 내용이 넘칠 때만 보이고, 비우면 사라지며, 값이 표시 영역과 같다")]
        public void ScrollBar_AppearsOnlyWhenContentOverflows() => Wpf.Run(() =>
        {
            ChatView view = Create(out ChatLineDisplay display, out ScrollViewer scrollViewer, out ScrollBar scrollBar);
            Assert.Same(scrollViewer, display.ScrollOwner);

            AddLines(view, display, 0, 3);
            Assert.Equal(Visibility.Collapsed, scrollBar.Visibility);
            double widthWithoutBar = display.ActualWidth;

            AddLines(view, display, 3, 100);
            display.ScrollToEnd();
            view.UpdateLayout();
            Assert.Equal(Visibility.Visible, scrollBar.Visibility);
            // 막대만큼 글 영역이 좁아진다
            Assert.True(display.ActualWidth < widthWithoutBar);

            Assert.Equal(display.ScrollableHeight, scrollViewer.ScrollableHeight, 6);
            Assert.Equal(display.VerticalOffset, scrollViewer.VerticalOffset, 6);
            Assert.Equal(display.ViewportHeight, scrollViewer.ViewportHeight, 6);
            Assert.Equal(display.ScrollableHeight, scrollBar.Maximum, 6);
            Assert.Equal(display.VerticalOffset, scrollBar.Value, 6);

            display.Clear();
            view.UpdateLayout();
            Assert.Equal(Visibility.Collapsed, scrollBar.Visibility);
            Assert.Equal(widthWithoutBar, display.ActualWidth, 6);
        });

        [Fact(DisplayName = "맨 아래 표시: 올려 보면 꺼지고, 스크롤 뷰어로 끝까지 내리면 다시 켜진다")]
        public void IsAutoScrollEnabled_FollowsWhetherViewIsAtEnd() => Wpf.Run(() =>
        {
            ChatView view = Create(out ChatLineDisplay display, out ScrollViewer scrollViewer, out _);
            Assert.True(view.IsAutoScrollEnabled);

            AddLines(view, display, 0, 100);
            display.ScrollToEnd();
            view.UpdateLayout();
            Assert.True(view.IsAutoScrollEnabled);

            display.SetVerticalOffset(100);
            view.UpdateLayout();
            Assert.False(view.IsAutoScrollEnabled);
            Assert.Equal(100, scrollViewer.VerticalOffset, 6);

            // 스크롤 막대를 끌어 내린 것처럼 스크롤 뷰어에 위치를 준다
            scrollViewer.ScrollToVerticalOffset(scrollViewer.ScrollableHeight);
            view.UpdateLayout();
            Assert.True(display.IsAtEnd);
            Assert.True(view.IsAutoScrollEnabled);
        });
    }
}
