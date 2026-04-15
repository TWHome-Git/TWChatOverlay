using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Addons
{
    /// <summary>
    /// 장비 DB 조회 기능을 제공하는 뷰입니다.
    /// </summary>
    public partial class EquipmentDbView : UserControl
    {
        private static readonly SolidColorBrush ActiveBackground = new((Color)ColorConverter.ConvertFromString("#1F6FEB"));
        private static readonly SolidColorBrush InactiveForeground = new((Color)ColorConverter.ConvertFromString("#8B949E"));

        private readonly EquipmentService _equipmentService = new();
        private readonly DispatcherTimer _equipmentSearchDebounceTimer;

        private string _selectedCategory = "전체";
        private FrameworkElement? _lastSelectedControl;
        private List<EquipmentModel> _allEquipments = new();
        private List<CategoryGroup> _categoryGroups = new();

        private static readonly Dictionary<string, int> MajorOrder = new()
        {
            { "무기", 1 }, { "손목", 2 }, { "갑옷", 3 }, { "장비 세트", 4 }, { "효과", 5 }, { "아티팩트", 6 }
        };

        private static readonly Dictionary<string, int> SubOrder = new()
        {
            { "투구", 1 }, { "머리", 2 }, { "몸", 3 }, { "손", 4 }, { "발", 5 },
            { "찌르기", 1 }, { "베기", 2 }, { "마법공격", 3 }, { "마법방어(신성)", 4 }, { "물리 복합", 5 }, { "마법 베기", 6 }
        };

        public EquipmentDbView()
        {
            InitializeComponent();

            _equipmentSearchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _equipmentSearchDebounceTimer.Tick += (_, _) =>
            {
                _equipmentSearchDebounceTimer.Stop();
                ApplyFilters();
            };

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await LoadEquipmentData();
        }

        private async System.Threading.Tasks.Task LoadEquipmentData()
        {
            try
            {
                var data = await _equipmentService.GetEquipmentsAsync();
                _allEquipments = data ?? new List<EquipmentModel>();

                GenerateCategories();
                CategoryMenuControl.ItemsSource = _categoryGroups;
                ApplyFilters();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EquipmentDbView] Data Load Error: {ex.Message}");
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _equipmentSearchDebounceTimer.Stop();
            _equipmentSearchDebounceTimer.Start();
        }

        private void CategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                _selectedCategory = element.Tag?.ToString() ?? "전체";
                UpdateSelectedUI(element);
                ApplyFilters();
            }
        }

        private void MajorCategory_Header_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => CategoryButton_Click(sender, e);

        private void EquipmentGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (EquipmentGrid.SelectedItem is not EquipmentModel selected)
                return;

            var detailWindow = new EquipmentDetailWindow(selected)
            {
                Owner = Window.GetWindow(this)
            };

            detailWindow.Show();
        }

        private void ApplyFilters()
        {
            if (_allEquipments == null) return;

            string searchText = SearchBox.Text?.Trim().ToLower() ?? string.Empty;

            var filtered = _allEquipments.Where(item =>
                (_selectedCategory == "전체" || item.SubCategory == _selectedCategory || item.MajorCategory == _selectedCategory) &&
                (searchText == string.Empty || (item.Name?.Contains(searchText, StringComparison.OrdinalIgnoreCase) ?? false))
            ).ToList();

            EquipmentGrid.ItemsSource = filtered;
        }

        private void UpdateSelectedUI(FrameworkElement newControl)
        {
            if (ReferenceEquals(_lastSelectedControl, newControl)) return;

            SetControlHighlight(_lastSelectedControl, false);
            SetControlHighlight(newControl, true);
            _lastSelectedControl = newControl;
        }

        private static void SetControlHighlight(FrameworkElement? control, bool isHighlighted)
        {
            if (control == null) return;

            var bg = isHighlighted ? ActiveBackground : Brushes.Transparent;
            var fg = isHighlighted ? Brushes.White : InactiveForeground;

            if (control is Button btn)
            {
                btn.Background = bg;
                btn.Foreground = fg;
            }
            else if (control is Border brd)
            {
                brd.Background = bg;
                if (brd.Child is TextBlock tb) tb.Foreground = fg;
            }
        }

        private void GenerateCategories()
        {
            if (_allEquipments == null) return;

            _categoryGroups = _allEquipments
                .GroupBy(x => x.MajorCategory ?? "기타")
                .Select(g => new CategoryGroup
                {
                    MajorName = g.Key,
                    SubCategories = g.Select(x => x.SubCategory)
                                     .Where(sub => !string.IsNullOrEmpty(sub))
                                     .Distinct()
                                     .OrderBy(sub => SubOrder.GetValueOrDefault(sub!, 100))
                                     .ThenBy(sub => sub)
                                     .Select(sub => sub!)
                                     .ToList()
                })
                .OrderBy(x => MajorOrder.GetValueOrDefault(x.MajorName!, 100))
                .ToList();
        }

        private sealed class CategoryGroup
        {
            public string? MajorName { get; set; }
            public List<string> SubCategories { get; set; } = new();
        }
    }
}
