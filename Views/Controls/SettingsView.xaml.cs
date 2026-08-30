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

        /// <summary>[?] 버튼 — 버튼 위치 옆에 해당 도움말을 띄운다.</summary>
        private void Help_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.Button button || button.Tag is not string key)
                return;

            System.Windows.Point? anchor = null;
            try
            {
                // 버튼 오른쪽 살짝 위 지점 (장치 px → DIP 변환)
                var devicePoint = button.PointToScreen(new System.Windows.Point(button.ActualWidth + 8, -4));
                var source = PresentationSource.FromVisual(button);
                anchor = source?.CompositionTarget != null
                    ? source.CompositionTarget.TransformFromDevice.Transform(devicePoint)
                    : devicePoint;
            }
            catch { }

            HelpWindow.ShowTopic(key, Window.GetWindow(this), anchor);
        }

        /// <summary>잠금 해제 모드를 시작하고, 배치가 잘 보이도록 설정 창을 닫는다.</summary>
        private void UnlockMode_Click(object sender, RoutedEventArgs e)
        {
            // 설정에서 들어간 잠금 해제는 종료 시 설정 창으로 복귀한다
            Services.UiLockService.ReturnToSettingsOnLock = !OnlyChatMode;
            Services.UiLockService.Set(true);
            if (!OnlyChatMode)
            {
                try { Window.GetWindow(this)?.Close(); } catch { }
            }
        }

        /// <summary>설정 마법사를 다시 실행한다. 마법사가 화면을 정리하므로 설정 창은 먼저 닫는다.</summary>
        private void NavWizard_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = System.Windows.Application.Current.Windows
                .OfType<MainWindow>()
                .FirstOrDefault();

            try { Window.GetWindow(this)?.Close(); } catch { }
            mainWindow?.ShowSetupWizardOnDemand();
        }

        /// <summary>패치 노트(GitHub 릴리스 페이지)를 기본 브라우저로 연다.</summary>
        private void NavPatchNotes_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/TWHome-Git/TWChatOverlay/releases",
                    UseShellExecute = true,
                });
            }
            catch (System.Exception ex)
            {
                Services.AppLogger.Warn("Failed to open patch notes page.", ex);
            }
        }

        /// <summary>
        /// 선택된 추가 기능 내비+서브 탭 인덱스(위치 미리보기용). 선택이 없으면 -1.
        /// 값은 내비*10 + 서브탭 — 서브 탭에 해당하는 창만 미리보기로 띄우는 데 쓴다.
        /// </summary>
        private int SelectedAddonTabIndex
        {
            get
            {
                if (NavAddonKeyword.IsChecked == true) return 0;
                if (NavAddonExp.IsChecked == true)
                    return 10 + (ExpTabLowEff.IsChecked == true ? 1 : 0);
                if (NavAddonDungeon.IsChecked == true)
                {
                    int sub = DungeonTabAbyss.IsChecked == true ? 1
                        : DungeonTabEclipse.IsChecked == true ? 2
                        : DungeonTabAbandon.IsChecked == true ? 3
                        : DungeonTabCraving.IsChecked == true ? 4
                        : 0;
                    return 20 + sub;
                }
                if (NavAddonItem.IsChecked == true)
                    return 30 + (ItemTabFilter.IsChecked == true ? 1 : 0);
                if (NavAddonBuff.IsChecked == true) return 40;
                if (NavAddonBoss.IsChecked == true) return 50;
                return -1;
            }
        }

        /// <summary>추가 기능 서브 탭 전환 시 해당 탭의 창만 미리보기로 갱신한다.</summary>
        private void AddonSubTab_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || !_addonPreviewActive)
                return;

            SetAddonPositionPreview(true, SelectedAddonTabIndex);
        }

        private bool _addonPreviewActive;
        private bool _wizardSingleMode;
        private ViewModels.AddonViewModel? _addonViewModel;

        /// <summary>
        /// 설정 마법사 임베드용: 내비 없이 지정한 설정 패널 하나만 표시한다.
        /// 실제 설정 화면과 같은 패널·바인딩·미리보기 로직을 그대로 재사용한다.
        /// </summary>
        public void ShowWizardPanel(string navKey)
        {
            _wizardSingleMode = true;

            RadioButton target = navKey switch
            {
                "Chat" => NavChat,
                "Shout" => NavShout,
                "Display" => NavDisplay,
                "Keyword" => NavAddonKeyword,
                "Exp" => NavAddonExp,
                "Dungeon" => NavAddonDungeon,
                "Item" => NavAddonItem,
                "Buff" => NavAddonBuff,
                "Boss" => NavAddonBoss,
                _ => NavChat,
            };

            if (target.IsChecked == true)
                ApplyPanelVisibility(); // 같은 패널 재진입 시에도 표시 갱신
            else
                target.IsChecked = true; // Nav_Checked → ApplyPanelVisibility → 미리보기 연동
        }

        /// <summary>마법사 종료 시 미리보기 상태를 정리한다.</summary>
        public void EndWizardPanelMode()
        {
            if (_addonPreviewActive)
            {
                _addonPreviewActive = false;
                SetAddonPositionPreview(false, -1);
            }
            _wizardSingleMode = false;
        }

        /// <summary>추가 기능 패널들의 DataContext(AddonViewModel)를 준비한다.</summary>
        /// <summary>설정 마법사가 완료 시 경험치 상태 적용 등에 재사용한다.</summary>
        internal ViewModels.AddonViewModel? AddonViewModelInstance => _addonViewModel;

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
                NavToolsColumn.Visibility = Visibility.Collapsed;
                AppHeader.Visibility = Visibility.Collapsed;
                ChatPanel.Visibility = Visibility.Visible;
                ShoutPanel.Visibility = Visibility.Visible;
                DisplayPanel.Visibility = Visibility.Collapsed;
                HotkeyPanel.Visibility = Visibility.Collapsed;
                SystemPanel.Visibility = Visibility.Collapsed;
                PresetPanel.Visibility = Visibility.Collapsed;
                AddonKeywordPanel.Visibility = Visibility.Collapsed;
                AddonExpPanel.Visibility = Visibility.Collapsed;
                AddonDungeonPanel.Visibility = Visibility.Collapsed;
                AddonItemPanel.Visibility = Visibility.Collapsed;
                AddonBuffPanel.Visibility = Visibility.Collapsed;
                AddonBossPanel.Visibility = Visibility.Collapsed;
                return;
            }

            // 설정 마법사 임베드 모드: 내비/도구/헤더 없이 선택된 패널만 보여준다
            NavColumn.Visibility = _wizardSingleMode ? Visibility.Collapsed : Visibility.Visible;
            NavToolsColumn.Visibility = _wizardSingleMode ? Visibility.Collapsed : Visibility.Visible;
            AppHeader.Visibility = _wizardSingleMode ? Visibility.Collapsed : Visibility.Visible;

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
            PresetPanel.Visibility = NavPresets.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            if (NavPresets.IsChecked == true)
                RefreshProfileList();
            AddonKeywordPanel.Visibility = NavAddonKeyword.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AddonExpPanel.Visibility = NavAddonExp.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AddonDungeonPanel.Visibility = NavAddonDungeon.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AddonItemPanel.Visibility = NavAddonItem.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AddonBuffPanel.Visibility = NavAddonBuff.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AddonBossPanel.Visibility = NavAddonBoss.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            DebugOptionsBorder.Visibility = _debugOptionsAllowed ? Visibility.Visible : Visibility.Collapsed;
            BossAlertTestBorder.Visibility = _debugOptionsAllowed ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>디버그 전용: 선택한 보스의 실제 알림 흐름(사운드+팝업)을 즉시 발사한다.</summary>
        private void BossAlertTest_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string label)
                return;
            if (BossAlertTestCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string bossId)
                return;

            string bossName = item.Content as string ?? bossId;
            var settings = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault()?.DataContext as Models.ChatSettings;
            Services.BossAlarmSchedulerService.FireTestAlert(bossId, bossName, label, settings);
        }

        // ===== 프로필: 현재 설정 전체 저장/불러오기 =====

        /// <summary>프로필 목록 UI를 다시 그린다. (기본 2개 + 추가 프로필)</summary>
        private void RefreshProfileList()
        {
            if (ProfileListPanel == null)
                return;

            ProfileListPanel.Children.Clear();
            foreach (string name in Services.SettingsProfileService.GetProfileNames())
                ProfileListPanel.Children.Add(BuildProfileRow(name));
        }

        private UIElement BuildProfileRow(string name)
        {
            bool exists = Services.SettingsProfileService.Exists(name);
            bool isDefault = Services.SettingsProfileService.IsDefaultProfile(name);

            Button MakeButton(string text, bool enabled, RoutedEventHandler onClick, bool danger = false)
            {
                var button = new Button
                {
                    Content = text,
                    MinWidth = 64,
                    Height = 26,
                    Padding = new Thickness(6, 0, 6, 0),
                    FontSize = 12,
                    Margin = new Thickness(6, 0, 0, 0),
                    IsEnabled = enabled,
                    VerticalContentAlignment = VerticalAlignment.Center,
                };
                button.SetResourceReference(StyleProperty, danger ? "DangerButtonStyle" : "SecondaryButtonStyle");
                button.Click += onClick;
                return button;
            }

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(MakeButton("저장", enabled: true, (_, _) => SaveProfile(name)));
            right.Children.Add(MakeButton("이름 변경", enabled: exists, (_, _) => RenameProfile(name)));
            right.Children.Add(MakeButton("불러오기", enabled: exists, (_, _) => LoadProfile(name)));
            if (!isDefault)
                right.Children.Add(MakeButton("삭제", enabled: true, (_, _) => DeleteProfile(name), danger: true));

            var label = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var nameText = new TextBlock { Text = name };
            nameText.SetResourceReference(StyleProperty, "SettingsRowLabelStyle");
            label.Children.Add(nameText);
            if (!exists)
            {
                var emptyHint = new TextBlock { Text = "  (비어 있음)", FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
                emptyHint.SetResourceReference(TextBlock.ForegroundProperty, "OverlaySubtleTextBrush");
                label.Children.Add(emptyHint);
            }

            var row = new DockPanel();
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(label);

            var border = new Border { Child = row };
            border.SetResourceReference(StyleProperty, "SettingsRowStyle");
            return border;
        }

        private void SaveProfile(string name)
        {
            if (DataContext is not ViewModels.SettingsViewModel viewModel)
                return;

            if (Services.SettingsProfileService.Exists(name) &&
                !ConfirmDialogWindow.Confirm(Window.GetWindow(this), $"'{name}'에 저장된 설정을 현재 설정으로 덮어쓸까요?", "저장"))
                return;

            viewModel.SaveProfile(name);
            RefreshProfileList();
        }

        private void LoadProfile(string name)
        {
            if (DataContext is not ViewModels.SettingsViewModel viewModel)
                return;

            if (!ConfirmDialogWindow.Confirm(Window.GetWindow(this),
                    $"'{name}' 프로필을 불러올까요?\n현재 설정이 모두 이 프로필의 내용으로 바뀝니다.", "불러오기"))
                return;

            viewModel.LoadProfile(name);
        }

        private void AddProfile_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SettingsViewModel viewModel)
                return;

            // 새 프로필 = 현재 설정을 새 이름으로 즉시 저장
            string name = Services.SettingsProfileService.SuggestNewName();
            viewModel.SaveProfile(name);
            RefreshProfileList();
        }

        private void DeleteProfile(string name)
        {
            if (!ConfirmDialogWindow.Confirm(Window.GetWindow(this), $"'{name}' 프로필을 삭제할까요?", "삭제"))
                return;

            Services.SettingsProfileService.Delete(name);
            RefreshProfileList();
        }

        private void RenameProfile(string name)
        {
            var dialog = new ProfileNameDialog(name) { Owner = Window.GetWindow(this) };
            if (dialog.ShowDialog() != true)
                return;

            if (!Services.SettingsProfileService.Rename(name, dialog.ResultName, out string? error))
            {
                ConfirmDialogWindow.Confirm(Window.GetWindow(this), error ?? "이름을 바꾸지 못했습니다.", "확인", "닫기");
                return;
            }

            RefreshProfileList();
        }

        /// <summary>프로필 이름 입력 다이얼로그 — 다른 팝업들과 같은 다크·민트 스타일.</summary>
        private sealed class ProfileNameDialog : Window
        {
            public string ResultName { get; private set; } = string.Empty;

            private readonly TextBox _input;

            public ProfileNameDialog(string currentName)
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = System.Windows.Media.Brushes.Transparent;
                ShowInTaskbar = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                SizeToContent = SizeToContent.WidthAndHeight;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
                Services.WindowFontService.Apply(this);

                var mint = System.Windows.Media.Color.FromRgb(0x0C, 0xD2, 0x9D);

                var title = new TextBlock
                {
                    Text = "프로필 이름 변경",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new System.Windows.Media.SolidColorBrush(mint),
                    Margin = new Thickness(0, 0, 0, 8),
                };

                _input = new TextBox
                {
                    Width = 180,
                    Height = 26,
                    FontSize = 13,
                    Text = currentName,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(4, 0, 4, 0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x22, 0x1E)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x33, 0x2E)),
                    CaretBrush = System.Windows.Media.Brushes.White,
                };

                Button MakeDialogButton(string text, bool isPrimary)
                {
                    var button = new Button
                    {
                        Content = text,
                        Width = 56,
                        Height = 28,
                        Padding = new Thickness(0),
                        Margin = new Thickness(isPrimary ? 0 : 6, 0, 0, 0),
                        FontSize = 12,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        Cursor = System.Windows.Input.Cursors.Hand,
                    };
                    button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
                    return button;
                }

                var applyButton = MakeDialogButton("적용", isPrimary: true);
                applyButton.Click += (_, _) => TryAccept();
                var cancelButton = MakeDialogButton("취소", isPrimary: false);
                cancelButton.Click += (_, _) => { DialogResult = false; Close(); };

                var buttonRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 10, 0, 0),
                };
                buttonRow.Children.Add(applyButton);
                buttonRow.Children.Add(cancelButton);

                var root = new StackPanel();
                root.Children.Add(title);
                root.Children.Add(_input);
                root.Children.Add(buttonRow);

                Content = new Border
                {
                    Padding = new Thickness(14, 10, 14, 12),
                    CornerRadius = new CornerRadius(6),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(0xF6, 0x10, 0x16, 0x14)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x33, 0x2E)),
                    BorderThickness = new Thickness(1),
                    Child = root,
                };

                Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
                PreviewKeyDown += (_, args) =>
                {
                    if (args.Key == Key.Enter) { TryAccept(); args.Handled = true; }
                    else if (args.Key == Key.Escape) { DialogResult = false; Close(); args.Handled = true; }
                };
            }

            private void TryAccept()
            {
                string name = (_input.Text ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    _input.BorderBrush = System.Windows.Media.Brushes.IndianRed;
                    return;
                }

                ResultName = name;
                DialogResult = true;
                Close();
            }
        }

        /// <summary>내보낸 프로필(.json) 파일을 골라 현재 설정에 적용한다.</summary>
        private void ImportProfileFile_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SettingsViewModel viewModel)
                return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "프로필 파일 불러오기",
                Filter = "설정 프로필 (*.json)|*.json|모든 파일 (*.*)|*.*",
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                return;

            if (!ConfirmDialogWindow.Confirm(Window.GetWindow(this),
                    $"'{System.IO.Path.GetFileName(dialog.FileName)}' 파일을 불러올까요?\n현재 설정이 모두 이 파일의 내용으로 바뀝니다.", "불러오기"))
                return;

            if (!viewModel.LoadProfileFromFile(dialog.FileName))
                ConfirmDialogWindow.Confirm(Window.GetWindow(this), "프로필 파일을 불러오지 못했습니다.\n올바른 설정 파일인지 확인해 주세요.", "확인", "닫기");
        }

        /// <summary>현 시점의 모든 설정을 프로필 파일로 내보낸다.</summary>
        private void ExportProfileFile_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.SettingsViewModel viewModel)
                return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "프로필 파일로 내보내기",
                FileName = $"TWChat_Profile_{System.DateTime.Now:yyyyMMdd_HHmm}.json",
                Filter = "설정 프로필 (*.json)|*.json",
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true)
                return;

            if (!viewModel.ExportCurrentSettings(dialog.FileName))
                ConfirmDialogWindow.Confirm(Window.GetWindow(this), "프로필 파일을 저장하지 못했습니다.", "확인", "닫기");
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

        private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (FontFamilyCombo.SelectedItem is string fontName &&
                DataContext is ViewModels.SettingsViewModel viewModel &&
                viewModel.FontFamily != fontName)
            {
                viewModel.FontFamily = fontName;
            }
        }

        private void SyncFontOptions()
        {
            if (DataContext is not ViewModels.SettingsViewModel viewModel) return;

            FontFamilyCombo.ItemsSource ??= Services.FontService.GetAvailableFonts();
            FontFamilyCombo.SelectedItem = viewModel.FontFamily;
        }

        private void SettingsView_Loaded(object sender, RoutedEventArgs e)
        {
            SyncFontOptions();
            _addonViewModel?.Attach();
        }

        private void SettingsView_Unloaded(object sender, RoutedEventArgs e)
        {
            HelpWindow.HideIfOpen();
            _itemDropPreviewTimer?.Stop();
            // ChatSettings는 앱 수명 내내 살아 있으므로 구독을 남기면 VM 전체가 누수된다
            _addonViewModel?.Detach();
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
