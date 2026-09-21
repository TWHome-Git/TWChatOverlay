using System;
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
    ///  - 위치/크기 영속화: 창은 <see cref="PersistBounds"/>만 override해 자기 설정 필드에 쓴다.
    ///    LocationChanged/SizeChanged마다 호출되고, 저장은 ConfigService.SaveDeferred가 디바운스한다.
    ///  - 저장 위치 적용(<see cref="ApplyStoredBounds"/>): 적용 중에는 영속화를 멈춰 옛 값이 되쓰이지 않게 한다.
    ///  - 확장 스타일 도우미: 툴윈도우(알트탭 숨김), 마우스 통과(WS_EX_TRANSPARENT), 최상위 재배치.
    ///
    /// XAML 창은 루트를 &lt;ov:OverlayWindowBase&gt;로, 코드 창은 : OverlayWindowBase 로 바꾸면 된다.
    /// 창별 정책은 virtual 프로퍼티로 조정한다 (예: 달력·메모처럼 잠금 상태에서도 끌 수 있는 창은 DragRequiresUnlock=false).
    /// </summary>
    public class OverlayWindowBase : Window
    {
        private bool _commonSetupDone;
        private bool _hasLoadedOnce;
        private bool _isDragging;
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

        /// <summary>LocationChanged/SizeChanged마다 <see cref="PersistBounds"/>를 부를지. false면 드래그 종료·명시 호출 때만.</summary>
        protected virtual bool PersistBoundsOnChange => true;

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

        /// <summary>드래그 중인지 (LocationChanged 처리에서 드래그 여부를 구분할 때).</summary>
        protected bool IsDragging => _isDragging;

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

            _isDragging = true;
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove는 왼쪽 버튼 다운 처리 중에만 유효하다. 그 외 상황이면 무시.
            }
            finally
            {
                _isDragging = false;
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

        /// <summary>설정 인스턴스. 생성자에서 설정을 받는 창은 override해 그 인스턴스를 돌려주면 탐색 비용이 없다.</summary>
        protected virtual ChatSettings? ResolveSettings()
        {
            if (Owner is MainWindow ownerMain && ownerMain.DataContext is ChatSettings ownerSettings)
                return ownerSettings;

            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow main && main.DataContext is ChatSettings settings)
                        return settings;
                }
            }
            catch { }

            return null;
        }

        /// <summary>지금 위치/크기를 설정에 쓰고 디바운스 저장을 건다. 닫힐 때 안전망으로도 쓴다.</summary>
        protected void PersistBoundsNow()
        {
            // Loaded 전(초기 레이아웃)의 기본 크기는 쓰지 않는다. 닫히는 중(IsLoaded=false)에는 마지막 값을 쓴다.
            if (!_hasLoadedOnce || _suppressPersistDepth > 0 || WindowState == WindowState.Minimized)
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
        /// 저장된 위치/크기를 창에 적용한다. 적용하는 동안은 영속화를 멈춰
        /// (새 Left, 옛 Top) 같은 중간 상태가 설정에 되쓰이지 않게 한다.
        /// </summary>
        protected void ApplyStoredBounds(double? left, double? top, double? width = null, double? height = null)
        {
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
    }
}
