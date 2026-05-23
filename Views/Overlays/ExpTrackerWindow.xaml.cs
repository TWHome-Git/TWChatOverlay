using System;
using System.Windows;
using System.Windows.Input;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.ViewModels;

namespace TWChatOverlay.Views
{
    public partial class ExpTrackerWindow : Window
    {
        public ExpTrackerWindow(object? dataContext)
        {
            InitializeComponent();
            DataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
            Loaded += ExpTrackerWindow_Loaded;
            LocationChanged += (_, _) => PersistPosition();
        }

        private void ExpTrackerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= ExpTrackerWindow_Loaded;
        }

        public void ApplyPositionMode(bool isEnabled)
        {
            DragBar.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ApplyStoredPosition(double? left, double? top)
        {
            if (left.HasValue)
                Left = left.Value;
            if (top.HasValue)
                Top = top.Value;
        }

        private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
                return;

            try
            {
                DragMove();
            }
            catch
            {
            }

            PersistPosition();
        }

        private void PersistPosition()
        {
            if (DataContext is not ExpTrackerViewModel)
                return;

            if (WindowState == WindowState.Minimized)
                return;

            if (Owner is MainWindow main && main.DataContext is ChatSettings settings)
            {
                settings.ExpTrackerWindowLeft = Left;
                settings.ExpTrackerWindowTop = Top;
                ConfigService.SaveDeferred(settings);
            }
        }
    }
}
