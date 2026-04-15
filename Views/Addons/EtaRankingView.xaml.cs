using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Addons
{
    /// <summary>
    /// 에타 랭킹 조회 기능을 제공하는 뷰입니다.
    /// </summary>
    public partial class EtaRankingView : UserControl
    {
        private static readonly SolidColorBrush ActiveBackground = new((Color)ColorConverter.ConvertFromString("#1F6FEB"));
        private static readonly SolidColorBrush InactiveForeground = new((Color)ColorConverter.ConvertFromString("#8B949E"));

        private string _selectedEtaCategory = "전체";
        private FrameworkElement? _lastSelectedEtaControl;
        private List<EtaProfileResolver.EtaRankingEntry> _allEtaRankings = new();
        private readonly DispatcherTimer _etaSearchDebounceTimer;

        public EtaRankingView()
        {
            InitializeComponent();

            _etaSearchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _etaSearchDebounceTimer.Tick += (_, _) =>
            {
                _etaSearchDebounceTimer.Stop();
                ApplyEtaRankingFilter();
            };

            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnLoaded;
            await LoadEtaRankingData();
        }

        private async System.Threading.Tasks.Task LoadEtaRankingData()
        {
            try
            {
                await EtaProfileResolver.EnsureLoadedAsync();

                _allEtaRankings = EtaProfileResolver.GetRankings().ToList();

                EtaCategoryMenuControl.ItemsSource = new List<CategoryGroup>
                {
                    new CategoryGroup
                    {
                        MajorName = "캐릭터",
                        SubCategories = _allEtaRankings
                            .Select(x => x.CharacterName)
                            .Distinct()
                            .OrderBy(x => x)
                            .ToList()
                    }
                };

                ApplyEtaRankingFilter();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EtaRankingView] ETA Ranking Load Error: {ex.Message}");
            }
        }

        private void EtaCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                var selectedTag = element.Tag?.ToString() ?? "전체";
                _selectedEtaCategory = selectedTag == "캐릭터" ? "전체" : selectedTag;
                UpdateEtaSelectedUI(element);
            }

            ApplyEtaRankingFilter();
        }

        private void EtaMajorCategory_Header_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => EtaCategoryButton_Click(sender, e);

        private void EtaSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _etaSearchDebounceTimer.Stop();
            _etaSearchDebounceTimer.Start();
        }

        private void ApplyEtaRankingFilter()
        {
            if (EtaRankingGrid == null) return;

            string etaSearchText = EtaSearchBox?.Text?.Trim() ?? string.Empty;

            IEnumerable<EtaProfileResolver.EtaRankingEntry> filtered = _selectedEtaCategory == "전체"
                ? _allEtaRankings.OrderByDescending(x => x.Level).ThenByDescending(x => x.Essence).ThenBy(x => x.OriginalOrder)
                : _allEtaRankings.Where(x => x.CharacterName == _selectedEtaCategory)
                                 .OrderByDescending(x => x.Level)
                                 .ThenByDescending(x => x.Essence)
                                 .ThenBy(x => x.OriginalOrder);

            if (!string.IsNullOrWhiteSpace(etaSearchText))
            {
                filtered = filtered.Where(x =>
                    x.UserId.Contains(etaSearchText, StringComparison.OrdinalIgnoreCase) ||
                    x.CharacterName.Contains(etaSearchText, StringComparison.OrdinalIgnoreCase));
            }

            EtaRankingGrid.ItemsSource = filtered
                .Select((x, index) => new EtaRankingDisplayRow
                {
                    Rank = index + 1,
                    CharacterName = x.CharacterName,
                    CharacterImagePath = $"pack://application:,,,/Data/images/etachar/{x.CharacterCode}.png",
                    UserId = x.UserId,
                    Level = x.Level,
                    Essence = x.Essence
                })
                .ToList();
        }

        private void UpdateEtaSelectedUI(FrameworkElement newControl)
        {
            if (ReferenceEquals(_lastSelectedEtaControl, newControl)) return;

            SetControlHighlight(_lastSelectedEtaControl, false);
            SetControlHighlight(newControl, true);

            _lastSelectedEtaControl = newControl;
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

        private sealed class EtaRankingDisplayRow
        {
            public int Rank { get; set; }
            public string CharacterName { get; set; } = string.Empty;
            public string CharacterImagePath { get; set; } = string.Empty;
            public string UserId { get; set; } = string.Empty;
            public int Level { get; set; }
            public int Essence { get; set; }
        }

        private sealed class CategoryGroup
        {
            public string? MajorName { get; set; }
            public List<string> SubCategories { get; set; } = new();
        }
    }
}
