using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// Reasserts the main window topmost state after a secondary window hides or closes.
    /// </summary>
    public static class SecondaryWindowTopmostRefreshService
    {
        private static readonly object SyncRoot = new();
        private static readonly HashSet<Window> ObservedWindows = new();
        private static bool _isInitialized;

        public static void Initialize()
        {
            lock (SyncRoot)
            {
                if (_isInitialized)
                    return;

                _isInitialized = true;
            }

            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(Window_Loaded),
                handledEventsToo: true);
        }

        private static void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window)
                return;

            if (window is MainWindow)
                return;

            Attach(window);
        }

        private static void Attach(Window window)
        {
            lock (SyncRoot)
            {
                if (!ObservedWindows.Add(window))
                    return;
            }

            window.IsVisibleChanged += Window_IsVisibleChanged;
            window.Closed += Window_Closed;
        }

        private static void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not Window window)
                return;

            if (e.NewValue is bool isVisible && !isVisible)
            {
                RequestMainWindowTopmostRefresh();
            }
        }

        private static void Window_Closed(object? sender, EventArgs e)
        {
            if (sender is not Window window)
                return;

            Detach(window);
            RequestMainWindowTopmostRefresh();
        }

        private static void Detach(Window window)
        {
            lock (SyncRoot)
            {
                ObservedWindows.Remove(window);
            }

            window.IsVisibleChanged -= Window_IsVisibleChanged;
            window.Closed -= Window_Closed;
        }

        private static void RequestMainWindowTopmostRefresh()
        {
            try
            {
                var mainWindow = Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
                mainWindow?.RequestTopmostRefresh();
            }
            catch
            {
            }
        }
    }
}
