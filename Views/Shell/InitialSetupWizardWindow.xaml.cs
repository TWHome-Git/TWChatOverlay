using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.ViewModels;

namespace TWChatOverlay.Views
{
    public partial class InitialSetupWizardWindow : Window
    {
        private sealed record WizardStep(string Title, string Description, bool SupportsPositionPreview);

        // 채팅창 위치 설정 단계는 잠금 해제 모드(창 편집)로 대체되어 제거됨
        private readonly List<WizardStep> _steps = new()
        {
            new("1. 채팅 로그 위치 설정", "채팅 로그 폴더를 지정합니다.", false),
            new("2. 채팅창 설정", "", false),
            new("3. 외치기 설정", "외치기 팝업/위치/자동복사/유지시간/텍스트 크기를 설정합니다.", true),
            new("4. 키워드 알림 설정", "키워드 알림 기능을 설정합니다.", false),
            new("5. 경험치 추적 설정", "경험치 추적 및 누적 알림을 설정합니다.", true),
            new("6. 던전 도우미 설정", "던전 도우미 알림 항목을 설정합니다.", true),
            new("7. 아이템 획득 알림 설정", "아이템 획득 알림 및 필터를 설정합니다.", true),
            new("8. 버프 추적 설정", "버프 추적 알림 및 종료 사운드를 설정합니다.", true),
            new("9. 필드 보스 알림 설정", "필드 보스 알림을 설정합니다.", false),
            new("10. 일일/주간 컨텐츠 추적 설정", "일일/주간 컨텐츠 체크 항목을 설정합니다.", false)
        };

        private int _stepIndex;
        private readonly ChatSettings _settings;
        private readonly MainWindow? _mainWindow;
        private bool _positionPreviewEnabled;
        private bool _shoutPreviewEnabled;

        private UIElement? _dailyWeeklyStepContent;
        private SettingsView? _embeddedSettings;

        public event EventHandler<bool>? WizardFinished;
        public event EventHandler<string>? LogPathConfirmed;

        public InitialSetupWizardWindow(ChatSettings settings, MainWindow? mainWindow)
        {
            InitializeComponent();
            WindowFontService.Apply(this);
            _settings = settings;
            _mainWindow = mainWindow;
            if (!_settings.InitialSetupWizardCompleted)
            {
                // 최초 실행: 공장 기본 설정(Defaults\DefaultSettings.json)을 마법사 시작값으로 적용
                try { _mainWindow?.SettingsViewModelInstance.ApplyFactoryDefaultsForWizard(); }
                catch (Exception ex) { AppLogger.Warn("Failed to apply factory defaults for setup wizard.", ex); }
                ResetInitialWindowPositionsToOrigin();
                ConfigService.SaveDeferred(_settings);
            }
            RenderStep();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch { }
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SkipButton_Click(sender, e);
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

            UpdateStepSpecificPreviews();

            // 임베드된 설정 패널은 같은 인스턴스를 재사용하므로 불필요한 재장착(Unloaded/Loaded)을 피한다
            UIElement content = BuildStepContent(_stepIndex);
            if (!ReferenceEquals(StepContentHost.Content, content))
                StepContentHost.Content = content;
        }

        private void UpdateStepSpecificPreviews()
        {
            // 외치기 단계에서만 외치기 위치 미리보기를 표시 (설정 화면에는 없는 마법사 전용 보조)
            bool shouldShowShoutPreview = _stepIndex == 2;
            if (shouldShowShoutPreview != _shoutPreviewEnabled)
            {
                _shoutPreviewEnabled = shouldShowShoutPreview;
                try
                {
                    if (_shoutPreviewEnabled)
                        ShoutToastService.ShowPositionPreview(_settings, force: true);
                    else
                        ShoutToastService.ClosePositionPreview(_settings);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to toggle shout position preview in setup wizard.", ex);
                }
            }

            // 임베드되지 않는 단계(로그 위치·일일/주간)에서는 추가 기능 미리보기를 정리한다
            if (_stepIndex == 0 || _stepIndex == _steps.Count - 1)
                _embeddedSettings?.EndWizardPanelMode();
        }

        private UIElement BuildStepContent(int stepIndex)
        {
            // 1~8단계는 실제 설정 화면 패널을 그대로 임베드한다 (설정과 마법사의 이중 관리 제거)
            return stepIndex switch
            {
                0 => BuildLogPathStepContent(),
                1 => GetEmbeddedSettingsPanel("Chat"),
                2 => GetEmbeddedSettingsPanel("Shout"),
                3 => GetEmbeddedSettingsPanel("Keyword"),
                4 => GetEmbeddedSettingsPanel("Exp"),
                5 => GetEmbeddedSettingsPanel("Dungeon"),
                6 => GetEmbeddedSettingsPanel("Item"),
                7 => GetEmbeddedSettingsPanel("Buff"),
                8 => GetEmbeddedSettingsPanel("Boss"),
                9 => BuildDailyWeeklyStepContent(),
                _ => new TextBlock { Text = "준비 중", Foreground = ThemeBrushes.Get("TextBrush", Brushes.White) }
            };
        }

        /// <summary>
        /// 설정 화면(SettingsView)의 실제 패널을 마법사 단계로 임베드한다.
        /// 같은 인스턴스를 재사용하며 ShowWizardPanel이 해당 패널만 표시한다.
        /// </summary>
        private SettingsView GetEmbeddedSettingsPanel(string navKey)
        {
            if (_embeddedSettings == null)
            {
                _embeddedSettings = new SettingsView();
                if (_mainWindow != null)
                    _embeddedSettings.DataContext = _mainWindow.SettingsViewModelInstance;
            }

            _embeddedSettings.ShowWizardPanel(navKey);
            return _embeddedSettings;
        }

        private UIElement BuildLogPathStepContent()
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "채팅 로그 폴더", Foreground = ThemeBrushes.Get("TextBrush", Brushes.White), Margin = new Thickness(0, 0, 0, 6) });

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // 색상은 창의 암시적 TextBox 스타일(테마 브러시)을 그대로 사용
            var pathBox = new TextBox
            {
                Height = 30,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(8, 0, 8, 0)
            };
            pathBox.SetBinding(TextBox.TextProperty, new Binding(nameof(ChatSettings.ChatLogFolderPath)) { Source = _settings, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            row.Children.Add(pathBox);

            var browseBtn = new Button { Content = "찾아보기", Width = 96, Height = 30, Margin = new Thickness(8, 0, 0, 0) };
            browseBtn.Click += (_, _) =>
            {
                // .NET 8 WPF 기본 제공 폴더 선택 대화상자 (WinForms 불필요)
                var dlg = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "채팅 로그 폴더를 선택하세요",
                    InitialDirectory = string.IsNullOrWhiteSpace(_settings.ChatLogFolderPath) ? @"C:\Nexon\TalesWeaver\ChatLog" : _settings.ChatLogFolderPath
                };
                if (dlg.ShowDialog(this) == true)
                {
                    _settings.ChatLogFolderPath = dlg.FolderName;
                }
            };
            Grid.SetColumn(browseBtn, 1);
            row.Children.Add(browseBtn);

            panel.Children.Add(row);
            panel.Children.Add(new TextBlock { Text = "경로 저장은 완료 시 자동 반영됩니다.", Foreground = ThemeBrushes.Get("OverlaySubtleTextBrush"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0) });
            return panel;
        }

        private UIElement BuildDailyWeeklyStepContent()
        {
            _dailyWeeklyStepContent ??= CreateDailyWeeklyChecklistStep();
            return _dailyWeeklyStepContent;
        }

        private UIElement CreateDailyWeeklyChecklistStep()
        {
            var root = new Grid { Margin = new Thickness(0, 0, 8, 12) };
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
                ("에타 일일 도전 과제", "에타 일일 도전 과제"),
                ("에타의 의지 퀘스트", "에타의 의지 퀘스트"),
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
                ("- 차원의 틈", "차원의 틈"),
                ("- 이터널 플로어", "이터널 플로어")
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
                ("- 코어 던전", "코어 던전"),
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
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = root
            };
        }

        private void AddSectionTitle(Panel parent, string title)
        {
            parent.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = ThemeBrushes.Get("OverlayAccentTextBrush"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 6)
            });
        }

        private void AddSubgroup(Panel parent, string title)
        {
            parent.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = ThemeBrushes.Get("TextBrush", Brushes.White),
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
                {
                    cfg = new DungeonItemConfig { IsEnabled = true };
                    _settings.DungeonItemConfigs[resolvedKey] = cfg;
                }

                parent.Children.Add(CreateToggleListRow(label, indent, cfg.IsEnabled,
                    v => SetDungeonItemEnabled(resolvedKey, v)));
            }
        }


        /// <summary>목록형 토글 행: 라벨 왼쪽(들여쓰기 지원) + 토글 스위치 오른쪽 — 설정 화면과 같은 스타일.</summary>
        private static UIElement CreateToggleListRow(string label, double indent, bool initial, Action<bool> setValue)
        {
            var toggle = new CheckBox { VerticalAlignment = VerticalAlignment.Center, IsChecked = initial };
            toggle.SetResourceReference(FrameworkElement.StyleProperty, "ToggleSwitchCheckBoxStyle");
            toggle.Checked += (_, _) => setValue(true);
            toggle.Unchecked += (_, _) => setValue(false);

            var dock = new DockPanel { Margin = new Thickness(indent, 3, 0, 3) };
            DockPanel.SetDock(toggle, Dock.Right);
            dock.Children.Add(toggle);
            dock.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeBrushes.Get("TextBrush", Brushes.White),
            });
            return dock;
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
                _ => key
            };
        }

        private void ResetInitialWindowPositionsToOrigin()
        {
            _settings.LineMarginLeft = 0.0;
            _settings.LineMargin = 0.0;
            _settings.DailyWeeklyContentOverlayLeft = 0.0;
            _settings.DailyWeeklyContentOverlayTop = 0.0;
            _settings.SubAddonWindowLeft = 0.0;
            _settings.SubAddonWindowTop = 0.0;
            _settings.SubMenuWindowLeft = 0.0;
            _settings.SubMenuWindowTop = 0.0;
            _settings.MenuWindowLeft = 0.0;
            _settings.MenuWindowTop = 0.0;
            _settings.ItemDropWindowLeft = 0.0;
            _settings.ItemDropWindowTop = 0.0;
            _settings.BuffTrackerWindowLeft = 0.0;
            _settings.BuffTrackerWindowTop = 0.0;
            _settings.ItemCalendarWindowLeft = 0.0;
            _settings.ItemCalendarWindowTop = 0.0;
            _settings.AbandonRoadSummaryWindowLeft = 0.0;
            _settings.AbandonRoadSummaryWindowTop = 0.0;
            _settings.RecaptureSupplyWindowLeft = 0.0;
            _settings.RecaptureSupplyWindowTop = 0.0;
            _settings.ExperienceLimitAlertWindowLeft = 0.0;
            _settings.ExperienceLimitAlertWindowTop = 0.0;
            _settings.DungeonCountDisplayWindowLeft = 0.0;
            _settings.DungeonCountDisplayWindowTop = 0.0;
            _settings.ShoutToastWindowLeft = 0.0;
            _settings.ShoutToastWindowTop = 0.0;
            _settings.MessengerToastWindowLeft = 0.0;
            _settings.MessengerToastWindowTop = 0.0;
            _settings.ChatCloneWindow1Left = 0.0;
            _settings.ChatCloneWindow1Top = 0.0;
            _settings.ChatCloneWindow1Width = null;
            _settings.ChatCloneWindow1Height = null;
            _settings.ChatCloneWindow2Left = 0.0;
            _settings.ChatCloneWindow2Top = 0.0;
            _settings.ChatCloneWindow2Width = null;
            _settings.ChatCloneWindow2Height = null;
            _settings.ChatCloneWindow1IsOpen = false;
            _settings.ChatCloneWindow2IsOpen = false;
            _settings.ShoutReplayWindowLeft = 0.0;
            _settings.ShoutReplayWindowTop = 0.0;
            _settings.MemoOverlayWindowLeft = 0.0;
            _settings.MemoOverlayWindowTop = 0.0;
        }

        private void PrevButton_Click(object sender, RoutedEventArgs e)
        {
            _stepIndex--;
            RenderStep();
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_stepIndex == 0)
            {
                LogPathConfirmed?.Invoke(this, _settings.ChatLogFolderPath ?? string.Empty);
            }

            _stepIndex++;
            RenderStep();
        }

        private void SetPositionPreview(bool enabled)
        {
            _positionPreviewEnabled = enabled;
            try
            {
                _mainWindow?.SetWizardChatPositionMode(false);
                _mainWindow?.SetSettingsPositionMode(enabled);
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
            // 위치는 LineMargin 값으로 이미 저장된다 (프리셋 시스템은 프로필로 대체되어 제거됨)
            try
            {
                ConfigService.SaveDeferred(_settings);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save settings from setup wizard.", ex);
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            try { ShoutToastService.ClosePositionPreview(_settings); } catch { }
            try { _embeddedSettings?.EndWizardPanelMode(); } catch { }
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
                _embeddedSettings?.EndWizardPanelMode();
                SetPositionPreview(false);
                SaveMainWindowPositionToPreset1();
                ApplyExperienceLimitStateFromWizard();
                ConfigService.Save(_settings);
            }
            catch
            {
            }

            WizardFinished?.Invoke(this, true);
            Close();
        }

        private void ApplyExperienceLimitStateFromWizard()
        {
            // 임베드된 설정 화면의 VM을 재사용하고, 없으면(추가 기능 단계를 건너뜀) 일회용으로 만들어 즉시 해제
            var vm = _embeddedSettings?.AddonViewModelInstance;
            if (vm != null)
            {
                vm.ApplyExperienceLimitStateFromSettings();
                return;
            }

            var temp = new AddonViewModel(_settings);
            try { temp.ApplyExperienceLimitStateFromSettings(); }
            finally { temp.Detach(); }
        }

        protected override void OnClosed(EventArgs e)
        {
            try { ShoutToastService.ClosePositionPreview(_settings); } catch { }
            try { _embeddedSettings?.EndWizardPanelMode(); } catch { }
            try { SetPositionPreview(false); } catch { }
            base.OnClosed(e);
        }
    }
}
