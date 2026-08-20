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
            DependencyProperty.Register(nameof(OnlyChatMode), typeof(bool), typeof(SettingsView),
                new PropertyMetadata(false, (d, _) => ((SettingsView)d).ApplyPanelVisibility()));

        public bool OnlyChatMode
        {
            get => (bool)GetValue(OnlyChatModeProperty);
            set => SetValue(OnlyChatModeProperty, value);
        }

        public SettingsView()
        {
            InitializeComponent();
            this.PreviewKeyDown += SettingsView_PreviewKeyDown;
            Loaded += SettingsView_Loaded;
            Unloaded += SettingsView_Unloaded;
            DataContextChanged += (_, _) => SyncFontOptions();
            _debugOptionsAllowed = false;
#if DEBUG
            _debugOptionsAllowed = true;
#endif
            ApplyPanelVisibility();
        }

        private readonly bool _debugOptionsAllowed;

        /// <summary>
        /// 단축키 화면이 실제로 보이며 선택된 상태인지. (이때만 전역 단축키를 억제)
        /// 창이 숨겨진 상태(트레이 등)에서는 억제하지 않아야 트레이 복원 단축키가 동작한다.
        /// </summary>
        public bool IsHotkeyInteractionActive => IsLoaded && IsVisible && NavHotkeys.IsChecked == true && !OnlyChatMode;

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            ApplyPanelVisibility();
        }

        /// <summary>선택된 추가 기능 내비 인덱스(AddonView 탭 인덱스). 선택이 없으면 -1.</summary>
        private int SelectedAddonTabIndex
        {
            get
            {
                if (NavAddonKeyword.IsChecked == true) return 0;
                if (NavAddonExp.IsChecked == true) return 1;
                if (NavAddonDungeon.IsChecked == true) return 2;
                if (NavAddonItem.IsChecked == true) return 3;
                if (NavAddonBuff.IsChecked == true) return 4;
                if (NavAddonBoss.IsChecked == true) return 5;
                return -1;
            }
        }

        private bool _addonEmbedInitialized;
        private bool _addonPreviewActive;

        /// <summary>
        /// 선택된 내비게이션 항목의 패널만 표시한다.
        /// 컴팩트 모드(OnlyChatMode)에서는 내비게이션을 숨기고 채팅+외치기 패널을 함께 보여준다.
        /// 추가 기능 항목은 임베드된 AddonView의 해당 탭을 보여준다.
        /// </summary>
        private void ApplyPanelVisibility()
        {
            if (ChatPanel == null) return;

            if (OnlyChatMode)
            {
                NavColumn.Visibility = Visibility.Collapsed;
                AddonHost.Visibility = Visibility.Collapsed;
                SettingsScroll.Visibility = Visibility.Visible;
                ChatPanel.Visibility = Visibility.Visible;
                ShoutPanel.Visibility = Visibility.Visible;
                DisplayPanel.Visibility = Visibility.Collapsed;
                HotkeyPanel.Visibility = Visibility.Collapsed;
                SystemPanel.Visibility = Visibility.Collapsed;
                return;
            }

            NavColumn.Visibility = Visibility.Visible;

            int addonTab = SelectedAddonTabIndex;
            if (addonTab >= 0)
            {
                if (!_addonEmbedInitialized)
                {
                    AddonHost.SetEmbeddedMode();
                    _addonEmbedInitialized = true;
                }

                SettingsScroll.Visibility = Visibility.Collapsed;
                AddonHost.Visibility = Visibility.Visible;
                AddonHost.ShowEmbeddedTab(addonTab);
                _addonPreviewActive = true;
                return;
            }

            if (_addonPreviewActive)
            {
                _addonPreviewActive = false;
                AddonHost.NotifyEmbeddedHidden();
            }

            AddonHost.Visibility = Visibility.Collapsed;
            SettingsScroll.Visibility = Visibility.Visible;
            ChatPanel.Visibility = NavChat.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ShoutPanel.Visibility = NavShout.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            DisplayPanel.Visibility = NavDisplay.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            HotkeyPanel.Visibility = NavHotkeys.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            SystemPanel.Visibility = NavSystem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            DebugOptionsBorder.Visibility = _debugOptionsAllowed ? Visibility.Visible : Visibility.Collapsed;
        }

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
                    RootBorder.Padding = new Thickness(compact ? 4 : 12);
                    FontSize = compact ? 12 : 13;
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

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            SyncFontOptions();
            UpdateSettingsPositionMode(true);
        }

        private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
        {
            UpdateSettingsPositionMode(false);
        }

        private void UpdateSettingsPositionMode(bool isEnabled)
        {
            if (OnlyChatMode)
                return;

            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow mainWindow)
                {
                    mainWindow.SetSettingsPositionMode(isEnabled);
                    break;
                }
            }
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

        /// <summary>idtag.txt를 기본 텍스트 편집기로 엽니다. 저장하면 감시기가 즉시 다시 읽습니다.</summary>
        private void OpenIdTagFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string path = Services.IdTagService.FilePath;
                if (!System.IO.File.Exists(path))
                    Services.IdTagService.Initialize();

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                Services.AppLogger.Warn("Failed to open idtag.txt.", ex);
                try { MessageBox.Show($"idtag.txt를 열 수 없습니다:\n{Services.IdTagService.FilePath}", "오류", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
            }
        }

        private void HotKeyTextBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is TextBox tb)
            {
                tb.Focus();
                e.Handled = true;
            }
        }

    }
}
