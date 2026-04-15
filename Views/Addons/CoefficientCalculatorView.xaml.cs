using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using TWChatOverlay.Converters;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Addons
{
    /// <summary>
    /// 캐릭터별 계수 계산기 진입 화면을 제공하는 뷰입니다.
    /// </summary>
    public partial class CoefficientCalculatorView : UserControl
    {
        private static readonly string[] MainSlotNames =
        {
            "무기", "무기 어빌리티", "갑옷", "갑옷 어빌리티", "손목", "손목 어빌리티",
            "투구", "머리", "몸", "손", "손 어빌리티", "다리", "효과", "아티팩트"
        };

        private static readonly string[] AccessorySlotNames =
        {
            "스탯", "아바타", "커프", "렐릭", "칭호", "코어"
        };

        private static readonly Dictionary<string, IReadOnlyList<CoefficientCalculatorType>> CharacterCalculatorTypeMap = new()
        {
            ["나야트레이"] = new[] { CoefficientCalculatorType.Stab, CoefficientCalculatorType.Hack, CoefficientCalculatorType.PhysicalHybrid },
            ["루시안"] = new[] { CoefficientCalculatorType.Stab, CoefficientCalculatorType.Hack, CoefficientCalculatorType.PhysicalHybrid },
            ["이자크"] = new[] { CoefficientCalculatorType.Stab, CoefficientCalculatorType.Hack },

            ["막시민"] = new[] { CoefficientCalculatorType.PhysicalHybrid, CoefficientCalculatorType.Hack, CoefficientCalculatorType.MagicHack },
            ["보리스"] = new[] { CoefficientCalculatorType.Hack, CoefficientCalculatorType.PhysicalHybrid, CoefficientCalculatorType.MagicHack },
            ["시벨린"] = new[] { CoefficientCalculatorType.Stab, CoefficientCalculatorType.PhysicalHybrid },

            ["티치엘"] = new[] { CoefficientCalculatorType.MagicAttack, CoefficientCalculatorType.MagicDefense, CoefficientCalculatorType.PhysicalHybrid },
            ["클로에"] = new[] { CoefficientCalculatorType.MagicAttack },
            ["아나이스"] = new[] { CoefficientCalculatorType.MagicAttack, CoefficientCalculatorType.MagicDefense },
            ["벤야"] = new[] { CoefficientCalculatorType.Hack, CoefficientCalculatorType.MagicDefense },

            ["리체"] = new[] { CoefficientCalculatorType.Hack },

            ["밀라"] = new[] { CoefficientCalculatorType.Hack, CoefficientCalculatorType.PhysicalHybrid },
            ["이스핀"] = new[] { CoefficientCalculatorType.Stab, CoefficientCalculatorType.Hack, CoefficientCalculatorType.PhysicalHybrid },
            ["녹턴"] = new[] { CoefficientCalculatorType.Stab },

            ["조슈아"] = new[] { CoefficientCalculatorType.Stab, CoefficientCalculatorType.MagicAttack },
            ["란지에"] = new[] { CoefficientCalculatorType.Stab, CoefficientCalculatorType.MagicAttack },
            ["로아미니"] = new[] { CoefficientCalculatorType.MagicAttack },
            ["예프넨"] = new[] { CoefficientCalculatorType.Hack },
            ["이솔렛"] = new[] { CoefficientCalculatorType.Hack, CoefficientCalculatorType.MagicDefense },
        };

        private record SlotFilterConfig(string WeaponKeyword, string[] WristKeywords, string[] ArmorKeywords, string ArtifactKeyword);

        private static readonly Dictionary<(string Character, CoefficientCalculatorType Type), SlotFilterConfig> CharacterTypeSlotMap = new()
        {
            // 나야트레이
            [("나야트레이", CoefficientCalculatorType.Stab)] = new("단검", new[] { "리스트" }, new[] { "아머", "슈츠" }, "찌르기"),
            [("나야트레이", CoefficientCalculatorType.PhysicalHybrid)] = new("단도", new[] { "리스트" }, new[] { "아머", "슈츠" }, "물리복합"),
            [("나야트레이", CoefficientCalculatorType.Hack)] = new("도끼", new[] { "리스트" }, new[] { "아머", "슈츠" }, "베기"),
            // 루시안
            [("루시안", CoefficientCalculatorType.Stab)] = new("세검", new[] { "리스트" }, new[] { "메일", "아머" }, "찌르기"),
            [("루시안", CoefficientCalculatorType.PhysicalHybrid)] = new("장검", new[] { "리스트" }, new[] { "메일", "아머" }, "물리복합"),
            [("루시안", CoefficientCalculatorType.Hack)] = new("평도", new[] { "리스트" }, new[] { "메일", "아머" }, "베기"),
            // 이자크
            [("이자크", CoefficientCalculatorType.Stab)] = new("클로", new[] { "리스트" }, new[] { "메일", "아머", "슈츠" }, "찌르기"),
            [("이자크", CoefficientCalculatorType.Hack)] = new("카라", new[] { "리스트" }, new[] { "메일", "아머", "슈츠" }, "베기"),
            // 막시민
            [("막시민", CoefficientCalculatorType.MagicHack)] = new("대검", new[] { "리스트" }, new[] { "마법갑옷", "메일", "아머" }, "마법베기"),
            [("막시민", CoefficientCalculatorType.PhysicalHybrid)] = new("태도", new[] { "리스트" }, new[] { "마법갑옷", "메일", "아머" }, "물리복합"),
            [("막시민", CoefficientCalculatorType.Hack)] = new("평도", new[] { "리스트" }, new[] { "마법갑옷", "메일", "아머" }, "베기"),
            // 보리스
            [("보리스", CoefficientCalculatorType.MagicHack)] = new("대검", new[] { "리스트" }, new[] { "마법갑옷", "메일", "아머" }, "마법베기"),
            [("보리스", CoefficientCalculatorType.PhysicalHybrid)] = new("태도", new[] { "리스트" }, new[] { "마법갑옷", "메일", "아머" }, "물리복합"),
            [("보리스", CoefficientCalculatorType.Hack)] = new("평도", new[] { "리스트" }, new[] { "마법갑옷", "메일", "아머" }, "베기"),
            // 시벨린
            [("시벨린", CoefficientCalculatorType.Stab)] = new("창", new[] { "리스트" }, new[] { "메일", "아머" }, "찌르기"),
            [("시벨린", CoefficientCalculatorType.PhysicalHybrid)] = new("봉", new[] { "리스트" }, new[] { "메일", "아머" }, "물리복합"),
            // 티치엘
            [("티치엘", CoefficientCalculatorType.MagicAttack)] = new("스태프", new[] { "암릿" }, new[] { "로브" }, "마법공격"),
            [("티치엘", CoefficientCalculatorType.MagicDefense)] = new("로드", new[] { "암릿" }, new[] { "로브" }, "신성"),
            [("티치엘", CoefficientCalculatorType.PhysicalHybrid)] = new("메이스", new[] { "리스트" }, new[] { "아머", "로브" }, "물리복합"),
            // 클로에
            [("클로에", CoefficientCalculatorType.MagicAttack)] = new("스태프", new[] { "암릿" }, new[] { "로브" }, "마법공격"),
            // 아나이스
            [("아나이스", CoefficientCalculatorType.MagicAttack)] = new("셉터", new[] { "암릿" }, new[] { "로브" }, "마법공격"),
            [("아나이스", CoefficientCalculatorType.MagicDefense)] = new("핸드벨", new[] { "암릿" }, new[] { "로브" }, "신성"),
            // 벤야
            [("벤야", CoefficientCalculatorType.Hack)] = new("사이드", new[] { "리스트" }, new[] { "메일", "아머", "슈츠" }, "베기"),
            [("벤야", CoefficientCalculatorType.MagicDefense)] = new("해머", new[] { "수정구" }, new[] { "메일", "아머", "슈츠" }, "신성"),
            // 리체
            [("리체", CoefficientCalculatorType.Hack)] = new("아밍소드", new[] { "리스트" }, new[] { "메일", "아머" }, "베기"),
            // 밀라
            [("밀라", CoefficientCalculatorType.Hack)] = new("채찍", new[] { "리스트" }, new[] { "아머", "슈츠" }, "베기"),
            [("밀라", CoefficientCalculatorType.PhysicalHybrid)] = new("플레일", new[] { "리스트" }, new[] { "아머", "슈츠" }, "물리복합"),
            // 이스핀
            [("이스핀", CoefficientCalculatorType.Stab)] = new("세검", new[] { "리스트" }, new[] { "메일", "아머" }, "찌르기"),
            [("이스핀", CoefficientCalculatorType.PhysicalHybrid)] = new("장검", new[] { "리스트" }, new[] { "메일", "아머" }, "물리복합"),
            [("이스핀", CoefficientCalculatorType.Hack)] = new("평도", new[] { "리스트" }, new[] { "메일", "아머" }, "베기"),
            // 녹턴
            [("녹턴", CoefficientCalculatorType.Stab)] = new("핸드런처", new[] { "리스트" }, new[] { "아머", "마법갑옷" }, "찌르기"),
            // 조슈아
            [("조슈아", CoefficientCalculatorType.Stab)] = new("스몰소드", new[] { "리스트" }, new[] { "아머", "마법갑옷" }, "찌르기"),
            [("조슈아", CoefficientCalculatorType.MagicAttack)] = new("완드", new[] { "스펠북" }, new[] { "아머", "마법갑옷" }, "마법공격"),
            // 란지에
            [("란지에", CoefficientCalculatorType.Stab)] = new("물리총", new[] { "물리 탄창" }, new[] { "아머", "마법갑옷" }, "찌르기"),
            [("란지에", CoefficientCalculatorType.MagicAttack)] = new("마법총", new[] { "마법 탄창" }, new[] { "아머", "마법갑옷" }, "마법공격"),
            // 로아미니
            [("로아미니", CoefficientCalculatorType.MagicAttack)] = new("토템", new[] { "암릿" }, new[] { "로브" }, "마법공격"),
            // 예프넨
            [("예프넨", CoefficientCalculatorType.Hack)] = new("소드셰이프", new[] { "리스트" }, new[] { "마법갑옷", "메일", "아머" }, "베기"),
            // 이솔렛
            [("이솔렛", CoefficientCalculatorType.Hack)] = new("물리검", new[] { "물리검" }, new[] { "메일", "마법갑옷" }, "베기"),
            [("이솔렛", CoefficientCalculatorType.MagicDefense)] = new("마법검", new[] { "마법검" }, new[] { "메일", "마법갑옷" }, "신성"),
        };

        private readonly EquipmentService _equipmentService = new();
        private readonly ObservableCollection<CalculatorSlotRow> _slotRows = new();
        private List<EquipmentModel> _allEquipments = new();
        private bool _isEquipmentLoaded;

        private IReadOnlyList<CoefficientCalculatorType> _selectedCharacterTypes = Array.Empty<CoefficientCalculatorType>();
        private string _selectedCharacterName = string.Empty;
        private ComboBox? _typeComboBox;
        private DataGrid? _slotGrid;
        private TextBlock? _summaryPrimaryBaseLabel;
        private TextBlock? _summaryPrimaryBaseValue;
        private TextBlock? _summaryPrimaryEnchantLabel;
        private TextBlock? _summaryPrimaryEnchantValue;
        private TextBlock? _summarySecondaryLabel;
        private TextBlock? _summarySecondaryValue;
        private TextBlock? _summarySecondaryEnchantLabel;
        private TextBlock? _summarySecondaryEnchantValue;
        private TextBlock? _summaryTotalValue;
        private DataGridTextColumn? _primaryValueColumn;
        private DataGridTextColumn? _primaryEnchantColumn;
        private DataGridTextColumn? _secondaryValueColumn;
        private DataGridTextColumn? _secondaryEnchantColumn;
        private TextBlock? _rightPrimaryHeader;
        private TextBlock? _rightSecondaryHeader;
        private CheckBox? _avatarMainEnhanceCheckBox;
        private CheckBox? _avatarSubEnhanceCheckBox;
        private TextBlock? _contentSummaryHeader;
        private TextBlock? _contentSummaryValue;
        private readonly Dictionary<string, TextBlock> _contentStatusLabels = new();
        private readonly ObservableCollection<CalculatorSlotRow> _accessoryRows = new();
        private CoefficientSaveData _saveData = CoefficientDataService.Load();
        private string _lastSaveKey = string.Empty;

        public CoefficientCalculatorView()
        {
            InitializeComponent();
            foreach (string slot in AccessorySlotNames)
            {
                var row = new CalculatorSlotRow(slot);
                row.PropertyChanged += RowPropertyChanged;
                _accessoryRows.Add(row);
            }
            BuildCalculatorDetailLayout();
            InitializeCharacters();
            InitializeCalculatorTable();
            Loaded += OnLoaded;
            Unloaded += (_, _) => SaveCurrentState();
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await EnsureEquipmentLoadedAsync();
        }

        private void InitializeCharacters()
        {
            var charNames = new[]
            {
                "나야트레이", "녹턴", "란지에", "로아미니", "루시안", "리체",
                "막시민", "밀라", "벤야", "보리스", "시벨린", "아나이스",
                "예프넨", "이솔렛", "이자크", "이스핀", "조슈아", "클로에", "티치엘"
            };

            CharacterListControl.ItemsSource = charNames
                .Select(name => new CharacterCalculatorItem
                {
                    Name = name,
                    CalculatorTypes = ResolveCalculatorTypes(name)
                })
                .ToList();
        }

        private void InitializeCalculatorTable()
        {
            if (_slotGrid == null) return;

            _slotGrid.ItemsSource = _slotRows;
            foreach (string slot in MainSlotNames)
            {
                var row = new CalculatorSlotRow(slot);
                row.PropertyChanged += RowPropertyChanged;
                _slotRows.Add(row);
            }

        }

        private void RowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not CalculatorSlotRow row) return;

            if (e.PropertyName == nameof(CalculatorSlotRow.SelectedEquipmentName))
            {
                ApplyEquipmentToRow(row);
                row.RecalculateCoefficient();
            }

            if (e.PropertyName is nameof(CalculatorSlotRow.Coefficient) or nameof(CalculatorSlotRow.AttackValue) or nameof(CalculatorSlotRow.AttackEnchant) or nameof(CalculatorSlotRow.DefenseValue) or nameof(CalculatorSlotRow.DefenseEnchant) or nameof(CalculatorSlotRow.PrimaryStatValue) or nameof(CalculatorSlotRow.SecondaryStatValue))
            {
                RecalculateTotalCoefficient();
            }
        }

        private async void Character_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string charName)
                return;

            SaveCurrentState();

            await EnsureEquipmentLoadedAsync();

            _selectedCharacterTypes = ResolveCalculatorTypes(charName);
            _selectedCharacterName = charName;
            SelectedCharName.Text = charName;

            if (_typeComboBox == null)
                return;

            _typeComboBox.ItemsSource = _selectedCharacterTypes
                .Select(x => new CalculatorTypeOption(x, GetCalculatorTypeDisplayName(x)))
                .ToList();
            _typeComboBox.SelectedIndex = 0;

            RefreshAllRows();

            CharacterSelectView.Visibility = Visibility.Collapsed;
            CalculatorDetailView.Visibility = Visibility.Visible;
        }

        private async System.Threading.Tasks.Task EnsureEquipmentLoadedAsync()
        {
            if (_isEquipmentLoaded && _allEquipments.Count > 0)
                return;

            _allEquipments = await _equipmentService.GetEquipmentsAsync();

            if (_allEquipments.Count == 0)
            {
                bool refreshed = await _equipmentService.ForceRefreshAsync();
                if (refreshed)
                {
                    _allEquipments = await _equipmentService.GetEquipmentsAsync();
                }
            }

            _isEquipmentLoaded = _allEquipments.Count > 0;

            if (CalculatorDetailView.Visibility == Visibility.Visible)
            {
                RefreshAllRows();
            }
        }

        private void BackToCharSelect_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentState();
            CalculatorDetailView.Visibility = Visibility.Collapsed;
            CharacterSelectView.Visibility = Visibility.Visible;
        }

        private static ControlTemplate CreateDarkComboBoxTemplate()
        {
            var tbTemplate = new ControlTemplate(typeof(ToggleButton));
            var tbBorder = new FrameworkElementFactory(typeof(Border));
            tbBorder.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            tbTemplate.VisualTree = tbBorder;

            var wrapGrid = new FrameworkElementFactory(typeof(Grid));

            var mainBorder = new FrameworkElementFactory(typeof(Border), "MainBorder");
            mainBorder.SetBinding(Border.BackgroundProperty,
                new Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            mainBorder.SetBinding(Border.BorderBrushProperty,
                new Binding("BorderBrush") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            mainBorder.SetBinding(Border.BorderThicknessProperty,
                new Binding("BorderThickness") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            var innerGrid = new FrameworkElementFactory(typeof(Grid));

            var dockPanel = new FrameworkElementFactory(typeof(DockPanel));
            dockPanel.SetValue(DockPanel.LastChildFillProperty, true);

            var arrowBlock = new FrameworkElementFactory(typeof(TextBlock));
            arrowBlock.SetValue(DockPanel.DockProperty, Dock.Right);
            arrowBlock.SetValue(TextBlock.TextProperty, "▾");
            arrowBlock.SetValue(TextBlock.ForegroundProperty,
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x94, 0x9E)));
            arrowBlock.SetValue(TextBlock.FontSizeProperty, 10.0);
            arrowBlock.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrowBlock.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 0, 6, 0));
            arrowBlock.SetValue(UIElement.IsHitTestVisibleProperty, false);

            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetBinding(ContentPresenter.ContentProperty,
                new Binding("SelectionBoxItem") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            cp.SetBinding(ContentPresenter.ContentTemplateProperty,
                new Binding("SelectionBoxItemTemplate") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetBinding(FrameworkElement.MarginProperty,
                new Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            cp.SetValue(UIElement.IsHitTestVisibleProperty, false);

            dockPanel.AppendChild(arrowBlock);
            dockPanel.AppendChild(cp);

            var toggleButton = new FrameworkElementFactory(typeof(ToggleButton), "ToggleButton");
            toggleButton.SetValue(Control.TemplateProperty, tbTemplate);
            toggleButton.SetValue(Control.FocusableProperty, false);
            toggleButton.SetBinding(ToggleButton.IsCheckedProperty,
                new Binding("IsDropDownOpen") { Mode = BindingMode.TwoWay, RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            innerGrid.AppendChild(dockPanel);
            innerGrid.AppendChild(toggleButton);
            mainBorder.AppendChild(innerGrid);
            wrapGrid.AppendChild(mainBorder);

            var popup = new FrameworkElementFactory(typeof(Popup), "PART_Popup");
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            popup.SetValue(Popup.PopupAnimationProperty, PopupAnimation.Slide);
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            popup.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);
            popup.SetBinding(Popup.IsOpenProperty,
                new Binding("IsDropDownOpen") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            popup.SetBinding(Popup.PlacementTargetProperty,
                new Binding() { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            var dropBorder = new FrameworkElementFactory(typeof(Border));
            dropBorder.SetValue(Border.BackgroundProperty,
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x1B, 0x22)));
            dropBorder.SetValue(Border.BorderBrushProperty,
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D)));
            dropBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            dropBorder.SetBinding(FrameworkElement.MinWidthProperty,
                new Binding("ActualWidth") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            var dropSv = new FrameworkElementFactory(typeof(ScrollViewer));
            dropSv.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            dropSv.SetBinding(FrameworkElement.MaxHeightProperty,
                new Binding("MaxDropDownHeight") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });

            var ip = new FrameworkElementFactory(typeof(ItemsPresenter), "ItemsPresenter");
            dropSv.AppendChild(ip);
            dropBorder.AppendChild(dropSv);
            popup.AppendChild(dropBorder);
            wrapGrid.AppendChild(popup);

            var template = new ControlTemplate(typeof(ComboBox));
            template.VisualTree = wrapGrid;

            var hoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty,
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0xA6, 0xFF)), "MainBorder"));
            template.Triggers.Add(hoverTrigger);

            return template;
        }

        private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.RemovedItems.Count > 0)
            {
                SaveCurrentState();
            }
            RefreshAllRows();
        }

        private void SlotGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
        {
        }

        private void BuildCalculatorDetailLayout()
        {
            CalculatorDetailView.Children.Clear();
            CalculatorDetailView.RowDefinitions.Clear();

            CalculatorDetailView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var contentGrid = new Grid();
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            scrollViewer.Content = contentGrid;

            var bgDarkest = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0D, 0x11, 0x17));
            var bgRow = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x1B, 0x22));
            var bgRowAlt = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x21, 0x28));
            var borderDark = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D));
            var headerFg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x94, 0x9E));
            var accentBlue = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0xA6, 0xFF));
            var goldenBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x26, 0x4F, 0x78));
            var darkHeaderFg = System.Windows.Media.Brushes.White;
            var hoverBorder = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0xA6, 0xFF));
            var selectedRowBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1C, 0x2D, 0x44));
            var comboDropdownBg = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x16, 0x1B, 0x22));
            var comboHighlight = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x6F, 0xEB));

            var headerGrid = new Grid { Margin = new Thickness(10, 4, 10, 4) };
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var backButton = new Button { Content = "← 뒤로", Margin = new Thickness(0, 0, 10, 0), Cursor = System.Windows.Input.Cursors.Hand };
            backButton.Click += BackToCharSelect_Click;

            var backBtnTemplate = new ControlTemplate(typeof(Button));
            var backBtnBorder = new FrameworkElementFactory(typeof(Border), "BackBorder");
            backBtnBorder.SetValue(Border.BackgroundProperty, bgRow);
            backBtnBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            backBtnBorder.SetValue(Border.BorderBrushProperty, borderDark);
            backBtnBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            backBtnBorder.SetValue(Border.PaddingProperty, new Thickness(10, 3, 10, 3));
            var backBtnContent = new FrameworkElementFactory(typeof(ContentPresenter));
            backBtnContent.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            backBtnContent.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            backBtnBorder.AppendChild(backBtnContent);
            backBtnTemplate.VisualTree = backBtnBorder;
            var backBtnHoverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            backBtnHoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, bgRowAlt, "BackBorder"));
            backBtnHoverTrigger.Setters.Add(new Setter(Border.BorderBrushProperty, hoverBorder, "BackBorder"));
            backBtnTemplate.Triggers.Add(backBtnHoverTrigger);
            var backButtonStyle = new Style(typeof(Button));
            backButtonStyle.Setters.Add(new Setter(Control.TemplateProperty, backBtnTemplate));
            backButtonStyle.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White));
            backButtonStyle.Setters.Add(new Setter(Control.FontSizeProperty, 11.0));
            backButtonStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            backButton.Style = backButtonStyle;

            SelectedCharName.Visibility = Visibility.Visible;
            SelectedCharName.Foreground = accentBlue;
            SelectedCharName.FontSize = 15;
            SelectedCharName.FontWeight = FontWeights.SemiBold;
            SelectedCharName.VerticalAlignment = VerticalAlignment.Center;
            SelectedCharName.TextAlignment = TextAlignment.Center;

            var comboBoxItemStyle = new Style(typeof(ComboBoxItem));
            comboBoxItemStyle.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White));
            comboBoxItemStyle.Setters.Add(new Setter(Control.BackgroundProperty, comboDropdownBg));
            comboBoxItemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
            comboBoxItemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            comboBoxItemStyle.Triggers.Add(new Trigger
            {
                Property = ComboBoxItem.IsHighlightedProperty,
                Value = true,
                Setters =
                {
                    new Setter(Control.BackgroundProperty, comboHighlight),
                    new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White)
                }
            });
            comboBoxItemStyle.Triggers.Add(new Trigger
            {
                Property = ComboBoxItem.IsSelectedProperty,
                Value = true,
                Setters =
                {
                    new Setter(Control.BackgroundProperty, comboHighlight),
                    new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White)
                }
            });

            var comboBoxStyle = new Style(typeof(ComboBox));
            comboBoxStyle.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White));
            comboBoxStyle.Setters.Add(new Setter(Control.BackgroundProperty, bgDarkest));
            comboBoxStyle.Setters.Add(new Setter(Control.BorderBrushProperty, borderDark));
            comboBoxStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            comboBoxStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));
            comboBoxStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            comboBoxStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            comboBoxStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            comboBoxStyle.Setters.Add(new Setter(ComboBox.ItemContainerStyleProperty, comboBoxItemStyle));
            comboBoxStyle.Setters.Add(new Setter(Control.TemplateProperty, CreateDarkComboBoxTemplate()));
            comboBoxStyle.Triggers.Add(new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true,
                Setters =
                {
                    new Setter(Control.BorderBrushProperty, hoverBorder)
                }
            });

            var gridComboDisplayStyle = new Style(typeof(ComboBox), comboBoxStyle);
            gridComboDisplayStyle.Setters.Add(new Setter(ItemsControl.ItemsSourceProperty, new Binding(nameof(CalculatorSlotRow.EquipmentCandidates))));
            gridComboDisplayStyle.Setters.Add(new Setter(ComboBox.SelectedItemProperty, new Binding(nameof(CalculatorSlotRow.SelectedEquipmentName)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }));
            gridComboDisplayStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            gridComboDisplayStyle.Setters.Add(new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
            gridComboDisplayStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 0, 4, 0)));

            var gridComboEditingStyle = new Style(typeof(ComboBox), comboBoxStyle);
            gridComboEditingStyle.Setters.Add(new Setter(ItemsControl.ItemsSourceProperty, new Binding(nameof(CalculatorSlotRow.EquipmentCandidates))));
            gridComboEditingStyle.Setters.Add(new Setter(ComboBox.SelectedItemProperty, new Binding(nameof(CalculatorSlotRow.SelectedEquipmentName)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }));
            gridComboEditingStyle.Setters.Add(new Setter(ComboBox.MaxDropDownHeightProperty, 360.0));
            gridComboEditingStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(4, 0, 4, 0)));

            var typeItemTemplate = new DataTemplate();
            var typeItemTextFactory = new FrameworkElementFactory(typeof(TextBlock));
            typeItemTextFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(CalculatorTypeOption.DisplayName)));
            typeItemTextFactory.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.White);
            typeItemTextFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
            typeItemTextFactory.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            typeItemTemplate.VisualTree = typeItemTextFactory;

            _typeComboBox = new ComboBox { Width = 140 };
            _typeComboBox.ItemTemplate = typeItemTemplate;
            _typeComboBox.SelectionChanged += TypeComboBox_SelectionChanged;
            _typeComboBox.Style = comboBoxStyle;

            Grid.SetColumn(backButton, 0);
            Grid.SetColumn(SelectedCharName, 1);
            Grid.SetColumn(_typeComboBox, 2);
            headerGrid.Children.Add(backButton);
            headerGrid.Children.Add(SelectedCharName);
            headerGrid.Children.Add(_typeComboBox);

            _slotGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                CanUserSortColumns = false,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                HorizontalGridLinesBrush = borderDark,
                RowHeight = 24,
                Background = System.Windows.Media.Brushes.Transparent,
                RowBackground = bgRow,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                SelectionMode = DataGridSelectionMode.Single,
                SelectionUnit = DataGridSelectionUnit.FullRow,
                Margin = new Thickness(0)
            };
            _slotGrid.AlternatingRowBackground = bgRowAlt;
            _slotGrid.AlternationCount = 2;
            ScrollViewer.SetVerticalScrollBarVisibility(_slotGrid, ScrollBarVisibility.Disabled);

            var colHeaderStyle = new Style(typeof(DataGridColumnHeader));
            colHeaderStyle.Setters.Add(new Setter(Control.BackgroundProperty, bgDarkest));
            colHeaderStyle.Setters.Add(new Setter(Control.ForegroundProperty, headerFg));
            colHeaderStyle.Setters.Add(new Setter(Control.FontSizeProperty, 11.0));
            colHeaderStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
            colHeaderStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
            colHeaderStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            colHeaderStyle.Setters.Add(new Setter(Control.BorderBrushProperty, borderDark));
            colHeaderStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            _slotGrid.ColumnHeaderStyle = colHeaderStyle;

            var rowStyle = new Style(typeof(DataGridRow));
            rowStyle.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White));
            rowStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            rowStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            rowStyle.Triggers.Add(new Trigger
            {
                Property = DataGridRow.IsSelectedProperty,
                Value = true,
                Setters =
                {
                    new Setter(Control.BackgroundProperty, selectedRowBg),
                    new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White)
                }
            });
            rowStyle.Triggers.Add(new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true,
                Setters =
                {
                    new Setter(Control.BackgroundProperty, bgRowAlt)
                }
            });
            _slotGrid.RowStyle = rowStyle;

            var cellStyle = new Style(typeof(DataGridCell));
            cellStyle.Setters.Add(new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent));
            cellStyle.Setters.Add(new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White));
            cellStyle.Setters.Add(new Setter(Control.BorderBrushProperty, System.Windows.Media.Brushes.Transparent));
            cellStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
            cellStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            cellStyle.Triggers.Add(new Trigger
            {
                Property = DataGridCell.IsSelectedProperty,
                Value = true,
                Setters =
                {
                    new Setter(Control.BackgroundProperty, System.Windows.Media.Brushes.Transparent),
                    new Setter(Control.ForegroundProperty, System.Windows.Media.Brushes.White)
                }
            });
            _slotGrid.CellStyle = cellStyle;

            var textDisplayStyle = new Style(typeof(TextBlock));
            textDisplayStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.White));
            textDisplayStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            textDisplayStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));

            var textEditStyle = new Style(typeof(TextBox));
            textEditStyle.Setters.Add(new Setter(TextBox.ForegroundProperty, System.Windows.Media.Brushes.White));
            textEditStyle.Setters.Add(new Setter(TextBox.BackgroundProperty, bgDarkest));
            textEditStyle.Setters.Add(new Setter(TextBox.BorderBrushProperty, borderDark));
            textEditStyle.Setters.Add(new Setter(TextBox.BorderThicknessProperty, new Thickness(1)));
            textEditStyle.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Center));
            textEditStyle.Setters.Add(new Setter(TextBox.CaretBrushProperty, System.Windows.Media.Brushes.White));
            textEditStyle.Setters.Add(new Setter(TextBox.PaddingProperty, new Thickness(4, 2, 4, 2)));

            var enchantDisplayStyle = new Style(typeof(TextBlock), textDisplayStyle);
            enchantDisplayStyle.Triggers.Add(new DataTrigger
            {
                Binding = new Binding(nameof(CalculatorSlotRow.CanEditEnchant)),
                Value = false,
                Setters = { new Setter(TextBlock.ForegroundProperty, headerFg) }
            });

            var coreExcludeDisplayStyle = new Style(typeof(TextBlock), textDisplayStyle);
            coreExcludeDisplayStyle.Triggers.Add(new DataTrigger
            {
                Binding = new Binding(nameof(CalculatorSlotRow.IsCoreSlot)),
                Value = true,
                Setters = { new Setter(TextBlock.ForegroundProperty, headerFg) }
            });

            var coreOnlyDisplayStyle = new Style(typeof(TextBlock), textDisplayStyle);
            coreOnlyDisplayStyle.Triggers.Add(new DataTrigger
            {
                Binding = new Binding(nameof(CalculatorSlotRow.IsCoreSlot)),
                Value = false,
                Setters = { new Setter(TextBlock.ForegroundProperty, headerFg) }
            });

            _slotGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "부위",
                Binding = new Binding(nameof(CalculatorSlotRow.SlotName)),
                Width = 90,
                IsReadOnly = true,
                ElementStyle = textDisplayStyle
            });

            var equipmentTemplateColumn = new DataGridTemplateColumn { Header = "아이템", Width = 170 };
            var comboDisplayFactory = new FrameworkElementFactory(typeof(ComboBox));
            comboDisplayFactory.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(CalculatorSlotRow.EquipmentCandidates)));
            comboDisplayFactory.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(CalculatorSlotRow.SelectedEquipmentName)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            comboDisplayFactory.SetValue(FrameworkElement.StyleProperty, gridComboDisplayStyle);
            comboDisplayFactory.SetValue(ComboBox.MaxDropDownHeightProperty, 360.0);

            var comboEditFactory = new FrameworkElementFactory(typeof(ComboBox));
            comboEditFactory.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(CalculatorSlotRow.EquipmentCandidates)));
            comboEditFactory.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(CalculatorSlotRow.SelectedEquipmentName)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            comboEditFactory.SetValue(FrameworkElement.StyleProperty, gridComboEditingStyle);
            comboEditFactory.SetValue(ComboBox.MaxDropDownHeightProperty, 360.0);

            equipmentTemplateColumn.CellTemplate = new DataTemplate { VisualTree = comboDisplayFactory };
            equipmentTemplateColumn.CellEditingTemplate = new DataTemplate { VisualTree = comboEditFactory };
            _slotGrid.Columns.Add(equipmentTemplateColumn);

            var zeroFallback = new DoubleZeroFallbackConverter();

            _primaryValueColumn = new DataGridTextColumn
            {
                Header = "공격력",
                Binding = new Binding(nameof(CalculatorSlotRow.AttackValue)) { Converter = zeroFallback, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 80,
                ElementStyle = textDisplayStyle,
                EditingElementStyle = textEditStyle
            };
            _slotGrid.Columns.Add(_primaryValueColumn);
            _primaryEnchantColumn = new DataGridTextColumn
            {
                Header = "강화 공격력",
                Binding = new Binding(nameof(CalculatorSlotRow.AttackEnchant)) { Converter = zeroFallback, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 80,
                ElementStyle = enchantDisplayStyle,
                EditingElementStyle = textEditStyle
            };
            _slotGrid.Columns.Add(_primaryEnchantColumn);
            _secondaryValueColumn = new DataGridTextColumn
            {
                Header = "보조공격력",
                Binding = new Binding(nameof(CalculatorSlotRow.DefenseValue)) { Converter = zeroFallback, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 80,
                ElementStyle = coreExcludeDisplayStyle,
                EditingElementStyle = textEditStyle
            };
            _slotGrid.Columns.Add(_secondaryValueColumn);
            _secondaryEnchantColumn = new DataGridTextColumn
            {
                Header = "강화 보조공격력",
                Binding = new Binding(nameof(CalculatorSlotRow.DefenseEnchant)) { Converter = zeroFallback, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                Width = 80,
                ElementStyle = enchantDisplayStyle,
                EditingElementStyle = textEditStyle
            };
            _slotGrid.Columns.Add(_secondaryEnchantColumn);
            _slotGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "계수",
                Binding = new Binding(nameof(CalculatorSlotRow.Coefficient)) { Converter = zeroFallback },
                Width = 120,
                IsReadOnly = true,
                ElementStyle = textDisplayStyle
            });

            var rightTableGrid = new Grid();
            rightTableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
            rightTableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rightTableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rightTableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            foreach (int h in new[] { 26, 26, 26, 26, 26, 26, 26, 26 })
                rightTableGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(h) });

            Border RtCell(int row, int col, System.Windows.Media.Brush bg, Thickness brd, int colSpan = 1)
            {
                var b = new Border { Background = bg, BorderBrush = borderDark, BorderThickness = brd };
                Grid.SetRow(b, row); Grid.SetColumn(b, col);
                if (colSpan > 1) Grid.SetColumnSpan(b, colSpan);
                rightTableGrid.Children.Add(b);
                return b;
            }

            TextBlock RtHdr(string text) => new()
            {
                Text = text,
                Foreground = darkHeaderFg,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };

            TextBlock RtLbl(string text, double fs = 11) => new()
            {
                Text = text,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = fs,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            TextBox RtInput() => new()
            {
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                CaretBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Cyan)
            };

            TextBlock RtVal() => new()
            {
                Text = "0",
                Foreground = accentBlue,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var cBrd = new Thickness(0, 0, 1, 1);
            var rBrd = new Thickness(0, 0, 0, 1);

            RtCell(0, 0, bgDarkest, cBrd);
            RtCell(0, 1, bgDarkest, cBrd).Child = RtHdr("주스탯");
            RtCell(0, 2, bgDarkest, cBrd).Child = RtHdr("부스탯");
            RtCell(0, 3, bgDarkest, rBrd).Child = RtHdr("계수");

            RtCell(1, 0, bgRowAlt, cBrd).Child = RtLbl("스탯");
            var statMRInput = RtInput();
            statMRInput.SetBinding(TextBox.TextProperty, new Binding(nameof(CalculatorSlotRow.PrimaryStatValue)) { Source = _accessoryRows[0], Converter = zeroFallback, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            RtCell(1, 1, System.Windows.Media.Brushes.Transparent, cBrd).Child = statMRInput;
            var statINTInput = RtInput();
            statINTInput.SetBinding(TextBox.TextProperty, new Binding(nameof(CalculatorSlotRow.SecondaryStatValue)) { Source = _accessoryRows[0], Converter = zeroFallback, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            RtCell(1, 2, System.Windows.Media.Brushes.Transparent, cBrd).Child = statINTInput;
            var statCoeffVal = RtVal();
            statCoeffVal.SetBinding(TextBlock.TextProperty, new Binding(nameof(CalculatorSlotRow.Coefficient)) { Source = _accessoryRows[0], Converter = zeroFallback });
            RtCell(1, 3, System.Windows.Media.Brushes.Transparent, rBrd).Child = statCoeffVal;

            RtCell(2, 0, bgDarkest, cBrd);
            _rightPrimaryHeader = RtHdr("공격력");
            RtCell(2, 1, bgDarkest, cBrd).Child = _rightPrimaryHeader;
            _rightSecondaryHeader = RtHdr("보조공격력");
            RtCell(2, 2, bgDarkest, cBrd).Child = _rightSecondaryHeader;
            RtCell(2, 3, bgDarkest, rBrd);

            (string name, int idx)[] midSlots = { ("아바타", 1), ("커프", 2), ("렐릭", 3) };
            foreach (var (slotName, slotIdx) in midSlots)
            {
                int gr = 2 + slotIdx;
                var accRow = _accessoryRows[slotIdx];
                RtCell(gr, 0, bgRowAlt, cBrd).Child = RtLbl(slotName, slotName.Length > 2 ? 10 : 11);
                var col1 = RtInput();
                col1.SetBinding(TextBox.TextProperty, new Binding(nameof(CalculatorSlotRow.AccessoryValue1)) { Source = accRow, Converter = zeroFallback, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                RtCell(gr, 1, System.Windows.Media.Brushes.Transparent, cBrd).Child = col1;
                var col2 = RtInput();
                col2.SetBinding(TextBox.TextProperty, new Binding(nameof(CalculatorSlotRow.AccessoryValue2)) { Source = accRow, Converter = zeroFallback, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
                RtCell(gr, 2, System.Windows.Media.Brushes.Transparent, cBrd).Child = col2;
                var cv = RtVal();
                cv.SetBinding(TextBlock.TextProperty, new Binding(nameof(CalculatorSlotRow.Coefficient)) { Source = accRow, Converter = zeroFallback });
                RtCell(gr, 3, System.Windows.Media.Brushes.Transparent, rBrd).Child = cv;
            }

            // Row 6: 칭호
            RtCell(6, 0, bgRowAlt, cBrd).Child = RtLbl("칭호");
            var titleInput = RtInput();
            titleInput.SetBinding(TextBox.TextProperty, new Binding(nameof(CalculatorSlotRow.TitleValue)) { Source = _accessoryRows[4], Converter = zeroFallback, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            RtCell(6, 1, System.Windows.Media.Brushes.Transparent, cBrd).Child = titleInput;
            RtCell(6, 2, bgDarkest, cBrd);
            var titleCoeffVal = RtVal();
            titleCoeffVal.SetBinding(TextBlock.TextProperty, new Binding(nameof(CalculatorSlotRow.Coefficient)) { Source = _accessoryRows[4], Converter = zeroFallback });
            RtCell(6, 3, System.Windows.Media.Brushes.Transparent, rBrd).Child = titleCoeffVal;

            // Row 7: 코어
            RtCell(7, 0, bgRowAlt, new Thickness(0, 0, 1, 0)).Child = RtLbl("코어");
            var coreInput = RtInput();
            coreInput.SetBinding(TextBox.TextProperty, new Binding(nameof(CalculatorSlotRow.CoreValue)) { Source = _accessoryRows[5], Converter = zeroFallback, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            RtCell(7, 1, System.Windows.Media.Brushes.Transparent, new Thickness(0, 0, 1, 0)).Child = coreInput;
            RtCell(7, 2, bgDarkest, new Thickness(0, 0, 1, 0));
            var coreCoeffVal = RtVal();
            coreCoeffVal.SetBinding(TextBlock.TextProperty, new Binding(nameof(CalculatorSlotRow.Coefficient)) { Source = _accessoryRows[5], Converter = zeroFallback });
            RtCell(7, 3, System.Windows.Media.Brushes.Transparent, new Thickness(0)).Child = coreCoeffVal;

            var rightTableBorder = new Border
            {
                Background = bgRow,
                CornerRadius = new CornerRadius(8),
                BorderBrush = borderDark,
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Child = rightTableGrid
            };

            var checkboxPanel = new StackPanel { Margin = new Thickness(8, 8, 0, 0) };

            _avatarMainEnhanceCheckBox = new CheckBox
            {
                Content = "아바타 강화(주스탯)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 4),
                MinHeight = 22
            };
            _avatarSubEnhanceCheckBox = new CheckBox
            {
                Content = "아바타 강화(부스탯)",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 6),
                MinHeight = 22
            };
            _avatarMainEnhanceCheckBox.Style = (Style)FindResource("ToggleSwitchCheckBoxStyle");
            _avatarSubEnhanceCheckBox.Style = (Style)FindResource("ToggleSwitchCheckBoxStyle");
            _avatarMainEnhanceCheckBox.Checked += AvatarEnhancementCheckChanged;
            _avatarMainEnhanceCheckBox.Unchecked += AvatarEnhancementCheckChanged;
            _avatarSubEnhanceCheckBox.Checked += AvatarEnhancementCheckChanged;
            _avatarSubEnhanceCheckBox.Unchecked += AvatarEnhancementCheckChanged;
            checkboxPanel.Children.Add(_avatarMainEnhanceCheckBox);
            checkboxPanel.Children.Add(_avatarSubEnhanceCheckBox);

            var rightPanel = new StackPanel { Margin = new Thickness(8, 28, 0, 0) };
            rightPanel.Children.Add(rightTableBorder);
            rightPanel.Children.Add(checkboxPanel);

            var summaryGrid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
            for (int sc = 0; sc < 10; sc++)
                summaryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = sc % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(55) });
            summaryGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

            Border SumCell(int c)
            {
                var b = new Border
                {
                    Background = bgDarkest,
                    BorderBrush = borderDark,
                    BorderThickness = new Thickness(0, 0, c < 9 ? 1 : 0, 0)
                };
                Grid.SetRow(b, 0); Grid.SetColumn(b, c);
                summaryGrid.Children.Add(b);
                return b;
            }

            TextBlock SumTxt(string text, bool isValue = false) => new()
            {
                Text = text,
                Foreground = isValue ? accentBlue : darkHeaderFg,
                FontWeight = FontWeights.Bold,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            _summaryPrimaryBaseLabel = SumTxt("공격력");
            SumCell(0).Child = _summaryPrimaryBaseLabel;
            _summaryPrimaryBaseValue = SumTxt("0", true);
            SumCell(1).Child = _summaryPrimaryBaseValue;
            _summaryPrimaryEnchantLabel = SumTxt("강화 공격력");
            SumCell(2).Child = _summaryPrimaryEnchantLabel;
            _summaryPrimaryEnchantValue = SumTxt("0", true);
            SumCell(3).Child = _summaryPrimaryEnchantValue;
            _summarySecondaryLabel = SumTxt("보조공격력");
            SumCell(4).Child = _summarySecondaryLabel;
            _summarySecondaryValue = SumTxt("0", true);
            SumCell(5).Child = _summarySecondaryValue;
            _summarySecondaryEnchantLabel = SumTxt("강화 보조공격력");
            SumCell(6).Child = _summarySecondaryEnchantLabel;
            _summarySecondaryEnchantValue = SumTxt("0", true);
            SumCell(7).Child = _summarySecondaryEnchantValue;
            SumCell(8).Child = SumTxt("계수");
            _summaryTotalValue = SumTxt("0", true);
            SumCell(9).Child = _summaryTotalValue;

            var summaryBorder = new Border
            {
                Background = bgRow,
                CornerRadius = new CornerRadius(4),
                BorderBrush = borderDark,
                BorderThickness = new Thickness(1),
                ClipToBounds = true,
                Child = summaryGrid
            };

            var leftPanel = new StackPanel();
            leftPanel.Children.Add(_slotGrid);
            leftPanel.Children.Add(summaryBorder);

            var bodyGrid = new Grid();
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(leftPanel, 0);
            Grid.SetColumn(rightPanel, 1);
            bodyGrid.Children.Add(leftPanel);
            bodyGrid.Children.Add(rightPanel);

            var contentTableBorder = new Border
            {
                Background = bgDarkest,
                BorderBrush = borderDark,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(10, 4, 10, 10),
                Padding = new Thickness(6)
            };
            var contentNames = new[] { "총 공격력", "최후의 결전", "아페 어려움", "이클 토벌전", "이클 6보스", "오딘 전면전", "아페 EX" };
            var contentTable = new Grid();
            for (int i = 0; i < contentNames.Length; i++)
                contentTable.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            contentTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            contentTable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int i = 0; i < contentNames.Length; i++)
            {
                var lbl = new TextBlock
                {
                    Text = contentNames[i],
                    Foreground = headerFg,
                    FontSize = 14,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(2)
                };
                Grid.SetColumn(lbl, i);
                Grid.SetRow(lbl, 0);
                contentTable.Children.Add(lbl);

                var diffLbl = new TextBlock
                {
                    Text = "-",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontSize = 10,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(2)
                };
                Grid.SetColumn(diffLbl, i);
                Grid.SetRow(diffLbl, 1);
                contentTable.Children.Add(diffLbl);

                if (i == 0)
                {
                    _contentSummaryHeader = lbl;
                    _contentSummaryValue = diffLbl;
                }
                else
                {
                    _contentStatusLabels[contentNames[i]] = diffLbl;
                }
            }
            contentTableBorder.Child = contentTable;

            Grid.SetRow(headerGrid, 0);
            Grid.SetRow(bodyGrid, 1);
            Grid.SetRow(contentTableBorder, 2);

            contentGrid.Children.Add(headerGrid);
            contentGrid.Children.Add(bodyGrid);
            contentGrid.Children.Add(contentTableBorder);

            Grid.SetRow(scrollViewer, 0);
            CalculatorDetailView.Children.Add(scrollViewer);
        }

        private void RefreshAllRows()
        {
            UpdateColumnHeaders();

            var type = GetSelectedCalculatorType();
            foreach (var row in _slotRows)
            {
                row.CurrentType = type;
                if (row.SlotName.Contains("어빌리티"))
                {
                    row.RecalculateCoefficient();
                }
                else
                {
                    row.EquipmentCandidates = BuildEquipmentCandidates(row.SlotName, type, _selectedCharacterName);
                    row.SelectedEquipmentName = row.EquipmentCandidates.FirstOrDefault() ?? "수동 입력";
                    ApplyEquipmentToRow(row);
                    row.RecalculateCoefficient();
                }
            }

            foreach (var row in _accessoryRows)
            {
                row.CurrentType = type;
                if (row.SlotName is "스탯" or "아바타" or "커프" or "칭호" or "코어" or "렐릭")
                {
                    switch (row.SlotName)
                    {
                        case "아바타":
                            row.AccessoryValue1 = 15;
                            row.AccessoryValue2 = 15;
                            break;
                        case "커프":
                            row.AccessoryValue1 = 50;
                            row.AccessoryValue2 = 50;
                            break;
                        case "렐릭":
                            row.AccessoryValue1 = 17;
                            row.AccessoryValue2 = 17;
                            break;
                        case "칭호":
                            row.TitleValue = 50;
                            break;
                        case "코어":
                            row.CoreValue = 120;
                            break;
                    }
                    row.RecalculateCoefficient();
                }
                else
                {
                    row.EquipmentCandidates = BuildEquipmentCandidates(row.SlotName, type, _selectedCharacterName);
                    row.SelectedEquipmentName = row.EquipmentCandidates.FirstOrDefault() ?? "수동 입력";
                    ApplyEquipmentToRow(row);
                    row.RecalculateCoefficient();
                }
            }

            LoadSavedState();
            RecalculateTotalCoefficient();
        }

        private void SaveCurrentState()
        {
            if (string.IsNullOrEmpty(_lastSaveKey)) return;

            var snapshots = new List<CoefficientSlotSnapshot>();
            foreach (var row in _slotRows)
            {
                snapshots.Add(new CoefficientSlotSnapshot
                {
                    SlotName = row.SlotName,
                    SelectedEquipment = row.SelectedEquipmentName,
                    AttackValue = row.AttackValue,
                    AttackEnchant = row.AttackEnchant,
                    DefenseValue = row.DefenseValue,
                    DefenseEnchant = row.DefenseEnchant,
                    PrimaryStatValue = row.PrimaryStatValue,
                    SecondaryStatValue = row.SecondaryStatValue
                });
            }
            foreach (var row in _accessoryRows)
            {
                snapshots.Add(new CoefficientSlotSnapshot
                {
                    SlotName = row.SlotName,
                    SelectedEquipment = row.SelectedEquipmentName,
                    AttackValue = row.AccessoryValue1,
                    AttackEnchant = row.AccessoryValue2,
                    DefenseValue = row.TitleValue,
                    DefenseEnchant = row.DefenseEnchant,
                    PrimaryStatValue = row.PrimaryStatValue,
                    SecondaryStatValue = row.SecondaryStatValue
                });
            }

            _saveData.Entries[_lastSaveKey] = snapshots.ToArray();
            CoefficientDataService.Save(_saveData);
        }

        private void LoadSavedState()
        {
            string key = $"{_selectedCharacterName}::{GetSelectedCalculatorType()}";
            _lastSaveKey = key;

            if (!_saveData.Entries.TryGetValue(key, out var snapshots))
                return;

            var snapshotMap = new Dictionary<string, CoefficientSlotSnapshot>();
            foreach (var s in snapshots)
                snapshotMap[s.SlotName] = s;

            foreach (var row in _slotRows)
            {
                if (!snapshotMap.TryGetValue(row.SlotName, out var snap)) continue;

                if (!string.IsNullOrEmpty(snap.SelectedEquipment) && row.EquipmentCandidates.Contains(snap.SelectedEquipment))
                {
                    row.SelectedEquipmentName = snap.SelectedEquipment;
                    ApplyEquipmentToRow(row);
                }

                row.AttackEnchant = snap.AttackEnchant;
                row.DefenseEnchant = snap.DefenseEnchant;

                if (row.SelectedEquipmentName == "수동 입력" || row.SlotName.Contains("어빌리티"))
                {
                    row.AttackValue = snap.AttackValue;
                    row.DefenseValue = snap.DefenseValue;
                }

                row.PrimaryStatValue = snap.PrimaryStatValue;
                row.SecondaryStatValue = snap.SecondaryStatValue;
                row.RecalculateCoefficient();
            }

            foreach (var row in _accessoryRows)
            {
                if (!snapshotMap.TryGetValue(row.SlotName, out var snap)) continue;
                row.AccessoryValue1 = snap.AttackValue;
                row.AccessoryValue2 = snap.AttackEnchant;
                row.TitleValue = snap.DefenseValue;
                row.DefenseEnchant = snap.DefenseEnchant;
                row.PrimaryStatValue = snap.PrimaryStatValue;
                row.SecondaryStatValue = snap.SecondaryStatValue;
                row.RecalculateCoefficient();
            }
        }

        private void UpdateColumnHeaders()
        {
            var type = GetSelectedCalculatorType();
            var (primary, secondary) = type switch
            {
                CoefficientCalculatorType.Stab => ("찌르기", "베기"),
                CoefficientCalculatorType.Hack => ("베기", "찌르기"),
                CoefficientCalculatorType.MagicAttack => ("마법공격", "마법방어"),
                CoefficientCalculatorType.MagicDefense => ("마법방어", "마법공격"),
                CoefficientCalculatorType.PhysicalHybrid => ("찌르기", "베기"),
                CoefficientCalculatorType.MagicHack => ("베기", "마법공격"),
                _ => ("공격력", "방어력")
            };

            if (_primaryValueColumn != null) _primaryValueColumn.Header = primary;
            if (_primaryEnchantColumn != null) _primaryEnchantColumn.Header = $"강화 {primary}";
            if (_secondaryValueColumn != null) _secondaryValueColumn.Header = secondary;
            if (_secondaryEnchantColumn != null) _secondaryEnchantColumn.Header = $"강화 {secondary}";

            if (_summaryPrimaryBaseLabel != null) _summaryPrimaryBaseLabel.Text = primary;
            if (_summaryPrimaryEnchantLabel != null) _summaryPrimaryEnchantLabel.Text = $"강화 {primary}";
            if (_summarySecondaryLabel != null) _summarySecondaryLabel.Text = secondary;
            if (_summarySecondaryEnchantLabel != null) _summarySecondaryEnchantLabel.Text = $"강화 {secondary}";

            if (_rightPrimaryHeader != null) _rightPrimaryHeader.Text = primary;
            if (_rightSecondaryHeader != null) _rightSecondaryHeader.Text = secondary;
            if (_contentSummaryHeader != null) _contentSummaryHeader.Text = $"총 {primary}";
        }

        private void ApplyEquipmentToRow(CalculatorSlotRow row)
        {
            if (row.SlotName is "스탯" or "아바타" or "커프" or "칭호" or "코어" or "렐릭" || row.SlotName.Contains("어빌리티"))
                return;

            if (_allEquipments.Count == 0 || string.IsNullOrWhiteSpace(row.SelectedEquipmentName) || row.SelectedEquipmentName == "수동 입력")
            {
                row.AttackValue = 0;
                row.DefenseValue = 0;
                row.PrimaryStatValue = 0;
                row.SecondaryStatValue = 0;
                return;
            }

            var item = _allEquipments.FirstOrDefault(x => string.Equals(x.Name, row.SelectedEquipmentName, StringComparison.Ordinal));
            if (item == null) return;

            var type = GetSelectedCalculatorType();
            var (primaryStat, secondaryStat) = type switch
            {
                CoefficientCalculatorType.Stab => (item.Stab.Max, item.Hack.Max),
                CoefficientCalculatorType.Hack => (item.Hack.Max, item.Stab.Max),
                CoefficientCalculatorType.MagicAttack => (item.Int.Max, item.MR.Max),
                CoefficientCalculatorType.MagicDefense => (item.MR.Max, item.Int.Max),
                CoefficientCalculatorType.PhysicalHybrid => (item.Stab.Max, item.Hack.Max),
                CoefficientCalculatorType.MagicHack => (item.Hack.Max, item.Int.Max),
                _ => (item.Stab.Max, item.Hack.Max)
            };

            row.AttackValue = primaryStat;
            row.DefenseValue = secondaryStat;
            row.PrimaryStatValue = 0;
            row.SecondaryStatValue = 0;
        }

        private void RecalculateTotalCoefficient()
        {
            var avatarRow = GetAccessoryRow("아바타");
            var cuffRow = GetAccessoryRow("커프");
            var relicRow = GetAccessoryRow("렐릭");
            var titleRow = GetAccessoryRow("칭호");
            var coreRow = GetAccessoryRow("코어");

            double avatarMainEnhanceBonus = _avatarMainEnhanceCheckBox?.IsChecked == true ? 50 : 0;
            double avatarSubEnhanceBonus = _avatarSubEnhanceCheckBox?.IsChecked == true ? 50 : 0;

            double primaryBaseSum =
                _slotRows.Sum(x => x.AttackValue)
                + (avatarRow?.AccessoryValue1 ?? 0)
                + (cuffRow?.AccessoryValue1 ?? 0)
                + (relicRow?.AccessoryValue1 ?? 0)
                + (titleRow?.TitleValue ?? 0);

            double primaryEnchantSum =
                _slotRows.Sum(x => x.AttackEnchant)
                + (coreRow?.CoreValue ?? 0)
                + avatarMainEnhanceBonus;

            double secondarySum =
                _slotRows.Sum(x => x.DefenseValue)
                + (avatarRow?.AccessoryValue2 ?? 0)
                + (cuffRow?.AccessoryValue2 ?? 0)
                + (relicRow?.AccessoryValue2 ?? 0);

            double secondaryEnchantSum =
                _slotRows.Sum(x => x.DefenseEnchant)
                + avatarSubEnhanceBonus;

            double baseTotal = _slotRows.Sum(x => x.Coefficient) + _accessoryRows.Sum(x => x.Coefficient);
            double bonusCoefficient = CalculateAvatarEnhancementBonusCoefficient(avatarMainEnhanceBonus, avatarSubEnhanceBonus);
            double total = baseTotal + bonusCoefficient;
            double totalPrimarySum = primaryBaseSum + primaryEnchantSum;

            if (_summaryPrimaryBaseValue != null) _summaryPrimaryBaseValue.Text = primaryBaseSum.ToString("F0");
            if (_summaryPrimaryEnchantValue != null) _summaryPrimaryEnchantValue.Text = primaryEnchantSum.ToString("F0");
            if (_summarySecondaryValue != null) _summarySecondaryValue.Text = secondarySum.ToString("F0");
            if (_summarySecondaryEnchantValue != null) _summarySecondaryEnchantValue.Text = secondaryEnchantSum.ToString("F0");
            if (_summaryTotalValue != null) _summaryTotalValue.Text = total.ToString("F2");
            if (_contentSummaryValue != null) _contentSummaryValue.Text = totalPrimarySum.ToString("F0");

            UpdateContentAvailability(total, secondaryEnchantSum);
        }

        private void UpdateContentAvailability(double totalCoefficient, double secondaryEnchantCoefficientInput)
        {
            SetContentStatus("최후의 결전", EvaluateByThreshold(totalCoefficient, 90000, 93000, 95000));
            SetContentStatus("아페 어려움", EvaluateByThreshold(totalCoefficient, 67500, 70000, 72500));
            SetContentStatus("이클 토벌전", EvaluateByThreshold(totalCoefficient, 67500, 70000, 72500));
            SetContentStatus("이클 6보스", EvaluateByThreshold(totalCoefficient, 45000, 52500, 55000));

            double odinMetric = totalCoefficient - secondaryEnchantCoefficientInput;
            SetContentStatus("오딘 전면전", EvaluateByThreshold(odinMetric, 47000, 49500, 51000));
        }

        private static string EvaluateByThreshold(double value, double impossibleMax, double hardMax, double possibleMax)
        {
            if (value <= impossibleMax) return "불가능";
            if (value <= hardMax) return "힘듬";
            if (value <= possibleMax) return "가능";
            return "원활";
        }

        private void SetContentStatus(string contentName, string value)
        {
            if (_contentStatusLabels.TryGetValue(contentName, out var label))
            {
                label.Text = value;
                label.Foreground = value switch
                {
                    "불가능" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0x4D, 0x4F)),
                    "힘듬" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xD6, 0x4A)),
                    "가능" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x57, 0xE3, 0x89)),
                    "원활" => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xB6, 0xFF)),
                    _ => System.Windows.Media.Brushes.White
                };
            }
        }

        private CalculatorSlotRow? GetAccessoryRow(string slotName)
        {
            return _accessoryRows.FirstOrDefault(x => x.SlotName == slotName);
        }

        private void AvatarEnhancementCheckChanged(object sender, RoutedEventArgs e)
        {
            RecalculateTotalCoefficient();
        }

        private double CalculateAvatarEnhancementBonusCoefficient(double mainEnhanceBonus, double subEnhanceBonus)
        {
            var type = GetSelectedCalculatorType();
            return type switch
            {
                CoefficientCalculatorType.Stab => 32.5 * mainEnhanceBonus + 18.75 * subEnhanceBonus,
                CoefficientCalculatorType.Hack => 32.5 * mainEnhanceBonus + 18.75 * subEnhanceBonus,
                CoefficientCalculatorType.MagicAttack => 32.5 * mainEnhanceBonus + 18.25 * subEnhanceBonus,
                CoefficientCalculatorType.MagicDefense => 32.5 * mainEnhanceBonus + 16.75 * subEnhanceBonus,
                CoefficientCalculatorType.PhysicalHybrid => 28.75 * (mainEnhanceBonus + subEnhanceBonus),
                CoefficientCalculatorType.MagicHack => 28.75 * (mainEnhanceBonus + subEnhanceBonus),
                _ => 0
            };
        }

        private CoefficientCalculatorType GetSelectedCalculatorType()
        {
            if (_typeComboBox?.SelectedItem is CalculatorTypeOption option)
                return option.Type;

            return _selectedCharacterTypes.FirstOrDefault();
        }

        private List<string> BuildEquipmentCandidates(string slot, CoefficientCalculatorType type, string characterName)
        {
            IEnumerable<EquipmentModel> candidates = _allEquipments;

            CharacterTypeSlotMap.TryGetValue((characterName, type), out var config);

            candidates = candidates.Where(x => IsUsableByCharacter(x, characterName));

            candidates = slot switch
            {
                "무기" when config != null => candidates.Where(x => ContainsKeyword(x, config.WeaponKeyword)),
                "무기" => candidates.Where(x => IsWeaponMatchByType(x, type)),
                "손목" when config != null => candidates.Where(x => config.WristKeywords.Any(k => ContainsKeyword(x, k))),
                "갑옷" when config != null => candidates.Where(x =>
                    ContainsCategory(x, "갑옷")
                    && config.ArmorKeywords.Any(k => ContainsCategory(x, k))
                    && IsMatchByAttackType(x, type, allowWhenUnknown: true)),
                "갑옷" => candidates.Where(x => ContainsCategory(x, "갑옷") && IsMatchByAttackType(x, type, allowWhenUnknown: true)),
                "아티팩트" when config != null => candidates.Where(x => ContainsCategory(x, "아티팩트") && ContainsKeyword(x, config.ArtifactKeyword)),
                "다리" => candidates.Where(x => ContainsCategory(x, "발") || ContainsCategory(x, "다리")),
                "손" => candidates.Where(x => ContainsCategory(x, "손") && !ContainsCategory(x, "손목")),
                _ when slot.Contains("어빌리티") => Enumerable.Empty<EquipmentModel>(),
                "스탯" or "아바타" or "커프" or "칭호" or "코어" or "렐릭" => Enumerable.Empty<EquipmentModel>(),
                _ => candidates.Where(x => ContainsCategory(x, slot))
            };

            var names = candidates
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .Cast<string>()
                .ToList();

            names.Insert(0, "수동 입력");
            return names;
        }

        private static bool IsUsableByCharacter(EquipmentModel item, string characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName))
                return true;

            if (item.Characters == null || item.Characters.Count == 0)
                return true;

            return item.Characters.Any(x => string.Equals(x, characterName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ContainsCategory(EquipmentModel item, string keyword)
        {
            bool inSub = item.SubCategory?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true;
            bool inMajor = item.MajorCategory?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true;
            return inSub || inMajor;
        }

        private static bool ContainsKeyword(EquipmentModel item, string keyword)
        {
            return item.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true
                || item.SubCategory?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true
                || item.MajorCategory?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true;
        }

        private static bool IsWeaponMatchByType(EquipmentModel item, CoefficientCalculatorType type)
        {
            string matchKeyword = type switch
            {
                CoefficientCalculatorType.Stab => "찌르기",
                CoefficientCalculatorType.Hack => "베기",
                CoefficientCalculatorType.MagicAttack => "마법공격",
                CoefficientCalculatorType.MagicDefense => "마법방어",
                CoefficientCalculatorType.PhysicalHybrid => "물리복합",
                CoefficientCalculatorType.MagicHack => "마법베기",
                _ => string.Empty
            };

            bool isWeapon = item.MajorCategory?.Contains("무기", StringComparison.OrdinalIgnoreCase) == true
                || item.SubCategory?.Contains("무기", StringComparison.OrdinalIgnoreCase) == true;

            if (!isWeapon) return false;

            return IsMatchByAttackType(item, type, allowWhenUnknown: false);
        }

        private static bool IsMatchByAttackType(EquipmentModel item, CoefficientCalculatorType type, bool allowWhenUnknown)
        {
            string matchKeyword = type switch
            {
                CoefficientCalculatorType.Stab => "찌르기",
                CoefficientCalculatorType.Hack => "베기",
                CoefficientCalculatorType.MagicAttack => "마법공격",
                CoefficientCalculatorType.MagicDefense => "마법방어",
                CoefficientCalculatorType.PhysicalHybrid => "물리복합",
                CoefficientCalculatorType.MagicHack => "마법베기",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(matchKeyword))
                return true;

            if (item.AttackTypes.Any())
                return item.AttackTypes.Any(x => string.Equals(x, matchKeyword, StringComparison.OrdinalIgnoreCase));

            bool textMatched =
                item.SubCategory?.Contains(matchKeyword, StringComparison.OrdinalIgnoreCase) == true
                || item.Name?.Contains(matchKeyword, StringComparison.OrdinalIgnoreCase) == true;

            if (textMatched)
                return true;

            return allowWhenUnknown;
        }

        private static IReadOnlyList<CoefficientCalculatorType> ResolveCalculatorTypes(string characterName)
        {
            return CharacterCalculatorTypeMap.TryGetValue(characterName, out var calculatorTypes) && calculatorTypes.Count > 0
                ? calculatorTypes
                : new[] { CoefficientCalculatorType.Stab };
        }

        private static string GetCalculatorTypeDisplayName(CoefficientCalculatorType calculatorType)
        {
            return calculatorType switch
            {
                CoefficientCalculatorType.Stab => "찌르기",
                CoefficientCalculatorType.Hack => "베기",
                CoefficientCalculatorType.MagicAttack => "마법공격",
                CoefficientCalculatorType.MagicDefense => "마법방어",
                CoefficientCalculatorType.PhysicalHybrid => "물리 복합",
                CoefficientCalculatorType.MagicHack => "마법베기",
                _ => "찌르기"
            };
        }

        private enum CoefficientCalculatorType
        {
            Stab,
            Hack,
            MagicAttack,
            MagicDefense,
            PhysicalHybrid,
            MagicHack
        }

        private sealed class CharacterCalculatorItem : CharacterModel
        {
            public IReadOnlyList<CoefficientCalculatorType> CalculatorTypes { get; init; } = Array.Empty<CoefficientCalculatorType>();
            public string CalculatorTypeName => string.Join(" / ", CalculatorTypes.Select(GetCalculatorTypeDisplayName));
        }

        private sealed class CalculatorTypeOption
        {
            public CalculatorTypeOption(CoefficientCalculatorType type, string displayName)
            {
                Type = type;
                DisplayName = displayName;
            }

            public CoefficientCalculatorType Type { get; }
            public string DisplayName { get; }
        }

        private sealed class CalculatorSlotRow : INotifyPropertyChanged
        {
            private string _selectedEquipmentName = "수동 입력";
            private double _attackValue;
            private double _attackEnchant;
            private double _defenseValue;
            private double _defenseEnchant;
            private double _primaryStatValue;
            private double _secondaryStatValue;
            private double _coefficient;
            private List<string> _equipmentCandidates = new() { "수동 입력" };

            public CalculatorSlotRow(string slotName)
            {
                SlotName = slotName;
                if (slotName is "스탯" or "아바타" or "커프" or "칭호" or "코어" or "렐릭" || slotName.Contains("어빌리티"))
                    _selectedEquipmentName = "";
            }

            public string SlotName { get; }
            public CoefficientCalculatorType CurrentType { get; set; }

            public bool IsAccessorySlot => SlotName is "커프" or "렐릭";
            public bool IsCoreSlot => SlotName == "코어";
            public bool IsStatRow => SlotName == "스탯";
            public bool IsAvatarRow => SlotName == "아바타";
            public bool IsTitleRow => SlotName == "칭호";
            public bool IsWellikRow => SlotName == "렐릭";

            public bool CanEditEnchant => !IsAccessorySlot && !IsCoreSlot && !IsStatRow && !IsAvatarRow && !IsTitleRow;
            public bool CanEditPrimaryBase => SlotName is "아바타" or "커프" or "렐릭";
            public bool CanEditPrimaryEnchant => SlotName is "아바타" or "커프" or "렐릭" or "코어";
            public bool CanEditSecondaryBase => SlotName == "칭호";
            public bool CanEditSecondaryEnchant => false;
            public bool CanEditMR => SlotName == "스탯";
            public bool CanEditINT => SlotName == "스탯";

            public double AccessoryValue1
            {
                get => AttackValue;
                set => AttackValue = value;
            }

            public double AccessoryValue2
            {
                get => AttackEnchant;
                set => AttackEnchant = value;
            }

            public double TitleValue
            {
                get => DefenseValue;
                set => DefenseValue = value;
            }

            public double CoreValue
            {
                get => AttackEnchant;
                set => AttackEnchant = value;
            }

            public List<string> EquipmentCandidates
            {
                get => _equipmentCandidates;
                set
                {
                    _equipmentCandidates = value;
                    OnPropertyChanged();
                }
            }

            public string SelectedEquipmentName
            {
                get => _selectedEquipmentName;
                set
                {
                    if (_selectedEquipmentName == value) return;
                    _selectedEquipmentName = value;
                    OnPropertyChanged();
                }
            }

            public double AttackValue
            {
                get => _attackValue;
                set
                {
                    _attackValue = value;
                    RecalculateCoefficient();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AccessoryValue1));
                }
            }

            public double AttackEnchant
            {
                get => _attackEnchant;
                set
                {
                    _attackEnchant = value;
                    RecalculateCoefficient();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(AccessoryValue2));
                    OnPropertyChanged(nameof(CoreValue));
                }
            }

            public double DefenseValue
            {
                get => _defenseValue;
                set
                {
                    _defenseValue = value;
                    RecalculateCoefficient();
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TitleValue));
                }
            }

            public double DefenseEnchant
            {
                get => _defenseEnchant;
                set { _defenseEnchant = value; RecalculateCoefficient(); OnPropertyChanged(); }
            }

            public double PrimaryStatValue
            {
                get => _primaryStatValue;
                set { _primaryStatValue = value; RecalculateCoefficient(); OnPropertyChanged(); }
            }

            public double SecondaryStatValue
            {
                get => _secondaryStatValue;
                set { _secondaryStatValue = value; RecalculateCoefficient(); OnPropertyChanged(); }
            }

            public double Coefficient
            {
                get => _coefficient;
                private set
                {
                    _coefficient = value;
                    OnPropertyChanged();
                }
            }

            public void RecalculateCoefficient()
            {
                if (IsStatRow)
                {
                    Coefficient = CurrentType switch
                    {
                        CoefficientCalculatorType.Stab => 2.1 * PrimaryStatValue + 1.08 * SecondaryStatValue,
                        CoefficientCalculatorType.Hack => 2.1 * PrimaryStatValue + 1.08 * SecondaryStatValue,
                        CoefficientCalculatorType.MagicAttack => 2.4 * PrimaryStatValue + 0.6 * SecondaryStatValue,
                        CoefficientCalculatorType.MagicDefense => 2.55 * PrimaryStatValue + 0.45 * SecondaryStatValue,
                        CoefficientCalculatorType.PhysicalHybrid => 1.8 * (PrimaryStatValue + SecondaryStatValue),
                        CoefficientCalculatorType.MagicHack => 1.8 * (PrimaryStatValue + SecondaryStatValue),
                        _ => 0
                    };
                    return;
                }

                if (IsCoreSlot)
                {
                    Coefficient = CurrentType switch
                    {
                        CoefficientCalculatorType.Stab => 32.5 * CoreValue,
                        CoefficientCalculatorType.Hack => 32.5 * CoreValue,
                        CoefficientCalculatorType.MagicAttack => 32.5 * CoreValue,
                        CoefficientCalculatorType.MagicDefense => 32.5 * CoreValue,
                        CoefficientCalculatorType.PhysicalHybrid => 28.75 * CoreValue,
                        CoefficientCalculatorType.MagicHack => 28.75 * CoreValue,
                        _ => 0
                    };
                    return;
                }

                if (IsTitleRow)
                {
                    Coefficient = CurrentType switch
                    {
                        CoefficientCalculatorType.Stab => 23.75 * TitleValue,
                        CoefficientCalculatorType.Hack => 23.75 * TitleValue,
                        CoefficientCalculatorType.MagicAttack => 23.75 * TitleValue,
                        CoefficientCalculatorType.MagicDefense => 20.5 * TitleValue,
                        CoefficientCalculatorType.PhysicalHybrid => 14.5 * TitleValue,
                        CoefficientCalculatorType.MagicHack => 14.5 * TitleValue,
                        _ => 0
                    };
                    return;
                }

                if (IsAvatarRow || IsAccessorySlot)
                {
                    Coefficient = CurrentType switch
                    {
                        CoefficientCalculatorType.Stab => 23.75 * AccessoryValue1 + 3.75 * AccessoryValue2,
                        CoefficientCalculatorType.Hack => 23.75 * AccessoryValue1 + 3.75 * AccessoryValue2,
                        CoefficientCalculatorType.MagicAttack => 23.75 * AccessoryValue1 + 2.5 * AccessoryValue2,
                        CoefficientCalculatorType.MagicDefense => 20.5 * AccessoryValue1 + 2.5 * AccessoryValue2,
                        CoefficientCalculatorType.PhysicalHybrid => 14.5 * (AccessoryValue1 + AccessoryValue2),
                        CoefficientCalculatorType.MagicHack => 14.5 * (AccessoryValue1 + AccessoryValue2),
                        _ => 0
                    };
                    return;
                }

                Coefficient = CurrentType switch
                {
                    CoefficientCalculatorType.Stab => 23.75 * AttackValue + 32.5 * AttackEnchant + 3.75 * DefenseValue + 18.75 * DefenseEnchant,
                    CoefficientCalculatorType.Hack => 23.75 * AttackValue + 32.5 * AttackEnchant + 3.75 * DefenseValue + 18.75 * DefenseEnchant,
                    CoefficientCalculatorType.MagicAttack => 23.75 * AttackValue + 32.5 * AttackEnchant + 2.5 * DefenseValue + 18.25 * DefenseEnchant,
                    CoefficientCalculatorType.MagicDefense => 20.5 * AttackValue + 32.5 * AttackEnchant + 2.5 * DefenseValue + 16.75 * DefenseEnchant,
                    CoefficientCalculatorType.PhysicalHybrid => 14.5 * (AttackValue + DefenseValue) + 28.75 * (AttackEnchant + DefenseEnchant),
                    CoefficientCalculatorType.MagicHack => 14.5 * (AttackValue + DefenseValue) + 28.75 * (AttackEnchant + DefenseEnchant),
                    _ => 0
                };
            }

            public event PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
