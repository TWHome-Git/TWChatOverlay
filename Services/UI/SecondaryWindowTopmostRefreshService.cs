using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// Reasserts the main window topmost state after a secondary window hides or closes.
    /// </summary>
    public sealed class SecondaryWindowTopmostRefreshService
    {
        private readonly object SyncRoot = new();
        private readonly HashSet<Window> ObservedWindows = new();
        private bool _isInitialized;

        public void Initialize()
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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window)
                return;

            if (window is IMainWindowHost)
                return;

            Attach(window);
        }

        private void Attach(Window window)
        {
            lock (SyncRoot)
            {
                if (!ObservedWindows.Add(window))
                    return;
            }

            window.IsVisibleChanged += Window_IsVisibleChanged;
            window.Closed += Window_Closed;
        }

        private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not Window window)
                return;

            if (e.NewValue is bool isVisible && !isVisible)
            {
                RequestMainWindowTopmostRefresh();
            }
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            if (sender is not Window window)
                return;

            Detach(window);
            RequestMainWindowTopmostRefresh();
        }

        private void Detach(Window window)
        {
            lock (SyncRoot)
            {
                ObservedWindows.Remove(window);
            }

            window.IsVisibleChanged -= Window_IsVisibleChanged;
            window.Closed -= Window_Closed;
        }

        private void RequestMainWindowTopmostRefresh()
        {
            try
            {
                MainWindowHost.Current?.RequestTopmostRefresh();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to request main window topmost refresh.", ex);
            }
        }
    }
}
