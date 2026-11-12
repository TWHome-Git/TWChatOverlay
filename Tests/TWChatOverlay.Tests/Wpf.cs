using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace TWChatOverlay.Tests
{
    /// <summary>
    /// WPF 컨트롤은 STA 스레드에서만 만들 수 있다. 테스트 전체가 스레드 하나를 같이 쓴다
    /// (앱 리소스의 스타일·브러시는 만든 스레드에서만 쓸 수 있어서 테스트마다 스레드를 새로 만들 수 없다).
    /// 창은 하나도 띄우지 않는다 — 컨트롤을 직접 재고 배치한다.
    /// </summary>
    internal static class Wpf
    {
        private static readonly Dispatcher UiDispatcher = Start();

        private static Dispatcher Start()
        {
            Dispatcher? dispatcher = null;
            Exception? startError = null;
            using var ready = new ManualResetEventSlim();
            var thread = new Thread(() =>
            {
                try
                {
                    dispatcher = Dispatcher.CurrentDispatcher;
                    // 채팅창이 쓰는 스타일(스크롤 막대, 오른쪽 메뉴)은 앱 리소스에서 찾는다
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri("/TWChatOverlay;component/Resources/Styles.xaml", UriKind.Relative),
                    });
                }
                catch (Exception ex)
                {
                    startError = ex;
                }
                finally
                {
                    ready.Set();
                }

                if (startError == null)
                    Dispatcher.Run();
            })
            { IsBackground = true, Name = "WPF tests" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.Wait();

            if (startError != null)
                ExceptionDispatchInfo.Capture(startError).Throw();
            return dispatcher!;
        }

        /// <summary>테스트 본문을 WPF 스레드에서 돌린다. 안에서 난 예외(검증 실패 포함)는 그대로 밖으로 나온다.</summary>
        public static void Run(Action test)
        {
            Exception? error = null;
            UiDispatcher.Invoke(() =>
            {
                try
                {
                    test();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });

            if (error != null)
                ExceptionDispatchInfo.Capture(error).Throw();
        }

        /// <summary>창 없이 컨트롤을 주어진 크기로 재고 배치한다. 크기를 바꿀 때도 다시 부른다.</summary>
        public static void Layout(FrameworkElement element, double width, double height)
        {
            element.Measure(new Size(width, height));
            element.Arrange(new Rect(0, 0, width, height));
            element.UpdateLayout();
        }

        public static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T typed)
                    return typed;
                T? found = FindDescendant<T>(child);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
