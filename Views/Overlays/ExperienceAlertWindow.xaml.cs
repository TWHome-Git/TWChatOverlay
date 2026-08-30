using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class ExperienceAlertWindow : Window
    {
        private ChatSettings _settings;
        private bool _isDragging;

        public ExperienceAlertWindow(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            InitializeComponent();
            SettingsHostZOrder.Register(this); // 설정 창이 열려 있으면 그 아래로 표시
            WindowFontService.Apply(this);
            MessageTextBlock.FontSize = _settings.ExperienceAlertFontSize;
            LocationChanged += (_, _) => SyncPositionToSettings(notify: false);
        }

        public void SetSettings(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            MessageTextBlock.FontSize = _settings.ExperienceAlertFontSize;
        }

        public void SetMessage(string message)
        {
            MessageTextBlock.Text = message;
        }

        /// <summary>잠금 해제 인스펙터에서 폰트 크기 변경 시 즉시 반영.</summary>
        public void SetFontSize(double size)
        {
            MessageTextBlock.FontSize = size;
            PreviewLabel.FontSize = size;
        }

        /// <summary>위치 미리보기: 통일 라벨("경험치 누적 알림창")만 표시.</summary>
        public void SetPreviewMode(bool isPreview)
        {
            NormalContent.Visibility = isPreview ? Visibility.Collapsed : Visibility.Visible;
            PreviewLabel.Visibility = isPreview ? Visibility.Visible : Visibility.Collapsed;
        }

        public void BringToFront()
        {
            TopmostWindowHelper.BringToTopmost(this);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyToolWindowStyle();
        }

        protected override void OnClosed(EventArgs e)
        {
            SyncPositionToSettings(notify: true);
            base.OnClosed(e);
        }

        private void ApplyToolWindowStyle()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW);
            }
            catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>[수정]: 설정 화면에 가지 않고 저장된 누적 경험치를 바로 고치는 작은 창.</summary>
        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                long currentEok = 0;
                if (ExperienceAlertWindowService.TryGetStateSnapshot(_settings, out var snapshot))
                    currentEok = snapshot.TotalExp / 100_000_000L;

                var dialog = new ExpEditDialog(currentEok, _settings.EnableExperienceLimitAlert)
                {
                    Owner = this,
                    Left = Left,
                    Top = Top + ActualHeight + 6,
                };
                if (dialog.ShowDialog() != true)
                    return;

                _settings.EnableExperienceLimitAlert = dialog.ResultAlertEnabled;

                long totalExp = checked(dialog.ResultEok * 100_000_000L);

                // 설정의 '현재 누적 경험치(억)' [적용]과 동일한 반영 경로
                _settings.ExperienceLimitTotalExp = totalExp;
                _settings.ExperienceLimitStateInitialized = true;
                ConfigService.Save(_settings);
                ExperienceAlertWindowService.ApplyStateSnapshot(new ExperienceAlertStateSnapshot { TotalExp = totalExp });
                ExperienceWeeklyRefreshService.MarkCurrentWeekRefreshed(_settings, DateTime.Now);
                AppLogger.Info($"Applied manual total exp from alert-window edit. Eok={dialog.ResultEok:N0}, TotalExp={totalExp:N0}");

                SetMessage($"누적 경험치 {dialog.ResultEok:N0}억");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to edit accumulated exp from alert window.", ex);
            }
        }

        /// <summary>누적 알림 ON/OFF + 누적 경험치(억) 입력 다이얼로그 — 좌표/도움말 창과 같은 다크·민트 스타일.</summary>
        private sealed class ExpEditDialog : Window
        {
            public long ResultEok { get; private set; }
            public bool ResultAlertEnabled { get; private set; }

            private readonly System.Windows.Controls.TextBox _input;
            private readonly System.Windows.Controls.CheckBox _alertToggle;

            public ExpEditDialog(long currentEok, bool alertEnabled)
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = System.Windows.Media.Brushes.Transparent;
                ShowInTaskbar = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                SizeToContent = SizeToContent.WidthAndHeight;
                WindowFontService.Apply(this);

                var mint = System.Windows.Media.Color.FromRgb(0x0C, 0xD2, 0x9D);

                var title = new System.Windows.Controls.TextBlock
                {
                    Text = "경험치 누적 알림 설정",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new System.Windows.Media.SolidColorBrush(mint),
                    Margin = new Thickness(0, 0, 0, 8),
                };

                // 누적 알림 ON/OFF (설정 > 경험치 추적 > 경험치 누적 알림과 같은 값)
                _alertToggle = new System.Windows.Controls.CheckBox
                {
                    IsChecked = alertEnabled,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _alertToggle.SetResourceReference(StyleProperty, "ToggleSwitchCheckBoxStyle");

                var toggleRow = new System.Windows.Controls.DockPanel { Margin = new Thickness(0, 0, 0, 8) };
                System.Windows.Controls.DockPanel.SetDock(_alertToggle, System.Windows.Controls.Dock.Right);
                toggleRow.Children.Add(_alertToggle);
                toggleRow.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = "누적 알림",
                    FontSize = 13,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                });

                _input = new System.Windows.Controls.TextBox
                {
                    Width = 72,
                    Height = 26,
                    FontSize = 13,
                    Text = currentEok.ToString(),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Right,
                    Padding = new Thickness(4, 0, 4, 0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1A, 0x22, 0x1E)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2A, 0x33, 0x2E)),
                    CaretBrush = System.Windows.Media.Brushes.White,
                };
                _input.PreviewTextInput += (_, args) => args.Handled = !long.TryParse(args.Text, out long _);

                var unit = new System.Windows.Controls.TextBlock
                {
                    Text = "억",
                    FontSize = 13,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 0, 0),
                };

                var inputLabel = new System.Windows.Controls.TextBlock
                {
                    Text = "누적 경험치",
                    FontSize = 13,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                };

                var inputRow = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
                inputRow.Children.Add(inputLabel);
                inputRow.Children.Add(_input);
                inputRow.Children.Add(unit);

                System.Windows.Controls.Button MakeButton(string text, bool isApply)
                {
                    var button = new System.Windows.Controls.Button
                    {
                        Content = text,
                        Width = 56,
                        Height = 28,
                        Padding = new Thickness(0),
                        Margin = new Thickness(isApply ? 0 : 6, 0, 0, 0),
                        FontSize = 12,
                        VerticalContentAlignment = VerticalAlignment.Center,
                        HorizontalContentAlignment = HorizontalAlignment.Center,
                        Cursor = System.Windows.Input.Cursors.Hand,
                    };
                    button.SetResourceReference(StyleProperty, "SecondaryButtonStyle");
                    return button;
                }

                var applyButton = MakeButton("적용", isApply: true);
                applyButton.Click += (_, _) => TryAccept();
                var cancelButton = MakeButton("취소", isApply: false);
                cancelButton.Click += (_, _) => { DialogResult = false; Close(); };

                var buttonRow = new System.Windows.Controls.StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 10, 0, 0),
                };
                buttonRow.Children.Add(applyButton);
                buttonRow.Children.Add(cancelButton);

                var root = new System.Windows.Controls.StackPanel();
                root.Children.Add(title);
                root.Children.Add(toggleRow);
                root.Children.Add(inputRow);
                root.Children.Add(buttonRow);

                Content = new System.Windows.Controls.Border
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
                string normalized = (_input.Text ?? string.Empty).Replace(",", string.Empty).Replace("억", string.Empty).Trim();
                if (normalized.Length == 0)
                    normalized = "0";
                if (!long.TryParse(normalized, out long eok) || eok < 0)
                {
                    _input.BorderBrush = System.Windows.Media.Brushes.IndianRed;
                    return;
                }

                ResultEok = eok;
                ResultAlertEnabled = _alertToggle.IsChecked == true;
                DialogResult = true;
                Close();
            }
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!UiLockService.IsUnlocked) return;
            UiLockService.Select(this);
            if (e.ButtonState != MouseButtonState.Pressed || !IsVisible)
                return;

            _isDragging = true;
            try { DragMove(); } catch { }
            finally
            {
                _isDragging = false;
                SyncPositionToSettings(notify: true);
            }
        }

        private void SyncPositionToSettings(bool notify)
        {
            if (_settings == null || !IsVisible)
                return;

            _settings.ExperienceLimitAlertWindowLeft = Left;
            _settings.ExperienceLimitAlertWindowTop = Top;

            if (_isDragging || notify)
                ConfigService.SaveDeferred(_settings);
        }
    }
}
