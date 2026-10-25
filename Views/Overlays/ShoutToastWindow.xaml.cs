using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class ShoutToastWindow : ToastWindowBase
    {
        private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex ShoutPrefixRegex = new(
            @"^(?:\[\s*(?:\d{1,2}:\d{2}(?::\d{2})?|\d{1,2}\s*시\s*\d{1,2}\s*분(?:\s*\d{1,2}\s*초)?)\s*\]\s*)?(?:외치기|shout)\s*:\s*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WhitespaceRunRegex = new(@"\s+", RegexOptions.Compiled);
        // ShoutPrefixRegex와 달리 '외치기:'가 뒤따르지 않아도 맨 앞의 시각 괄호를 여러 개까지 지운다
        private static readonly Regex LeadingTimeBracketsRegex = new(
            @"^(?:\[\s*(?:\d{1,2}:\d{2}(?::\d{2})?|\d{1,2}\s*시\s*\d{1,2}\s*분(?:\s*\d{1,2}\s*초)?)\s*\]\s*)+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex RepeatedShoutLabelRegex = new(
            @"^\s*(?:외치기|shout)\s*:\s*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private const double ScreenEdgePadding = 16;
        private const double BaseToastWidth = 420;
        private ChatSettings _settings;

        public ShoutToastWindow(string message, FontFamily fontFamily, ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            InitializeComponent();

            FontFamily = fontFamily;
            ToastText.FontFamily = fontFamily;
            ApplyToastSettings();
            ToastText.Text = NormalizeMessage(message);
            ApplyLayoutConstraints();
        }

        public void SetSettings(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            ApplyToastSettings();
        }

        /// <summary>잠금 해제 인스펙터에서 폰트 크기 변경 시 즉시 반영.</summary>
        public void SetFontSize(double size)
        {
            ToastText.FontSize = size;
        }

        /// <summary>목표 자리에 슬라이드 등장. 폭(BaseToastWidth) 기준으로 가운데 맞추고 가상 데스크톱 안으로 Left를 클램프한다.</summary>
        public void ShowAnimated(double targetLeft, double targetTop, int durationSeconds = 5)
        {
            double centerX = targetLeft + (BaseToastWidth / 2.0);
            ShowSlideIn(targetLeft, targetTop, offset: 18);
            UpdateLayout();
            Left = ClampLeftToWorkArea(centerX - (ActualWidth / 2.0));
            BringToFront();

            StartAutoClose(durationSeconds);
        }

        protected override bool ApplyAppFont => false;          // 생성자 인자의 폰트를 쓴다
        protected override bool UseToolWindowStyle => true;
        // 알림 스택이 자리를 정하고 늘 설정 창 위에 떠 있던 창이라 설정 창 아래 배치 등록은 하지 않는다 (기존 z-순서 유지)
        protected override bool KeepBelowSettingsHost => false;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            SetMousePassthrough(true); // 늘 클릭 통과 — 선택 하이라이트만 받는다
        }

        /// <summary>실제 알림은 스택이 배치하므로 끌 수 없다. 선택 하이라이트만 허용.</summary>
        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            AppServices.Get<UiLockService>().Select(this);
        }

        private void ApplyToastSettings()
        {
            ToastText.FontSize = _settings.ShoutToastFontSize;
            ApplyLayoutConstraints();
        }

        private void ApplyLayoutConstraints()
        {
            var area = SystemParameters.WorkArea;
            MaxWidth = Math.Max(MinWidth, area.Width - (ScreenEdgePadding * 2));
            ToastText.MaxWidth = Math.Max(100, MaxWidth - 52);
        }

        private double ClampLeftToWorkArea(double requestedLeft)
        {
            // 주 모니터 WorkArea가 아니라 가상 데스크톱 전체를 기준으로 클램프해야
            // 보조 모니터에 놓인 토스트가 주 모니터로 끌려오지 않는다.
            var area = ScreenBoundsHelper.GetVirtualDesktop();
            double width = ActualWidth > 0 ? ActualWidth : MinWidth;
            double minLeft = area.Left + ScreenEdgePadding;
            double maxLeft = area.Right - width - ScreenEdgePadding;
            if (maxLeft < minLeft)
                return minLeft;

            return Math.Max(minLeft, Math.Min(maxLeft, requestedLeft));
        }

        private static string NormalizeMessage(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string decoded = WebUtility.HtmlDecode(text).Replace("&nbsp;", " ");
            decoded = HtmlTagRegex.Replace(decoded, " ");
            decoded = WhitespaceRunRegex.Replace(decoded, " ").Trim();
            decoded = LeadingTimeBracketsRegex.Replace(decoded, string.Empty);
            decoded = ShoutPrefixRegex.Replace(decoded, string.Empty).Trim();
            decoded = RepeatedShoutLabelRegex.Replace(decoded, string.Empty).Trim();
            return decoded;
        }
    }
}
