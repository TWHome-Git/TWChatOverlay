using System;
using System.Windows;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>아이템 획득 토스트. 클릭을 통과시키고 잠시 뒤 스스로 닫힌다. 위치는 알림 스택이 정한다.</summary>
    public partial class ItemDropToastWindow : ToastWindowBase
    {
        private static readonly TimeSpan Lifetime = TimeSpan.FromMilliseconds(2400);

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
                    ItemIcon.Source = PackImageCache.Get(iconUri);
                    ItemIcon.Visibility = Visibility.Visible;
                }
            }
            catch { }
            ItemNameText.Foreground = (Brush?)ChatBrushResolver.ForItemGrade(grade) ?? Brushes.White;
        }

        protected override bool ApplyAppFont => false;          // 생성자 인자의 폰트를 쓴다
        protected override bool UseToolWindowStyle => true;
        protected override TimeSpan FadeOutDuration => TimeSpan.FromMilliseconds(260);

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            SetMousePassthrough(true);
        }

        public void ShowAnimated(double targetLeft, double targetTop)
        {
            // 페이드 인 목표를 창별 저장 투명도로 (잠금 해제 인스펙터에서 지정한 값)
            UiLockService.ApplyStoredOpacity(this);
            double targetOpacity = Opacity > 0 ? Opacity : 1.0;

            ShowSlideIn(targetLeft, targetTop, offset: 24, targetOpacity: targetOpacity);
            BringToFront();
            StartAutoClose(Lifetime);
        }
    }
}
