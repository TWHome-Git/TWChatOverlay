using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 필드 보스 알림 팝업. 도움말 예시와 같은 다크·민트 토스트 스타일로
    /// "필드 보스 / 아칸 등장 3분 전"을 표시하고, 보스 출현 5초 후 자동으로 닫힌다.
    /// 같은 창을 재사용해 이후 알림(1분 전/5초 전)이 오면 문구만 갱신한다.
    /// </summary>
    public sealed class BossAlertToastWindow : Window
    {
        private static BossAlertToastWindow? _instance;

        private static readonly Color DangerCol = Color.FromRgb(0xFF, 0x5A, 0x5A);

        private readonly TextBlock _bodyText;
        private readonly DispatcherTimer _closeTimer;

        public BossAlertToastWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowFontService.Apply(this);

            // 던전 도우미(던전 카운트) 알림 창과 같은 시각 언어:
            // 가운데 정렬 민트 타이틀 + 크고 굵은 본문, 둥근 어두운 판
            var title = new TextBlock
            {
                Text = "필드 보스",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
            };
            title.SetResourceReference(TextBlock.ForegroundProperty, "OverlayTitleAccentTextBrush");

            _bodyText = new TextBlock
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
            };
            _bodyText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var stack = new StackPanel { MinWidth = 150 };
            stack.Children.Add(title);
            stack.Children.Add(_bodyText);

            var root = new Border
            {
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 11),
                Child = stack,
            };
            root.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
            root.SetResourceReference(Border.BorderBrushProperty, "OverlayAccentBorderBrush");
            Content = root;

            // 잡고 끌어서 옮길 수 있게 (위치는 세션 내에서만 유지)
            root.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                {
                    try { DragMove(); } catch { }
                }
            };

            _closeTimer = new DispatcherTimer();
            _closeTimer.Tick += (_, _) =>
            {
                _closeTimer.Stop();
                try { Close(); } catch { }
            };
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW);
            }
            catch { }
        }

        /// <summary>알림 문구 설정. 5초 전(등장 임박)은 붉은색으로 강조한다.</summary>
        public void SetAlert(string bossName, string label)
        {
            bool imminent = string.Equals(label, "5초 전", StringComparison.Ordinal);
            _bodyText.Text = imminent ? $"{bossName} 곧 등장!" : $"{bossName} 등장 {label}";
            if (imminent)
                _bodyText.Foreground = new SolidColorBrush(DangerCol);
            else
                _bodyText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        }

        /// <summary>
        /// 알림 팝업 표시(싱글턴 재사용). 보스 출현 시각 + 5초에 자동으로 닫는다.
        /// 이후 단계 알림이 오면 문구를 갱신하고 닫힘 시각을 다시 계산한다.
        /// </summary>
        public static void ShowAlert(string bossName, string label, DateTime occurrence)
        {
            if (TrayAllWindowsService.IsTrayed)
                return; // 트레이 최소화 중에는 알림 창을 띄우지 않는다

            try
            {
                bool isNew = _instance == null || !_instance.IsLoaded;
                if (isNew)
                {
                    _instance = new BossAlertToastWindow();
                    _instance.Closed += (_, _) => _instance = null;
                }

                var window = _instance!;
                window.SetAlert(bossName, label);

                // 보스 출현 5초 후 삭제 (이미 지났으면 5초만 유지)
                TimeSpan lifetime = occurrence.AddSeconds(5) - DateTime.Now;
                if (lifetime < TimeSpan.FromSeconds(5))
                    lifetime = TimeSpan.FromSeconds(5);
                window._closeTimer.Stop();
                window._closeTimer.Interval = lifetime;
                window._closeTimer.Start();

                if (!window.IsVisible)
                {
                    window.Show();
                    if (isNew)
                    {
                        // 처음 뜰 때만 기본 위치(작업 영역 상단 중앙) — 이후엔 사용자가 끈 위치 유지
                        Rect workArea = SystemParameters.WorkArea;
                        window.Left = workArea.Left + ((workArea.Width - window.ActualWidth) / 2.0);
                        window.Top = workArea.Top + 120;
                    }
                }

                TopmostWindowHelper.BringToTopmost(window);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to show boss alert toast.", ex);
            }
        }
    }
}
