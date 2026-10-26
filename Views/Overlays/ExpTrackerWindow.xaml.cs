using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.ViewModels;

namespace TWChatOverlay.Views
{
    public partial class ExpTrackerWindow : OverlayWindowBase
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
            PersistBoundsNow();
        }

        /// <summary>복원 경로(베이스): 오른쪽 가장자리 고정(Right)이 있으면 그 기준으로 Left를 잡는다.</summary>
        public override void ApplyStoredBounds(ChatSettings? settings = null)
        {
            settings ??= ResolveSettings();
            if (settings == null)
                return;

            _storedLeft = settings.ExpTrackerWindowLeft;
            _storedTop = settings.ExpTrackerWindowTop;
            _storedRight = settings.ExpTrackerWindowRight;

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

        // 오른쪽 앵커 기준 위치 로직이 따로 있어 베이스의 자동 영속화는 끄고, 위 LocationChanged·드래그 끝·닫힘에서 저장한다
        protected override bool PersistBoundsOnChange => false;
        // 잠금 중에도 클릭을 받는다 — 리셋 버튼을 눌러야 하므로 다른 알림 창과 달리 클릭 통과(PassthroughWhenLocked)를 쓰지 않는다

        // 잠금 해제 모드에서는 창 어디를 잡아도 선택+드래그 가능 (창 전체 Preview에서 받는다)
        private void UnlockDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => TryBeginDrag(e, markHandled: true);

        protected override void OnDragCompleted()
        {
            UpdateRightAnchorFromCurrentBounds();
            PersistBoundsNow();
        }

        protected override bool PersistBounds(ChatSettings settings)
        {
            if (!_isReady || DataContext is not ExpTrackerViewModel)
                return false;

            settings.ExpTrackerWindowLeft = Left;
            settings.ExpTrackerWindowTop = Top;
            settings.ExpTrackerWindowRight = _rightAnchor ?? (Left + ActualWidth);
            return true;
        }

    }
}


