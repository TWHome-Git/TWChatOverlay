using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Unlock
{
    /// <summary>주 모니터 상단 중앙의 안내 + 완료 버튼 배너.</summary>
    internal sealed class BannerWindow : Window
    {
        public BannerWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;

            var banner = new Border
            {
                Padding = new Thickness(14, 8, 10, 8),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
            };
            banner.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
            banner.SetResourceReference(Border.BorderBrushProperty, "OverlayAccentBorderBrush");

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            var text = new TextBlock
            {
                Text = "잠금 해제 모드 — 창을 클릭해 선택하고 드래그해 배치하세요",
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0),
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, "OverlayInfoTextBrush");
            panel.Children.Add(text);

            var snapButton = new Button
            {
                Padding = new Thickness(12, 4, 12, 4),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 8, 0),
            };
            snapButton.SetResourceReference(FrameworkElement.StyleProperty, "SimButtonStyle");
            void RefreshSnapButton() =>
                snapButton.Content = AppServices.Get<UiLockService>().SnapEnabled ? "자석 ON" : "자석 OFF";
            RefreshSnapButton();
            snapButton.Click += (_, _) =>
            {
                AppServices.Get<UiLockService>().SnapEnabled = !AppServices.Get<UiLockService>().SnapEnabled;
                RefreshSnapButton();
                try
                {
                    var settings = ToastPresentationHelper.FindSharedSettings();
                    if (settings != null)
                    {
                        settings.WindowSnapEnabled = AppServices.Get<UiLockService>().SnapEnabled;
                        ConfigService.SaveDeferred(settings);
                    }
                }
                catch { }
            };
            IsVisibleChanged += (_, _) => RefreshSnapButton();
            panel.Children.Add(snapButton);

            var doneButton = new Button
            {
                Content = "완료",
                Padding = new Thickness(16, 4, 16, 4),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Foreground = Brushes.White,
            };
            doneButton.SetResourceReference(FrameworkElement.StyleProperty, "SimButtonStyle");
            doneButton.Click += (_, _) => AppServices.Get<UiLockService>().Set(false);
            panel.Children.Add(doneButton);

            banner.Child = panel;
            Content = banner;

            // 주 모니터 상단 중앙 배치 (크기 확정 후)
            Loaded += (_, _) => PositionTopCenter();
            SizeChanged += (_, _) => PositionTopCenter();

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) AppServices.Get<UiLockService>().Set(false);
            };
        }

        private void PositionTopCenter()
        {
            double width = ActualWidth > 0 ? ActualWidth : 320;
            Left = (SystemParameters.PrimaryScreenWidth - width) / 2.0;
            Top = 0;
        }
    }
}
