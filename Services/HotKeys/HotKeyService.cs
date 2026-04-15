using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 전역 단축키 등록/해제 및 입력 수신을 처리하는 서비스입니다.
    /// </summary>
    public class HotKeyService : IDisposable
    {
        public const int EXIT_HOTKEY_ID = 9001;
        public const int TOGGLE_OVERLAY_ID = 9002;
        public const int TOGGLE_ADDON_ID = 9003;
        public const int TOGGLE_ALWAYS_VISIBLE_ID = 9004;
        public const int TOGGLE_DAILY_WEEKLY_CONTENT_ID = 9005;
        public const int TOGGLE_ETA_RANKING_ID = 9006;
        public const int TOGGLE_COEFFICIENT_ID = 9007;
        public const int TOGGLE_EQUIPMENTDB_ID = 9008;
        public const int TOGGLE_ENCRYPT_ID = 9009;
        public const int TOGGLE_SETTINGS_ID = 9010;

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint VK_F1 = 0x70;
        public const uint VK_F2 = 0x71;
        public const uint VK_F3 = 0x72;
        public const uint VK_F4 = 0x73;
        public const uint VK_F5 = 0x74;
        public const uint VK_F6 = 0x75;
        public const uint VK_F7 = 0x76;
        public const uint VK_F8 = 0x77;
        public const uint VK_F9 = 0x78;

        private readonly IntPtr _handle;
        private readonly HwndSource _source;
        private readonly System.Collections.Generic.HashSet<int> _registeredIds = new();
        public event Action<int>? HotKeyPressed;

        /// <summary>
        /// 지정한 윈도우 핸들에 단축키 훅을 연결합니다.
        /// </summary>
        public HotKeyService(IntPtr handle)
        {
            _handle = handle;
            _source = HwndSource.FromHwnd(_handle);
            _source.AddHook(HwndHook);
            AppLogger.Info($"HotKeyService attached to handle={_handle}.");
        }

        public bool Register(int id, uint modifiers, uint vk)
        {
            if (_registeredIds.Contains(id))
            {
                return true;
            }

            bool ok = NativeMethods.RegisterHotKey(_handle, id, modifiers, vk);
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                AppLogger.Warn($"Failed to register hotkey id={id} modifiers=0x{modifiers:X} key=0x{vk:X} win32={err}.");
                return false;
            }

            _registeredIds.Add(id);
            AppLogger.Info($"Registered hotkey id={id} modifiers=0x{modifiers:X} key=0x{vk:X}.");
            return true;
        }

        public void Unregister(int id)
        {
            try
            {
                NativeMethods.UnregisterHotKey(_handle, id);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to unregister hotkey id={id}.", ex);
            }
            _registeredIds.Remove(id);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                AppLogger.Debug($"Hotkey pressed id={id}.");
                HotKeyPressed?.Invoke(id);
                handled = true;
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            // Unregister all keys we registered
            try
            {
                foreach (var id in _registeredIds)
                {
                    try { NativeMethods.UnregisterHotKey(_handle, id); }
                    catch (Exception ex) { AppLogger.Warn($"Failed to unregister hotkey id={id} during dispose.", ex); }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("HotKeyService dispose encountered an error.", ex);
            }
            _registeredIds.Clear();
            _source?.RemoveHook(HwndHook);
            AppLogger.Info("HotKeyService disposed.");
        }

        public static bool TryParseHotKey(string? text, out uint modifiers, out uint vk)
        {
            modifiers = 0;
            vk = 0;

            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            Key? keyPart = null;

            foreach (var raw in parts)
            {
                var token = raw.Trim();
                if (token.Equals("CTRL", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("CONTROL", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= MOD_CONTROL;
                    continue;
                }
                if (token.Equals("ALT", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= MOD_ALT;
                    continue;
                }
                if (token.Equals("SHIFT", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= MOD_SHIFT;
                    continue;
                }
                if (token.Equals("WIN", StringComparison.OrdinalIgnoreCase) ||
                    token.Equals("WINDOWS", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= MOD_WIN;
                    continue;
                }

                if (token.Equals("ESC", StringComparison.OrdinalIgnoreCase))
                {
                    keyPart = Key.Escape;
                    continue;
                }

                if (Enum.TryParse<Key>(token, true, out var parsedKey))
                {
                    keyPart = parsedKey;
                }
            }

            if (keyPart == null) return false;

            vk = (uint)KeyInterop.VirtualKeyFromKey(keyPart.Value);
            return true;
        }
    }
}
