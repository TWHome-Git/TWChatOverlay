using System.Collections.Generic;
using System.Linq;
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

        /// <summary>잠금 해제 모드를 시작하고, 배치가 잘 보이도록 설정 창을 닫는다.</summary>
        private void UnlockMode_Click(object sender, RoutedEventArgs e)
        {
            Services.UiLockService.Set(true);
            if (!OnlyChatMode)
            {
                try { Window.GetWindow(this)?.Close(); } catch { }
            }
        }

        /// <summary>선택된 추가 기능 내비 인덱스(위치 미리보기용 탭 인덱스). 선택이 없으면 -1.</summary>
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

        private bool _addonPreviewActive;
        private ViewModels.AddonViewModel? _addonViewModel;

        /// <summary>추가 기능 패널들의 DataContext(AddonViewModel)를 준비한다.</summary>
        private void EnsureAddonViewModel()
        {
            if (_addonViewModel != null) return;

            var mainWindow = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
            if (mainWindow?.DataContext is not Models.ChatSettings settings) return;

            _addonViewModel = new ViewModels.AddonViewModel(settings);
            AddonKeywordPanel.DataContext = _addonViewModel;
            AddonExpPanel.DataContext = _addonViewModel;
            AddonDungeonPanel.DataContext = _addonViewModel;
            AddonItemPanel.DataContext = _addonViewModel;
            AddonBuffPanel.DataContext = _addonViewModel;
            AddonBossPanel.DataContext = _addonViewModel;
        }

        /// <summary>추가 기능 카테고리 표시 중 관련 오버레이 창의 위치 미리보기를 켜고 끈다.</summary>
        private static void SetAddonPositionPreview(bool isEnabled, int tabIndex)
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow mainWindow)
                {
                    mainWindow.SetAddonPositionPreviewTabIndex(tabIndex);
                    mainWindow.SetAddonPositionMode(isEnabled);
                    return;
                }
            }
        }

        /// <summary>
        /// 선택된 내비게이션 항목의 패널만 표시한다.
        /// 컴팩트 모드(OnlyChatMode)에서는 내비게이션을 숨기고 채팅+외치기 패널을 함께 보여준다.
        /// </summary>
        private void ApplyPanelVisibility()
        {
            if (ChatPanel == null) return;

            if (OnlyChatMode)
            {
                NavColumn.Visibility = Visibility.Collapsed;
                ChatPanel.Visibility = Visibility.Visible;
                ShoutPanel.Visibility = Visibility.Visible;
                DisplayPanel.Visibility = Visibility.Collapsed;
                HotkeyPanel.Visibility = Visibility.Collapsed;
                SystemPanel.Visibility = Visibility.Collapsed;
                AddonKeywordPanel.Visibility = Visibility.Collapsed;
                AddonExpPanel.Visibility = Visibility.Collapsed;
                AddonDungeonPanel.Visibility = Visibility.Collapsed;
                AddonItemPanel.Visibility = Visibility.Collapsed;
                AddonBuffPanel.Visibility = Visibility.Collapsed;
                AddonBossPanel.Visibility = Visibility.Collapsed;
                return;
            }

            NavColumn.Visibility = Visibility.Visible;

            int addonTab = SelectedAddonTabIndex;
            if (addonTab >= 0)
            {
                EnsureAddonViewModel();
                SetAddonPositionPreview(true, addonTab);
                _addonPreviewActive = true;
            }
            else if (_addonPreviewActive)
            {
                _addonPreviewActive = false;
                SetAddonPositionPreview(false, -1);
            }

            ChatPanel.Visibility = NavChat.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ShoutPanel.Visibility = NavShout.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            DisplayPanel.Visibility = NavDisplay.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            HotkeyPanel.Visibility = NavHotkeys.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            SystemPanel.Visibility = NavSystem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AddonKeywordPanel.Visibility = NavAddonKeyword.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AddonExpPanel.Visibility = NavAddonExp.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AddonDungeonPanel.Visibility = NavAddonDungeon.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AddonItemPanel.Visibility = NavAddonItem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AddonBuffPanel.Visibility = NavAddonBuff.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AddonBossPanel.Visibility = NavAddonBoss.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
        }

        private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
        {
            _itemDropPreviewTimer?.Stop();
            if (_addonPreviewActive)
            {
                _addonPreviewActive = false;
                SetAddonPositionPreview(false, -1);
            }
        }

        #region 추가 기능 핸들러 (구 AddonView에서 이식)

        private System.Windows.Threading.DispatcherTimer? _itemDropPreviewTimer;
        private string? _pendingPreviewSoundFile;

        private void EnsurePreviewTimer()
        {
            if (_itemDropPreviewTimer != null) return;
            _itemDropPreviewTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = System.TimeSpan.FromMilliseconds(180)
            };
            _itemDropPreviewTimer.Tick += (_, _) =>
            {
                _itemDropPreviewTimer.Stop();
                if (!string.IsNullOrWhiteSpace(_pendingPreviewSoundFile))
                {
                    Services.NotificationService.PlayAlert(_pendingPreviewSoundFile);
                }
            };
        }

        private void PreviewSoundSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded)
                return;

            if (Equals(e.OldValue, e.NewValue))
                return;

            if (sender is not Slider slider || slider.Tag is not string soundFile || string.IsNullOrWhiteSpace(soundFile))
                return;

            // 바인딩 갱신(로드/전환)에는 반응하지 않고 직접 조작할 때만 미리듣기
            bool isUserInteraction =
                slider.IsMouseCaptureWithin ||
                (slider.IsKeyboardFocusWithin &&
                 (Keyboard.IsKeyDown(Key.Left) || Keyboard.IsKeyDown(Key.Right) || Keyboard.IsKeyDown(Key.Up) || Keyboard.IsKeyDown(Key.Down)));
            if (!isUserInteraction)
                return;

            EnsurePreviewTimer();
            _pendingPreviewSoundFile = soundFile;
            _itemDropPreviewTimer!.Stop();
            _itemDropPreviewTimer.Start();
        }

        private void MoveDropItemsToCustom_Click(object sender, RoutedEventArgs e)
        {
            _addonViewModel?.MoveToCustom(System.Linq.Enumerable.ToList(
                System.Linq.Enumerable.Cast<ViewModels.DropItemFilterEntry>(DefaultDropItemsListBox.SelectedItems)));
        }

        private void MoveDropItemsToDefault_Click(object sender, RoutedEventArgs e)
        {
            _addonViewModel?.MoveToDefault(System.Linq.Enumerable.ToList(
                System.Linq.Enumerable.Cast<ViewModels.DropItemFilterEntry>(CustomDropItemsListBox.SelectedItems)));
        }

        private void ExperienceLimitExp_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9,]+$");
        }

        private void ExperienceLimitExp_Pasting(object sender, System.Windows.DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text, true))
            {
                e.CancelCommand();
                return;
            }

            if (e.SourceDataObject.GetData(DataFormats.Text) is not string text ||
                !Regex.IsMatch(text, @"^[0-9,]+$"))
            {
                e.CancelCommand();
            }
        }

        #endregion

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
