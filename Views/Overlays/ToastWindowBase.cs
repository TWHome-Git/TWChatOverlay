using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 잠시 떠 있다 스스로 닫히는 알림 창의 공통 베이스. 창마다 복붙돼 있던 세 가지를 모았다.
    ///
    ///  - 수명 타이머(<see cref="StartAutoClose(TimeSpan)"/> / <see cref="CancelAutoClose"/>): 1회성 DispatcherTimer.
    ///    시간이 되면 <see cref="OnAutoCloseElapsed"/>(기본 = 페이드 아웃 뒤 Close). 0 이하면 자동 닫힘 없음.
    ///    초 단위 수명은 1~300초로 클램프한다(설정 setter와 이중 방어). 닫힐 때 타이머는 베이스가 멈춘다.
    ///  - 등장 애니메이션(<see cref="ShowSlideIn"/>): 목표 자리 위에서 Top 슬라이드 + 투명도 페이드 인.
    ///  - 이동 애니메이션(<see cref="AnimateTop"/>): 알림 스택이 다시 배치할 때 Top을 부드럽게 옮긴다.
    ///    ToastStackService는 창 종류를 모르고 이 메서드만 부른다.
    ///
    /// 자리는 보통 알림 스택이나 서비스가 정하므로 LocationChanged 저장(PersistBoundsOnChange)과 닫힘 저장(PersistBoundsOnClose)은 기본 꺼져 있다.
    /// 잠금 해제 드래그(TryBeginDrag)는 여전히 저장된다. 위치를 늘 저장하는 알림(던전 특수 알림 등)은 두 정책을 다시 켠다.
    /// </summary>
    public class ToastWindowBase : OverlayWindowBase
    {
        public const int MinLifetimeSeconds = 1;
        public const int MaxLifetimeSeconds = 300;

        private readonly DispatcherTimer _lifetimeTimer = new();
        private bool _isClosing;

        public ToastWindowBase()
        {
            _lifetimeTimer.Tick += (_, _) =>
            {
                _lifetimeTimer.Stop();
                OnAutoCloseElapsed();
            };
        }

        protected override bool PersistBoundsOnChange => false;
        protected override bool PersistBoundsOnClose => false;

        /// <summary>페이드 아웃 길이. 창마다 조금 다르던 값(180~260ms)의 기본.</summary>
        protected virtual TimeSpan FadeOutDuration => TimeSpan.FromMilliseconds(220);

        /// <summary>설정의 초 단위 수명을 타이머 간격으로. 0 이하는 그대로(자동 닫힘 없음), 그 외 1~300초.</summary>
        public static TimeSpan ClampLifetime(int seconds)
            => seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(Math.Max(MinLifetimeSeconds, Math.Min(MaxLifetimeSeconds, seconds)));

        #region 수명

        /// <summary>자동 닫힘 타이머를 (다시) 건다. 0 이하면 멈추기만 한다 — 저절로 닫히지 않는 창.</summary>
        protected void StartAutoClose(TimeSpan lifetime)
        {
            _lifetimeTimer.Stop();
            if (lifetime <= TimeSpan.Zero)
                return;

            _lifetimeTimer.Interval = lifetime;
            _lifetimeTimer.Start();
        }

        /// <summary>초 단위 수명으로 자동 닫힘을 건다 (1~300초 클램프, 0 이하면 없음).</summary>
        protected void StartAutoClose(int seconds) => StartAutoClose(ClampLifetime(seconds));

        /// <summary>자동 닫힘을 멈춘다 (미리보기·사용자 조작 중).</summary>
        protected void CancelAutoClose() => _lifetimeTimer.Stop();

        /// <summary>자동 닫힘이 걸려 있는지.</summary>
        protected bool IsAutoCloseArmed => _lifetimeTimer.IsEnabled;

        /// <summary>수명이 다했을 때. 기본은 페이드 아웃 뒤 Close. 바로 닫거나 다른 처리를 하려면 override.</summary>
        protected virtual void OnAutoCloseElapsed() => CloseWithFade();

        protected override void OnClosed(EventArgs e)
        {
            _lifetimeTimer.Stop();
            base.OnClosed(e);
        }

        #endregion

        #region 애니메이션

        /// <summary>페이드 아웃 중(또는 끝나 닫히는 중)인지.</summary>
        protected bool IsFadingOut => _isClosing;

        /// <summary>투명도를 서서히 0으로 내린 뒤 닫는다. 이미 닫는 중이면 무시.</summary>
        protected void CloseWithFade()
        {
            if (_isClosing)
                return;

            _isClosing = true;
            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = FadeOutDuration,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            fade.Completed += (_, _) => { try { Close(); } catch { } };
            BeginAnimation(OpacityProperty, fade);
        }

        /// <summary>닫히던 중에 다시 쓰이게 된 창(내용 갱신·재표시)을 되살린다: 페이드를 멈추고 완전히 보이게.</summary>
        protected void ResetPresentation()
        {
            _isClosing = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
        }

        /// <summary>
        /// 목표 자리 위쪽 <paramref name="offset"/>px에서 미끄러져 내려오며 나타난다 (Top 슬라이드 + 페이드 인).
        /// Show 뒤에 부르는 쪽이 Left를 다시 맞춰도 된다 (폭에 맞춰 가운데 정렬 등) — 애니메이션은 Top만 잡는다.
        /// </summary>
        /// <param name="targetOpacity">페이드 인 목표(창별 저장 투명도 등). 기본 1.</param>
        protected void ShowSlideIn(double left, double top, double offset = 18, double targetOpacity = 1.0)
        {
            Left = left;
            Top = top - offset;
            Opacity = 0;
            if (!IsVisible)
                Show();

            BeginAnimation(TopProperty, new DoubleAnimation
            {
                To = top,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                To = targetOpacity > 0 ? targetOpacity : 1.0,
                Duration = TimeSpan.FromMilliseconds(180),
            });
        }

        /// <summary>알림 스택 재배치: Top을 부드럽게 옮긴다. 애니메이션이 어울리지 않는 창(미리보기 등)은 override해 바로 대입.</summary>
        public virtual void AnimateTop(double top)
        {
            BeginAnimation(TopProperty, new DoubleAnimation
            {
                To = top,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }

        #endregion
    }
}
