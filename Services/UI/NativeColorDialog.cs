using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// WinForms ColorDialog 없이 comdlg32 ChooseColor를 직접 호출하는 색 선택 대화상자.
    /// WinForms가 내부적으로 감싸던 바로 그 네이티브 대화상자라 화면은 동일하다.
    /// </summary>
    public static class NativeColorDialog
    {
        private const uint CC_RGBINIT = 0x00000001;
        private const uint CC_FULLOPEN = 0x00000002;
        private const uint CC_ANYCOLOR = 0x00000100;

        // 사용자 지정 색 16칸은 세션 동안 유지된다 (WinForms ColorDialog와 같은 동작)
        private static readonly uint[] CustomColors = new uint[16];

        /// <summary>색 선택 대화상자를 띄운다. 확인이면 true와 선택한 색.</summary>
        public static bool TryPick(Color initial, out Color result, Window? owner = null)
        {
            result = initial;

            IntPtr custom = Marshal.AllocHGlobal(sizeof(uint) * 16);
            try
            {
                Marshal.Copy((int[])(object)CustomColors, 0, custom, 16);

                var cc = new CHOOSECOLOR
                {
                    lStructSize = Marshal.SizeOf<CHOOSECOLOR>(),
                    hwndOwner = ResolveOwnerHandle(owner),
                    rgbResult = ToColorRef(initial),
                    lpCustColors = custom,
                    Flags = CC_RGBINIT | CC_FULLOPEN | CC_ANYCOLOR,
                };

                if (!ChooseColorW(ref cc))
                    return false;

                Marshal.Copy(custom, (int[])(object)CustomColors, 0, 16);
                result = FromColorRef(cc.rgbResult);
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(custom);
            }
        }

        private static IntPtr ResolveOwnerHandle(Window? owner)
        {
            try
            {
                var window = owner ?? Application.Current?.MainWindow;
                return window == null ? IntPtr.Zero : new WindowInteropHelper(window).Handle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        // COLORREF = 0x00BBGGRR
        private static uint ToColorRef(Color c) => (uint)(c.R | (c.G << 8) | (c.B << 16));

        private static Color FromColorRef(uint v)
            => Color.FromRgb((byte)(v & 0xFF), (byte)((v >> 8) & 0xFF), (byte)((v >> 16) & 0xFF));

        [StructLayout(LayoutKind.Sequential)]
        private struct CHOOSECOLOR
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public uint rgbResult;
            public IntPtr lpCustColors;
            public uint Flags;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpTemplateName;
        }

        [DllImport("comdlg32.dll", CharSet = CharSet.Unicode)]
        private static extern bool ChooseColorW(ref CHOOSECOLOR lpcc);
    }
}
