using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 확대 프로그램(Magpie)의 확대 화면 위에 오버레이를 유지한다. "항상 위"가 켜져 있을 때만 동작한다.
    ///
    /// Magpie(0.12+)는 게임이 전경이 되면 확대 창을 HWND_TOPMOST로 올려 오버레이를 덮고, 올린 뒤
    /// "MagpieScalingChanged"를 브로드캐스트한다 (소스 확인). 그 알림을 받아 확대 화면 창이 실제로 오버레이를
    /// 덮었을 때만 다시 올린다 (이미 위에 있으면 건드리지 않아 깜박이지 않는다).
    /// 전경 전환 때도 한 번 확인한다. 확대 중이 아니면 아무것도 하지 않는다.
    /// 다시 올리는 규칙은 ForegroundTopmostGuard.ReassertNow와 같고, 잠금 해제 중에는 UiLockService.ReassertZOrder로 편집 화면 순서를 지킨다.
    ///
    /// Magpie는 관리자 권한으로만 동작하므로 브로드캐스트는 이 앱과 같거나 높은 권한에서 오고, UIPI에 막히지 않는다.
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

        private const string MagpieScalingChangedMessageName = "MagpieScalingChanged";
        private const uint MSGFLT_ALLOW = 1;

        // 이벤트(전경 전환·Magpie 알림) 뒤 첫 확인까지. Magpie가 창을 올린 직후에 보도록 짧게 둔다.
        private static readonly TimeSpan EventCheckDelay = TimeSpan.FromMilliseconds(40);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool ChangeWindowMessageFilterEx(IntPtr hwnd, uint message, uint action, IntPtr pChangeFilterStruct);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        private readonly ChatSettings _settings;
        private HwndSource? _source;
        private uint _magpieScalingChangedMessage;
        private IntPtr _scalerWindow = IntPtr.Zero;
        private DispatcherTimer? _eventTimer;

        public ScalerTopmostGuard(ChatSettings settings)
        {
            _settings = settings;
        }

        /// <summary>창이 Magpie의 확대 화면 창인지 (클래스 이름으로 판별).</summary>
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

                return string.Equals(buffer.ToString(), MagpieWindowClass, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        #region 시작 / 종료

        /// <summary>MainWindow 핸들이 만들어진 뒤 호출. Magpie 브로드캐스트 훅을 건다.</summary>
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
                    // Magpie는 관리자 권한으로 돌아 이 앱보다 낮을 일이 없지만, 만약을 위해 필터를 열어 둔다
                    if (!ChangeWindowMessageFilterEx(handle, _magpieScalingChangedMessage, MSGFLT_ALLOW, IntPtr.Zero))
                        AppLogger.Debug($"ChangeWindowMessageFilterEx for Magpie message failed. win32={Marshal.GetLastWin32Error()}");
                    _source.AddHook(HwndHook);
                }
                else
                {
                    AppLogger.Warn($"Failed to register Magpie message. win32={Marshal.GetLastWin32Error()}");
                }

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

        /// <summary>ForegroundTopmostGuard가 전경 전환을 받을 때 부른다. Magpie는 게임이 전경이 될 때 확대 화면을 올린다.</summary>
        public void NotifyForegroundChanged()
        {
            if (_scalerWindow != IntPtr.Zero)
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
                    CheckNow();
                }

                ScheduleCheck();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to handle Magpie scaling message.", ex);
            }

            return IntPtr.Zero;
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatSettings.OverlaysAlwaysOnTop))
                ScheduleCheck();
        }

        /// <summary>
        /// 이벤트 직후 짧게 기다렸다 한 번 확인한다. 전경 전환은 원자적이지 않아 Magpie가 창을 올리는 것과
        /// 알림이 오는 순서가 어긋날 수 있다.
        /// </summary>
        private void ScheduleCheck()
        {
            if (_eventTimer == null)
            {
                _eventTimer = new DispatcherTimer { Interval = EventCheckDelay };
                _eventTimer.Tick += (_, _) =>
                {
                    _eventTimer!.Stop();
                    CheckNow();
                };
            }

            _eventTimer.Stop();
            _eventTimer.Start();
        }

        #endregion

        #region 점검

        private void CheckNow()
        {
            try
            {
                IntPtr scaler = _settings.OverlaysAlwaysOnTop ? ResolveScalerWindow() : IntPtr.Zero;
                if (scaler == IntPtr.Zero)
                    return; // 확대가 끝났거나 '항상 위'가 꺼졌다

                KeepOverlaysAboveScaler();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Scaler topmost check failed.", ex);
            }
        }

        /// <summary>보이는 확대 화면 창. 알고 있던 창이 사라졌으면 클래스 이름으로 다시 찾는다.</summary>
        private IntPtr ResolveScalerWindow()
        {
            if (_scalerWindow != IntPtr.Zero && IsWindow(_scalerWindow) && NativeMethods.IsWindowVisible(_scalerWindow) && IsScalerWindow(_scalerWindow))
                return _scalerWindow;

            _scalerWindow = IntPtr.Zero;
            IntPtr hwnd = NativeMethods.FindWindow(MagpieWindowClass, null);
            if (hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd))
                _scalerWindow = hwnd;

            return _scalerWindow;
        }

        /// <summary>확대 화면 창이 우리 오버레이를 실제로 덮고 있을 때만 다시 올린다.</summary>
        private void KeepOverlaysAboveScaler()
        {
            if (!TopmostWindowHelper.IsAnyOverlayCovered(IsScalerWindow))
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

        #endregion
    }
}
