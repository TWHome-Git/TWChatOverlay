using System;
using System.Windows;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 최소화(모든 창 숨기기) 동안 작업 표시줄에 남는 복원용 창.
    /// 작업 표시줄 버튼을 클릭하면 모든 창을 복원하고 스스로 닫힌다.
    /// </summary>
    public sealed class TrayRestoreProxyWindow : Window
    {
        public TrayRestoreProxyWindow()
        {
            Title = "테일즈 채팅 오버레이";
            ShowInTaskbar = true;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = 1;
            Height = 1;
            // 최소화 상태로만 쓰지만, 혹시 잠깐 복원되어도 화면 밖이라 보이지 않는다
            Left = -32000;
            Top = -32000;
            ShowActivated = false;
            WindowState = WindowState.Minimized;
            StateChanged += OnStateChanged;
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                return;

            // 작업 표시줄 버튼 클릭으로 복원됨 → 모든 창 복원 (이 창은 RestoreAll이 닫는다)
            try { Services.TrayAllWindowsService.RestoreAll(); } catch { }
        }
    }
}
