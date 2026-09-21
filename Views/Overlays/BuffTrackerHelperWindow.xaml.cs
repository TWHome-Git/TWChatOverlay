using System;
using System.Linq;
using System.Windows.Input;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>잠금 해제 모드에서 버프 추적창의 최대 크기를 보여주는 미리보기 창. 위치는 실제 버프창과 함께 움직인다.</summary>
    public partial class BuffTrackerHelperWindow : OverlayWindowBase
    {
        public static BuffTrackerHelperWindow? Instance { get; private set; }

        public BuffTrackerHelperWindow()
        {
            InitializeComponent();
            Instance = this;
            var previewItems = BuffTrackerService.CreatePreviewItems();
            PreviewRareItems.ItemsSource = previewItems.Where(item => item.IsRare).ToList();
            PreviewExpItems.ItemsSource = previewItems.Where(item => !item.IsRare).ToList();
        }

        protected override void OnClosed(EventArgs e)
        {
            PersistBoundsNow();

            if (ReferenceEquals(Instance, this))
                Instance = null;

            base.OnClosed(e);
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => TryBeginDrag(e);

        protected override bool PersistBounds(ChatSettings settings)
        {
            settings.SetBuffTrackerWindowPosition(Left, Top, notify: false);
            SyncTrackerWindowPosition();
            return true;
        }

        private void SyncTrackerWindowPosition()
        {
            var trackerWindow = BuffTrackerWindow.Instance;
            if (trackerWindow == null)
                return;

            trackerWindow.Left = Left;
            trackerWindow.Top = Top;
        }
    }
}
