using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class ItemDropToastWindow : Window
    {
        private readonly DispatcherTimer _lifetimeTimer;

        public ItemDropToastWindow(string itemName, ItemDropGrade grade, FontFamily fontFamily)
        {
            InitializeComponent();
            SettingsHostZOrder.Register(this); // 설정 창이 열려 있으면 그 아래로 표시
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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyMousePassthroughStyle();
        }

        public void ShowAnimated(double targetLeft, double targetTop)
        {
            Left = targetLeft;
            Top = targetTop - 24;
            Opacity = 0;
            Show();
            TopmostWindowHelper.BringToTopmost(this);

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

        private void ApplyMousePassthroughStyle()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW);
            }
            catch { }
        }
    }
}
