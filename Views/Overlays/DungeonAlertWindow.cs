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
    /// <summary>던전 알림의 종류. 종류마다 창이 따로 뜨고, 자리(설정)만 함께 쓴다.</summary>
    public enum DungeonAlertSource
    {
        /// <summary>어비스 감전(방전 상태). 풀릴 때까지 떠 있다.</summary>
        AbyssDischarge,
        /// <summary>어비스 반사 패턴. 정해진 시간만 떠 있다.</summary>
        AbyssReflection,
        /// <summary>오를리 방어전 남은 공격 횟수.</summary>
        OrlyAttack,
        /// <summary>베스티지 성난 빅테디 출현.</summary>
        VestigeBoss,
        /// <summary>심연의 보물창고 자동 퇴장 예고.</summary>
        TreasuryExit,
    }

    /// <summary>
    /// 던전 알림 창 (어비스 감전·반사, 오를리 남은 공격, 베스티지 빅테디 출현, 보물창고 종료).
    ///
    /// 종류마다 창을 따로 만들고, 자리 설정만 하나를 함께 쓴다 —
    /// 자리를 한 번 잡으면 어느 던전에서든 거기서부터 뜨고, 둘이 겹치면 아래로 쌓인다.
    /// 감전이 걸린 채 반사가 돌 때처럼 둘 다 유효한 상황에서 하나가 가려지지 않게 하기 위함이다.
    /// 통합 알림 스택에 얹지 않고, 잠금 해제 모드에서 끌어 옮기면 그 위치가 설정에 남는다.
    ///
    /// 감전·빅테디는 상태라 풀릴 때까지 떠 있고(<see cref="Show"/> → <see cref="HideAlert"/>),
    /// 반사·오를리·보물창고는 정해진 시간만 떠 있다(<see cref="Flash"/>).
    /// </summary>
    public sealed class DungeonAlertWindow : ToastWindowBase
    {
        /// <summary>종류별로 떠 있는 창. 쌓는 순서는 열거형 순서를 따른다.</summary>
        private static readonly Dictionary<DungeonAlertSource, DungeonAlertWindow> Instances = new();

        /// <summary>풀림 줄을 못 봤을 때 알림이 영영 남지 않도록 하는 상한.</summary>
        private static readonly TimeSpan SafetyLifetime = TimeSpan.FromMinutes(3);
        private const double DefaultWidth = 220;
        private const double DefaultTop = 200;
        /// <summary>쌓을 때 창 사이 간격.</summary>
        private const double StackGap = 6;

        private static readonly SolidColorBrush DangerBrush = CreateFrozenBrush(Color.FromRgb(0xFF, 0x5A, 0x5A));

        private static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private readonly ChatSettings? _settings;
        private readonly TextBlock _titleText;
        private readonly TextBlock _bodyText;
        private bool _isPreview;
        /// <summary>이 창이 맡은 알림 종류.</summary>
        private readonly DungeonAlertSource _source;

        protected override bool UseToolWindowStyle => true;
        /// <summary>표시만 하는 알림. 잠금 중엔 클릭이 아래 게임으로 간다.</summary>
        protected override bool PassthroughWhenLocked => true;
        protected override ChatSettings? ResolveSettings() => _settings;
        // 자리를 잠금 해제 중에만 저장하는 창 — 스택에 얹히지 않으므로 LocationChanged·닫힘 저장을 켜 둔다 (PersistBounds가 잠금 중엔 거른다)
        protected override bool PersistBoundsOnChange => true;
        protected override bool PersistBoundsOnClose => true;
        protected override bool PersistOnlyWhenUnlocked => true;

        private DungeonAlertWindow(ChatSettings? settings, DungeonAlertSource source)
        {
            _settings = settings;
            _source = source;

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

            _bodyText.Foreground = DangerBrush;

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
        }

        /// <summary>수명이 다하면 자기 창만 닫고 남은 창을 다시 쌓는다 (페이드 없음).</summary>
        protected override void OnAutoCloseElapsed() => CloseSelf();

        /// <summary>자기 창만 닫고 남은 창을 다시 쌓는다.</summary>
        private void CloseSelf()
        {
            if (Instances.TryGetValue(_source, out var current) && ReferenceEquals(current, this))
                Instances.Remove(_source);

            try { Close(); } catch { }
            Restack();
        }

        /// <summary>
        /// 잠금 해제 중 옮긴 자리만 저장한다 (자동으로 뜰 때의 기본 자리는 저장하지 않는다).
        /// 아래에 쌓인 창을 끌었으면 그만큼 빼서 맨 위 기준 자리로 저장한다.
        /// </summary>
        protected override bool PersistBounds(ChatSettings settings)
        {
            if (!IsVisible)
                return false;

            settings.DungeonAlertWindowLeft = Left;
            settings.DungeonAlertWindowTop = Top - StackOffsetOf(_source);
            return true;
        }

        /// <summary>이 종류보다 위에 쌓인 창들의 높이 합.</summary>
        private static double StackOffsetOf(DungeonAlertSource source)
        {
            double offset = 0;
            foreach (var pair in Instances.OrderBy(x => x.Key))
            {
                if (pair.Key == source)
                    break;
                if (pair.Value.IsVisible)
                    offset += pair.Value.ActualHeight + StackGap;
            }

            return offset;
        }

        /// <summary>떠 있는 창들을 저장된 자리에서부터 아래로 쌓는다.</summary>
        /// <summary>저장된 자리로 떠 있는 알림들을 다시 쌓는다. 설정 전체 교체·위치 초기화 뒤에 쓴다.</summary>
        public static void ApplyStoredPosition() => Restack();

        /// <summary>복원 경로(베이스): 이 창 하나가 아니라 같은 자리를 쓰는 창 전부를 다시 쌓는다.</summary>
        public override void ApplyStoredBounds(ChatSettings? settings = null) => Restack();

        private static void Restack()
        {
            ChatSettings? settings = Instances.Values.Select(w => w._settings).FirstOrDefault(s => s != null);
            var (left, top) = ToastPresentationHelper.ResolveBasePosition(
                settings?.DungeonAlertWindowLeft, settings?.DungeonAlertWindowTop, DefaultWidth, DefaultTop);

            double y = top;
            foreach (var pair in Instances.OrderBy(x => x.Key))
            {
                DungeonAlertWindow window = pair.Value;
                if (!window.IsVisible)
                    continue;

                using (window.SuppressBoundsPersist()) // 쌓느라 옮긴 자리는 저장하지 않는다
                {
                    window.Left = left;
                    window.Top = y;
                }
                y += window.ActualHeight + StackGap;
            }
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
                    if (!Instances.TryGetValue(source, out var window) || !window.IsLoaded)
                    {
                        var created = new DungeonAlertWindow(settings, source);
                        created.Closed += (_, _) =>
                        {
                            if (Instances.TryGetValue(source, out var cur) && ReferenceEquals(cur, created))
                                Instances.Remove(source);
                        };
                        Instances[source] = created;
                        window = created;
                    }

                    window._isPreview = isPreview;
                    window._titleText.Text = title;
                    window._bodyText.Text = message;
                    ApplyBodyColor(window, source, isPreview);

                    if (!window.IsVisible)
                        window.Show();

                    // 내용이 바뀌면 높이가 달라질 수 있으므로 재어 본 뒤 쌓는다
                    window.UpdateLayout();
                    Restack();

                    window.StartAutoClose(lifetime); // 0이면 풀릴 때까지 떠 있다 (안전 상한은 호출한 쪽이 넘긴다)

                    TopmostWindowHelper.BringToTopmost(window);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to show dungeon alert.", ex);
                }
            }));
        }

        /// <summary>경고(감전·반사·빅테디)는 붉은색, 세거나 알려 주는 값(오를리·보물창고 퇴장)은 다른 곳의 횟수와 같은 색. 자리 잡기 미리보기는 경고가 아니므로 평소 글자색.</summary>
        private static void ApplyBodyColor(DungeonAlertWindow window, DungeonAlertSource source, bool isPreview)
        {
            if (isPreview)
                window._bodyText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            else if (source is DungeonAlertSource.OrlyAttack or DungeonAlertSource.TreasuryExit)
                window._bodyText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayRareAccentBrush");
            else
                window._bodyText.Foreground = DangerBrush;
        }

        /// <summary>그 종류의 알림만 내린다 (감전이 풀렸을 때, 빅테디를 잡았을 때, 보물창고에 다시 들어갔을 때).</summary>
        public static void HideAlert(DungeonAlertSource source)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!Instances.TryGetValue(source, out var window))
                    return;

                try
                {
                    window.CancelAutoClose();
                    window.CloseSelf();
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

            // 자리는 맨 위 한 자리만 잡으면 되므로 미리보기도 하나만 띄운다 (겹치면 아래로 쌓인다)
            ShowInternal(DungeonAlertSource.AbyssDischarge, settings,
                "감전 · 반사 · 오를리 · 빅테디", "던전 특수 알림", TimeSpan.Zero, isPreview: true);
        }

        /// <summary>잠금 해제 종료: 미리보기로 떠 있던 창만 닫는다.</summary>
        public static void ClosePositionPreview()
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var source in Instances.Keys.ToList())
                {
                    if (!Instances.TryGetValue(source, out var window) || !window._isPreview)
                        continue;

                    try { window.CloseSelf(); } catch { }
                }
            }));
        }
    }
}
