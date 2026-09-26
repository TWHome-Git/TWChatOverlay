using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 어비스 패턴 알림 창 (감전·반사). 다른 알림 스택에 얹지 않고 자기 자리를 따로 가진다.
    /// 잠금 해제 모드에서 끌어 옮기면 그 위치가 설정에 남는다.
    ///
    /// 감전은 상태라 풀릴 때까지 떠 있고(<see cref="Show"/> → <see cref="Hide"/>),
    /// 반사는 순간 알림이라 잠깐 떴다 사라진다(<see cref="Flash"/>).
    /// </summary>
    public sealed class PatternAlertWindow : OverlayWindowBase
    {
        private static PatternAlertWindow? _instance;

        /// <summary>풀림 줄을 못 봤을 때 알림이 영영 남지 않도록 하는 상한.</summary>
        private static readonly TimeSpan SafetyLifetime = TimeSpan.FromMinutes(3);
        private const double DefaultWidth = 220;
        private const double DefaultTop = 200;

        private static readonly Color DangerColor = Color.FromRgb(0xFF, 0x5A, 0x5A);

        private readonly ChatSettings? _settings;
        private readonly TextBlock _titleText;
        private readonly TextBlock _bodyText;
        private readonly DispatcherTimer _closeTimer;
        private bool _isPreview;

        protected override bool UseToolWindowStyle => true;
        protected override ChatSettings? ResolveSettings() => _settings;

        private PatternAlertWindow(ChatSettings? settings)
        {
            _settings = settings;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            MinWidth = DefaultWidth;

            _titleText = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
            };
            _titleText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayTitleAccentTextBrush");

            _bodyText = new TextBlock
            {
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = new SolidColorBrush(DangerColor),
            };

            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(_titleText);
            stack.Children.Add(_bodyText);

            var root = new Border
            {
                BorderThickness = new Thickness(1.2),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(18, 10, 18, 11),
                Child = stack,
            };
            root.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
            root.SetResourceReference(Border.BorderBrushProperty, "OverlayAccentBorderBrush");
            Content = root;

            // 잠금 해제 모드에서 창을 잡고 옮긴다
            PreviewMouseLeftButtonDown += (_, e) => TryBeginDrag(e);

            _closeTimer = new DispatcherTimer();
            _closeTimer.Tick += (_, _) =>
            {
                _closeTimer.Stop();
                Hide();
            };
        }

        /// <summary>잠금 해제 중 옮긴 자리만 저장한다 (자동으로 뜰 때의 기본 자리는 저장하지 않는다).</summary>
        protected override bool PersistBounds(ChatSettings settings)
        {
            if (!IsVisible || !AppServices.Get<UiLockService>().IsUnlocked)
                return false;

            settings.PatternAlertWindowLeft = Left;
            settings.PatternAlertWindowTop = Top;
            return true;
        }

        // ===== 표시 =====

        /// <summary>풀릴 때까지 떠 있는 알림 (감전).</summary>
        public static void Show(ChatSettings? settings, string title, string message)
            => ShowInternal(settings, title, message, SafetyLifetime, isPreview: false);

        /// <summary>잠깐 떴다 사라지는 알림 (반사).</summary>
        public static void Flash(ChatSettings? settings, string title, string message, TimeSpan lifetime)
            => ShowInternal(settings, title, message, lifetime, isPreview: false);

        private static void ShowInternal(ChatSettings? settings, string title, string message, TimeSpan lifetime, bool isPreview)
        {
            if (!isPreview && AppServices.Get<TrayAllWindowsService>().IsTrayed)
                return;

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_instance == null || !_instance.IsLoaded)
                    {
                        var created = new PatternAlertWindow(settings);
                        created.Closed += (_, _) =>
                        {
                            if (ReferenceEquals(_instance, created))
                                _instance = null;
                        };
                        _instance = created;
                    }

                    var window = _instance;
                    window._isPreview = isPreview;
                    window._titleText.Text = title;
                    window._bodyText.Text = message;

                    if (!window.IsVisible)
                    {
                        var (left, top) = ToastPresentationHelper.ResolveBasePosition(
                            settings?.PatternAlertWindowLeft, settings?.PatternAlertWindowTop, DefaultWidth, DefaultTop);
                        window.Left = left;
                        window.Top = top;
                        window.Show();
                    }

                    window._closeTimer.Stop();
                    if (lifetime > TimeSpan.Zero)
                    {
                        window._closeTimer.Interval = lifetime;
                        window._closeTimer.Start();
                    }

                    TopmostWindowHelper.BringToTopmost(window);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to show pattern alert.", ex);
                }
            }));
        }

        /// <summary>알림을 닫는다 (감전이 풀렸을 때, 또는 상한 시간이 지났을 때).</summary>
        public static void HideAlert()
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _instance;
                if (window == null)
                    return;

                try
                {
                    window._closeTimer.Stop();
                    _instance = null;
                    window.Close();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to hide pattern alert.", ex);
                }
            }));
        }

        // ===== 잠금 해제 위치 조정 =====

        /// <summary>잠금 해제 모드: 자리를 잡을 수 있게 띄운다. 저절로 닫히지 않는다.</summary>
        public static void ShowPositionPreview(ChatSettings settings)
        {
            if (settings == null || (!settings.EnableDischargeAlert && !settings.EnableReflectionPatternAlert))
                return; // 두 알림이 다 꺼져 있으면 배치할 창도 없다

            ShowInternal(settings, "패턴 알림", "방전 상태", TimeSpan.Zero, isPreview: true);
        }

        /// <summary>잠금 해제 종료: 미리보기로 떠 있던 창만 닫는다.</summary>
        public static void ClosePositionPreview()
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_instance?._isPreview != true)
                    return;

                try { _instance.Close(); } catch { }
                _instance = null;
            }));
        }
    }
}
