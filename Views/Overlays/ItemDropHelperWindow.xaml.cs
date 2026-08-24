using System;
using System.Windows;
using System.Windows.Input;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class ItemDropHelperWindow : Window
    {
        public static ItemDropHelperWindow? Instance { get; private set; }

        /// <summary>잠금 해제 인스펙터의 폰트 크기 조절 — 라벨에 즉시 반영 (실제 토스트 크기 설정과 연동).</summary>
        public void SetFontSize(double size)
        {
            PreviewLabel.FontSize = size;
        }

        public ItemDropHelperWindow()
        {
            InitializeComponent();
            SettingsHostZOrder.Register(this); // 설정 창이 열려 있으면 그 아래로 표시
            WindowFontService.Apply(this);
            Instance = this;
            LocationChanged += (_, _) => SyncPositionToSettings();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;

            base.OnClosed(e);
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!UiLockService.IsUnlocked) return;
            UiLockService.Select(this);
            if (e.ButtonState != MouseButtonState.Pressed)
                return;

            WindowDragBehavior.BeginDrag(this, e);
        }

        private void SyncPositionToSettings()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow mainWindow && mainWindow.DataContext is ChatSettings settings)
                    {
                        settings.ItemDropWindowLeft = Left;
                        settings.ItemDropWindowTop = Top;
                        break;
                    }
                }
            }
            catch { }
        }
    }
}
