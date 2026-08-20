using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 잠금 해제 인스펙터 패널과 같은 다크/민트 스타일의 확인 대화상자.
    /// 윈도우 기본 MessageBox 대신 사용한다. 확인이면 true를 돌려준다.
    /// </summary>
    public sealed class ConfirmDialogWindow : Window
    {
        // 인스펙터 패널과 동일한 팔레트 (UiLockService.InspectorWindow 참고)
        private static readonly Color PanelBg = Color.FromRgb(0x10, 0x16, 0x14);
        private static readonly Color FieldBg = Color.FromRgb(0x0D, 0x12, 0x10);
        private static readonly Color BorderCol = Color.FromRgb(0x2A, 0x33, 0x2E);
        private static readonly Color TextCol = Color.FromRgb(0xE8, 0xEA, 0xE9);
        private static readonly Color Mint = Color.FromRgb(0x0C, 0xD2, 0x9D);

        private bool _confirmed;

        private ConfirmDialogWindow(string message, string confirmText, string cancelText)
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            FontFamily = WindowFontService.ResolveCurrentFont();

            var text = new TextBlock
            {
                Text = message,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextCol),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14),
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            buttons.Children.Add(CreateButton(confirmText, isAccent: true, onClick: () =>
            {
                _confirmed = true;
                Close();
            }));
            buttons.Children.Add(CreateButton(cancelText, isAccent: false, onClick: Close));

            var root = new StackPanel();
            root.Children.Add(text);
            root.Children.Add(buttons);

            var panel = new Border
            {
                Padding = new Thickness(20, 16, 20, 14),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Background = new SolidColorBrush(Color.FromArgb(0xF6, PanelBg.R, PanelBg.G, PanelBg.B)),
                BorderBrush = new SolidColorBrush(BorderCol),
                Child = root,
            };
            Content = panel;

            // 패널 아무 곳이나 잡고 옮길 수 있게
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
                    Close();
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter)
                {
                    _confirmed = true;
                    Close();
                    e.Handled = true;
                }
            };
        }

        private Button CreateButton(string text, bool isAccent, Action onClick)
        {
            var border = new Border
            {
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(18, 5, 18, 5),
                Background = isAccent
                    ? new SolidColorBrush(Color.FromArgb(0x2A, Mint.R, Mint.G, Mint.B))
                    : new SolidColorBrush(FieldBg),
                BorderBrush = isAccent
                    ? new SolidColorBrush(Mint)
                    : new SolidColorBrush(BorderCol),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(isAccent ? Mint : TextCol),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var button = new Button
            {
                Margin = new Thickness(6, 0, 6, 0),
                Cursor = Cursors.Hand,
                Focusable = false,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Content = border,
            };

            // 템플릿 없이 콘텐츠만 그대로 노출 (기본 크롬 제거)
            button.Template = CreateBareTemplate();
            button.Click += (_, _) => onClick();
            button.MouseEnter += (_, _) => border.Background = isAccent
                ? new SolidColorBrush(Color.FromArgb(0x45, Mint.R, Mint.G, Mint.B))
                : new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
            button.MouseLeave += (_, _) => border.Background = isAccent
                ? new SolidColorBrush(Color.FromArgb(0x2A, Mint.R, Mint.G, Mint.B))
                : new SolidColorBrush(FieldBg);
            return button;
        }

        private static ControlTemplate CreateBareTemplate()
        {
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            return new ControlTemplate(typeof(Button)) { VisualTree = presenter };
        }

        /// <summary>확인 대화상자를 모달로 띄운다. 확인을 누르면 true.</summary>
        public static bool Confirm(Window? owner, string message, string confirmText = "확인", string cancelText = "취소")
        {
            var dialog = new ConfirmDialogWindow(message, confirmText, cancelText);

            if (owner?.IsVisible == true)
            {
                dialog.Owner = owner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            try
            {
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Confirm dialog failed; treating as confirmed.", ex);
                return true;
            }

            return dialog._confirmed;
        }
    }
}
