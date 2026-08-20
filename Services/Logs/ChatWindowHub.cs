using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    public static class ChatWindowHub
    {
        public static LogTabBufferStore SharedLogBuffers { get; } = new(200);

        public static event EventHandler? BuffersChanged;

        private static readonly HashSet<int> OpenSlots = new();
        private static readonly object NotificationLock = new();
        private static DispatcherTimer? _bufferNotificationTimer;
        private static bool _isBufferNotificationPending;

        public static bool IsShuttingDown { get; private set; }
        public static bool CanOpenClone => OpenSlots.Count < 2;
        public static IReadOnlyCollection<int> OpenCloneSlots => OpenSlots.ToList().AsReadOnly();

        public static void BeginShutdown()
        {
            IsShuttingDown = true;
        }

        public static int? RegisterClone(int? preferredSlot = null)
        {
            if (preferredSlot.HasValue)
            {
                int slot = preferredSlot.Value;
                if (slot < 1 || slot > 2)
                    return null;

                return OpenSlots.Add(slot) ? slot : null;
            }

            for (int slot = 1; slot <= 2; slot++)
            {
                if (OpenSlots.Add(slot))
                    return slot;
            }

            return null;
        }

        public static void UnregisterClone(int slot)
        {
            OpenSlots.Remove(slot);
        }

        public static void NotifyBuffersChanged()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                BuffersChanged?.Invoke(null, EventArgs.Empty);
                return;
            }

            if (!dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(NotifyBuffersChanged), DispatcherPriority.Background);
                return;
            }

            lock (NotificationLock)
            {
                _isBufferNotificationPending = true;
                _bufferNotificationTimer ??= CreateBufferNotificationTimer(dispatcher);
                if (!_bufferNotificationTimer.IsEnabled)
                    _bufferNotificationTimer.Start();
            }
        }

        private static DispatcherTimer CreateBufferNotificationTimer(Dispatcher dispatcher)
        {
            var timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };

            timer.Tick += (_, _) =>
            {
                lock (NotificationLock)
                {
                    timer.Stop();
                    if (!_isBufferNotificationPending)
                        return;

                    _isBufferNotificationPending = false;
                }

                BuffersChanged?.Invoke(null, EventArgs.Empty);
            };

            return timer;
        }

        public static void FlushBufferNotifications()
        {
            lock (NotificationLock)
            {
                if (!_isBufferNotificationPending)
                    return;

                _isBufferNotificationPending = false;
                _bufferNotificationTimer?.Stop();
            }

            BuffersChanged?.Invoke(null, EventArgs.Empty);
        }
    }
}
