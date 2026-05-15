using System;
using System.Windows;
using System.Windows.Controls;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Addons
{
    /// <summary>
    /// 계수/대미지 계산기 탭 컨테이너 뷰입니다.
    /// </summary>
    public partial class CalculatorTabsView : UserControl
    {
        private readonly int _initialSelectedIndex;
        private CoefficientCalculatorView? _coefficientView;
        private DamageCalculatorView? _damageView;

        public CalculatorTabsView(int initialSelectedIndex = 0)
        {
            InitializeComponent();
            _initialSelectedIndex = initialSelectedIndex;
            Loaded += CalculatorTabsView_Loaded;
        }

        private void CalculatorTabsView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= CalculatorTabsView_Loaded;

            if (CalculatorTabControl != null)
            {
                int maxIndex = Math.Max(0, CalculatorTabControl.Items.Count - 1);
                CalculatorTabControl.SelectedIndex = Math.Clamp(_initialSelectedIndex, 0, maxIndex);
            }

            LoadSelectedTabContent();
        }

        private void CalculatorTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, CalculatorTabControl))
                return;

            LoadSelectedTabContent();
        }

        private void LoadSelectedTabContent()
        {
            if (CalculatorTabControl == null || CalculatorContentHost == null)
                return;

            if (CalculatorTabControl.SelectedItem is not TabItem selectedTab)
                return;

            string key = selectedTab.Tag?.ToString() ?? string.Empty;
            var coefficientView = _coefficientView ??= new CoefficientCalculatorView();

            try
            {
                CalculatorContentHost.Content = key switch
                {
                    "Damage" => _damageView ??= new DamageCalculatorView(coefficientView),
                    "Coefficient" => coefficientView,
                    _ => coefficientView
                };
            }
            catch (Exception ex)
            {
                AppLogger.Error("[CalculatorTabsView] Failed to load selected tab content.", ex);
                CalculatorContentHost.Content = coefficientView;
            }
        }
    }
}
