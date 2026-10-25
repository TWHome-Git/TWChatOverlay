using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 휠 입력으로 깨어나는 클릭 통과 창. <see cref="ChatWheelPassthroughService"/>가 관리한다.
    /// 모든 멤버는 UI 스레드에서 불린다.
    /// </summary>
    public interface IWheelPassthroughTarget
    {
        Window Window { get; }

        /// <summary>true면 유휴 상태에서 창을 클릭 통과(WS_EX_TRANSPARENT)로 둔다. 잠금 해제·위치 편집 중에는 false.</summary>
        bool IsAutoPassthroughAllowed { get; }

        /// <summary>창이 통과 상태일 때 전역 훅이 받은 휠을 넘긴다 (delta는 120 단위, 양수 = 위로).</summary>
        void ScrollByWheel(int delta);

        /// <summary>통과 ↔ 상호작용 전환 알림. interactive=true면 이제 창이 마우스를 받는다.</summary>
        void OnInteractiveChanged(bool interactive);

        /// <summary>상호작용 상태에서 창 위의 마우스 입력이 있을 때 (유휴 타이머가 다시 시작된 뒤).</summary>
        void OnInteractiveActivity();
    }

    /// <summary>
    /// 채팅창처럼 "평소엔 아래 게임이 눌리고, 휠을 굴리면 잠시 창이 마우스를 받는" 동작을 만든다.
    ///
    /// 동작:
    ///  1. 잠금 상태(편집 아님)에서는 대상 창에 WS_EX_TRANSPARENT를 주어 클릭이 게임으로 간다.
    ///     확대 프로그램(Magpie)도 이 스타일이 있는 창은 "가리는 창"으로 보지 않아 커서 캡처를 유지한다.
    ///  2. 전역 마우스 훅(WH_MOUSE_LL)이 휠을 받아, 커서가 대상 창 위면 창을 대신 스크롤하고
    ///     (통과 상태라 창에는 휠이 안 들어온다) 게임에는 넘기지 않는다. 동시에 통과를 풀어 상호작용 상태로 바꾼다.
    ///  3. 상호작용 상태에서는 창이 평소처럼 휠·클릭·드래그 선택을 받는다. 창 위의 마우스 입력마다 유휴 타이머를 다시 센다.
    ///  4. 유휴 시간(설정 ChatPassthroughIdleSeconds) 동안 창 위 입력이 없고 버튼을 누르고 있지 않으면 다시 통과 상태로 돌아간다.
    ///  5. 설정이 꺼져 있거나 잠금 해제·위치 편집 중(<see cref="IWheelPassthroughTarget.IsAutoPassthroughAllowed"/> = false)에는 항상 상호작용 상태.
    ///
    /// 스레드: 저수준 훅은 설치한 스레드의 메시지 루프에서 불리고, 늦게 돌아오면(LowLevelHooksTimeout, 기본 약 300ms)
    /// Windows가 훅을 조용히 뗀다. UI 스레드는 로그 백필·문서 렌더로 오래 바쁠 수 있어 훅은 전용 스레드에 설치한다.
    /// 훅 콜백은 UI 스레드가 갱신해 두는 스냅샷(창 핸들·사각형·표시 여부·상태)만 보고 삼킬지 정하고,
    /// 스크롤·스타일 변경·타이머는 전부 UI 디스패처로 넘긴다. 만약을 위해 훅은 주기적으로 다시 설치한다.
    ///
    /// Magpie 확대 중에는 진짜 커서가 원본(게임) 창 좌표에 있고 화면에는 확대 좌표에 그려진다. Magpie가 확대 창에
    /// 붙여 두는 속성(Magpie.Src*/Dest*)으로 커서 위치를 확대 좌표로 바꿔 창 안인지 판단한다.
    /// 참고: https://github.com/Blinue/Magpie/blob/main/docs/Interact%20with%20Magpie%20programally.md
    /// </summary>
    public sealed class ChatWheelPassthroughService
    {
        /// <summary>설정값이 이상할 때 쓰는 유휴 시간. 실제 값은 ChatSettings.ChatPassthroughIdleSeconds.</summary>
        private static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(3);

        /// <summary>훅이 조용히 떨어졌을 때를 대비해 이 주기로 다시 설치한다.</summary>
        private static readonly TimeSpan HookReinstallInterval = TimeSpan.FromSeconds(30);

        /// <summary>마우스 이동으로 타이머를 연장하는 요청은 이 간격으로만 UI 스레드에 보낸다.</summary>
        private const int ActivityPostIntervalMs = 200;

        /// <summary>확대 창 핸들·속성을 다시 찾는 간격 (훅 스레드 전용 캐시).</summary>
        private const int ScalerCacheMs = 1000;

        #region Win32

        private const int WH_MOUSE_LL = 14;
        private const int WM_QUIT = 0x0012;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_APP_REINSTALL_HOOK = 0x8000 + 1;
        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetProp(IntPtr hWnd, string lpString);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        #endregion

        #region 대상 상태

        /// <summary>UI 스레드가 소유하는 대상 상태.</summary>
        private sealed class TargetState
        {
            public TargetState(IWheelPassthroughTarget target, TimeSpan idleTimeout)
            {
                Target = target;
                IdleTimer = new DispatcherTimer { Interval = idleTimeout };
            }

            public IWheelPassthroughTarget Target { get; }
            public DispatcherTimer IdleTimer { get; }

            /// <summary>지금 WS_EX_TRANSPARENT가 걸려 있는지 (우리가 건 것 기준).</summary>
            public bool IsPassthrough { get; set; }

            public IntPtr Handle { get; set; }

            public EventHandler? BoundsChangedHandler { get; set; }
            public SizeChangedEventHandler? SizeChangedHandler { get; set; }
            public DependencyPropertyChangedEventHandler? VisibleChangedHandler { get; set; }
        }

        /// <summary>훅 스레드가 읽는 불변 스냅샷. UI 스레드가 상태가 바뀔 때마다 통째로 바꿔 끼운다.</summary>
        private sealed class TargetSnapshot
        {
            public TargetState State = null!;
            public IntPtr Handle;
            public NativeMethods.RECT Rect;
            public bool IsVisible;
            public bool IsPassthrough;
            public bool IsAllowed;
            public bool IdleTimerRunning;

            public bool Contains(POINT pt)
                => pt.X >= Rect.Left && pt.X < Rect.Right && pt.Y >= Rect.Top && pt.Y < Rect.Bottom;
        }

        #endregion

        private readonly List<TargetState> _targets = new();
        private readonly object _sync = new();
        private volatile TargetSnapshot[] _snapshots = Array.Empty<TargetSnapshot>();
        private readonly LowLevelMouseProc _hookProc; // GC에 걷히지 않게 붙들어 둔다
        private Dispatcher? _uiDispatcher;
        private bool _unlockHooked;

        // 훅 스레드
        private Thread? _hookThread;
        private uint _hookThreadId;
        private IntPtr _hook = IntPtr.Zero;
        private DispatcherTimer? _reinstallTimer;

        // 훅 스레드 전용 (다른 스레드에서 만지지 않는다)
        private long _lastActivityPostTick;
        private IntPtr _cachedScaler = IntPtr.Zero;
        private long _cachedScalerTick;

        private readonly ChatSettings _settings;

        public ChatWheelPassthroughService(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _hookProc = HookProc;
            _settings.PropertyChanged += Settings_PropertyChanged;
        }

        /// <summary>설정의 유휴 시간. 1~60초 밖이면 기본값.</summary>
        private TimeSpan IdleTimeout
        {
            get
            {
                int seconds = _settings.ChatPassthroughIdleSeconds;
                return seconds >= 1 && seconds <= 60 ? TimeSpan.FromSeconds(seconds) : DefaultIdleTimeout;
            }
        }

        private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 이름 없는 변경은 설정 전체 교체(프로필 불러오기·초기화) 신호라 둘 다 다시 본다
            bool all = string.IsNullOrEmpty(e.PropertyName);
            if (all || e.PropertyName == nameof(ChatSettings.ChatPassthroughIdleSeconds))
            {
                TimeSpan timeout = IdleTimeout;
                foreach (var state in SnapshotStates())
                {
                    if (state.IdleTimer.Interval == timeout)
                        continue;
                    bool running = state.IdleTimer.IsEnabled;
                    state.IdleTimer.Stop();
                    state.IdleTimer.Interval = timeout;
                    if (running)
                        state.IdleTimer.Start();
                }
            }

            if (all || e.PropertyName == nameof(ChatSettings.ChatPassthroughEnabled))
                RefreshAll();
        }

        #region 등록 (UI 스레드)

        /// <summary>창 핸들이 만들어진 뒤(SourceInitialized) 부른다. 현재 상태에 맞춰 바로 통과/상호작용을 적용한다.</summary>
        public void Attach(IWheelPassthroughTarget target)
        {
            if (target == null || FindState(target) != null)
                return;

            _uiDispatcher ??= Dispatcher.CurrentDispatcher;

            var state = new TargetState(target, IdleTimeout)
            {
                Handle = new WindowInteropHelper(target.Window).Handle,
            };
            state.IdleTimer.Tick += (_, _) => OnIdleTimerTick(state);
            state.BoundsChangedHandler = (_, _) => PublishSnapshots();
            state.SizeChangedHandler = (_, _) => PublishSnapshots();
            state.VisibleChangedHandler = (_, _) => PublishSnapshots();
            target.Window.LocationChanged += state.BoundsChangedHandler;
            target.Window.SizeChanged += state.SizeChangedHandler;
            target.Window.IsVisibleChanged += state.VisibleChangedHandler;

            lock (_sync)
                _targets.Add(state);

            EnsureUnlockSubscription();
            ApplyAutoState(state);
            EnsureHookThread();
            AppLogger.Info($"Wheel passthrough attached: {target.Window.GetType().Name}.");
        }

        /// <summary>창이 닫힐 때 부른다. 마지막 창이 빠지면 훅 스레드도 내린다.</summary>
        public void Detach(IWheelPassthroughTarget target)
        {
            var state = FindState(target);
            if (state == null)
                return;

            state.IdleTimer.Stop();
            try
            {
                if (state.BoundsChangedHandler != null)
                    target.Window.LocationChanged -= state.BoundsChangedHandler;
                if (state.SizeChangedHandler != null)
                    target.Window.SizeChanged -= state.SizeChangedHandler;
                if (state.VisibleChangedHandler != null)
                    target.Window.IsVisibleChanged -= state.VisibleChangedHandler;
            }
            catch { }

            bool empty;
            lock (_sync)
            {
                _targets.Remove(state);
                empty = _targets.Count == 0;
            }
            PublishSnapshots();

            if (empty)
                StopHookThread();
        }

        /// <summary>잠금·편집 모드가 바뀐 뒤 부른다. 허용되지 않으면 상호작용으로, 허용되면(유휴면) 통과로 맞춘다.</summary>
        public void RefreshAll()
        {
            foreach (var state in SnapshotStates())
                ApplyAutoState(state);
            PublishSnapshots();
        }

        /// <summary>종료 때. 훅 스레드를 내리고 창 스타일은 건드리지 않는다.</summary>
        public void Shutdown()
        {
            foreach (var state in SnapshotStates())
                state.IdleTimer.Stop();
            lock (_sync)
                _targets.Clear();
            PublishSnapshots();
            StopHookThread();
        }

        private TargetState[] SnapshotStates()
        {
            lock (_sync)
                return _targets.ToArray();
        }

        private TargetState? FindState(IWheelPassthroughTarget target)
        {
            lock (_sync)
            {
                foreach (var state in _targets)
                {
                    if (ReferenceEquals(state.Target, target))
                        return state;
                }
            }

            return null;
        }

        private void EnsureUnlockSubscription()
        {
            if (_unlockHooked)
                return;

            try
            {
                AppServices.Get<UiLockService>().UnlockChanged += _ => RefreshAll();
                _unlockHooked = true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to subscribe unlock changes for wheel passthrough.", ex);
            }
        }

        #endregion

        #region 스냅샷 (UI 스레드가 쓰고 훅 스레드가 읽는다)

        private void PublishSnapshots()
        {
            TargetState[] states = SnapshotStates();
            var list = new List<TargetSnapshot>(states.Length);

            foreach (var state in states)
            {
                try
                {
                    IntPtr hwnd = state.Handle;
                    if (hwnd == IntPtr.Zero)
                        hwnd = state.Handle = new WindowInteropHelper(state.Target.Window).Handle;
                    if (hwnd == IntPtr.Zero)
                        continue;

                    var snapshot = new TargetSnapshot
                    {
                        State = state,
                        Handle = hwnd,
                        IsVisible = state.Target.Window.IsVisible && NativeMethods.IsWindowVisible(hwnd),
                        IsPassthrough = state.IsPassthrough,
                        IsAllowed = state.Target.IsAutoPassthroughAllowed,
                        IdleTimerRunning = state.IdleTimer.IsEnabled,
                    };
                    if (!NativeMethods.GetWindowRect(hwnd, out snapshot.Rect))
                        continue;

                    list.Add(snapshot);
                }
                catch
                {
                    // 창이 닫히는 중일 수 있다
                }
            }

            _snapshots = list.ToArray();
        }

        #endregion

        #region 상태 전환 (UI 스레드)

        private void ApplyAutoState(TargetState state)
        {
            if (!state.Target.IsAutoPassthroughAllowed)
            {
                SetInteractive(state, startIdleTimer: false);
                return;
            }

            // 허용 상태인데 타이머가 돌고 있으면 사용자가 방금 쓰던 중이다 — 타이머가 알아서 돌려놓는다
            if (!state.IsPassthrough && state.IdleTimer.IsEnabled)
                return;

            SetPassthrough(state);
        }

        private void SetPassthrough(TargetState state)
        {
            state.IdleTimer.Stop();
            if (!SetTransparentStyle(state, true))
                return;

            bool changed = !state.IsPassthrough;
            state.IsPassthrough = true;
            PublishSnapshots();
            if (changed)
                SafeNotify(state, interactive: false);
        }

        private void SetInteractive(TargetState state, bool startIdleTimer)
        {
            state.IdleTimer.Stop();
            if (startIdleTimer)
                state.IdleTimer.Start();

            if (!SetTransparentStyle(state, false))
                return;

            bool changed = state.IsPassthrough;
            state.IsPassthrough = false;
            PublishSnapshots();
            if (changed)
                SafeNotify(state, interactive: true);
        }

        private static bool SetTransparentStyle(TargetState state, bool transparent)
        {
            try
            {
                IntPtr hwnd = state.Handle;
                if (hwnd == IntPtr.Zero)
                    hwnd = state.Handle = new WindowInteropHelper(state.Target.Window).Handle;
                if (hwnd == IntPtr.Zero)
                    return false;

                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                int next = transparent
                    ? exStyle | NativeMethods.WS_EX_TRANSPARENT
                    : exStyle & ~NativeMethods.WS_EX_TRANSPARENT;
                if (next != exStyle)
                    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, next);
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to change passthrough style.", ex);
                return false;
            }
        }

        private static void SafeNotify(TargetState state, bool interactive)
        {
            try { state.Target.OnInteractiveChanged(interactive); }
            catch (Exception ex) { AppLogger.Warn("Passthrough state callback failed.", ex); }
        }

        /// <summary>상호작용 상태에서 창 위 입력이 있었다 — 유휴 타이머를 다시 센다.</summary>
        private void Touch(TargetState state)
        {
            if (state.IsPassthrough || !state.IdleTimer.IsEnabled)
                return;

            state.IdleTimer.Stop();
            state.IdleTimer.Start();

            try { state.Target.OnInteractiveActivity(); }
            catch (Exception ex) { AppLogger.Warn("Passthrough activity callback failed.", ex); }
        }

        /// <summary>통과 상태에서 휠이 왔다 — 대신 굴리고 상호작용으로 깨운다.</summary>
        private void WakeByWheel(TargetState state, int delta)
        {
            if (FindState(state.Target) == null)
                return;

            if (!state.Target.IsAutoPassthroughAllowed)
            {
                SetInteractive(state, startIdleTimer: false);
                return;
            }

            if (!state.IsPassthrough)
            {
                // 그 사이 이미 깨어났다 (창이 직접 휠을 받았을 것) — 타이머만 연장
                Touch(state);
                return;
            }

            try { state.Target.ScrollByWheel(delta); }
            catch (Exception ex) { AppLogger.Warn("Wheel scroll callback failed.", ex); }

            SetInteractive(state, startIdleTimer: true);
        }

        private void OnIdleTimerTick(TargetState state)
        {
            // 드래그 선택·버튼 누름 중에는 되돌리지 않고 다음 주기에 다시 본다
            if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0 || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0)
                return;

            state.IdleTimer.Stop();
            if (!state.Target.IsAutoPassthroughAllowed)
            {
                PublishSnapshots();
                return;
            }

            SetPassthrough(state);
        }

        #endregion

        #region 훅 스레드

        private void EnsureHookThread()
        {
            if (_hookThread != null && _hookThread.IsAlive)
                return;

            var ready = new ManualResetEventSlim(false);
            _hookThread = new Thread(() => HookThreadMain(ready))
            {
                IsBackground = true,
                Name = "ChatWheelPassthroughHook",
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();
            ready.Wait(TimeSpan.FromSeconds(2));

            if (_reinstallTimer == null)
            {
                _reinstallTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = HookReinstallInterval };
                _reinstallTimer.Tick += (_, _) => RequestHookReinstall();
            }
            _reinstallTimer.Start();
        }

        private void StopHookThread()
        {
            _reinstallTimer?.Stop();

            uint threadId = _hookThreadId;
            if (threadId != 0)
            {
                try { PostThreadMessage(threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero); }
                catch (Exception ex) { AppLogger.Warn("Failed to stop mouse hook thread.", ex); }
            }

            _hookThread = null;
            _hookThreadId = 0;
        }

        /// <summary>훅이 조용히 떨어졌을 수 있어 주기적으로 다시 건다. 훅 스레드에서 처리한다.</summary>
        private void RequestHookReinstall()
        {
            uint threadId = _hookThreadId;
            if (threadId == 0 || _hookThread == null || !_hookThread.IsAlive)
            {
                // 스레드가 죽었으면 새로 띄운다
                if (_snapshots.Length > 0)
                    EnsureHookThread();
                return;
            }

            PostThreadMessage(threadId, WM_APP_REINSTALL_HOOK, IntPtr.Zero, IntPtr.Zero);
        }

        private void HookThreadMain(ManualResetEventSlim ready)
        {
            _hookThreadId = GetCurrentThreadId();
            InstallHook();
            ready.Set();

            try
            {
                while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.message == WM_APP_REINSTALL_HOOK)
                    {
                        RemoveHook();
                        InstallHook();
                        continue;
                    }

                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Mouse hook thread failed.", ex);
            }
            finally
            {
                RemoveHook();
            }
        }

        private void InstallHook()
        {
            try
            {
                _hook = SetWindowsHookEx(WH_MOUSE_LL, _hookProc, GetModuleHandle(null), 0);
                if (_hook == IntPtr.Zero)
                    AppLogger.Warn($"SetWindowsHookEx(WH_MOUSE_LL) failed. win32={Marshal.GetLastWin32Error()}");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to install mouse hook.", ex);
            }
        }

        private void RemoveHook()
        {
            if (_hook == IntPtr.Zero)
                return;

            try { UnhookWindowsHookEx(_hook); }
            catch (Exception ex) { AppLogger.Warn("Failed to remove mouse hook.", ex); }
            _hook = IntPtr.Zero;
        }

        /// <summary>
        /// 훅 스레드에서 불린다. 스냅샷만 보고 삼킬지 정하고, 나머지는 UI 디스패처로 넘긴다.
        /// </summary>
        private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0)
                return CallNextHookEx(_hook, nCode, wParam, lParam);

            TargetSnapshot[] snapshots = _snapshots;
            if (snapshots.Length == 0)
                return CallNextHookEx(_hook, nCode, wParam, lParam);

            try
            {
                int msg = (int)wParam.ToInt64();
                if (msg == WM_MOUSEWHEEL)
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    int delta = unchecked((short)(info.mouseData >> 16));
                    var hit = FindTargetAt(snapshots, MapThroughScaler(info.pt));
                    if (hit != null)
                    {
                        if (hit.IsPassthrough && hit.IsAllowed)
                        {
                            // 통과 상태라 창에는 휠이 안 들어온다 — 대신 굴리고, 상호작용으로 깨운다
                            PostToUi(() => WakeByWheel(hit.State, delta));
                            return (IntPtr)1; // 게임에는 넘기지 않는다
                        }

                        if (!hit.IsPassthrough)
                            PostToUi(() => Touch(hit.State)); // 창이 직접 받는다 — 타이머만 연장
                    }
                }
                else if (msg == WM_MOUSEMOVE || msg == WM_LBUTTONDOWN || msg == WM_RBUTTONDOWN || msg == WM_MBUTTONDOWN)
                {
                    if (HasInteractiveWithTimer(snapshots))
                    {
                        long now = Environment.TickCount64;
                        bool isButton = msg != WM_MOUSEMOVE;
                        if (isButton || now - _lastActivityPostTick >= ActivityPostIntervalMs)
                        {
                            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                            var hit = FindTargetAt(snapshots, MapThroughScaler(info.pt));
                            if (hit != null && !hit.IsPassthrough)
                            {
                                _lastActivityPostTick = now;
                                PostToUi(() => Touch(hit.State));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Mouse hook handling failed.", ex);
            }

            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private void PostToUi(Action action)
        {
            var dispatcher = _uiDispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted)
                return;

            dispatcher.BeginInvoke(action, DispatcherPriority.Input);
        }

        private static bool HasInteractiveWithTimer(TargetSnapshot[] snapshots)
        {
            foreach (var snapshot in snapshots)
            {
                if (!snapshot.IsPassthrough && snapshot.IdleTimerRunning)
                    return true;
            }

            return false;
        }

        /// <summary>물리 픽셀 화면 좌표가 들어 있는 보이는 대상 창. 여러 개면 먼저 등록된 창.</summary>
        private static TargetSnapshot? FindTargetAt(TargetSnapshot[] snapshots, POINT pt)
        {
            foreach (var snapshot in snapshots)
            {
                if (snapshot.IsVisible && snapshot.Contains(pt))
                    return snapshot;
            }

            return null;
        }

        #endregion

        #region Magpie 좌표 변환 (훅 스레드)

        /// <summary>
        /// Magpie가 커서를 잡고 있으면(확대 창에 WS_EX_TRANSPARENT가 걸린 동안) 진짜 커서는 원본 창 좌표에 있다.
        /// 그 위치를 화면의 확대 영역 좌표로 바꾼다. 확대 중이 아니거나 커서가 풀려 있으면 그대로 돌려준다.
        /// 좌표는 모두 물리 픽셀이다 (앱은 Per-Monitor V2).
        /// </summary>
        private POINT MapThroughScaler(POINT pt)
        {
            try
            {
                IntPtr scaler = ResolveScalerWindow();
                if (scaler == IntPtr.Zero)
                    return pt;

                // Magpie는 커서를 잡는 동안만 확대 창을 클릭 통과로 둔다 (CursorManager 소스)
                if ((NativeMethods.GetWindowLong(scaler, NativeMethods.GWL_EXSTYLE) & NativeMethods.WS_EX_TRANSPARENT) == 0)
                    return pt;

                int srcLeft = ReadProp(scaler, "Magpie.SrcLeft");
                int srcTop = ReadProp(scaler, "Magpie.SrcTop");
                int srcRight = ReadProp(scaler, "Magpie.SrcRight");
                int srcBottom = ReadProp(scaler, "Magpie.SrcBottom");
                int destLeft = ReadProp(scaler, "Magpie.DestLeft");
                int destTop = ReadProp(scaler, "Magpie.DestTop");
                int destRight = ReadProp(scaler, "Magpie.DestRight");
                int destBottom = ReadProp(scaler, "Magpie.DestBottom");

                int srcW = srcRight - srcLeft, srcH = srcBottom - srcTop;
                int destW = destRight - destLeft, destH = destBottom - destTop;
                if (srcW <= 0 || srcH <= 0 || destW <= 0 || destH <= 0)
                    return pt; // 속성이 아직 없다 (확대 창 초기화 전)

                if (pt.X < srcLeft || pt.X >= srcRight || pt.Y < srcTop || pt.Y >= srcBottom)
                    return pt;

                return new POINT
                {
                    X = destLeft + (int)Math.Round((pt.X - srcLeft) * (double)destW / srcW),
                    Y = destTop + (int)Math.Round((pt.Y - srcTop) * (double)destH / srcH),
                };
            }
            catch
            {
                return pt;
            }
        }

        /// <summary>보이는 확대 창 핸들. 매 마우스 이동마다 찾지 않도록 잠시 캐시한다.</summary>
        private IntPtr ResolveScalerWindow()
        {
            long now = Environment.TickCount64;
            if (_cachedScaler != IntPtr.Zero && now - _cachedScalerTick < ScalerCacheMs)
                return NativeMethods.IsWindowVisible(_cachedScaler) ? _cachedScaler : IntPtr.Zero;

            if (now - _cachedScalerTick < ScalerCacheMs)
                return IntPtr.Zero; // 방금 찾아봤는데 없었다

            _cachedScalerTick = now;
            IntPtr hwnd = NativeMethods.FindWindow(ScalerTopmostGuard.MagpieWindowClass, null);
            _cachedScaler = hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd) ? hwnd : IntPtr.Zero;
            return _cachedScaler;
        }

        private static int ReadProp(IntPtr hwnd, string name)
            => unchecked((int)GetProp(hwnd, name).ToInt64());

        #endregion
    }
}
