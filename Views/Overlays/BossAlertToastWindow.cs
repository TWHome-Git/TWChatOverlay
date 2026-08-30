using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 필드 보스 알림 팝업. 던전 도우미 알림 창과 같은 시각 언어(가운데 민트 타이틀 + 굵은 본문)로
    /// "필드 보스 / 아칸 등장 3분 전"을 표시하고, 보스 출현 5초 후 자동으로 닫힌다.
    /// 같은 창을 재사용해 이후 알림(1분 전/5초 전)이 오면 문구만 갱신한다.
    /// 잠금 해제 모드에서 위치를 조정할 수 있고, 위치는 설정에 저장된다.
    /// </summary>
    public sealed class BossAlertToastWindow : Window
    {
        private static BossAlertToastWindow? _instance;
        private static bool _isPreviewShowing;

        private static readonly Color DangerCol = Color.FromRgb(0xFF, 0x5A, 0x5A);

        private readonly Grid _normalContent;
        private readonly TextBlock _bodyText;
        private readonly TextBlock _previewLabel;
        private readonly DispatcherTimer _closeTimer;
        private ChatSettings? _settings;
        private bool _isDragging;

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

            _normalContent = new Grid();
            _normalContent.Children.Add(stack);

            // 잠금 해제(위치 미리보기) 공통 라벨
            _previewLabel = new TextBlock
            {
                Text = "필드 보스 알림창",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            };
            _previewLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var rootGrid = new Grid { MinWidth = 150, MinHeight = 44 };
            rootGrid.Children.Add(_normalContent);
            rootGrid.Children.Add(_previewLabel);

            var root = new Border
            {
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 11),
                Child = rootGrid,
            };
            root.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
            root.SetResourceReference(Border.BorderBrushProperty, "OverlayAccentBorderBrush");
            Content = root;

            // 잠금 해제 모드에서만 드래그로 이동 (위치는 설정에 저장)
            root.MouseLeftButtonDown += RootBorder_MouseLeftButtonDown;
            LocationChanged += (_, _) => SyncPositionToSettings(save: false);

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

        protected override void OnClosed(EventArgs e)
        {
            SyncPositionToSettings(save: true);
            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
                _isPreviewShowing = false;
            }
            base.OnClosed(e);
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!UiLockService.IsUnlocked) return;
            UiLockService.Select(this);
            if (e.ButtonState != MouseButtonState.Pressed || !IsVisible)
                return;

            _isDragging = true;
            try { DragMove(); } catch { }
            finally
            {
                _isDragging = false;
                SyncPositionToSettings(save: true);
            }
        }

        private void SyncPositionToSettings(bool save)
        {
            if (_settings == null || !IsVisible)
                return;

            _settings.BossAlertToastWindowLeft = Left;
            _settings.BossAlertToastWindowTop = Top;

            if (_isDragging || save)
                ConfigService.SaveDeferred(_settings);
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

        /// <summary>잠금 해제 인스펙터에서 폰트 크기 변경 시 즉시 반영.</summary>
        public void SetFontSize(double size)
        {
            _bodyText.FontSize = size;
            _previewLabel.FontSize = size;
        }

        /// <summary>위치 미리보기: 통일 라벨("필드 보스 알림창")만 표시.</summary>
        public void SetPreviewMode(bool isPreview)
        {
            _normalContent.Visibility = isPreview ? Visibility.Collapsed : Visibility.Visible;
            _previewLabel.Visibility = isPreview ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 알림 팝업 표시(싱글턴 재사용). 보스 출현 시각 + 5초에 자동으로 닫는다.
        /// 이후 단계 알림이 오면 문구를 갱신하고 닫힘 시각을 다시 계산한다.
        /// </summary>
        public static void ShowAlert(string bossName, string label, DateTime occurrence, ChatSettings? settings)
        {
            if (TrayAllWindowsService.IsTrayed)
                return; // 트레이 최소화 중에는 알림 창을 띄우지 않는다

            try
            {
                var window = EnsureInstance(settings);
                window.SetAlert(bossName, label);
                window.SetPreviewMode(false);
                _isPreviewShowing = false;

                // 보스 출현 5초 후 삭제 (이미 지났으면 5초만 유지)
                TimeSpan lifetime = occurrence.AddSeconds(5) - DateTime.Now;
                if (lifetime < TimeSpan.FromSeconds(5))
                    lifetime = TimeSpan.FromSeconds(5);
                window._closeTimer.Stop();
                window._closeTimer.Interval = lifetime;
                window._closeTimer.Start();

                ShowAtStoredPosition(window, settings);
                TopmostWindowHelper.BringToTopmost(window);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to show boss alert toast.", ex);
            }
        }

        /// <summary>잠금 해제 모드: 위치 조정용 미리보기를 표시한다.</summary>
        public static void ShowPositionPreview(ChatSettings settings)
        {
            try
            {
                var window = EnsureInstance(settings);
                window._closeTimer.Stop(); // 미리보기 동안에는 자동 닫힘 없음
                window.SetPreviewMode(true);
                _isPreviewShowing = true;

                ShowAtStoredPosition(window, settings);
                TopmostWindowHelper.BringToTopmost(window);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to show boss alert toast preview.", ex);
            }
        }

        /// <summary>잠금 해제 종료: 미리보기였다면 위치를 저장하고 닫는다.</summary>
        public static void ClosePositionPreview()
        {
            try
            {
                if (_instance == null || !_isPreviewShowing)
                    return;

                _instance.SyncPositionToSettings(save: true);
                _instance.Close();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to close boss alert toast preview.", ex);
            }
        }

        private static BossAlertToastWindow EnsureInstance(ChatSettings? settings)
        {
            if (_instance == null || !_instance.IsLoaded)
            {
                _instance = new BossAlertToastWindow();
                _instance.Closed += (_, _) => { };
            }

            if (settings != null)
            {
                _instance._settings = settings;
                _instance.SetFontSize(settings.BossAlertToastFontSize);
            }
            return _instance;
        }

        private static void ShowAtStoredPosition(BossAlertToastWindow window, ChatSettings? settings)
        {
            bool wasVisible = window.IsVisible;
            if (!wasVisible)
                window.Show();

            if (!wasVisible)
            {
                if (settings?.BossAlertToastWindowLeft is double left &&
                    settings.BossAlertToastWindowTop is double top)
                {
                    window.Left = left;
                    window.Top = top;
                }
                else
                {
                    // 기본 위치: 작업 영역 상단 중앙
                    Rect workArea = SystemParameters.WorkArea;
                    window.Left = workArea.Left + ((workArea.Width - window.ActualWidth) / 2.0);
                    window.Top = workArea.Top + 120;
                }
            }
        }
    }
}
