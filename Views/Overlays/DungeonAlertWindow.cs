using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>던전 알림을 띄우는 쪽. 창은 하나뿐이고, 지금 누가 쓰고 있는지 가려내는 데 쓴다.</summary>
    public enum DungeonAlertSource
    {
        /// <summary>어비스 감전·반사 패턴.</summary>
        AbyssPattern,
        /// <summary>오를리 방어전 남은 공격 횟수.</summary>
        OrlyAttack,
        /// <summary>베스티지 성난 빅테디 출현.</summary>
        VestigeBoss,
    }

    /// <summary>
    /// 던전 알림 창 하나 (어비스 감전·반사, 오를리 남은 공격, 베스티지 빅테디 출현).
    /// 서로 다른 던전의 알림이라 같이 뜰 일이 없으므로 창과 자리를 하나로 쓴다 —
    /// 자리를 한 번만 잡으면 어느 던전에서든 같은 곳에 뜬다.
    /// 통합 알림 스택에 얹지 않고, 잠금 해제 모드에서 끌어 옮기면 그 위치가 설정에 남는다.
    ///
    /// 감전·빅테디는 상태라 풀릴 때까지 떠 있고(<see cref="Show"/> → <see cref="HideAlert"/>),
    /// 반사·오를리는 정해진 시간만 떠 있다(<see cref="Flash"/>).
    /// 내릴 때는 띄운 쪽을 함께 넘겨, 그 사이 다른 던전 알림이 창을 가져갔으면 건드리지 않는다.
    /// </summary>
    public sealed class DungeonAlertWindow : OverlayWindowBase
    {
        private static DungeonAlertWindow? _instance;

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
        /// <summary>지금 창에 떠 있는 내용을 띄운 쪽.</summary>
        private DungeonAlertSource _owner;

        protected override bool UseToolWindowStyle => true;
        protected override ChatSettings? ResolveSettings() => _settings;

        private DungeonAlertWindow(ChatSettings? settings)
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
            };

            _bodyText.Foreground = new SolidColorBrush(DangerColor);

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

            settings.DungeonAlertWindowLeft = Left;
            settings.DungeonAlertWindowTop = Top;
            return true;
        }

        // ===== 표시 =====

        /// <summary>풀릴 때까지 떠 있는 알림 (감전·빅테디 출현).</summary>
        public static void Show(DungeonAlertSource source, ChatSettings? settings, string title, string message)
            => ShowInternal(source, settings, title, message, SafetyLifetime, isPreview: false);

        /// <summary>정해진 시간 동안만 떠 있는 알림 (반사·오를리). 패턴이 도는 시간과 같게 준다.</summary>
        public static void Flash(DungeonAlertSource source, ChatSettings? settings, string title, string message, TimeSpan lifetime)
            => ShowInternal(source, settings, title, message, lifetime, isPreview: false);

        private static void ShowInternal(DungeonAlertSource source, ChatSettings? settings, string title, string message, TimeSpan lifetime, bool isPreview)
        {
            if (!isPreview && AppServices.Get<TrayAllWindowsService>().IsTrayed)
                return;

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_instance == null || !_instance.IsLoaded)
                    {
                        var created = new DungeonAlertWindow(settings);
                        created.Closed += (_, _) =>
                        {
                            if (ReferenceEquals(_instance, created))
                                _instance = null;
                        };
                        _instance = created;
                    }

                    var window = _instance;
                    window._isPreview = isPreview;
                    window._owner = source;
                    window._titleText.Text = title;
                    window._bodyText.Text = message;
                    ApplyBodyColor(window, source, isPreview);

                    if (!window.IsVisible)
                    {
                        var (left, top) = ToastPresentationHelper.ResolveBasePosition(
                            settings?.DungeonAlertWindowLeft, settings?.DungeonAlertWindowTop, DefaultWidth, DefaultTop);
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
                    AppLogger.Warn("Failed to show dungeon alert.", ex);
                }
            }));
        }

        /// <summary>경고(감전·반사·빅테디)는 붉은색, 세는 숫자(오를리)는 다른 곳의 횟수와 같은 색. 자리 잡기 미리보기는 경고가 아니므로 평소 글자색.</summary>
        private static void ApplyBodyColor(DungeonAlertWindow window, DungeonAlertSource source, bool isPreview)
        {
            if (isPreview)
                window._bodyText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            else if (source == DungeonAlertSource.OrlyAttack)
                window._bodyText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayRareAccentBrush");
            else
                window._bodyText.Foreground = new SolidColorBrush(DangerColor);
        }

        /// <summary>
        /// 알림을 내린다 (감전이 풀렸을 때, 빅테디를 잡았을 때, 또는 상한 시간이 지났을 때).
        /// 그 사이 다른 던전 알림이 창을 가져갔으면 그대로 둔다.
        /// </summary>
        public static void HideAlert(DungeonAlertSource source)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _instance;
                if (window == null || window._owner != source)
                    return;

                try
                {
                    window._closeTimer.Stop();
                    _instance = null;
                    window.Close();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to hide dungeon alert.", ex);
                }
            }));
        }

        // ===== 잠금 해제 위치 조정 =====

        /// <summary>잠금 해제 모드: 자리를 잡을 수 있게 띄운다. 저절로 닫히지 않는다.</summary>
        public static void ShowPositionPreview(ChatSettings settings)
        {
            if (settings == null)
                return;

            // 네 알림이 한 창을 쓰므로, 특정 알림 문구 대신 창이 무엇인지와 누가 쓰는지를 적는다
            ShowInternal(DungeonAlertSource.AbyssPattern, settings,
                "감전 · 반사 · 오를리 · 빅테디", "던전 특수 알림", TimeSpan.Zero, isPreview: true);
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
