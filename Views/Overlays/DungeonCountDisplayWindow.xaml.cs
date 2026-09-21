using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>던전 카운트 알림창 (어밴던로드·갈망하는 즐거움 등). 일정 시간 뒤 스스로 닫힌다.</summary>
    public partial class DungeonCountDisplayWindow : OverlayWindowBase
    {
        private readonly DispatcherTimer _closeTimer;
        private ChatSettings _settings;
        private bool _isClosing;

        public DungeonCountDisplayWindow(string message, FontFamily fontFamily, int durationSeconds, ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            InitializeComponent();
            FontFamily = fontFamily;
            MessageTextBlock.FontFamily = fontFamily;
            MessageTextBlock.FontSize = settings.DungeonCountDisplayFontSize;
            MessageTextBlock.Text = message;

            _closeTimer = new DispatcherTimer
            {
                Interval = durationSeconds > 0
                    ? TimeSpan.FromSeconds(Math.Max(1, Math.Min(300, durationSeconds)))
                    : TimeSpan.Zero
            };
            _closeTimer.Tick += (_, _) =>
            {
                _closeTimer.Stop();
                CloseAnimated();
            };
        }

        // 폰트는 생성자 인자로 받은 것을 쓴다 (앱 공통 폰트 자동 적용 안 함)
        protected override bool ApplyAppFont => false;
        protected override bool UseToolWindowStyle => true;
        protected override ChatSettings? ResolveSettings() => _settings;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            SetMousePassthrough(false); // 알림창은 클릭을 받아야 잠금 해제 편집이 된다
        }

        public void SetSettings(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            MessageTextBlock.FontSize = _settings.DungeonCountDisplayFontSize;
            SetExStyleFlags(add: NativeMethods.WS_EX_TOOLWINDOW, remove: NativeMethods.WS_EX_TRANSPARENT);
        }

        /// <summary>잠금 해제 인스펙터에서 폰트 크기 변경 시 즉시 반영.</summary>
        public void SetFontSize(double size)
        {
            MessageTextBlock.FontSize = size;
            PreviewLabel.FontSize = size;
        }

        /// <summary>메시지 왼쪽 아이콘 (금화 주머니 카운트 등). null이면 숨김.</summary>
        public void SetIcon(string? packUri)
        {
            try
            {
                if (string.IsNullOrEmpty(packUri))
                {
                    MessageIcon.Visibility = Visibility.Collapsed;
                    MessageIcon.Source = null;
                    return;
                }

                MessageIcon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(packUri, UriKind.Absolute));
                MessageIcon.Visibility = Visibility.Visible;
            }
            catch
            {
                MessageIcon.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>위치 미리보기: 통일 라벨("던전 카운트 알림창")만 표시.</summary>
        public void SetPreviewMode(bool isPreview)
        {
            NormalContent.Visibility = isPreview ? Visibility.Collapsed : Visibility.Visible;
            PreviewLabel.Visibility = isPreview ? Visibility.Visible : Visibility.Collapsed;
        }

        public void UpdateDisplay(string message, int durationSeconds)
        {
            _isClosing = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            MessageTextBlock.Text = message;
            _closeTimer.Stop();
            if (durationSeconds > 0)
            {
                _closeTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, Math.Min(300, durationSeconds)));
                _closeTimer.Start();
            }
            BringToFront();
        }

        public void ShowDisplay(double left, double top)
        {
            Left = left;
            Top = top;
            Show();
            BringToFront();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            if (_closeTimer.Interval > TimeSpan.Zero)
                _closeTimer.Start();
        }

        public void MoveTo(double top)
        {
            BeginAnimation(TopProperty, new DoubleAnimation(Top, top, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        protected override void OnClosed(EventArgs e)
        {
            PersistBoundsNow();
            base.OnClosed(e);
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsVisible) return;
            TryBeginDrag(e);
        }

        protected override bool PersistBounds(ChatSettings settings)
        {
            if (!IsVisible)
                return false;

            settings.DungeonCountDisplayWindowLeft = Left;
            settings.DungeonCountDisplayWindowTop = Top;
            return true;
        }

        private void CloseAnimated()
        {
            if (_isClosing)
                return;

            _isClosing = true;
            var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
            animation.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, animation);
        }
    }
}
