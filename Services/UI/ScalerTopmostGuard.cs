using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 확대 프로그램(Magpie, Lossless Scaling)의 확대 화면 위에 오버레이를 유지한다. "항상 위"가 켜져 있을 때만 동작한다.
    ///
    /// 두 프로그램 모두 게임이 전경이 되면 확대 화면 창을 topmost 맨 위로 올려 오버레이를 덮는다.
    ///  - Magpie(0.12+): 확대 창을 HWND_TOPMOST로 올린 뒤 "MagpieScalingChanged"를 브로드캐스트한다 (소스 확인).
    ///  - Lossless Scaling(3.x): 출력 창(클래스 "LosslessScaling")이 게임이 전경일 때 topmost+클릭 통과로 바뀌어 맨 위로 올라가고,
    ///    게임이 전경이 아니면 topmost를 풀고 내려간다. 알림 메시지는 없다 (실측).
    /// 그래서 확대 화면 창이 보이는 동안 짧은 간격으로 z-순서를 점검해, 확대 화면 창이 실제로 오버레이를 덮었을 때만 다시 올린다
    /// (이미 위에 있으면 건드리지 않아 깜박이지 않는다). 점검은 Magpie 브로드캐스트, 확대 화면 창이 나타날 때(EVENT_OBJECT_SHOW),
    /// 전경 전환 때 시작하고 확대 화면 창이 사라지면 멈춘다. 확대 프로그램이 없으면 아무것도 하지 않는다.
    /// 다시 올리는 규칙은 ForegroundTopmostGuard.ReassertNow와 같고, 잠금 해제 중에는 UiLockService.ReassertZOrder로 편집 화면 순서를 지킨다.
    ///
    /// Magpie 메시지 wParam: 0 = 확대 종료(lParam 0) 또는 원본 창이 포커스를 잃음(lParam 1),
    ///                      1 = 확대 시작 또는 원본 창이 다시 전경(lParam = 확대 창 HWND),
    ///                      2 = 창 모드 확대 창 위치·크기 변경, 3 = 창 모드 이동·크기 조절 시작.
    /// 참고: https://github.com/Blinue/Magpie/blob/main/docs/Interact%20with%20Magpie%20programally.md
    /// </summary>
    public sealed class ScalerTopmostGuard
    {
        /// <summary>Magpie 확대 창 클래스 이름. Magpie가 바꾸지 않겠다고 밝힌 값이다.</summary>
        public const string MagpieWindowClass = "Window_Magpie_967EB565-6F73-4E94-AE53-00CC42592A22";

        /// <summary>Lossless Scaling 출력 창 클래스 이름 (설정 창은 WPF HwndWrapper라 겹치지 않는다).</summary>
        public const string LosslessScalingWindowClass = "LosslessScaling";

        private static readonly string[] ScalerWindowClasses = { MagpieWindowClass, LosslessScalingWindowClass };

        private const string MagpieScalingChangedMessageName = "MagpieScalingChanged";
        private const uint MSGFLT_ALLOW = 1;

        private const uint EVENT_OBJECT_SHOW = 0x8002;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
        private const int OBJID_WINDOW = 0;

        // 확대 화면 창이 보이는 동안의 점검 간격. 점검은 z-순서만 읽어 가볍고, 덮였을 때만 z-순서를 바꾼다.
        private static readonly TimeSpan WatchInterval = TimeSpan.FromMilliseconds(150);
        // 이벤트(전경 전환·창 표시·Magpie 알림) 뒤 첫 확인까지. 확대 프로그램이 창을 올린 직후에 보도록 짧게 둔다.
        private static readonly TimeSpan EventCheckDelay = TimeSpan.FromMilliseconds(40);

        // 확대 프로그램이 계속 다시 덮어 서로 뺏는 상황이면 점검에 의한 재승격을 잠시 멈춘다 (깜박임 반복 방지)
        private const int FightLimit = 6;
        private static readonly TimeSpan FightWindow = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan FightPause = TimeSpan.FromSeconds(10);

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr pChangeFilterStruct);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private readonly ChatSettings _settings;
        private HwndSource? _source;
        private uint _magpieScalingChangedMessage;
        private WinEventDelegate? _showCallback; // 훅이 살아 있는 동안 GC 수집 방지
        private IntPtr _showHook = IntPtr.Zero;
        private IntPtr _scalerWindow = IntPtr.Zero;
        private DispatcherTimer? _eventTimer;
        private DispatcherTimer? _watchTimer;
        private readonly Queue<DateTime> _watchReasserts = new();
        private DateTime _fightPausedUntil = DateTime.MinValue;

        public ScalerTopmostGuard(ChatSettings settings)
        {
            _settings = settings;
        }

        /// <summary>창이 확대 프로그램(Magpie·Lossless Scaling)의 확대 화면 창인지 (클래스 이름으로 판별).</summary>
        public static bool IsScalerWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
                return false;

            try
            {
                var buffer = new StringBuilder(128);
                int length = GetClassName(hwnd, buffer, buffer.Capacity);
                if (length <= 0)
                    return false;

                string className = buffer.ToString();
                foreach (string scalerClass in ScalerWindowClasses)
                {
                    if (string.Equals(className, scalerClass, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        #region 시작 / 종료

        /// <summary>MainWindow 핸들이 만들어진 뒤 호출. Magpie 브로드캐스트 훅과 확대 화면 창 표시 감시를 건다.</summary>
        public void Attach(IntPtr handle)
        {
            if (_source != null || handle == IntPtr.Zero)
                return;

            try
            {
                _source = HwndSource.FromHwnd(handle);
                if (_source == null)
                    return;

                _magpieScalingChangedMessage = RegisterWindowMessage(MagpieScalingChangedMessageName);
                if (_magpieScalingChangedMessage != 0)
                {
                    // 이 앱이 Magpie보다 높은 권한(관리자 실행)이면 브로드캐스트가 UIPI에 막히므로 허용해 둔다
                    if (!ChangeWindowMessageFilterEx(handle, _magpieScalingChangedMessage, MSGFLT_ALLOW, IntPtr.Zero))
                        AppLogger.Debug($"ChangeWindowMessageFilterEx for Magpie message failed. win32={Marshal.GetLastWin32Error()}");
                    _source.AddHook(HwndHook);
                }
                else
                {
                    AppLogger.Warn($"Failed to register Magpie message. win32={Marshal.GetLastWin32Error()}");
                }

                // Lossless Scaling은 알림이 없으므로 확대 화면 창이 나타나는 순간을 잡는다 (Magpie도 함께 잡힌다)
                _showCallback = OnObjectShown;
                _showHook = SetWinEventHook(EVENT_OBJECT_SHOW, EVENT_OBJECT_SHOW, IntPtr.Zero, _showCallback, 0, 0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
                if (_showHook == IntPtr.Zero)
                    AppLogger.Warn("Scaler show hook failed to install.");

                _settings.PropertyChanged += Settings_PropertyChanged;
                AppLogger.Info("Scaler topmost guard attached.");

                // 이미 확대 중일 때 켜졌을 수 있다 (확대 화면 창이 없으면 아무것도 하지 않는다)
                ScheduleCheck();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to attach scaler topmost guard.", ex);
            }
        }

        /// <summary>종료 때 호출. 훅과 타이머를 정리한다. 여러 번 불러도 된다.</summary>
        public void Detach()
        {
            try
            {
                _eventTimer?.Stop();
                _watchTimer?.Stop();

                if (_showHook != IntPtr.Zero)
                {
                    UnhookWinEvent(_showHook);
                    _showHook = IntPtr.Zero;
                }
                _showCallback = null;

                if (_source != null)
                {
                    _source.RemoveHook(HwndHook);
                    _source = null;
                    _settings.PropertyChanged -= Settings_PropertyChanged;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to detach scaler topmost guard.", ex);
            }
        }

        #endregion

        #region 이벤트

        /// <summary>ForegroundTopmostGuard가 전경 전환을 받을 때 부른다. 확대 프로그램은 게임이 전경이 될 때 확대 화면을 올린다.</summary>
        public void NotifyForegroundChanged()
        {
            if (_watchTimer?.IsEnabled == true || _scalerWindow != IntPtr.Zero)
                ScheduleCheck();
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_magpieScalingChangedMessage == 0 || (uint)msg != _magpieScalingChangedMessage)
                return IntPtr.Zero;

            try
            {
                AppLogger.Debug($"MagpieScalingChanged event={wParam.ToInt64()} lParam=0x{lParam.ToInt64():X}.");

                // 1 = 확대 시작/원본 창 전경: Magpie가 확대 창을 이미 올린 뒤에 보낸 알림이라 바로 확인한다
                if (wParam.ToInt64() == 1 && lParam != IntPtr.Zero && IsScalerWindow(lParam))
                {
                    _scalerWindow = lParam;
                    CheckNow(fromWatch: false);
                }

                ScheduleCheck();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to handle Magpie scaling message.", ex);
            }

            return IntPtr.Zero;
        }

        private void OnObjectShown(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // 창 자체의 표시만 본다 (캐럿·커서·자식 요소 이벤트는 건너뜀)
            if (idObject != OBJID_WINDOW || idChild != 0 || !IsScalerWindow(hwnd))
                return;

            try
            {
                _scalerWindow = hwnd;
                ScheduleCheck();
            }
            catch { }
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatSettings.OverlaysAlwaysOnTop))
                ScheduleCheck();
        }

        /// <summary>
        /// 이벤트 직후 짧게 기다렸다 한 번 확인한다. 전경 전환은 원자적이지 않아 확대 프로그램이 창을 올리는 것과
        /// 알림·이벤트가 오는 순서가 어긋날 수 있다. 확인할 때 확대 화면 창이 보이면 주기 점검도 켠다.
        /// </summary>
        private void ScheduleCheck()
        {
            if (_eventTimer == null)
            {
                _eventTimer = new DispatcherTimer { Interval = EventCheckDelay };
                _eventTimer.Tick += (_, _) =>
                {
                    _eventTimer!.Stop();
                    CheckNow(fromWatch: false);
                };
            }

            _eventTimer.Stop();
            _eventTimer.Start();
        }

        #endregion

        #region 점검

        private void CheckNow(bool fromWatch)
        {
            try
            {
                IntPtr scaler = _settings.OverlaysAlwaysOnTop ? ResolveScalerWindow() : IntPtr.Zero;
                if (scaler == IntPtr.Zero)
                {
                    // 확대가 끝났거나 '항상 위'가 꺼졌다 → 점검을 멈춘다
                    _watchTimer?.Stop();
                    return;
                }

                EnsureWatchTimer();
                KeepOverlaysAboveScaler(fromWatch);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Scaler topmost check failed.", ex);
            }
        }

        /// <summary>보이는 확대 화면 창. 알고 있던 창이 사라졌으면 클래스 이름으로 다시 찾는다.</summary>
        private IntPtr ResolveScalerWindow()
        {
            if (_scalerWindow != IntPtr.Zero && IsWindow(_scalerWindow) && IsWindowVisible(_scalerWindow) && IsScalerWindow(_scalerWindow))
                return _scalerWindow;

            _scalerWindow = IntPtr.Zero;
            foreach (string scalerClass in ScalerWindowClasses)
            {
                IntPtr hwnd = FindWindow(scalerClass, null);
                if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
                {
                    _scalerWindow = hwnd;
                    break;
                }
            }

            return _scalerWindow;
        }

        private void EnsureWatchTimer()
        {
            if (_watchTimer == null)
            {
                _watchTimer = new DispatcherTimer { Interval = WatchInterval };
                _watchTimer.Tick += (_, _) => CheckNow(fromWatch: true);
            }

            if (!_watchTimer.IsEnabled)
                _watchTimer.Start();
        }

        /// <summary>확대 화면 창이 우리 오버레이를 실제로 덮고 있을 때만 다시 올린다.</summary>
        private void KeepOverlaysAboveScaler(bool fromWatch)
        {
            if (!TopmostWindowHelper.IsAnyOverlayCovered(IsScalerWindow))
                return;

            if (fromWatch && IsFighting())
                return;

            // 잠금 해제(편집) 중에는 일반 재승격이 꺼져 있다. 편집 중 게임을 누르면 확대 화면이 격자·오버레이를
            // 덮은 채 남으므로, 잠금 해제 화면의 순서(격자 → 오버레이 → 인스펙터)대로 다시 올린다.
            var uiLock = AppServices.Get<UiLockService>();
            if (uiLock.IsUnlocked)
            {
                uiLock.ReassertZOrder();
                AppLogger.Debug("Scaler window covered unlock overlays; reasserted unlock z-order.");
                return;
            }

            if (AppServices.Get<ForegroundTopmostGuard>().ReassertNow())
                AppLogger.Debug("Scaler window covered overlays; reasserted topmost.");
        }

        /// <summary>
        /// 전경 전환 없이 점검 재승격이 짧은 시간에 여러 번 필요하면, 확대 프로그램이 계속 다시 덮는 것으로 보고 잠시 멈춘다.
        /// (이벤트로 시작한 확인은 막지 않는다)
        /// </summary>
        private bool IsFighting()
        {
            DateTime now = DateTime.UtcNow;
            if (now < _fightPausedUntil)
                return true;

            while (_watchReasserts.Count > 0 && now - _watchReasserts.Peek() > FightWindow)
                _watchReasserts.Dequeue();

            _watchReasserts.Enqueue(now);
            if (_watchReasserts.Count <= FightLimit)
                return false;

            _watchReasserts.Clear();
            _fightPausedUntil = now + FightPause;
            AppLogger.Warn($"Scaler window keeps covering overlays; pausing periodic reassert for {FightPause.TotalSeconds:0}s.");
            return true;
        }

        #endregion
    }
}
