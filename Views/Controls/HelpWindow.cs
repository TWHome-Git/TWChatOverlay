using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 설정 [?] 버튼용 도움말 창. 다크/민트 스타일(ConfirmDialogWindow와 동일 팔레트).
    /// 싱글턴으로 재사용되며, 다른 항목의 [?]를 누르면 내용만 바뀐다.
    /// 내용은 <see cref="HelpTopics"/>에서 키로 가져온다.
    /// </summary>
    public sealed class HelpWindow : Window
    {
        private static HelpWindow? _instance;

        private static readonly Color PanelBg = Color.FromRgb(0x10, 0x16, 0x14);
        private static readonly Color BorderCol = Color.FromRgb(0x2A, 0x33, 0x2E);
        private static readonly Color TextCol = Color.FromRgb(0xE8, 0xEA, 0xE9);
        private static readonly Color SubTextCol = Color.FromRgb(0x8C, 0x91, 0x97);
        private static readonly Color Mint = Color.FromRgb(0x0C, 0xD2, 0x9D);

        private readonly TextBlock _titleText;
        private readonly TextBlock _bodyText;

        private HelpWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 360;
            FontFamily = WindowFontService.ResolveCurrentFont();

            _titleText = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Mint),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var closeButton = new Button
            {
                Content = "X",
                HorizontalAlignment = HorizontalAlignment.Right,
            };
            closeButton.SetResourceReference(StyleProperty, "WindowCloseButtonStyle");
            closeButton.Click += (_, _) => Hide();

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(closeButton, Dock.Right);
            header.Children.Add(closeButton);
            header.Children.Add(_titleText);

            _bodyText = new TextBlock
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(TextCol),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 19,
            };

            var bodyScroll = new ScrollViewer
            {
                Content = _bodyText,
                MaxHeight = 340,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };

            var hint = new TextBlock
            {
                Text = "ESC 또는 [닫기]로 닫습니다",
                FontSize = 10,
                Foreground = new SolidColorBrush(SubTextCol),
                Margin = new Thickness(0, 10, 0, 0),
            };

            var root = new StackPanel();
            root.Children.Add(header);
            root.Children.Add(bodyScroll);
            root.Children.Add(hint);

            var panel = new Border
            {
                Padding = new Thickness(16, 12, 16, 12),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0xF6, PanelBg.R, PanelBg.G, PanelBg.B)),
                BorderBrush = new SolidColorBrush(BorderCol),
                Child = root,
            };
            Content = panel;

            // 패널 아무 곳이나 잡고 이동 가능
            panel.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    Hide();
                    e.Handled = true;
                }
            };
        }

        /// <summary>도움말 창을 열거나(이미 열려 있으면 내용 교체) 지정한 주제를 표시한다.</summary>
        public static void ShowTopic(string key, Window? owner = null)
        {
            try
            {
                if (_instance == null || !_instance.IsLoaded)
                {
                    _instance = new HelpWindow();
                    _instance.Closed += (_, _) => _instance = null;
                }

                var (title, body) = HelpTopics.Get(key);
                _instance._titleText.Text = title;
                _instance._bodyText.Text = body;

                if (!_instance.IsVisible)
                {
                    // 소유 창(설정 창) 오른쪽 옆에 표시, 없으면 화면 중앙
                    if (owner?.IsVisible == true)
                    {
                        double left = owner.Left + owner.ActualWidth + 10;
                        if (left + _instance.Width > SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth)
                            left = owner.Left - _instance.Width - 10;

                        _instance.Left = left;
                        _instance.Top = owner.Top;
                    }
                    else
                    {
                        _instance.Left = (SystemParameters.PrimaryScreenWidth - _instance.Width) / 2.0;
                        _instance.Top = SystemParameters.PrimaryScreenHeight / 3.0;
                    }

                    _instance.Show();
                }

                // 숨겼다 다시 열면 이전 z-order(설정 창 뒤)에 남아 안 보일 수 있어 최상단으로 재삽입
                _instance.Topmost = false;
                _instance.Topmost = true;
                TopmostWindowHelper.BringToTopmost(_instance);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to show help window.", ex);
            }
        }

        /// <summary>설정 창이 닫힐 때 함께 닫는다.</summary>
        public static void HideIfOpen()
        {
            try { _instance?.Hide(); } catch { }
        }
    }
}
