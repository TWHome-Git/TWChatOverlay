using System;
using System.Windows;
using System.Windows.Controls;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Addons
{
    /// <summary>
    /// 인크립트/코어 강화 시뮬레이터 탭 컨테이너 뷰입니다.
    /// 탭 선택 시 해당 UserControl을 동적으로 로드합니다.
    /// </summary>
    public partial class EncryptSimulatorTabsView : UserControl
    {
        private EncryptSimulatorView? _encryptView;
        private CoreEnhanceSimulatorView? _coreView;
        private RelicExpectationSimulatorView? _relicView;

        public EncryptSimulatorTabsView()
        {
            InitializeComponent();
            Loaded += EncryptSimulatorTabsView_Loaded;
        }

        private void EncryptSimulatorTabsView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= EncryptSimulatorTabsView_Loaded;
            if (SimulatorTabControl != null && SimulatorTabControl.SelectedIndex < 0)
            {
                SimulatorTabControl.SelectedIndex = 0;
            }
            LoadSelectedTabContent();
        }

        private void SimulatorTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!ReferenceEquals(e.Source, SimulatorTabControl))
                return;

            LoadSelectedTabContent();
        }

        private void LoadSelectedTabContent()
        {
            if (SimulatorTabControl == null || SimulatorContentHost == null)
                return;

            if (SimulatorTabControl.SelectedItem is not TabItem selectedTab)
                return;

            string key = selectedTab.Tag?.ToString() ?? string.Empty;
            try
            {
                SimulatorContentHost.Content = key switch
                {
                    "Core" => _coreView ??= new CoreEnhanceSimulatorView(),
                    "Relic" => _relicView ??= new RelicExpectationSimulatorView(),
                    "Encrypt" => _encryptView ??= new EncryptSimulatorView(),
                    _ => _encryptView ??= new EncryptSimulatorView()
                };
            }
            catch (Exception ex)
            {
                AppLogger.Error("[EncryptSimulatorTabsView] Failed to load selected tab content.", ex);
                SimulatorContentHost.Content = _encryptView ??= new EncryptSimulatorView();
            }
        }
    }
}
