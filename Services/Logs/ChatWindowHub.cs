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
        private const double SnapInsetX = 5.0;
        private const double SnapInsetTop = 35.0;
        private const double SnapInsetBottom = 5.0;
        public static LogTabBufferStore SharedLogBuffers { get; } = new(200);

        public static event EventHandler? BuffersChanged;

        private static readonly HashSet<int> OpenSlots = new();
        private static readonly object NotificationLock = new();
        private static DispatcherTimer? _bufferNotificationTimer;
        private static bool _isBufferNotificationPending;

        public static bool CanOpenClone => OpenSlots.Count < 2;
        public static IReadOnlyCollection<int> OpenCloneSlots => OpenSlots.ToList().AsReadOnly();

        public static int? RegisterClone()
        {
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

        public static bool TryApplyMagneticSnap(Window movingWindow, double threshold = 14.0)
        {
            if (movingWindow == null)
                return false;

            if (!double.IsFinite(movingWindow.Left) || !double.IsFinite(movingWindow.Top))
                return false;

            Rect movingRect = GetVisibleFrame(movingWindow);
            if (movingRect.Width <= 0 || movingRect.Height <= 0)
                return false;

            bool changed = false;

            double? snappedLeft = null;
            double? snappedTop = null;
            double bestHorizontalDistance = threshold;
            double bestVerticalDistance = threshold;

            foreach (Window candidateWindow in GetSnapCandidates(movingWindow))
            {
                Rect candidateRect = GetVisibleFrame(candidateWindow);
                if (candidateRect.Width <= 0 || candidateRect.Height <= 0)
                    continue;

                double horizontalOverlap = Math.Min(movingRect.Right, candidateRect.Right) - Math.Max(movingRect.Left, candidateRect.Left);
                double verticalOverlap = Math.Min(movingRect.Bottom, candidateRect.Bottom) - Math.Max(movingRect.Top, candidateRect.Top);

                bool hasVerticalOverlap = verticalOverlap > 0;
                bool hasHorizontalOverlap = horizontalOverlap > 0;

                UpdateHorizontalSnap(
                    movingRect,
                    candidateRect.Left,
                    candidateRect.Right,
                    hasVerticalOverlap,
                    threshold,
                    ref bestHorizontalDistance,
                    ref snappedLeft);

                UpdateVerticalSnap(
                    movingRect,
                    candidateRect.Top,
                    candidateRect.Bottom,
                    hasHorizontalOverlap,
                    threshold,
                    ref bestVerticalDistance,
                    ref snappedTop);
            }

            if (snappedLeft.HasValue && Math.Abs(snappedLeft.Value - movingWindow.Left) > 0.01)
            {
                movingWindow.Left = snappedLeft.Value - SnapInsetX;
                changed = true;
            }

            if (snappedTop.HasValue && Math.Abs(snappedTop.Value - movingWindow.Top) > 0.01)
            {
                movingWindow.Top = snappedTop.Value - SnapInsetTop;
                changed = true;
            }

            return changed;
        }

        private static IEnumerable<Window> GetSnapCandidates(Window movingWindow)
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (ReferenceEquals(window, movingWindow))
                    continue;

                if (!window.IsVisible)
                    continue;

                if (window is MainWindow || window is ChatCloneWindow)
                    yield return window;
            }
        }

        private static Rect GetVisibleFrame(Window window)
        {
            double width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
            double height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

            double visibleLeft = window.Left + SnapInsetX;
            double visibleTop = window.Top + SnapInsetTop;
            double visibleWidth = Math.Max(0.0, width - (SnapInsetX * 2.0));
            double visibleHeight = Math.Max(0.0, height - SnapInsetTop - SnapInsetBottom);
            return new Rect(visibleLeft, visibleTop, visibleWidth, visibleHeight);
        }

        private static void UpdateHorizontalSnap(
            Rect movingRect,
            double candidateLeft,
            double candidateRight,
            bool hasVerticalOverlap,
            double threshold,
            ref double bestDistance,
            ref double? currentValue)
        {
            if (hasVerticalOverlap)
            {
                double leftDelta = Math.Abs(movingRect.Right - candidateLeft);
                if (leftDelta <= threshold && leftDelta < bestDistance)
                {
                    bestDistance = leftDelta;
                    currentValue = candidateLeft - movingRect.Width;
                }
            }

            if (hasVerticalOverlap)
            {
                double rightDelta = Math.Abs(movingRect.Left - candidateRight);
                if (rightDelta <= threshold && rightDelta < bestDistance)
                {
                    bestDistance = rightDelta;
                    currentValue = candidateRight;
                }
            }
        }

        private static void UpdateVerticalSnap(
            Rect movingRect,
            double candidateTop,
            double candidateBottom,
            bool hasHorizontalOverlap,
            double threshold,
            ref double bestDistance,
            ref double? currentValue)
        {
            if (hasHorizontalOverlap)
            {
                double topDelta = Math.Abs(movingRect.Bottom - candidateTop);
                if (topDelta <= threshold && topDelta < bestDistance)
                {
                    bestDistance = topDelta;
                    currentValue = candidateTop - movingRect.Height;
                }
            }

            if (hasHorizontalOverlap)
            {
                double bottomDelta = Math.Abs(movingRect.Top - candidateBottom);
                if (bottomDelta <= threshold && bottomDelta < bestDistance)
                {
                    bestDistance = bottomDelta;
                    currentValue = candidateBottom;
                }
            }
        }
    }
}
