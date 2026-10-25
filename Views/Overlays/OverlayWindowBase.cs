using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 오버레이 창 공통 베이스. 창마다 복제돼 있던 뼈대를 한 곳에 모은다.
    ///
    ///  - 설정 창 아래 z-순서 등록(SettingsHostZOrder) + 앱 공통 폰트 적용 → 초기화 시 자동
    ///  - 잠금 해제 게이팅 드래그(<see cref="TryBeginDrag"/>): 선택 하이라이트 → DragMove → 위치 저장
    ///  - 위치/크기 영속화(저장 경로 하나): 창은 <see cref="PersistBounds"/>만 override해 자기 설정 필드에 쓴다.
    ///    LocationChanged/SizeChanged(<see cref="PersistBoundsOnChange"/>)·드래그 종료·닫힘(<see cref="PersistBoundsOnClose"/>)에서
    ///    베이스가 부르고, 저장은 ConfigService.SaveDeferred가 디바운스한다. 앱 종료 경로는 FlushPendingSave로 마무리한다.
    ///  - 저장 위치 복원(복원 경로 하나): 창은 <see cref="StoredBounds"/>로 자기 설정 쌍을 돌려주고,
    ///    MainWindow·서비스·위치 목록은 <see cref="ApplyStoredBounds(ChatSettings?)"/>만 부른다.
    ///    적용 중에는 영속화를 멈춰 (새 Left, 옛 Top) 같은 중간 상태가 설정에 되쓰이지 않게 한다.
    ///  - 확장 스타일 도우미: 툴윈도우(알트탭 숨김), 마우스 통과(WS_EX_TRANSPARENT), 최상위 재배치.
    ///  - 가장자리 리사이즈(<see cref="EnableEdgeResize"/>): 테두리 없는 창의 가장자리 N px를 잡아 크기를 바꾸는 WndProc.
    ///
    /// XAML 창은 루트를 &lt;ov:OverlayWindowBase&gt;로, 코드 창은 : OverlayWindowBase 로 바꾸면 된다.
    /// 창별 정책은 virtual 프로퍼티로 조정한다 (예: 달력·메모처럼 잠금 상태에서도 끌 수 있는 창은 DragRequiresUnlock=false).
    /// 잠시 떠 있다 사라지는 알림은 <see cref="ToastWindowBase"/>를 쓴다.
    /// </summary>
    public class OverlayWindowBase : Window
    {
        private bool _commonSetupDone;
        private bool _hasLoadedOnce;
        private int _suppressPersistDepth;

        public OverlayWindowBase()
        {
            Loaded += (_, _) => _hasLoadedOnce = true;
            LocationChanged += OnOverlayBoundsChanged;
            SizeChanged += OnOverlayBoundsChanged;
        }

        #region 창별 정책

        /// <summary>설정 창이 열려 있으면 그 바로 아래에 두도록 등록할지. (기능 창 기본 true)</summary>
        protected virtual bool KeepBelowSettingsHost => true;

        /// <summary>앱 공통 폰트(Font 폴더 사용자 폰트)를 적용할지.</summary>
        protected virtual bool ApplyAppFont => true;

        /// <summary>알트탭 목록에서 숨기는 툴윈도우 스타일을 줄지.</summary>
        protected virtual bool UseToolWindowStyle => false;

        /// <summary>true면 잠금 해제 모드에서만 드래그·선택된다. 달력·메모처럼 항상 끌 수 있는 창은 false.</summary>
        protected virtual bool DragRequiresUnlock => true;

        /// <summary>LocationChanged/SizeChanged마다 <see cref="PersistBounds"/>를 부를지. false면 드래그 종료·닫힘·명시 호출 때만.</summary>
        protected virtual bool PersistBoundsOnChange => true;

        /// <summary>
        /// 닫힐 때(OnClosing) 마지막 위치를 저장할지. 자리를 스택·서비스가 정하는 알림(외치기·메신저 에타 등)은
        /// 배치된 좌표가 설정에 되쓰이면 앵커가 밀리므로 false로 둔다.
        /// </summary>
        protected virtual bool PersistBoundsOnClose => true;

        /// <summary>
        /// true면 잠금 해제 모드일 때만 저장한다. 자동으로 뜰 때의 자리(기본 위치 계산·스택 배치)는 사용자가 정한 것이
        /// 아니므로 남기지 않고, 잠금 해제 중 끌어 옮긴 자리만 남기는 창에 쓴다.
        /// </summary>
        protected virtual bool PersistOnlyWhenUnlocked => false;

        /// <summary>
        /// true면 잠금 상태에서 마우스 통과(WS_EX_TRANSPARENT)로 두고, 잠금 해제 때만 클릭을 받는다.
        /// 표시만 하는 창(알림·추적창)에 쓴다. 통과 중에는 아래 게임이 눌리고, 확대 프로그램(Magpie)도 이 창을
        /// "가리는 창"으로 보지 않아 커서 캡처를 유지한다. 플레이 중 눌러야 할 버튼이 있는 창에는 쓰지 않는다.
        /// </summary>
        protected virtual bool PassthroughWhenLocked => false;

        #endregion

        #region 초기화

        protected override void OnInitialized(EventArgs e)
        {
            base.OnInitialized(e);
            EnsureCommonSetup();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            EnsureCommonSetup();
            if (UseToolWindowStyle)
                SetExStyleFlags(add: NativeMethods.WS_EX_TOOLWINDOW);

            if (PassthroughWhenLocked)
            {
                _lockPassthroughHandler ??= _ => Dispatcher.BeginInvoke(new Action(ApplyLockPassthrough));
                AppServices.Get<UiLockService>().UnlockChanged += _lockPassthroughHandler;
                ApplyLockPassthrough();
            }

            if (_edgeResize != null)
                InstallEdgeResizeHook();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // 핸들러(Closing 이벤트)보다 먼저 써 두면, 창이 닫힘 경로에서 즉시 저장(ConfigService.Save)을 해도 최신 자리가 함께 간다
            if (PersistBoundsOnClose)
                PersistBoundsNow();
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_lockPassthroughHandler != null)
            {
                try { AppServices.Get<UiLockService>().UnlockChanged -= _lockPassthroughHandler; } catch { }
                _lockPassthroughHandler = null;
            }

            base.OnClosed(e);
        }

        private Action<bool>? _lockPassthroughHandler;

        /// <summary>
        /// <see cref="PassthroughWhenLocked"/> 창의 통과 스타일을 지금 잠금 상태에 맞춘다.
        /// 창이 확장 스타일을 스스로 다시 쓴 뒤(툴윈도우 비트 추가 등)에도 부른다.
        /// </summary>
        protected void ApplyLockPassthrough()
        {
            if (!PassthroughWhenLocked)
                return;

            try
            {
                SetMousePassthrough(!AppServices.Get<UiLockService>().IsUnlocked);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to apply lock passthrough of '{GetType().Name}'.", ex);
            }
        }

        private void EnsureCommonSetup()
        {
            if (_commonSetupDone)
                return;
            _commonSetupDone = true;

            if (KeepBelowSettingsHost)
                SettingsHostZOrder.Register(this);
            if (ApplyAppFont)
                WindowFontService.Apply(this);
        }

        #endregion

        #region 드래그

        /// <summary>
        /// 마우스 왼쪽 버튼 핸들러에서 호출한다. 잠금 정책을 확인하고, 잠금 해제 중이면 창을 선택한 뒤 드래그한다.
        /// 드래그가 끝나면 위치를 저장하고 <see cref="OnDragCompleted"/>를 부른다.
        /// </summary>
        /// <returns>드래그를 시작했으면 true.</returns>
        protected bool TryBeginDrag(MouseButtonEventArgs e, bool markHandled = false)
        {
            if (DragRequiresUnlock && !AppServices.Get<UiLockService>().IsUnlocked)
                return false;

            AppServices.Get<UiLockService>().Select(this); // 잠금 상태면 내부에서 무시된다
            if (e.ButtonState != MouseButtonState.Pressed || e.LeftButton != MouseButtonState.Pressed)
                return false;

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove는 왼쪽 버튼 다운 처리 중에만 유효하다. 그 외 상황이면 무시.
            }

            PersistBoundsNow();
            OnDragCompleted();
            if (markHandled)
                e.Handled = true;
            return true;
        }

        /// <summary>드래그가 끝난 직후 (위치 저장 후). 창별 후처리가 필요하면 override.</summary>
        protected virtual void OnDragCompleted()
        {
        }

        #endregion

        #region 위치/크기 영속화

        /// <summary>
        /// 현재 Left/Top/크기를 자기 설정 필드에 쓴다. 저장(SaveDeferred)은 베이스가 한다.
        /// 위치를 저장하지 않는 창은 override하지 않으면 된다.
        /// </summary>
        /// <returns>설정에 썼으면 true (false면 저장을 걸지 않는다).</returns>
        protected virtual bool PersistBounds(ChatSettings settings) => false;

        /// <summary>
        /// 설정에 저장된 이 창의 (Left, Top, Width, Height). 크기를 저장하지 않는 창은 Width/Height를 null로 둔다.
        /// null을 돌려주면 복원할 것이 없는 창이다. 기본 위치 계산(다른 창 위치 대체 등)이 있으면 여기서 해서 돌려준다.
        /// </summary>
        protected virtual (double? Left, double? Top, double? Width, double? Height)? StoredBounds(ChatSettings settings) => null;

        /// <summary>설정 인스턴스(DI 싱글턴). 컨테이너가 없는 도움말 이미지 생성 모드에서는 null.</summary>
        protected virtual ChatSettings? ResolveSettings() => AppServices.TryGet<ChatSettings>();

        /// <summary>지금 위치/크기를 설정에 쓰고 디바운스 저장을 건다. 닫힐 때 안전망으로도 쓴다.</summary>
        protected void PersistBoundsNow()
        {
            // Loaded 전(초기 레이아웃)의 기본 크기는 쓰지 않는다. 닫히는 중(IsLoaded=false)에는 마지막 값을 쓴다.
            if (!_hasLoadedOnce || _suppressPersistDepth > 0 || WindowState == WindowState.Minimized)
                return;

            if (PersistOnlyWhenUnlocked && !AppServices.Get<UiLockService>().IsUnlocked)
                return;

            ChatSettings? settings = ResolveSettings();
            if (settings == null)
                return;

            try
            {
                if (PersistBounds(settings))
                    ConfigService.SaveDeferred(settings);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to persist bounds of '{GetType().Name}'.", ex);
            }
        }

        private void OnOverlayBoundsChanged(object? sender, EventArgs e)
        {
            if (!PersistBoundsOnChange)
                return;

            PersistBoundsNow();
        }

        /// <summary>
        /// 저장된 위치/크기를 창에 적용한다 — 복원 경로는 이것 하나다. 시작 시, 설정 전체 교체(프로필 불러오기) 뒤,
        /// 설정의 창 위치 목록에서 자리를 고친 뒤에 부른다. 저장된 자리가 있으면 WindowStartupLocation을 Manual로 바꾼다
        /// (CenterScreen 창이 Show 때 다시 가운데로 가지 않게). 적용하는 동안은 영속화를 멈춘다.
        /// 오른쪽 가장자리 고정(경험치 추적창)처럼 자기만의 복원 규칙이 있는 창은 override한다.
        /// </summary>
        /// <param name="settings">null이면 <see cref="ResolveSettings"/>.</param>
        public virtual void ApplyStoredBounds(ChatSettings? settings = null)
        {
            settings ??= ResolveSettings();
            if (settings == null)
                return;

            var stored = StoredBounds(settings);
            if (stored == null)
                return;

            var (left, top, width, height) = stored.Value;
            if (left.HasValue && top.HasValue)
                WindowStartupLocation = WindowStartupLocation.Manual;

            using (SuppressBoundsPersist())
                WindowPlacement.ApplyStoredBounds(this, left, top, width, height);
        }

        /// <summary>블록 안에서 일어나는 위치/크기 변화는 설정에 쓰지 않는다.</summary>
        protected IDisposable SuppressBoundsPersist()
        {
            _suppressPersistDepth++;
            return new PersistSuppression(this);
        }

        private sealed class PersistSuppression : IDisposable
        {
            private OverlayWindowBase? _owner;
            public PersistSuppression(OverlayWindowBase owner) => _owner = owner;
            public void Dispose()
            {
                if (_owner == null) return;
                _owner._suppressPersistDepth--;
                _owner = null;
            }
        }

        #endregion

        #region 네이티브 스타일 / 최상위

        /// <summary>확장 윈도우 스타일 비트를 켜고 끈다. 핸들이 아직 없으면 아무것도 하지 않는다.</summary>
        protected void SetExStyleFlags(int add, int remove = 0)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                    return;

                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                int next = (exStyle | add) & ~remove;
                if (next != exStyle)
                    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, next);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to update extended style of '{GetType().Name}'.", ex);
            }
        }

        /// <summary>마우스 클릭을 아래 창(게임)으로 통과시킬지. 잠금 해제 편집 중에는 보통 false로 되돌린다.</summary>
        protected void SetMousePassthrough(bool passthrough)
            => SetExStyleFlags(
                add: passthrough ? NativeMethods.WS_EX_TRANSPARENT : 0,
                remove: passthrough ? 0 : NativeMethods.WS_EX_TRANSPARENT);

        /// <summary>최상위로 다시 올린다 (게임 창이 덮었을 때). 알림 서비스가 밖에서도 부른다.</summary>
        public void BringToFront() => TopmostWindowHelper.BringToTopmost(this);

        #endregion

        #region 가장자리 리사이즈

        private const int WM_NCHITTEST = 0x0084;
        private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14, HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;

        private (int Thickness, Func<bool>? When)? _edgeResize;
        private bool _edgeResizeHooked;

        /// <summary>
        /// 테두리 없는 창의 가장자리 <paramref name="thickness"/>px를 잡으면 크기가 조절되게 한다 (WM_NCHITTEST).
        /// <paramref name="when"/>이 false를 돌려주는 동안은 그냥 창 안쪽으로 취급한다 (예: 잠금 중에는 실수로 늘어나지 않게).
        /// 생성자에서 불러도 되고, 핸들이 생기면 베이스가 훅을 건다.
        /// </summary>
        protected void EnableEdgeResize(int thickness = 6, Func<bool>? when = null)
        {
            _edgeResize = (thickness, when);
            InstallEdgeResizeHook();
        }

        private void InstallEdgeResizeHook()
        {
            if (_edgeResizeHooked)
                return;

            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero)
                    return;
                HwndSource.FromHwnd(hwnd)?.AddHook(EdgeResizeHook);
                _edgeResizeHooked = true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to install edge-resize hook of '{GetType().Name}'.", ex);
            }
        }

        private IntPtr EdgeResizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg != WM_NCHITTEST || _edgeResize == null)
                return IntPtr.Zero;

            try
            {
                var (thickness, when) = _edgeResize.Value;
                if (WindowState == WindowState.Maximized || (when != null && !when()))
                    return IntPtr.Zero;

                Point screenPoint = new(
                    unchecked((short)(lParam.ToInt64() & 0xFFFF)),
                    unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF)));
                Point local = PointFromScreen(screenPoint);

                bool left = local.X <= thickness;
                bool right = local.X >= ActualWidth - thickness;
                bool top = local.Y <= thickness;
                bool bottom = local.Y >= ActualHeight - thickness;

                int hit =
                    left && top ? HTTOPLEFT :
                    right && top ? HTTOPRIGHT :
                    left && bottom ? HTBOTTOMLEFT :
                    right && bottom ? HTBOTTOMRIGHT :
                    left ? HTLEFT :
                    right ? HTRIGHT :
                    top ? HTTOP :
                    bottom ? HTBOTTOM : 0;
                if (hit == 0)
                    return IntPtr.Zero;

                handled = true;
                return (IntPtr)hit;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        #endregion
    }
}
