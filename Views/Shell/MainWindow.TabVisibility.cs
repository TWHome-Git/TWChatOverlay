using System;
using System.Windows;
using System.Windows.Threading;

namespace TWChatOverlay.Views
{
    /// <summary>메인 채팅 탭 행의 자동 숨김</summary>
    public partial class MainWindow
    {
        // ── 이 부분 클래스가 주로 쓰는 상태 ──
        private readonly DispatcherTimer _mainTabAutoHideTimer;

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
