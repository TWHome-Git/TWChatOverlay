using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.Services.LogAnalysis;
using TWChatOverlay.ViewModels;

namespace TWChatOverlay.Views
{
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
            SyncMarginsFromWindowPosition(this.Left, this.Top);
            _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);
            ChatWindowHub.TryApplyMagneticSnap(this);
            PersistSettings();
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
            SyncMarginsFromWindowPosition(this.Left, this.Top);
            _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);
            ChatWindowHub.TryApplyMagneticSnap(this);
            PersistSettings();
        }

        private void RightResize_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = this.Width + e.HorizontalChange;
            if (newWidth > this.MinWidth)
            {
                this.Width = newWidth;
                _settings.WindowWidth = newWidth;
            }
            SyncMarginsFromWindowPosition(this.Left, this.Top);
            _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);
            ChatWindowHub.TryApplyMagneticSnap(this);
            PersistSettings();
        }

        #endregion
    }
}
