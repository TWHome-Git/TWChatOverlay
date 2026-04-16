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

        public ItemDropHelperWindow()
        {
            InitializeComponent();
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
            if (e.ButtonState != MouseButtonState.Pressed)
                return;

            try { DragMove(); } catch { }
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
