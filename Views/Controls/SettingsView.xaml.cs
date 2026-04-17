using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 오버레이 설정 입력 UI를 제공하는 컨트롤입니다.
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public static readonly DependencyProperty OnlyChatModeProperty =
            DependencyProperty.Register(nameof(OnlyChatMode), typeof(bool), typeof(SettingsView), new PropertyMetadata(false));

        public bool OnlyChatMode
        {
            get => (bool)GetValue(OnlyChatModeProperty);
            set => SetValue(OnlyChatModeProperty, value);
        }

        public SettingsView()
        {
            InitializeComponent();
            this.PreviewKeyDown += SettingsView_PreviewKeyDown;
            Loaded += (_, _) => SyncFontOptions();
            DataContextChanged += (_, _) => SyncFontOptions();
#if DEBUG
            DebugOptionsBorder.Visibility = Visibility.Visible;
            DebugTestBorder.Visibility = Visibility.Visible;
#endif
        }

        public bool IsHotkeyInteractionActive => IsLoaded && HotkeyTab.IsSelected;

        private void SettingsView_PreviewKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                var focused = Keyboard.FocusedElement as FrameworkElement;
                if (focused is TextBox tb && tb.Tag is string tag && tag == "HotKey")
                {
                    tb.Text = string.Empty;
                    tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                    if (this.DataContext is ViewModels.SettingsViewModel svm)
                    {
                        try { svm.ApplyHotkeysCommand.Execute(null); } catch { }
                    }
                    e.Handled = true;
                }
            }
        }

        public void SetCompactMode(bool compact)
        {
            try
            {
                if (RootBorder != null)
                {
                    if (compact)
                    {
                        RootBorder.Padding = new Thickness(8);
                        SettingsTabControl.FontSize = 12;
                    }
                    else
                    {
                        RootBorder.Padding = new Thickness(15);
                        SettingsTabControl.FontSize = 14;
                    }
                }
            }
            catch { }
        }

        private void NumberOnly_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            string fullText = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength)
                                          .Insert(textBox.SelectionStart, e.Text);

            Regex regex = new Regex(@"^-?[0-9]*$");
            e.Handled = !regex.IsMatch(fullText);
        }

        private void FontOption_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton { Tag: string fontName } &&
                DataContext is ViewModels.SettingsViewModel viewModel &&
                viewModel.FontFamily != fontName)
            {
                viewModel.FontFamily = fontName;
            }
        }

        private void SyncFontOptions()
        {
            if (DataContext is not ViewModels.SettingsViewModel viewModel) return;

            NanumFontOption.IsChecked = viewModel.FontFamily == "나눔고딕";
            GulimFontOption.IsChecked = viewModel.FontFamily == "굴림";
            CustomFontOption.IsChecked = viewModel.FontFamily == "사용자 설정";
        }

        private void OffsetInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                var textBox = sender as TextBox;
                var binding = textBox?.GetBindingExpression(TextBox.TextProperty);
                binding?.UpdateSource();
                Keyboard.ClearFocus();
            }
        }

        private void HotKeyInput_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox textBox) return;

            if (e.Key == Key.Escape)
            {
                textBox.Text = string.Empty;
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                if (this.DataContext is ViewModels.SettingsViewModel svm2)
                {
                    try { svm2.ApplyHotkeysCommand.Execute(null); } catch { }
                }
                e.Handled = true;
                return;
            }

            if (e.Key is Key.Back or Key.Delete)
            {
                textBox.Text = string.Empty;
                textBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                e.Handled = true;
                return;
            }

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
                Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            {
                e.Handled = true;
                return;
            }

            var parts = new List<string>();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");

            string keyText = key.ToString();
            if (keyText.Length == 2 && keyText[0] == 'D' && char.IsDigit(keyText[1]))
                keyText = keyText[1].ToString();

            parts.Add(keyText);

            textBox.Text = string.Join("+", parts);
            var bindingExpression = textBox.GetBindingExpression(TextBox.TextProperty);
            bindingExpression?.UpdateSource();

            if (this.DataContext is ViewModels.SettingsViewModel svm &&
                bindingExpression?.ParentBinding?.Path?.Path is string propertyName)
            {
                svm.ResolveHotKeyConflict(propertyName, textBox.Text);
            }

            e.Handled = true;

            Keyboard.ClearFocus();
        }

        private void HotKeyTextBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.Focus();
                e.Handled = true;
            }
        }

        private void InjectDebugLogButton_Click(object sender, RoutedEventArgs e)
        {
#if DEBUG
            string rawText = DebugLogInputTextBox?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawText))
            {
                MessageBox.Show("테스트할 로그 텍스트를 입력해주세요.", "입력 필요", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow mainWindow)
                {
                    mainWindow.InjectDebugLogText(rawText);
                    DebugLogInputTextBox?.Clear();
                    return;
                }
            }

            MessageBox.Show("메인 윈도우를 찾을 수 없어 테스트 로그를 주입하지 못했습니다.", "주입 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
#endif
        }
    }
}
