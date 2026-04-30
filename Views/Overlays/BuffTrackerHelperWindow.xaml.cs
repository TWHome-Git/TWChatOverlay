using System;
using System.Windows;
using System.Windows.Input;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class BuffTrackerHelperWindow : Window
    {
        public static BuffTrackerHelperWindow? Instance { get; private set; }
        private bool _isDragging;

        public BuffTrackerHelperWindow()
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

            _isDragging = true;
            try { DragMove(); } catch { }
            finally
            {
                _isDragging = false;
                CommitPositionToSettings();
            }
        }

        private void SyncPositionToSettings()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow mainWindow && mainWindow.DataContext is ChatSettings settings)
                    {
                        if (_isDragging)
                        {
                            settings.SetBuffTrackerWindowPosition(Left, Top, notify: false);
                            SyncTrackerWindowPosition();
                        }
                        else
                        {
                            settings.SetBuffTrackerWindowPosition(Left, Top, notify: true);
                        }

                        break;
                    }
                }
            }
            catch { }
        }

        private void CommitPositionToSettings()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow mainWindow && mainWindow.DataContext is ChatSettings settings)
                    {
                        settings.SetBuffTrackerWindowPosition(Left, Top, notify: true);
                        SyncTrackerWindowPosition();
                        break;
                    }
                }
            }
            catch { }
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
