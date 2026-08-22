using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// WinForms NotifyIcon 없이 Shell_NotifyIcon으로 직접 구현한 트레이 아이콘.
    /// 트레이 아이콘 하나 때문에 WinForms 어셈블리 전체를 올리지 않기 위한 대체물이다.
    ///
    /// - 더블클릭 → <see cref="DoubleClick"/>
    /// - 우클릭 → 생성 시 받은 메뉴 항목을 네이티브 팝업 메뉴로 표시
    /// - 탐색기가 재시작되어 트레이가 비워지면(TaskbarCreated) 아이콘을 다시 등록한다
    /// </summary>
    public sealed class TrayIconService : IDisposable
    {
        public sealed record MenuItem(string Text, Action Click);

        private const int WM_APP = 0x8000;
        private const int WM_TRAYICON = WM_APP + 0x11;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;
        private const int WM_NULL = 0x0000;

        private const uint NIM_ADD = 0x0;
        private const uint NIM_MODIFY = 0x1;
        private const uint NIM_DELETE = 0x2;
        private const uint NIF_MESSAGE = 0x1;
        private const uint NIF_ICON = 0x2;
        private const uint NIF_TIP = 0x4;

        private const uint MF_STRING = 0x0;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const uint TPM_BOTTOMALIGN = 0x0020;

        private static readonly IntPtr HWND_MESSAGE = new(-3);
        private const int IDI_APPLICATION = 32512;

        private readonly HwndSource _sink;
        private readonly IReadOnlyList<MenuItem> _menuItems;
        private readonly string _tooltip;
        private readonly uint _taskbarCreatedMessage;
        private IntPtr _icon;
        private bool _iconOwned;
        private bool _disposed;

        /// <summary>트레이 아이콘 더블클릭.</summary>
        public event Action? DoubleClick;

        public TrayIconService(string tooltip, IReadOnlyList<MenuItem> menuItems)
        {
            _tooltip = tooltip ?? string.Empty;
            _menuItems = menuItems ?? Array.Empty<MenuItem>();

            // 메시지 수신 전용 숨은 창 (화면에 나타나지 않음)
            var parameters = new HwndSourceParameters("TWChatOverlayTraySink")
            {
                Width = 0,
                Height = 0,
                WindowStyle = 0,
                ParentWindow = HWND_MESSAGE,
            };
            _sink = new HwndSource(parameters);
            _sink.AddHook(WndProc);

            _taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
            LoadIcon();
            Register(NIM_ADD);
        }

        private void LoadIcon()
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath) && System.IO.File.Exists(exePath))
                {
                    IntPtr[] small = new IntPtr[1];
                    if (ExtractIconEx(exePath, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                    {
                        _icon = small[0];
                        _iconOwned = true;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to extract tray icon from executable; using system icon.", ex);
            }

            _icon = LoadIconW(IntPtr.Zero, (IntPtr)IDI_APPLICATION);
            _iconOwned = false;
        }

        private void Register(uint message)
        {
            var data = new NOTIFYICONDATA
            {
                cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _sink.Handle,
                uID = 1,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _icon,
                szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip,
            };

            if (!Shell_NotifyIconW(message, ref data))
                AppLogger.Warn($"Shell_NotifyIcon({message}) failed.");
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_disposed)
                return IntPtr.Zero;

            if (msg == WM_TRAYICON)
            {
                int mouseMessage = (int)(lParam.ToInt64() & 0xFFFF);
                switch (mouseMessage)
                {
                    case WM_LBUTTONDBLCLK:
                        handled = true;
                        try { DoubleClick?.Invoke(); }
                        catch (Exception ex) { AppLogger.Warn("Tray double-click handler failed.", ex); }
                        break;

                    case WM_RBUTTONUP:
                    case WM_CONTEXTMENU:
                        handled = true;
                        ShowContextMenu();
                        break;
                }
            }
            else if (_taskbarCreatedMessage != 0 && msg == (int)_taskbarCreatedMessage)
            {
                // 탐색기 재시작으로 트레이가 초기화됨 → 다시 등록
                Register(NIM_ADD);
            }

            return IntPtr.Zero;
        }

        private void ShowContextMenu()
        {
            if (_menuItems.Count == 0)
                return;

            IntPtr menu = CreatePopupMenu();
            if (menu == IntPtr.Zero)
                return;

            try
            {
                for (int i = 0; i < _menuItems.Count; i++)
                    AppendMenuW(menu, MF_STRING, (IntPtr)(i + 1), _menuItems[i].Text);

                if (!GetCursorPos(out POINT cursor))
                    return;

                // 팝업 메뉴가 바깥 클릭으로 닫히려면 호출 창이 포그라운드여야 한다 (MS 권장 패턴)
                SetForegroundWindow(_sink.Handle);
                int selected = TrackPopupMenuEx(
                    menu,
                    TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN,
                    cursor.X,
                    cursor.Y,
                    _sink.Handle,
                    IntPtr.Zero);
                PostMessage(_sink.Handle, WM_NULL, IntPtr.Zero, IntPtr.Zero);

                if (selected >= 1 && selected <= _menuItems.Count)
                {
                    try { _menuItems[selected - 1].Click(); }
                    catch (Exception ex) { AppLogger.Warn($"Tray menu '{_menuItems[selected - 1].Text}' failed.", ex); }
                }
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;

            try
            {
                var data = new NOTIFYICONDATA
                {
                    cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _sink.Handle,
                    uID = 1,
                };
                Shell_NotifyIconW(NIM_DELETE, ref data);
            }
            catch { }

            try { _sink.RemoveHook(WndProc); } catch { }
            try { _sink.Dispose(); } catch { }

            if (_iconOwned && _icon != IntPtr.Zero)
            {
                try { DestroyIcon(_icon); } catch { }
            }
            _icon = IntPtr.Zero;
        }

        // ===== P/Invoke =====

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATA
        {
            public uint cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, IntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
