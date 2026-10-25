using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TWChatOverlay.Models;

namespace TWChatOverlay.Views
{
    /// <summary>던전 카운트 알림창 (어밴던로드·갈망하는 즐거움 등). 일정 시간 뒤 스스로 닫힌다. 위치는 알림 스택이 정한다.</summary>
    public partial class DungeonCountDisplayWindow : ToastWindowBase
    {
        private ChatSettings _settings;
        private readonly TimeSpan _initialLifetime;

        public DungeonCountDisplayWindow(string message, FontFamily fontFamily, int durationSeconds, ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            InitializeComponent();
            FontFamily = fontFamily;
            MessageTextBlock.FontFamily = fontFamily;
            MessageTextBlock.FontSize = settings.DungeonCountDisplayFontSize;
            MessageTextBlock.Text = message;
            _initialLifetime = ClampLifetime(durationSeconds);
        }

        // 폰트는 생성자 인자로 받은 것을 쓴다 (앱 공통 폰트 자동 적용 안 함)
        protected override bool ApplyAppFont => false;
        protected override bool UseToolWindowStyle => true;
        protected override ChatSettings? ResolveSettings() => _settings;
        /// <summary>표시만 하는 창. 잠금 중엔 클릭이 아래 게임으로 가고, 잠금 해제 때만 클릭을 받아 편집된다.</summary>
        protected override bool PassthroughWhenLocked => true;
        protected override TimeSpan FadeOutDuration => TimeSpan.FromMilliseconds(180);

        public void SetSettings(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            MessageTextBlock.FontSize = _settings.DungeonCountDisplayFontSize;
            SetExStyleFlags(add: NativeMethods.WS_EX_TOOLWINDOW);
            ApplyLockPassthrough();
        }

        /// <summary>잠금 해제 인스펙터에서 폰트 크기 변경 시 즉시 반영.</summary>
        public void SetFontSize(double size)
        {
            MessageTextBlock.FontSize = size;
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

                MessageIcon.Source = Services.PackImageCache.Get(packUri);
                MessageIcon.Visibility = Visibility.Visible;
            }
            catch
            {
                MessageIcon.Visibility = Visibility.Collapsed;
            }
        }

        public void UpdateDisplay(string message, int durationSeconds)
        {
            ResetPresentation();
            MessageTextBlock.Text = message;
            StartAutoClose(durationSeconds);
            BringToFront();
        }

        public void ShowDisplay(double left, double top)
        {
            Left = left;
            Top = top;
            Show();
            BringToFront();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            StartAutoClose(_initialLifetime);
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!IsVisible) return;
            TryBeginDrag(e);
        }

        /// <summary>
        /// 잠금 해제 중 끌어 옮긴 자리만 저장한다 (TryBeginDrag 경로). 스택이 배치한 좌표는 LocationChanged·닫힘에서 되쓰지 않는다 —
        /// 분리 모드에서는 이 키가 그 그룹의 앵커라, 둘째 창의 자리가 저장되면 다음 배치 때 전체가 아래로 밀렸다.
        /// </summary>
        protected override bool PersistBounds(ChatSettings settings)
        {
            if (!IsVisible)
                return false;

            settings.DungeonCountDisplayWindowLeft = Left;
            settings.DungeonCountDisplayWindowTop = Top;
            return true;
        }
    }
}
