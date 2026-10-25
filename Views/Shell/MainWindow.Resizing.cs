using System.Windows.Controls.Primitives;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>메인 창 크기 조절</summary>
    public partial class MainWindow
    {
        #region Resizing

        private void TopResize_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newHeight = this.Height - e.VerticalChange;
            if (newHeight > this.MinHeight)
            {
                this.Top += e.VerticalChange;
                this.Height = newHeight;
                _settings.WindowHeight = newHeight;
            }
            AppServices.Get<ChatWindowHub>().TryApplyMagneticSnap(this);
            PersistCurrentMainWindowPosition();
        }

        private void LeftResize_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = this.Width - e.HorizontalChange;
            if (newWidth > this.MinWidth)
            {
                this.Left += e.HorizontalChange;
                this.Width = newWidth;
                _settings.WindowWidth = newWidth;
            }
            AppServices.Get<ChatWindowHub>().TryApplyMagneticSnap(this);
            PersistCurrentMainWindowPosition();
        }

        private void RightResize_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = this.Width + e.HorizontalChange;
            if (newWidth > this.MinWidth)
            {
                this.Width = newWidth;
                _settings.WindowWidth = newWidth;
            }
            AppServices.Get<ChatWindowHub>().TryApplyMagneticSnap(this);
            PersistCurrentMainWindowPosition();
        }

        #endregion
    }
}
