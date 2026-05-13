using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.ViewModels;
using Forms = System.Windows.Forms;

namespace TWChatOverlay.Views
{
    public partial class InitialSetupWizardWindow : Window
    {
        private sealed record WizardStep(string Title, string Description, bool SupportsPositionPreview);

        private readonly List<WizardStep> _steps = new()
        {
            new("1. 채팅 로그 위치 설정", "채팅 로그 폴더를 지정합니다.", false),
            new("2. 채팅창 위치 설정", "MainWindow 위치를 조정하고 프리셋1로 저장합니다.", true),
            new("3. 채팅창 설정", "", false),
            new("4. 외치기 설정", "외치기 팝업/위치/자동복사/유지시간/텍스트 크기를 설정합니다.", true),
            new("5. 키워드 알림 설정", "키워드 알림 기능을 설정합니다.", false),
            new("6. 경험치 추적 설정", "경험치 추적 및 누적 알림을 설정합니다.", true),
            new("7. 던전 도우미 설정", "던전 도우미 알림 항목을 설정합니다.", true),
            new("8. 아이템 획득 알림 설정", "아이템 획득 알림 및 필터를 설정합니다.", true),
            new("9. 버프 추적 설정", "버프 추적 알림 및 종료 사운드를 설정합니다.", true),
            new("10. 필드 보스 알림 설정", "필드 보스 알림을 설정합니다.", false),
            new("11. 일간/주간 컨텐츠 추적 설정", "일간/주간 컨텐츠 체크 항목을 설정합니다.", false)
        };

        private int _stepIndex;
        private readonly ChatSettings _settings;
        private readonly AddonViewModel _addonViewModel;
        private readonly MainWindow? _mainWindow;
        private bool _positionPreviewEnabled;
        private bool _shoutPreviewEnabled;

        private UIElement? _dailyWeeklyStepContent;

        public event EventHandler<bool>? WizardFinished;

        public InitialSetupWizardWindow(ChatSettings settings, MainWindow? mainWindow)
        {
            InitializeComponent();
            WindowFontService.Apply(this);
            _settings = settings;
            _addonViewModel = new AddonViewModel(settings);
            _mainWindow = mainWindow;
            RenderStep();
        }

        private void RenderStep()
        {
            _stepIndex = Math.Max(0, Math.Min(_steps.Count - 1, _stepIndex));
            WizardStep step = _steps[_stepIndex];

            StepTitleText.Text = step.Title;
            StepDescText.Text = string.Empty;
            StepDescText.Visibility = Visibility.Collapsed;
            StepDetailText.Text = string.Empty;
            StepDetailText.Visibility = Visibility.Collapsed;
            ProgressText.Text = $"{_stepIndex + 1} / {_steps.Count}";
            PrevButton.IsEnabled = _stepIndex > 0;
            NextButton.Visibility = _stepIndex == _steps.Count - 1 ? Visibility.Collapsed : Visibility.Visible;
            FinishButton.Visibility = _stepIndex == _steps.Count - 1 ? Visibility.Visible : Visibility.Collapsed;

            bool shouldEnablePositionPreview = _stepIndex == 1;
            if (shouldEnablePositionPreview != _positionPreviewEnabled)
                SetPositionPreview(shouldEnablePositionPreview);

            UpdateStepSpecificPreviews();

            StepContentHost.Content = BuildStepContent(_stepIndex);
        }

        private void UpdateStepSpecificPreviews()
        {
            bool shouldShowShoutPreview = _stepIndex == 3;
            if (shouldShowShoutPreview == _shoutPreviewEnabled)
            {
                _mainWindow?.ShowWizardStepPreviewWindows(_stepIndex);
                return;
            }

            _shoutPreviewEnabled = shouldShowShoutPreview;

            try
            {
                if (_shoutPreviewEnabled)
                {
                    ShoutToastService.ShowPositionPreview(_settings, force: true);
                }
                else
                {
                    ShoutToastService.ClosePositionPreview(_settings);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to toggle shout position preview in setup wizard.", ex);
            }

            _mainWindow?.ShowWizardStepPreviewWindows(_stepIndex);
        }

        private UIElement BuildStepContent(int stepIndex)
        {
            return stepIndex switch
            {
                0 => BuildLogPathStepContent(),
                1 => BuildMainPositionStepContent(),
                2 => BuildChatSettingsStepContent(),
                3 => BuildShoutSettingsStepContent(),
                4 => BuildKeywordAddonContent(),
                5 => BuildExperienceAddonContent(),
                6 => BuildDungeonAddonContent(),
                7 => BuildItemDropAddonContent(),
                8 => BuildBuffAddonContent(),
                9 => BuildFieldBossAddonContent(),
                10 => BuildDailyWeeklyStepContent(),
                _ => new TextBlock { Text = "준비 중", Foreground = System.Windows.Media.Brushes.White }
            };
        }

        private UIElement BuildLogPathStepContent()
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "채팅 로그 폴더", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 6) });

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pathBox = new TextBox
            {
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#333")),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#555")),
                Padding = new Thickness(8, 0, 8, 0)
            };
            pathBox.SetBinding(TextBox.TextProperty, new Binding(nameof(ChatSettings.ChatLogFolderPath)) { Source = _settings, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            row.Children.Add(pathBox);

            var browseBtn = new Button { Content = "찾아보기", Width = 96, Height = 30, Margin = new Thickness(8, 0, 0, 0) };
            browseBtn.Click += (_, _) =>
            {
                using var dlg = new Forms.FolderBrowserDialog
                {
                    Description = "채팅 로그 폴더를 선택하세요",
                    SelectedPath = string.IsNullOrWhiteSpace(_settings.ChatLogFolderPath) ? @"C:\\Nexon\\TalesWeaver\\ChatLog" : _settings.ChatLogFolderPath
                };
                if (dlg.ShowDialog() == Forms.DialogResult.OK)
                {
                    _settings.ChatLogFolderPath = dlg.SelectedPath;
                }
            };
            Grid.SetColumn(browseBtn, 1);
            row.Children.Add(browseBtn);

            panel.Children.Add(row);
            panel.Children.Add(new TextBlock { Text = "경로 저장은 완료 시 자동 반영됩니다.", Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B949E")), FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
            return panel;
        }

        private UIElement BuildMainPositionStepContent()
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "위치 미리보기를 켜고 메인 채팅창을 원하는 위치로 드래그하세요.", Foreground = System.Windows.Media.Brushes.White });
            panel.Children.Add(new TextBlock { Text = "다음 단계로 이동하면 현재 위치가 프리셋1로 저장됩니다.", Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B949E")), Margin = new Thickness(0, 8, 0, 0), FontSize = 12 });
            return panel;
        }

        private UIElement BuildChatSettingsStepContent()
        {
            var root = new StackPanel();

            root.Children.Add(CreateFilterColorRow("일반", nameof(ChatSettings.ShowNormal), nameof(ChatSettings.NormalColor)));
            root.Children.Add(CreateFilterColorRow("팀", nameof(ChatSettings.ShowTeam), nameof(ChatSettings.TeamColor)));
            root.Children.Add(CreateFilterColorRow("클럽", nameof(ChatSettings.ShowClub), nameof(ChatSettings.ClubColor)));
            root.Children.Add(CreateFilterColorRow("외치기", nameof(ChatSettings.ShowShout), nameof(ChatSettings.ShoutColor)));
            root.Children.Add(CreateFilterColorRow("시스템", nameof(ChatSettings.ShowSystem), nameof(ChatSettings.SystemColor)));

            root.Children.Add(new Border { Height = 1, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E3640")), Margin = new Thickness(0, 10, 0, 10) });

            root.Children.Add(CreateCheckRow("에타 레벨 표시", nameof(ChatSettings.ShowEtaLevel)));
            root.Children.Add(CreateCheckRow("캐릭터 표시", nameof(ChatSettings.ShowEtaCharacter)));
            root.Children.Add(CreateCheckRow("클럽 보스 메시지 표시", nameof(ChatSettings.ShowClubBoss)));
            root.Children.Add(CreateCheckRow("타임스탬프 표시", nameof(ChatSettings.ShowTimestamp)));

            root.Children.Add(new Border { Height = 1, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E3640")), Margin = new Thickness(0, 10, 0, 10) });

            root.Children.Add(new TextBlock
            {
                Text = "텍스트 폰트 및 크기",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var fontGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fontGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rbNanum = new RadioButton { Content = "나눔고딕", Foreground = Brushes.White, GroupName = "WizardFontFamily" };
            rbNanum.IsChecked = _settings.FontFamily == "나눔고딕";
            rbNanum.Checked += (_, _) => _settings.FontFamily = "나눔고딕";
            Grid.SetColumn(rbNanum, 0);
            fontGrid.Children.Add(rbNanum);

            var rbGulim = new RadioButton { Content = "굴림", Foreground = Brushes.White, GroupName = "WizardFontFamily" };
            rbGulim.IsChecked = _settings.FontFamily == "굴림";
            rbGulim.Checked += (_, _) => _settings.FontFamily = "굴림";
            Grid.SetColumn(rbGulim, 1);
            fontGrid.Children.Add(rbGulim);
            root.Children.Add(fontGrid);

            var rbCustom = new RadioButton { Content = "사용자 설정", Foreground = Brushes.White, GroupName = "WizardFontFamily", Margin = new Thickness(0, 0, 0, 8) };
            rbCustom.IsChecked = _settings.FontFamily == "사용자 설정";
            rbCustom.Checked += (_, _) => _settings.FontFamily = "사용자 설정";
            root.Children.Add(rbCustom);

            var sizePanel = new StackPanel { Orientation = Orientation.Horizontal };
            var fontSizeLabel = new TextBlock { Foreground = Brushes.White, Width = 110, VerticalAlignment = VerticalAlignment.Center };
            fontSizeLabel.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChatSettings.FontSize))
            {
                Source = _settings,
                StringFormat = "폰트 크기: {0:F0}pt"
            });
            sizePanel.Children.Add(fontSizeLabel);
            var fontSizeSlider = new Slider
            {
                Minimum = 10,
                Maximum = 40,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Width = 220
            };
            fontSizeSlider.SetBinding(Slider.ValueProperty, new Binding(nameof(ChatSettings.FontSize)) { Source = _settings, Mode = BindingMode.TwoWay });
            sizePanel.Children.Add(fontSizeSlider);
            root.Children.Add(sizePanel);

            return new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = root
            };
        }

        private UIElement CreateCheckRow(string label, string bindingPath)
        {
            var cb = new CheckBox { Content = label, Foreground = Brushes.White, Margin = new Thickness(0, 2, 0, 2) };
            cb.SetBinding(CheckBox.IsCheckedProperty, new Binding(bindingPath) { Source = _settings, Mode = BindingMode.TwoWay });
            return cb;
        }

        private UIElement CreateFilterColorRow(string label, string visibleBindingPath, string colorBindingPath)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cb = new CheckBox { Content = label, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            cb.SetBinding(CheckBox.IsCheckedProperty, new Binding(visibleBindingPath) { Source = _settings, Mode = BindingMode.TwoWay });
            grid.Children.Add(cb);

            var colorBtn = new Button { Width = 38, Height = 20, Margin = new Thickness(8, 0, 0, 0), BorderBrush = Brushes.Transparent };
            colorBtn.SetBinding(Button.BackgroundProperty, new Binding(colorBindingPath) { Source = _settings });
            colorBtn.Click += (_, _) =>
            {
                try
                {
                    using var dialog = new Forms.ColorDialog();
                    string current = colorBindingPath switch
                    {
                        nameof(ChatSettings.NormalColor) => _settings.NormalColor,
                        nameof(ChatSettings.TeamColor) => _settings.TeamColor,
                        nameof(ChatSettings.ClubColor) => _settings.ClubColor,
                        nameof(ChatSettings.ShoutColor) => _settings.ShoutColor,
                        nameof(ChatSettings.SystemColor) => _settings.SystemColor,
                        _ => "#FFFFFF"
                    };
                    dialog.Color = System.Drawing.ColorTranslator.FromHtml(current);
                    if (dialog.ShowDialog() != Forms.DialogResult.OK)
                        return;

                    string hex = $"#{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
                    switch (colorBindingPath)
                    {
                        case nameof(ChatSettings.NormalColor): _settings.NormalColor = hex; break;
                        case nameof(ChatSettings.TeamColor): _settings.TeamColor = hex; break;
                        case nameof(ChatSettings.ClubColor): _settings.ClubColor = hex; break;
                        case nameof(ChatSettings.ShoutColor): _settings.ShoutColor = hex; break;
                        case nameof(ChatSettings.SystemColor): _settings.SystemColor = hex; break;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to change wizard chat color.", ex);
                }
            };
            Grid.SetColumn(colorBtn, 1);
            grid.Children.Add(colorBtn);

            return grid;
        }

        private UIElement BuildShoutSettingsStepContent()
        {
            var panel = new StackPanel();

            var cbPopup = new CheckBox { Content = "외치기 팝업 ON/OFF", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 6) };
            cbPopup.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(ChatSettings.ShowShoutToastPopup)) { Source = _settings, Mode = BindingMode.TwoWay });
            panel.Children.Add(cbPopup);

            var cbAuto = new CheckBox { Content = "외치기 닉네임 자동복사 ON/OFF", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 10) };
            cbAuto.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(ChatSettings.AutoCopyShoutNickname)) { Source = _settings, Mode = BindingMode.TwoWay });
            panel.Children.Add(cbAuto);

            var durationLabel = new TextBlock { Foreground = System.Windows.Media.Brushes.White, FontSize = 12 };
            durationLabel.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChatSettings.ShoutToastDurationSeconds))
            {
                Source = _settings,
                StringFormat = "외치기 팝업 유지시간: {0}초"
            });
            panel.Children.Add(durationLabel);
            var duration = new Slider { Minimum = 1, Maximum = 60, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 10) };
            duration.SetBinding(Slider.ValueProperty, new Binding(nameof(ChatSettings.ShoutToastDurationSeconds)) { Source = _settings, Mode = BindingMode.TwoWay });
            duration.ValueChanged += (_, _) =>
            {
                if (_shoutPreviewEnabled)
                {
                    try { ShoutToastService.ShowPositionPreview(_settings, force: true); } catch { }
                }
            };
            panel.Children.Add(duration);

            var fontSizeLabel = new TextBlock { Foreground = System.Windows.Media.Brushes.White, FontSize = 12 };
            fontSizeLabel.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChatSettings.ShoutToastFontSize))
            {
                Source = _settings,
                StringFormat = "외치기 팝업 텍스트 크기: {0:F0}"
            });
            panel.Children.Add(fontSizeLabel);
            var fontSize = new Slider { Minimum = 10, Maximum = 40, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 4, 0, 0) };
            fontSize.SetBinding(Slider.ValueProperty, new Binding(nameof(ChatSettings.ShoutToastFontSize)) { Source = _settings, Mode = BindingMode.TwoWay });
            fontSize.ValueChanged += (_, _) =>
            {
                if (_shoutPreviewEnabled)
                {
                    try { ShoutToastService.ShowPositionPreview(_settings, force: true); } catch { }
                }
            };
            panel.Children.Add(fontSize);

            panel.Children.Add(new TextBlock { Text = "4단계에 진입하면 외치기 위치 미리보기 창이 자동으로 표시됩니다.", Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8B949E")), FontSize = 12, Margin = new Thickness(0, 10, 0, 0) });
            return panel;
        }

        private UIElement BuildDailyWeeklyStepContent()
        {
            _dailyWeeklyStepContent ??= CreateDailyWeeklyChecklistStep();
            return _dailyWeeklyStepContent;
        }

        private UIElement BuildKeywordAddonContent()
            => BuildKeywordAddonContentCore();

        private UIElement BuildKeywordAddonContentCore()
        {
            const string keywordPlaceholder = "ex)@드드해 @뜨뜨해";
            var panel = new StackPanel { Margin = new Thickness(8) };
            panel.Children.Add(CreateCheckRow("키워드 알림 ON/OFF", nameof(ChatSettings.UseKeywordAlert)));
            panel.Children.Add(CreateCheckRow("색상 강조 ON/OFF", nameof(ChatSettings.UseAlertColor)));
            panel.Children.Add(CreateCheckRow("알림음 재생 ON/OFF", nameof(ChatSettings.UseAlertSound)));
            var volumeLabel = new TextBlock { Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 4) };
            volumeLabel.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChatSettings.HighlightAlertVolumePercent))
            {
                Source = _settings,
                StringFormat = "키워드 알림 볼륨: {0:F0}%"
            });
            panel.Children.Add(volumeLabel);
            var volume = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = true };
            volume.SetBinding(Slider.ValueProperty, new Binding(nameof(ChatSettings.HighlightAlertVolumePercent)) { Source = _settings, Mode = BindingMode.TwoWay });
            panel.Children.Add(volume);
            panel.Children.Add(new TextBlock { Text = "알림 키워드(@키워드 형식)", Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 4) });
            var keywordHost = new Grid { Height = 72 };
            var keywordBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
            keywordBox.SetBinding(TextBox.TextProperty, new Binding(nameof(ChatSettings.KeywordInput)) { Source = _settings, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });

            var keywordHint = new TextBlock
            {
                Text = keywordPlaceholder,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280")),
                Opacity = 0.9,
                FontStyle = FontStyles.Italic,
                Margin = new Thickness(10, 0, 8, 0),
                IsHitTestVisible = false,
                VerticalAlignment = VerticalAlignment.Center
            };

            void UpdateKeywordHintVisibility()
            {
                keywordHint.Visibility = string.IsNullOrWhiteSpace(keywordBox.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }

            // If old settings accidentally stored the placeholder text as a real value, clear it.
            if (string.Equals((_settings.KeywordInput ?? string.Empty).Trim(), keywordPlaceholder, StringComparison.Ordinal))
            {
                _settings.KeywordInput = string.Empty;
                keywordBox.Text = string.Empty;
            }

            keywordBox.TextChanged += (_, _) => UpdateKeywordHintVisibility();
            keywordBox.GotFocus += (_, _) => UpdateKeywordHintVisibility();
            keywordBox.LostFocus += (_, _) => UpdateKeywordHintVisibility();
            UpdateKeywordHintVisibility();

            keywordHost.Children.Add(keywordBox);
            keywordHost.Children.Add(keywordHint);
            panel.Children.Add(keywordHost);
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private UIElement BuildExperienceAddonContent()
            => BuildExperienceAddonContentCore();

        private UIElement BuildExperienceAddonContentCore()
        {
            var panel = new StackPanel { Margin = new Thickness(8) };
            panel.Children.Add(CreateCheckRow("경험치 추적 ON/OFF", nameof(ChatSettings.ShowExpTracker)));
            panel.Children.Add(CreateCheckRow("경험치 누적 알림 ON/OFF", nameof(ChatSettings.EnableExperienceLimitAlert)));
            panel.Children.Add(new TextBlock { Text = "현재 누적 경험치(억)", Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 4) });
            var expBox = new TextBox();
            expBox.SetBinding(TextBox.TextProperty, new Binding(nameof(ChatSettings.ExperienceLimitTotalExp)) { Source = _settings, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            panel.Children.Add(expBox);
            var expHint = new TextBlock
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B949E")),
                FontSize = 12,
                Margin = new Thickness(0, 6, 0, 8)
            };
            expHint.Inlines.Add(new Run("캐릭터의 현재 경험치를 "));
            expHint.Inlines.Add(new Run("억")
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58A6FF")),
                FontWeight = FontWeights.SemiBold
            });
            expHint.Inlines.Add(new Run(" 단위로 넣어주세요."));
            panel.Children.Add(expHint);
            panel.Children.Add(CreateCheckRow("저효율 알림 ON/OFF", nameof(ChatSettings.IsExpAlarmEnabled)));
            panel.Children.Add(new TextBlock { Text = "저효율 알림 기준(만)", Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 4) });
            var threshold = new TextBox();
            threshold.SetBinding(TextBox.TextProperty, new Binding(nameof(ChatSettings.ExpAlarmThresholdMan)) { Source = _settings, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            panel.Children.Add(threshold);
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private UIElement BuildDungeonAddonContent()
            => BuildDungeonAddonContentCore();

        private UIElement BuildDungeonAddonContentCore()
        {
            var panel = new StackPanel { Margin = new Thickness(8) };
            panel.Children.Add(CreateCheckRow("웨이브 종료 알림 ON/OFF", nameof(ChatSettings.UseMagicCircleAlert)));
            var waveVolLabel = new TextBlock { Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 4) };
            waveVolLabel.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChatSettings.MagicCircleAlertVolumePercent))
            {
                Source = _settings,
                StringFormat = "웨이브 종료 알림 볼륨: {0:F0}%"
            });
            panel.Children.Add(waveVolLabel);
            var vol = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = true };
            vol.SetBinding(Slider.ValueProperty, new Binding(nameof(ChatSettings.MagicCircleAlertVolumePercent)) { Source = _settings, Mode = BindingMode.TwoWay });
            panel.Children.Add(vol);
            panel.Children.Add(CreateCheckRow("에토스 방향 알림 ON/OFF", nameof(ChatSettings.ShowEtosDirectionAlert)));
            panel.Children.Add(CreateCheckRow("어밴던로드 횟수 알리미", nameof(ChatSettings.EnableAbandonRoadCountAlert)));
            panel.Children.Add(CreateCheckRow("어밴던로드 누적 금액 알리미", nameof(ChatSettings.ShowAbandonRoadSummaryWindow)));
            panel.Children.Add(CreateCheckRow("갈망하는 즐거움 횟수 알리미", nameof(ChatSettings.EnableCravingPleasureCountAlert)));
            panel.Children.Add(new TextBlock { Text = "던전 카운터 지속시간(초)", Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 4) });
            var dur = new TextBox { Height = 30, VerticalContentAlignment = VerticalAlignment.Center };
            dur.SetBinding(TextBox.TextProperty, new Binding(nameof(ChatSettings.AbandonRoadCountAlertDurationSeconds)) { Source = _settings, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            panel.Children.Add(dur);
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private UIElement BuildItemDropAddonContent()
            => BuildItemDropAddonContentCore();

        private UIElement BuildItemDropAddonContentCore()
        {
            var panel = new StackPanel { Margin = new Thickness(8) };
            panel.Children.Add(CreateCheckRow("아이템 획득 알림 ON/OFF", nameof(ChatSettings.ShowItemDropAlert)));
            var itemVolLabel = new TextBlock { Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 4) };
            itemVolLabel.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChatSettings.ItemDropAlertVolumePercent))
            {
                Source = _settings,
                StringFormat = "아이템 획득 알림 볼륨: {0:F0}%"
            });
            panel.Children.Add(itemVolLabel);
            var vol = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = true };
            vol.SetBinding(Slider.ValueProperty, new Binding(nameof(ChatSettings.ItemDropAlertVolumePercent)) { Source = _settings, Mode = BindingMode.TwoWay });
            panel.Children.Add(vol);

            panel.Children.Add(new TextBlock
            {
                Text = "사용자 정의 필터",
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = "기본 목록에서 알림 받을 항목을 사용자 정의 목록으로 옮긴 뒤 적용하세요.",
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B949E")),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var modeButtons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var defaultBtn = new Button { Content = "기본", Width = 64, Margin = new Thickness(0, 0, 6, 0) };
            defaultBtn.Click += (_, _) =>
            {
                _settings.UseCustomDropItemFilter = false;
                _addonViewModel.SelectDefaultDropFilterCommand.Execute(null);
            };
            modeButtons.Children.Add(defaultBtn);

            var customBtn = new Button { Content = "사용자 정의", Width = 92 };
            customBtn.Click += (_, _) =>
            {
                _settings.UseCustomDropItemFilter = true;
                _addonViewModel.SelectCustomDropFilterCommand.Execute(null);
            };
            modeButtons.Children.Add(customBtn);
            panel.Children.Add(modeButtons);

            var customGrid = new Grid();
            customGrid.ColumnDefinitions.Add(new ColumnDefinition());
            customGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            customGrid.ColumnDefinitions.Add(new ColumnDefinition());
            customGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            customGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(170) });
            customGrid.SetBinding(VisibilityProperty, new Binding(nameof(ChatSettings.UseCustomDropItemFilter))
            {
                Source = _settings,
                Converter = new BooleanToVisibilityConverter()
            });

            customGrid.Children.Add(new TextBlock { Text = "기본 목록", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C9D1D9")), FontSize = 11, Margin = new Thickness(0, 0, 0, 4) });
            var customLabel = new TextBlock { Text = "사용자 정의 목록", Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C9D1D9")), FontSize = 11, Margin = new Thickness(0, 0, 0, 4) };
            Grid.SetColumn(customLabel, 2);
            customGrid.Children.Add(customLabel);

            var defaultList = new ListBox
            {
                SelectionMode = SelectionMode.Extended,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111820")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30363D")),
                Foreground = Brushes.White
            };
            defaultList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(AddonViewModel.DefaultDropItems)) { Source = _addonViewModel });
            defaultList.ItemTemplate = CreateDropFilterItemTemplate();
            Grid.SetRow(defaultList, 1);
            customGrid.Children.Add(defaultList);

            var customList = new ListBox
            {
                SelectionMode = SelectionMode.Extended,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#111820")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30363D")),
                Foreground = Brushes.White
            };
            customList.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(AddonViewModel.CustomDropItems)) { Source = _addonViewModel });
            customList.ItemTemplate = CreateDropFilterItemTemplate();
            Grid.SetColumn(customList, 2);
            Grid.SetRow(customList, 1);
            customGrid.Children.Add(customList);

            var movePanel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 5, 0) };
            var toCustom = new Button { Content = ">>", Margin = new Thickness(0, 0, 0, 8) };
            toCustom.Click += (_, _) => _addonViewModel.MoveToCustom(defaultList.SelectedItems.Cast<DropItemFilterEntry>().ToList());
            movePanel.Children.Add(toCustom);
            var toDefault = new Button { Content = "<<" };
            toDefault.Click += (_, _) => _addonViewModel.MoveToDefault(customList.SelectedItems.Cast<DropItemFilterEntry>().ToList());
            movePanel.Children.Add(toDefault);
            Grid.SetColumn(movePanel, 1);
            Grid.SetRow(movePanel, 1);
            customGrid.Children.Add(movePanel);
            panel.Children.Add(customGrid);

            var actionRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 4) };
            var applyBtn = new Button { Content = "적용", Width = 64, Margin = new Thickness(0, 0, 6, 0) };
            applyBtn.Click += (_, _) => _addonViewModel.ApplyCustomDropItemFilterCommand.Execute(null);
            actionRow.Children.Add(applyBtn);
            var loadBtn = new Button { Content = "불러오기", Width = 70, Margin = new Thickness(0, 0, 6, 0) };
            loadBtn.Click += (_, _) => _addonViewModel.LoadCustomDropItemFilterCommand.Execute(null);
            actionRow.Children.Add(loadBtn);
            var saveBtn = new Button { Content = "저장", Width = 64 };
            saveBtn.Click += (_, _) => _addonViewModel.SaveCustomDropItemFilterCommand.Execute(null);
            actionRow.Children.Add(saveBtn);
            panel.Children.Add(actionRow);

            var statusText = new TextBlock
            {
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B949E")),
                FontSize = 12
            };
            statusText.SetBinding(TextBlock.TextProperty, new Binding(nameof(AddonViewModel.CustomDropItemStatus)) { Source = _addonViewModel });
            panel.Children.Add(statusText);
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private static DataTemplate CreateDropFilterItemTemplate()
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetBinding(TextBlock.TextProperty, new Binding(nameof(DropItemFilterEntry.Name)));
            text.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(DropItemFilterEntry.Foreground)));
            text.SetValue(TextBlock.FontSizeProperty, 11.0);
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            return new DataTemplate { VisualTree = text };
        }

        private UIElement BuildBuffAddonContent()
            => BuildBuffAddonContentCore();

        private UIElement BuildBuffAddonContentCore()
        {
            var panel = new StackPanel { Margin = new Thickness(8) };
            panel.Children.Add(CreateCheckRow("버프 추적 알림 ON/OFF", nameof(ChatSettings.EnableBuffTrackerAlert)));
            panel.Children.Add(CreateCheckRow("버프 종료 사운드 알림 ON/OFF", nameof(ChatSettings.EnableBuffTrackerEndSound)));
            var buffVolLabel = new TextBlock { Foreground = Brushes.White, Margin = new Thickness(0, 8, 0, 4) };
            buffVolLabel.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChatSettings.BuffTrackerEndSoundVolumePercent))
            {
                Source = _settings,
                StringFormat = "버프 종료 사운드 볼륨: {0:F0}%"
            });
            panel.Children.Add(buffVolLabel);
            var vol = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = true };
            vol.SetBinding(Slider.ValueProperty, new Binding(nameof(ChatSettings.BuffTrackerEndSoundVolumePercent)) { Source = _settings, Mode = BindingMode.TwoWay });
            panel.Children.Add(vol);
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private UIElement BuildFieldBossAddonContent()
            => BuildFieldBossAddonContentCore();

        private UIElement BuildFieldBossAddonContentCore()
        {
            var panel = new StackPanel { Margin = new Thickness(8) };
            panel.Children.Add(new TextBlock { Text = "필드보스 알림 항목", Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            var cardsHost = new WrapPanel { Orientation = Orientation.Horizontal, ItemWidth = 210, Margin = new Thickness(0, 0, 0, 4) };
            foreach (var kv in _settings.BossAlertConfigs)
                cardsHost.Children.Add(CreateBossAlertCard(kv.Key, kv.Value));
            panel.Children.Add(cardsHost);

            var bossVolLabel = new TextBlock { Foreground = Brushes.White, Margin = new Thickness(0, 10, 0, 4) };
            bossVolLabel.SetBinding(TextBlock.TextProperty, new Binding(nameof(ChatSettings.BossAlertVolumePercent))
            {
                Source = _settings,
                StringFormat = "필드보스 알림 볼륨: {0:F0}%"
            });
            panel.Children.Add(bossVolLabel);
            var vol = new Slider { Minimum = 0, Maximum = 100, TickFrequency = 10, IsSnapToTickEnabled = true };
            vol.SetBinding(Slider.ValueProperty, new Binding(nameof(ChatSettings.BossAlertVolumePercent)) { Source = _settings, Mode = BindingMode.TwoWay });
            panel.Children.Add(vol);
            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private UIElement CreateDailyWeeklyChecklistStep()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            var middle = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            var right = new StackPanel();

            AddSectionTitle(left, "일일 컨텐츠");
            AddDungeonChecks(left, new[]
            {
                ("혼란한 대지", "혼란한 대지"),
                ("색을 잃은 땅", "색을 잃은 땅"),
                ("채굴장", "채굴장"),
                ("갈망하는 즐거움", "갈망하는 즐거움"),
                ("추종하는 환희(일반)", "추종하는 환희(일반)"),
                ("추종하는 환희(어려움)", "추종하는 환희(어려움)"),
                ("응시하는 슬픔(일반)", "응시하는 슬픔(일반)"),
                ("응시하는 슬픔(어려움)", "응시하는 슬픔(어려움)"),
                ("환희의 잔상", "환희의 잔상")
            });

            AddSectionTitle(middle, "주간 컨텐츠");
            AddSubgroup(middle, "머큐리얼");
            AddDungeonChecks(middle, new[]
            {
                ("- 머큐리얼 코어 마스터 던전", "머큐리얼 코어 마스터"),
                ("- 머큐리얼 주간", "머큐리얼 주간")
            }, indent: 10);

            AddSubgroup(middle, "어비스");
            AddDungeonChecks(middle, new[]
            {
                ("- 어비스 코어 마스터 던전", "어비스 코어 마스터"),
                ("- 어비스 지옥", "어비스 지옥"),
                ("- 심연의 보물창고", "심연의 보물창고"),
                ("- 차원의 틈", "차원의 틈")
            }, indent: 10);

            AddSubgroup(middle, "이클립스");
            AddDungeonChecks(middle, new[]
            {
                ("- 이클립스 코어 마스터 던전", "이클립스 코어 마스터"),
                ("- 이클립스 보스", "이클립스 보스"),
                ("- 이클립스 토벌전", "이클립스 토벌전"),
                ("- 보급품 탈환", "보급품 탈환"),
                ("- 훈련소", "훈련소"),
                ("- 별동대", "별동대"),
                ("- 아페티리아 EX", "아페티리아 EX"),
                ("- 아페티리아", "아페티리아"),
                ("- 최후의 결전", "최후의 결전")
            }, indent: 10);

            AddSectionTitle(right, "기타 지역");
            AddDungeonChecks(right, new[]
            {
                ("- 코어 던전", "코어던전"),
                ("- 발굴지", "발굴지"),
                ("- 렐릭", "렐릭"),
                ("- 청소 아르바이트", "청소 아르바이트"),
                ("- 프라바 방어전", "프라바 방어전"),
                ("- 베스티지", "베스티지"),
                ("- 오를리 방어전 지옥", "오를리 방어전 지옥"),
                ("- 카타콤 지옥", "카타콤 지옥"),
                ("- 신조의 둥지 어려움", "신조의 둥지 어려움"),
                ("- 시오칸 하임 보스 토벌전", "시오칸 하임 보스 토벌전"),
                ("- 시오칸 하임 오딘 전면전", "시오칸 하임 오딘 전면전"),
                ("- 어밴던로드", "어밴던로드")
            }, indent: 10);

            Grid.SetColumn(left, 0);
            Grid.SetColumn(middle, 1);
            Grid.SetColumn(right, 2);
            root.Children.Add(left);
            root.Children.Add(middle);
            root.Children.Add(right);

            return new ScrollViewer
            {
                Height = 330,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = root
            };
        }

        private void AddSectionTitle(Panel parent, string title)
        {
            parent.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58A6FF")),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 6)
            });
        }

        private void AddSubgroup(Panel parent, string title)
        {
            parent.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 6, 0, 2)
            });
        }

        private void AddDungeonChecks(Panel parent, IEnumerable<(string label, string key)> items, double indent = 0)
        {
            foreach (var (label, key) in items)
            {
                string resolvedKey = ResolveDungeonConfigKey(key);
                if (!_settings.DungeonItemConfigs.TryGetValue(resolvedKey, out var cfg))
                    continue;

                var cb = new CheckBox
                {
                    Content = label,
                    Foreground = Brushes.White,
                    Margin = new Thickness(indent, 2, 0, 2),
                    IsChecked = cfg.IsEnabled
                };
                cb.Checked += (_, _) => SetDungeonItemEnabled(resolvedKey, true);
                cb.Unchecked += (_, _) => SetDungeonItemEnabled(resolvedKey, false);
                parent.Children.Add(cb);
            }
        }


        private UIElement CreateBossAlertCard(string bossName, BossAlertConfig config)
        {
            var border = new Border
            {
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#404040")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#262626")),
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 10, 10),
                Width = 200
            };

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = ToKoreanBossName(bossName), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
            panel.Children.Add(CreateBossAlertCheck("3분 전", config.Alert3MinutesBefore, v => config.Alert3MinutesBefore = v));
            panel.Children.Add(CreateBossAlertCheck("1분 전", config.Alert1MinuteBefore, v => config.Alert1MinuteBefore = v));
            panel.Children.Add(CreateBossAlertCheck("5초 전", config.AlertAtSpawn, v => config.AlertAtSpawn = v));
            border.Child = panel;
            return border;
        }

        private static string ToKoreanBossName(string bossName)
        {
            return bossName switch
            {
                "Arkan" => "아칸",
                "Scherzendo" => "스페르첸드",
                "Origin of Doom" => "파멸의 기원",
                "Confused Land" => "혼란한 대지",
                "event" => "이벤트",
                _ => bossName
            };
        }

        private UIElement CreateBossAlertCheck(string label, bool initial, Action<bool> setValue)
        {
            var cb = new CheckBox { Content = label, Foreground = Brushes.White, Margin = new Thickness(0, 2, 0, 2), IsChecked = initial };
            cb.Checked += (_, _) => setValue(true);
            cb.Unchecked += (_, _) => setValue(false);
            return cb;
        }

        private void SetDungeonItemEnabled(string key, bool enabled)
        {
            SetDungeonItemEnabledRecursive(key, enabled, new HashSet<string>(StringComparer.Ordinal));
        }

        private void SetDungeonItemEnabledRecursive(string key, bool enabled, HashSet<string> visited)
        {
            string resolvedKey = ResolveDungeonConfigKey(key);
            if (!visited.Add(resolvedKey))
                return;

            if (_settings.DungeonItemConfigs.TryGetValue(resolvedKey, out var cfg))
            {
                cfg.IsEnabled = enabled;
                _settings.DungeonItemConfigs[resolvedKey] = cfg;
            }

            foreach (string child in GetDungeonChildKeys(resolvedKey))
            {
                SetDungeonItemEnabledRecursive(child, enabled, visited);
            }
        }

        private static IEnumerable<string> GetDungeonChildKeys(string key)
        {
            return key switch
            {
                "어밴던로드" => new[] { "필멸의 땅", "카디프", "오를란느" },
                "이클립스 보스" => new[] { "로카고스", "에토스", "체리아", "마티아", "티로로스", "라이코스" },
                "이클립스 코어 마스터" => new[] { "로카고스 코어 마스터", "에토스 코어 마스터", "체리아 코어 마스터", "마티아 코어 마스터", "라이코스 코어 마스터", "티로로스 코어 마스터" },
                "어비스 코어 마스터" => new[] { "심층Ⅰ 코어 마스터", "심층Ⅱ 코어 마스터", "심층Ⅲ 코어 마스터" },
                "어비스 지옥" => new[] { "어비스 - 심층Ⅰ", "어비스 - 심층Ⅱ", "어비스 - 심층Ⅲ" },
                "머큐리얼 코어 마스터" => new[] { "샐리온 코어 마스터 던전", "샐레아나 코어 마스터 던전", "실라이론 코어 마스터 던전", "실반 코어 마스터 던전", "루미너스 코어 마스터 던전" },
                "머큐리얼 주간" => new[] { "샐리온", "샐레아나", "실라이론", "실반", "루미너스", "루미너스(EX)" },
                _ => Array.Empty<string>()
            };
        }

        private string ResolveDungeonConfigKey(string key)
        {
            if (_settings.DungeonItemConfigs.ContainsKey(key))
                return key;

            return key switch
            {
                "아페티리아" => _settings.DungeonItemConfigs.ContainsKey("아페티리아") ? "아페티리아" : key,
                "아페티리아 어려움" => _settings.DungeonItemConfigs.ContainsKey("아페티리아") ? "아페티리아" : key,
                "아페티리아 일반" => _settings.DungeonItemConfigs.ContainsKey("아페티리아") ? "아페티리아" : key,
                "추종하는 환희(일반)" => _settings.DungeonItemConfigs.ContainsKey("아페티리아 일반") ? "아페티리아 일반" : key,
                "추종하는 환희(어려움)" => _settings.DungeonItemConfigs.ContainsKey("아페티리아 어려움") ? "아페티리아 어려움" : key,
                "응시하는 슬픔(일반)" => _settings.DungeonItemConfigs.ContainsKey("카디프") ? "카디프" : key,
                "응시하는 슬픔(어려움)" => _settings.DungeonItemConfigs.ContainsKey("오를란느") ? "오를란느" : key,
                "환희의 잔상" => _settings.DungeonItemConfigs.ContainsKey("필멸의 땅") ? "필멸의 땅" : key,
                _ => key
            };
        }

        private void AddDailyWeeklyGroup(Panel parent, string topGroup, string midGroup, IEnumerable<string> keys)
        {
            parent.Children.Add(new TextBlock
            {
                Text = topGroup,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#58A6FF")),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 3)
            });

            parent.Children.Add(new TextBlock
            {
                Text = midGroup,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B949E")),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 5)
            });

            foreach (string key in keys)
            {
                if (!_settings.DungeonItemConfigs.TryGetValue(key, out var cfg))
                    continue;

                var cb = new CheckBox
                {
                    Content = key,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 2, 0, 2),
                    IsChecked = cfg.IsEnabled
                };
                cb.Checked += (_, _) => SetDungeonItemEnabled(key, true);
                cb.Unchecked += (_, _) => SetDungeonItemEnabled(key, false);
                parent.Children.Add(cb);
            }
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            _stepIndex--;
            RenderStep();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_stepIndex == 1)
            {
                SaveMainWindowPositionToPreset1();
            }

            _stepIndex++;
            RenderStep();
        }

        private void SetPositionPreview(bool enabled)
        {
            _positionPreviewEnabled = enabled;
            try
            {
                if (_stepIndex == 1)
                {
                    _mainWindow?.SetWizardChatPositionMode(enabled);
                }
                else
                {
                    _mainWindow?.SetWizardChatPositionMode(false);
                    _mainWindow?.SetSettingsPositionMode(enabled);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to toggle settings position mode from setup wizard.", ex);
            }

            if (enabled)
            {
                try
                {
                    _mainWindow?.Show();
                    if (_mainWindow != null)
                    {
                        _mainWindow.Opacity = 1;
                        _mainWindow.IsHitTestVisible = true;
                        _mainWindow.Visibility = Visibility.Visible;
                    }
                }
                catch { }
            }
        }

        private void SaveMainWindowPositionToPreset1()
        {
            try
            {
                if (_mainWindow == null)
                    return;

                _settings.SavePreset(1, _mainWindow.Left, _mainWindow.Top, _settings.LineMarginLeft, _settings.LineMargin);
                _settings.LastSelectedPresetNumber = 1;
                ConfigService.SaveDeferred(_settings);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save preset1 from setup wizard.", ex);
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            try { ShoutToastService.ClosePositionPreview(_settings); } catch { }
            try { _mainWindow?.ShowWizardStepPreviewWindows(-1); } catch { }
            SetPositionPreview(false);
            ConfigService.Save(_settings);
            WizardFinished?.Invoke(this, false);
            Close();
        }

        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShoutToastService.ClosePositionPreview(_settings);
                _mainWindow?.ShowWizardStepPreviewWindows(-1);
                SetPositionPreview(false);
                SaveMainWindowPositionToPreset1();
                ConfigService.Save(_settings);
            }
            catch
            {
            }

            WizardFinished?.Invoke(this, true);
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            try { ShoutToastService.ClosePositionPreview(_settings); } catch { }
            try { _mainWindow?.ShowWizardStepPreviewWindows(-1); } catch { }
            try { SetPositionPreview(false); } catch { }
            base.OnClosed(e);
        }
    }
}
