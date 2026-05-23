using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.ViewModels;

namespace TWChatOverlay.Views
{
    public partial class ExpTrackerWindow : Window
    {
        private bool _isReady;
        private bool _isAdjustingForRightAnchor;
        private bool _isApplyingStoredPosition;
        private double? _storedLeft;
        private double? _storedTop;
        private double? _storedRight;
        private double? _rightAnchor;

        public ExpTrackerWindow(object? dataContext)
        {
            InitializeComponent();
            DataContext = dataContext ?? throw new ArgumentNullException(nameof(dataContext));
            Loaded += ExpTrackerWindow_Loaded;
            SizeChanged += ExpTrackerWindow_SizeChanged;
            LocationChanged += ExpTrackerWindow_LocationChanged;
        }

        private void ExpTrackerWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= ExpTrackerWindow_Loaded;
            UpdateWidthLimit();
            UpdateLayout();

            Dispatcher.BeginInvoke(new Action(ApplyStoredPositionInternal), DispatcherPriority.Loaded);
        }

        private void ExpTrackerWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_isApplyingStoredPosition || _isAdjustingForRightAnchor)
                return;

            if (!_isReady)
                return;

            if (_rightAnchor == null)
                _rightAnchor = Left + e.NewSize.Width;

            AdjustLeftToKeepRightFixed(e.NewSize.Width);
        }

        private void ExpTrackerWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (!_isReady || _isApplyingStoredPosition || _isAdjustingForRightAnchor)
                return;

            UpdateRightAnchorFromCurrentBounds();
            PersistPosition();
        }

        public void ApplyPositionMode(bool isEnabled)
        {
            DragBar.Visibility = isEnabled ? Visibility.Visible : Visibility.Collapsed;
        }

        public void ApplyStoredPosition(double? left, double? top)
            => ApplyStoredPosition(left, top, null);

        public void ApplyStoredPosition(double? left, double? top, double? right)
        {
            _storedLeft = left;
            _storedTop = top;
            _storedRight = right;

            if (_isReady)
                ApplyStoredPositionInternal();
        }

        private void ApplyStoredPositionInternal()
        {
            _isApplyingStoredPosition = true;
            try
            {
                UpdateLayout();

                if (_storedTop.HasValue)
                    Top = _storedTop.Value;

                if (_storedRight.HasValue)
                {
                    _rightAnchor = _storedRight.Value;
                    AdjustLeftToKeepRightFixed(GetCurrentWindowWidth());
                }
                else if (_storedLeft.HasValue)
                {
                    Left = _storedLeft.Value;
                    UpdateRightAnchorFromCurrentBounds();
                }
                else
                {
                    UpdateRightAnchorFromCurrentBounds();
                }

                _isReady = true;
            }
            finally
            {
                _isApplyingStoredPosition = false;
            }
        }

        private void AdjustLeftToKeepRightFixed(double width)
        {
            if (_rightAnchor == null)
                return;

            double targetLeft = _rightAnchor.Value - width;
            if (Math.Abs(Left - targetLeft) < 0.1)
                return;

            _isAdjustingForRightAnchor = true;
            try
            {
                Left = targetLeft;
            }
            finally
            {
                _isAdjustingForRightAnchor = false;
            }
        }

        private void UpdateRightAnchorFromCurrentBounds()
        {
            double width = GetCurrentWindowWidth();
            if (width <= 0)
                return;

            _rightAnchor = Left + width;
        }

        private double GetCurrentWindowWidth()
        {
            if (ActualWidth > 0)
                return ActualWidth;

            if (RenderSize.Width > 0)
                return RenderSize.Width;

            if (!double.IsNaN(Width) && Width > 0)
                return Width;

            return 0;
        }
        private void UpdateWidthLimit()
        {
            double maxWidth = SystemParameters.WorkArea.Width - 24.0;
            if (maxWidth < MinWidth)
                maxWidth = MinWidth;

            MaxWidth = maxWidth;
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

            UpdateRightAnchorFromCurrentBounds();
            PersistPosition();
        }

        private void PersistPosition()
        {
            if (!_isReady || DataContext is not ExpTrackerViewModel)
                return;

            if (WindowState == WindowState.Minimized)
                return;

            if (Owner is MainWindow main && main.DataContext is ChatSettings settings)
            {
                settings.ExpTrackerWindowLeft = Left;
                settings.ExpTrackerWindowTop = Top;
                settings.ExpTrackerWindowRight = _rightAnchor ?? (Left + ActualWidth);
                ConfigService.SaveDeferred(settings);
            }
        }
    }
}


