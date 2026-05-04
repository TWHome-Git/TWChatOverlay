using System;
using System.Windows;

namespace TWChatOverlay.Views
{
    public partial class MainWindow
    {
        private void ShowMainTabsTemporarily()
        {
            if (MainTabBackground == null || MainTabPanel == null)
                return;

            MainTabBackground.Visibility = Visibility.Visible;
            MainTabPanel.Visibility = Visibility.Visible;

            _mainTabAutoHideTimer.Stop();
            _mainTabAutoHideTimer.Start();
        }

        private void HideMainTabs()
        {
            if (_isSettingsPositionMode)
            {
                _mainTabAutoHideTimer.Stop();
                return;
            }

            if (MainBorder?.IsMouseOver == true)
            {
                _mainTabAutoHideTimer.Stop();
                _mainTabAutoHideTimer.Start();
                return;
            }

            _mainTabAutoHideTimer.Stop();

            if (MainTabBackground == null || MainTabPanel == null)
                return;

            MainTabBackground.Visibility = Visibility.Collapsed;
            MainTabPanel.Visibility = Visibility.Collapsed;
        }
    }
}
