using System;
using System.Windows.Input;
using TWChatOverlay.Models;

namespace TWChatOverlay.Views
{
    /// <summary>잠금 해제 모드에서 아이템 획득 알림의 위치·크기를 보여주는 미리보기 창.</summary>
    public partial class ItemDropHelperWindow : OverlayWindowBase
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
            Instance = this;
        }

        protected override void OnClosed(EventArgs e)
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;

            base.OnClosed(e);
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => TryBeginDrag(e);

        protected override bool PersistBounds(ChatSettings settings)
        {
            settings.ItemDropWindowLeft = Left;
            settings.ItemDropWindowTop = Top;
            return true;
        }
    }
}
