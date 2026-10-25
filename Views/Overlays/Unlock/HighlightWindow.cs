using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Unlock
{
    /// <summary>
    /// 선택된 창 위에 겹쳐 표시되는 하이라이트 테두리(클릭 통과).
    /// 대상보다 4px 크게 그려서 z-order 다툼과 무관하게 테두리가 항상 보인다.
    /// </summary>
    internal sealed class HighlightWindow : Window
    {
        private const double Inflate = 4;
        private static readonly SolidColorBrush HighlightBorderBrush = UnlockWindowStyle.Frozen(Color.FromRgb(0x0C, 0xD2, 0x9D));
        private static readonly SolidColorBrush HighlightFillBrush = UnlockWindowStyle.Frozen(Color.FromArgb(0x16, 0x0C, 0xD2, 0x9D));

        public HighlightWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            IsHitTestVisible = false;
            Focusable = false;

            Content = new Border
            {
                BorderBrush = HighlightBorderBrush,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(5),
                Background = HighlightFillBrush,
            };

            SourceInitialized += (_, _) => UnlockWindowStyle.ApplyClickThrough(this);
        }

        public void SyncTo(Window target)
        {
            try
            {
                Left = target.Left - Inflate;
                Top = target.Top - Inflate;
                Width = Math.Max(16, target.ActualWidth + Inflate * 2);
                Height = Math.Max(16, target.ActualHeight + Inflate * 2);
            }
            catch { }
        }
    }
}
