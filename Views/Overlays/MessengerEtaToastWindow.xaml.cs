using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Documents;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class MessengerEtaToastWindow : Window
    {
        private readonly ChatSettings _settings;
        private bool _isPreviewMode;
        public MessengerEtaToastWindow(FontFamily fontFamily, ChatSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            FontFamily = fontFamily;
            TitleText.FontFamily = fontFamily;
            CloseButton.FontFamily = fontFamily;
            CloseButton.Click += (_, _) => Close();
        }

        public void SetEntries(IReadOnlyList<string> entries)
        {
            var doc = new FlowDocument();
            if (entries != null)
            {
                foreach (string line in entries)
                {
                    doc.Blocks.Add(new Paragraph(new Run(line))
                    {
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                }
            }

            EntryRichText.Document = doc;
        }

        public void SetPreviewMode(bool isPreview)
        {
            _isPreviewMode = isPreview;
            RefreshMousePassthroughStyle(forceInteractive: isPreview);
        }

        public void SaveCurrentPosition()
        {
            _settings.MessengerToastWindowLeft = Left;
            _settings.MessengerToastWindowTop = Top;
            ConfigService.SaveDeferred(_settings);
        }

        public void ShowAt(double left, double top)
        {
            Left = left;
            Top = top;
            if (!IsVisible)
                Show();
            Visibility = Visibility.Visible;
            Opacity = 1.0;
            Activate();
            Topmost = true;
            TopmostWindowHelper.BringToTopmost(this);
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                NativeMethods.SetWindowPos(
                    hwnd,
                    NativeMethods.HWND_TOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE |
                    NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_NOOWNERZORDER);
            }
            catch { }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            RefreshMousePassthroughStyle(forceInteractive: _isPreviewMode);
        }

        private void RefreshMousePassthroughStyle(bool forceInteractive)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                int nextStyle = (exStyle & ~NativeMethods.WS_EX_TRANSPARENT) | NativeMethods.WS_EX_TOOLWINDOW;
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, nextStyle);
            }
            catch { }
        }

        private void RootBorder_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isPreviewMode || e.ButtonState != System.Windows.Input.MouseButtonState.Pressed)
                return;

            try
            {
                DragMove();
            }
            catch { }
            finally
            {
                SaveCurrentPosition();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_isPreviewMode || e.ButtonState != System.Windows.Input.MouseButtonState.Pressed)
                return;

            try
            {
                DragMove();
            }
            catch { }
            finally
            {
                SaveCurrentPosition();
            }
        }
    }
}
