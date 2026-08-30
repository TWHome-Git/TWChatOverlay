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

        private static readonly Color DangerCol = Color.FromRgb(0xFF, 0x5A, 0x5A);

        private readonly Grid _normalContent;
        private readonly TextBlock _bodyText;
        private readonly TextBlock _previewLabel;
        private readonly DispatcherTimer _closeTimer;
        private readonly DispatcherTimer _countdownTimer;
        private DateTime _countdownOccurrence;
        private string _countdownBossName = string.Empty;
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
            // 다른 알림 창(던전 도우미 등)과 같은 기본 크기 — 인스펙터에서 크기 조절·저장 가능
            Width = 420;
            Height = 72;
            MinWidth = 160;
            MinHeight = 56;
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
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0),
            };
            _bodyText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
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

            var rootGrid = new Grid();
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
            SizeChanged += (_, _) => SyncPositionToSettings(save: true);

            _closeTimer = new DispatcherTimer();
            _closeTimer.Tick += (_, _) =>
            {
                _closeTimer.Stop();
                try { Close(); } catch { }
            };

            _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _countdownTimer.Tick += (_, _) => UpdateCountdownText();
        }

        /// <summary>
        /// 초 단위 카운트다운 시작 ("아칸 등장 59초 전" → … → "5초 전"부터 붉은색).
        /// 출현 시각이 되면 '곧 등장!'으로 바뀐다. 1분 전 알림부터 사용한다.
        /// </summary>
        public void StartCountdown(string bossName, DateTime occurrence)
        {
            _countdownBossName = bossName;
            _countdownOccurrence = occurrence;
            UpdateCountdownText();
            _countdownTimer.Start();
        }

        private void UpdateCountdownText()
        {
            TimeSpan remaining = _countdownOccurrence - DateTime.Now;
            if (remaining <= TimeSpan.Zero)
            {
                _countdownTimer.Stop();
                _bodyText.Text = $"{_countdownBossName} 등장!";
                _bodyText.Foreground = new SolidColorBrush(DangerCol);
                return;
            }

            int totalSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
            if (totalSeconds <= 5)
            {
                // 등장 임박: "곧 등장!" + 초 카운트다운 (5초 → 1초)
                _bodyText.Text = $"{_countdownBossName} 곧 등장! {totalSeconds}초";
                _bodyText.Foreground = new SolidColorBrush(DangerCol);
            }
            else
            {
                _bodyText.Text = $"{_countdownBossName} 등장 {totalSeconds}초 전";
                _bodyText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            }
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
            try { _closeTimer.Stop(); } catch { }
            try { _countdownTimer.Stop(); } catch { }
            SyncPositionToSettings(save: true);
            if (ReferenceEquals(_instance, this))
            {
                _instance = null;
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
            // 위치는 통합 알림 스택(앵커)이 관리하므로 크기만 저장한다
            if (_settings == null || !IsVisible)
                return;

            _settings.BossAlertToastWindowWidth = Width;
            _settings.BossAlertToastWindowHeight = Height;

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
                // 3분 전: 정적 문구를 5초만 표시 후 닫는다. 1분 전부터는 초 단위 카운트다운.
                bool isThreeMinute = string.Equals(label, "3분 전", StringComparison.Ordinal);
                if (isThreeMinute)
                {
                    window._countdownTimer.Stop();
                    window.SetAlert(bossName, label);
                }
                else
                {
                    window.StartCountdown(bossName, occurrence);
                }
                window.SetPreviewMode(false);

                // 3분 전은 5초 뒤, 카운트다운은 보스 출현 5초 후 삭제 (이미 지났으면 5초만 유지)
                TimeSpan lifetime = isThreeMinute
                    ? TimeSpan.FromSeconds(5)
                    : occurrence.AddSeconds(5) - DateTime.Now;
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

        /// <summary>통합 알림 스택 앵커 미리보기로 위임.</summary>
        public static void ShowPositionPreview(ChatSettings settings)
            => ToastStackService.ShowPositionPreview(settings);

        /// <summary>설정 슬라이더 변경을 열려 있는 알림 창에 즉시 반영한다.</summary>
        public static void ApplyFontSize(double size)
        {
            try { _instance?.SetFontSize(size); } catch { }
        }

        /// <summary>통합 알림 스택 앵커 미리보기로 위임.</summary>
        public static void ClosePositionPreview()
            => ToastStackService.ClosePositionPreview();

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
            if (!window.IsVisible)
            {
                // 저장된 크기 복원 (기본 420x72)
                if (settings?.BossAlertToastWindowWidth is double width && width >= window.MinWidth)
                    window.Width = width;
                if (settings?.BossAlertToastWindowHeight is double height && height >= window.MinHeight)
                    window.Height = height;

                window.Show();
            }

            // 통합 알림 스택: 앵커 위치에서 다른 알림들 아래로 배치
            var (left, top) = ToastStackService.Attach(window);
            window.Left = left;
            window.Top = top;
        }
    }
}
