using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TWChatOverlay.Views.Addons
{
    /// <summary>
    /// 계수 계산기 스냅샷을 기준으로 대미지 계산 옵션을 구성하는 뷰입니다.
    /// </summary>
    public partial class DamageCalculatorView : UserControl
    {
        private readonly CoefficientCalculatorView? _coefficientView;
        private bool _subscribed;

        private readonly Group1State _group1 = new();
        private readonly Group2State _group2 = new();
        private readonly Group4State _group4 = new();
        private readonly Group5State _group5 = new();
        private readonly Group11State _group11 = new();

        public DamageCalculatorView()
        {
            InitializeComponent();
            ApplySnapshot(CoefficientDamageBaseSnapshot.Empty);
            RefreshGroupSummaryTexts();
        }

        public DamageCalculatorView(CoefficientCalculatorView coefficientView)
            : this()
        {
            _coefficientView = coefficientView;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_coefficientView == null || _subscribed)
                return;

            _coefficientView.SnapshotChanged += CoefficientView_SnapshotChanged;
            _subscribed = true;
            ApplySnapshot(_coefficientView.CurrentSnapshot);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_coefficientView == null || !_subscribed)
                return;

            _coefficientView.SnapshotChanged -= CoefficientView_SnapshotChanged;
            _subscribed = false;
        }

        private void CoefficientView_SnapshotChanged(CoefficientDamageBaseSnapshot snapshot)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ApplySnapshot(snapshot));
                return;
            }

            ApplySnapshot(snapshot);
        }

        private void ApplySnapshot(CoefficientDamageBaseSnapshot snapshot)
        {
            bool hasData = snapshot.HasData;
            NoDataPanel.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
            DataPanel.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;

            if (!hasData)
                return;

            CharacterNameText.Text = snapshot.CharacterName;
            CalculatorTypeText.Text = snapshot.CalculatorTypeName;
            TotalCoefficientText.Text = snapshot.TotalCoefficient.ToString("F2", CultureInfo.CurrentCulture);
            TotalPrimaryText.Text = snapshot.TotalPrimarySum.ToString("F0", CultureInfo.CurrentCulture);
            PrimaryEnchantText.Text = snapshot.PrimaryEnchantSum.ToString("F0", CultureInfo.CurrentCulture);
            SecondarySummaryText.Text =
                $"{snapshot.SecondarySum.ToString("F0", CultureInfo.CurrentCulture)} / {snapshot.SecondaryEnchantSum.ToString("F0", CultureInfo.CurrentCulture)}";
        }

        private void OpenGroup1Window_Click(object sender, RoutedEventArgs e)
        {
            var window = CreateSettingsWindow("그룹1 설정 (상한 40%)");
            var panel = CreateWindowRootPanel(window);

            var snowmanToggle = CreateToggle("눈사람 (20%)", _group1.Snowman);
            var illumiToggle = CreateToggle("일루미 (10%)", _group1.Illumi);
            snowmanToggle.Checked += (_, _) => illumiToggle.IsChecked = false;
            illumiToggle.Checked += (_, _) => snowmanToggle.IsChecked = false;
            panel.Children.Add(snowmanToggle);
            panel.Children.Add(illumiToggle);
            panel.Children.Add(CreateToggle("이자벨 대미지 (10%)", _group1.IsabelDamage));
            panel.Children.Add(CreateToggle("이자벨 특선 대미지 (10%)", _group1.IsabelSpecial));
            panel.Children.Add(CreateToggle("이자벨 전투 (10%)", _group1.IsabelBattle));
            panel.Children.Add(CreateLabeledTextBox("기타", _group1.EtcValue, out var etcText));

            AddSaveButton(panel, () =>
            {
                _group1.Snowman = snowmanToggle.IsChecked == true;
                _group1.Illumi = illumiToggle.IsChecked == true;
                _group1.IsabelDamage = ReadToggle(panel, 2);
                _group1.IsabelSpecial = ReadToggle(panel, 3);
                _group1.IsabelBattle = ReadToggle(panel, 4);
                _group1.EtcValue = etcText.Text.Trim();
                RefreshGroupSummaryTexts();
                window.DialogResult = true;
                window.Close();
            });

            window.ShowDialog();
        }

        private void OpenGroup2Window_Click(object sender, RoutedEventArgs e)
        {
            var window = CreateSettingsWindow("그룹2 설정 (상한 30%)");
            var panel = CreateWindowRootPanel(window);

            panel.Children.Add(CreateToggle("개각비 (5%)", _group2.Gaegakbi));
            panel.Children.Add(CreateToggle("클럽 Type-P (5%)", _group2.ClubTypeP));
            panel.Children.Add(CreateToggle("탐험 포인트 공증 (5%)", _group2.ExplorePoint));
            panel.Children.Add(CreateToggle("테일즈위버 기운 (5%)", _group2.TwPower));
            panel.Children.Add(CreateToggle("괴력의 햄 (10%)", _group2.Ham));
            panel.Children.Add(CreateToggle("이벤트 (10%)", _group2.Event));
            panel.Children.Add(CreateLabeledTextBox("기타", _group2.EtcValue, out var etcText));

            AddSaveButton(panel, () =>
            {
                _group2.Gaegakbi = ReadToggle(panel, 0);
                _group2.ClubTypeP = ReadToggle(panel, 1);
                _group2.ExplorePoint = ReadToggle(panel, 2);
                _group2.TwPower = ReadToggle(panel, 3);
                _group2.Ham = ReadToggle(panel, 4);
                _group2.Event = ReadToggle(panel, 5);
                _group2.EtcValue = etcText.Text.Trim();
                RefreshGroupSummaryTexts();
                window.DialogResult = true;
                window.Close();
            });

            window.ShowDialog();
        }

        private void OpenGroup4Window_Click(object sender, RoutedEventArgs e)
        {
            var window = CreateSettingsWindow("그룹4 설정 (상한 80%)");
            var panel = CreateWindowRootPanel(window);

            panel.Children.Add(CreateToggle("칭호 (20%)", _group4.TitleDamage));
            panel.Children.Add(CreateToggle("무기 부가 옵션 (10%)", _group4.WeaponOption));
            panel.Children.Add(CreateToggle("피버 (10%)", _group4.Fever));

            panel.Children.Add(CreateLabeledCombo("손목 어빌리티", new[] { "심연 (9%)", "상실 (10%)", "야성 (11%)" }, _group4.WristAbilityIndex, out var wristCombo));
            panel.Children.Add(CreateLabeledCombo("손 어빌리티", new[] { "심연 (7%)", "상실 (8%)", "야성 (9%)" }, _group4.HandAbilityIndex, out var handCombo));
            panel.Children.Add(CreateLabeledTextBox("루나리아 어빌리티", _group4.LunariaAbility, out var lunariaText));
            panel.Children.Add(CreateLabeledTextBox("심화 룬", _group4.DeepRune, out var deepRuneText));
            panel.Children.Add(CreateLabeledTextBox("기타", _group4.EtcValue, out var etcText));

            AddSaveButton(panel, () =>
            {
                _group4.TitleDamage = ReadToggle(panel, 0);
                _group4.WeaponOption = ReadToggle(panel, 1);
                _group4.Fever = ReadToggle(panel, 2);
                _group4.WristAbilityIndex = Math.Max(0, wristCombo.SelectedIndex);
                _group4.HandAbilityIndex = Math.Max(0, handCombo.SelectedIndex);
                _group4.LunariaAbility = lunariaText.Text.Trim();
                _group4.DeepRune = deepRuneText.Text.Trim();
                _group4.EtcValue = etcText.Text.Trim();
                RefreshGroupSummaryTexts();
                window.DialogResult = true;
                window.Close();
            });

            window.ShowDialog();
        }

        private void OpenGroup5Window_Click(object sender, RoutedEventArgs e)
        {
            var window = CreateSettingsWindow("그룹5 설정 (상한 73%)");
            var panel = CreateWindowRootPanel(window);

            panel.Children.Add(CreateLabeledCombo("아티팩트", new[] { "프시키 (15%)", "아크론 (20%)", "이클립스 (30%)", "에테리얼 (35%)" }, _group5.ArtifactIndex, out var artifactCombo));
            panel.Children.Add(CreateLabeledCombo("손목 부가", new[] { "25%", "26%", "27%", "28%" }, _group5.WristExtraIndex, out var wristCombo));
            panel.Children.Add(CreateLabeledCombo("루나리아 부가", new[] { "0%", "1%", "2%", "3%", "4%", "5%", "6%", "7%", "8%", "9%", "10%" }, _group5.LunariaExtraIndex, out var lunariaCombo));

            AddSaveButton(panel, () =>
            {
                _group5.ArtifactIndex = Math.Max(0, artifactCombo.SelectedIndex);
                _group5.WristExtraIndex = Math.Max(0, wristCombo.SelectedIndex);
                _group5.LunariaExtraIndex = Math.Max(0, lunariaCombo.SelectedIndex);
                RefreshGroupSummaryTexts();
                window.DialogResult = true;
                window.Close();
            });

            window.ShowDialog();
        }

        private void OpenGroup11Window_Click(object sender, RoutedEventArgs e)
        {
            var window = CreateSettingsWindow("그룹11 설정 - 추가 피해");
            var panel = CreateWindowRootPanel(window);

            panel.Children.Add(CreateLabeledCombo("저격 연마", new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" }, _group11.SniperIndex, out var sniperCombo));
            panel.Children.Add(CreateLabeledCombo("장비 강화석 옵션", new[] { "0%", "45%", "46%", "47%", "48%" }, _group11.GemOptionIndex, out var gemCombo));
            panel.Children.Add(CreateLabeledCombo("무기 부가", new[] { "0%", "18%", "19%", "20%", "21%" }, _group11.WeaponExtraIndex, out var weaponCombo));
            panel.Children.Add(CreateLabeledReadOnlyValue("캐릭터 고유 값", _group11.TraitValue));

            AddSaveButton(panel, () =>
            {
                _group11.SniperIndex = Math.Max(0, sniperCombo.SelectedIndex);
                _group11.GemOptionIndex = Math.Max(0, gemCombo.SelectedIndex);
                _group11.WeaponExtraIndex = Math.Max(0, weaponCombo.SelectedIndex);
                RefreshGroupSummaryTexts();
                window.DialogResult = true;
                window.Close();
            });

            window.ShowDialog();
        }

        private void RefreshGroupSummaryTexts()
        {
            Group1SummaryText.Text = $"눈사람:{OnOff(_group1.Snowman)} / 일루미:{OnOff(_group1.Illumi)} / 기타:{NullIfEmpty(_group1.EtcValue)}";
            Group2SummaryText.Text = $"활성 {CountTrue(_group2.Gaegakbi, _group2.ClubTypeP, _group2.ExplorePoint, _group2.TwPower, _group2.Ham, _group2.Event)}개 / 기타:{NullIfEmpty(_group2.EtcValue)}";
            Group4SummaryText.Text = $"손목:{IndexToName(_group4.WristAbilityIndex, new[] { "심연", "상실", "야성" })} / 손:{IndexToName(_group4.HandAbilityIndex, new[] { "심연", "상실", "야성" })}";
            Group5SummaryText.Text = $"아티팩트:{IndexToName(_group5.ArtifactIndex, new[] { "프시키", "아크론", "이클립스", "에테리얼" })}";
            Group11SummaryText.Text = $"저격:{_group11.SniperIndex + 1} / 강화석:{IndexToName(_group11.GemOptionIndex, new[] { "0", "45", "46", "47", "48" })}%";
        }

        private static Window CreateSettingsWindow(string title)
        {
            return new Window
            {
                Title = title,
                Width = 360,
                Height = 560,
                ResizeMode = ResizeMode.CanMinimize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = System.Windows.Media.Brushes.Transparent,
                AllowsTransparency = false
            };
        }

        private static StackPanel CreateWindowRootPanel(Window window)
        {
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x11, 0x18, 0x21)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12)
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var panel = new StackPanel();
            scrollViewer.Content = panel;
            border.Child = scrollViewer;
            window.Content = border;
            return panel;
        }

        private static CheckBox CreateToggle(string label, bool isChecked)
        {
            return new CheckBox
            {
                Content = label,
                IsChecked = isChecked,
                Margin = new Thickness(0, 0, 0, 6),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC9, 0xD1, 0xD9)),
                HorizontalAlignment = HorizontalAlignment.Left
            };
        }

        private static Border CreateLabeledTextBox(string label, string value, out TextBox textBox)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x94, 0x9E)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
            textBox = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(value) ? "0" : value,
                Height = 30,
                Width = 110,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0D, 0x11, 0x17)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            panel.Children.Add(textBox);
            return new Border { Child = panel };
        }

        private static Border CreateLabeledReadOnlyValue(string label, string value)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x94, 0x9E)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "0" : value,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 2)
            });
            return new Border { Child = panel };
        }

        private static Border CreateLabeledCombo(string label, string[] options, int selectedIndex, out ComboBox comboBox)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            panel.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x94, 0x9E)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
            comboBox = new ComboBox
            {
                Height = 30,
                Width = 110,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0D, 0x11, 0x17)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 0, 8, 0),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            foreach (var option in options)
                comboBox.Items.Add(option);
            comboBox.SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, comboBox.Items.Count - 1));
            panel.Children.Add(comboBox);
            return new Border { Child = panel };
        }

        private static void AddSaveButton(Panel panel, Action saveAction)
        {
            var saveButton = new Button
            {
                Content = "저장",
                Height = 34,
                Width = 120,
                Margin = new Thickness(0, 8, 0, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4B, 0x3A, 0x74)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6E, 0x40, 0xC9)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            saveButton.Click += (_, _) => saveAction();
            panel.Children.Add(saveButton);
        }

        private static bool ReadToggle(Panel panel, int index)
        {
            if (index < 0 || index >= panel.Children.Count)
                return false;

            return panel.Children[index] is CheckBox checkbox && checkbox.IsChecked == true;
        }

        private static string OnOff(bool value) => value ? "ON" : "OFF";
        private static int CountTrue(params bool[] values) => values.Count(static x => x);
        private static string NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? "0" : value;
        private static string IndexToName(int index, string[] names)
        {
            if (index < 0 || index >= names.Length)
                return "-";
            return names[index];
        }

        private sealed class Group1State
        {
            public bool Snowman { get; set; }
            public bool Illumi { get; set; }
            public bool IsabelDamage { get; set; }
            public bool IsabelSpecial { get; set; }
            public bool IsabelBattle { get; set; }
            public string EtcValue { get; set; } = "0";
        }

        private sealed class Group2State
        {
            public bool Gaegakbi { get; set; }
            public bool ClubTypeP { get; set; }
            public bool ExplorePoint { get; set; }
            public bool TwPower { get; set; }
            public bool Ham { get; set; }
            public bool Event { get; set; }
            public string EtcValue { get; set; } = "0";
        }

        private sealed class Group4State
        {
            public bool TitleDamage { get; set; }
            public bool WeaponOption { get; set; }
            public bool Fever { get; set; }
            public int WristAbilityIndex { get; set; }
            public int HandAbilityIndex { get; set; }
            public string LunariaAbility { get; set; } = "0";
            public string DeepRune { get; set; } = "0";
            public string EtcValue { get; set; } = "0";
        }

        private sealed class Group5State
        {
            public int ArtifactIndex { get; set; }
            public int WristExtraIndex { get; set; }
            public int LunariaExtraIndex { get; set; }
        }

        private sealed class Group11State
        {
            public int SniperIndex { get; set; }
            public int GemOptionIndex { get; set; }
            public int WeaponExtraIndex { get; set; }
            public string TraitValue { get; set; } = "0";
        }
    }
}
