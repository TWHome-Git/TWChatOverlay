using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>아이템 획득 토스트. 클릭을 통과시키고 잠시 뒤 스스로 닫힌다. 위치는 알림 스택이 정한다.</summary>
    public partial class ItemDropToastWindow : OverlayWindowBase
    {
        private readonly DispatcherTimer _lifetimeTimer;

        public ItemDropToastWindow(string itemName, ItemDropGrade grade, FontFamily fontFamily)
        {
            InitializeComponent();
            FontFamily = fontFamily;
            ItemNameText.FontFamily = fontFamily;
            ItemNameText.Text = $"[{itemName}] 획득";

            var sharedSettings = ToastPresentationHelper.FindSharedSettings();
            if (sharedSettings != null)
                ItemNameText.FontSize = sharedSettings.ItemDropToastFontSize;

            // 아이콘이 등록된 아이템이면 맨 앞에 이미지 표시
            try
            {
                string? iconUri = Models.ItemCalendarEntryViewModel.GetIconUri(itemName);
                if (iconUri != null)
                {
                    ItemIcon.Source = new System.Windows.Media.Imaging.BitmapImage(new Uri(iconUri));
                    ItemIcon.Visibility = Visibility.Visible;
                }
            }
            catch { }
            ItemNameText.Foreground = grade switch
            {
                ItemDropGrade.Rare => new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A)),
                ItemDropGrade.Special => new SolidColorBrush(Color.FromRgb(0xFF, 0x7E, 0xDB)),
                _ => Brushes.White
            };

            _lifetimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(2400)
            };
            _lifetimeTimer.Tick += (_, _) =>
            {
                _lifetimeTimer.Stop();
                StartCloseAnimation();
            };
        }

        protected override bool ApplyAppFont => false;          // 생성자 인자의 폰트를 쓴다
        protected override bool UseToolWindowStyle => true;
        protected override bool PersistBoundsOnChange => false; // 위치는 스택이 정한다

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            SetMousePassthrough(true);
        }

        public void ShowAnimated(double targetLeft, double targetTop)
        {
            Left = targetLeft;
            Top = targetTop - 24;
            Opacity = 0;
            Show();
            BringToFront();

            var topAnim = new DoubleAnimation
            {
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(TopProperty, topAnim);

            // 페이드 인 목표를 창별 저장 투명도로 (잠금 해제 인스펙터에서 지정한 값)
            UiLockService.ApplyStoredOpacity(this);
            double targetOpacity = Opacity > 0 ? Opacity : 1.0;
            Opacity = 0;
            var opacityAnim = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(180)
            };
            BeginAnimation(OpacityProperty, opacityAnim);

            _lifetimeTimer.Start();
        }

        public void MoveTo(double targetTop)
        {
            var topAnim = new DoubleAnimation
            {
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(TopProperty, topAnim);
        }

        private void StartCloseAnimation()
        {
            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }
    }
}
