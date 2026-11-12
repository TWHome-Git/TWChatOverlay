using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 게임 화면을 "보이는 대로" 찍는다 (화면 DC → BitBlt).
    ///
    /// 게임 클라이언트는 DirectX9 창이라 창 내용만 따로 찍는 PrintWindow는 검은 그림이 나온다 (2026-11-12 확인).
    /// 화면에서 그 자리를 복사하면 30ms쯤에 찍히지만, 게임 위에 떠 있는 다른 창(오버레이 포함)도 같이 찍힌다.
    /// 이 앱은 Per-Monitor V2 DPI 인식이라 창 좌표와 화면 픽셀이 그대로 맞는다.
    /// </summary>
    public static class ScreenCaptureHelper
    {
        /// <summary>화면 픽셀 단위 사각형.</summary>
        public readonly record struct PixelRect(int X, int Y, int Width, int Height)
        {
            public bool IsEmpty => Width <= 0 || Height <= 0;
        }

        /// <summary>찍은 그림에서 잘라 낼 자리. 왼쪽·위·너비·높이를 그림 크기에 대한 퍼센트로 적는다.</summary>
        public readonly record struct PercentRegion(double Left, double Top, double Width, double Height)
        {
            /// <summary>"34,17,32,15" 같은 글을 읽는다. 숫자 넷이 아니거나 범위를 벗어나면 false.</summary>
            public static bool TryParse(string? text, out PercentRegion region)
            {
                region = default;
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                string[] parts = text.Split(new[] { ',', ';', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 4)
                    return false;

                var values = new double[4];
                for (int i = 0; i < 4; i++)
                {
                    if (!double.TryParse(parts[i].TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                        return false;
                }

                if (values[0] < 0 || values[1] < 0 || values[2] <= 0 || values[3] <= 0)
                    return false;
                if (values[0] >= 100 || values[1] >= 100)
                    return false;

                region = new PercentRegion(values[0], values[1], Math.Min(values[2], 100 - values[0]), Math.Min(values[3], 100 - values[1]));
                return true;
            }

            public override string ToString()
                => string.Create(CultureInfo.InvariantCulture, $"{Left:0.##},{Top:0.##},{Width:0.##},{Height:0.##}");
        }

        private const int SRCCOPY = 0x00CC0020;
        private const int CAPTUREBLT = 0x40000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out NativeMethods.RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        private const uint WDA_NONE = 0x00000000;
        private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

        /// <summary>
        /// 그 창을 화면 캡처에서 뺀다 (Windows 10 2004 이상). 성공하면 이 앱이 화면을 찍을 때도, 다른 캡처 도구가 찍을 때도 그 창은 안 나온다.
        /// 실패(옛 Windows 등)하면 false — 그때는 창이 찍힐 자리를 가리는지 직접 봐야 한다.
        /// </summary>
        public static bool ExcludeFromCapture(IntPtr hwnd) => SetCaptureExclusion(hwnd, true);

        /// <summary>그 창을 화면 캡처에서 빼거나(true) 다시 찍히게(false) 한다. 돌려주는 값은 호출이 성공했는지.</summary>
        public static bool SetCaptureExclusion(IntPtr hwnd, bool exclude)
        {
            try
            {
                return hwnd != IntPtr.Zero && SetWindowDisplayAffinity(hwnd, exclude ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
            }
            catch
            {
                return false;
            }
        }

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int width, int height, IntPtr hdcSrc, int xSrc, int ySrc, int rop);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        /// <summary>
        /// 지금 게임이 보이는 화면 자리. 확대 프로그램(Magpie)이 켜져 있으면 눈에 보이는 것은 확대 화면이므로 그 창을,
        /// 아니면 게임 창의 안쪽(제목줄·테두리 제외)을 돌려준다. 게임 창이 없거나 최소화돼 있으면 null.
        /// </summary>
        /// <param name="processNames">게임 프로세스 이름 목록 (쉼표 구분, 설정의 TopmostGuardProcessNames).</param>
        /// <param name="scalerWindowClass">확대 화면 창 클래스 이름. 없으면 확대 화면은 보지 않는다.</param>
        public static PixelRect? ResolveGameArea(string? processNames, string? scalerWindowClass)
        {
            if (!string.IsNullOrEmpty(scalerWindowClass))
            {
                IntPtr scaler = NativeMethods.FindWindow(scalerWindowClass, null);
                if (scaler != IntPtr.Zero && NativeMethods.IsWindowVisible(scaler) &&
                    NativeMethods.GetWindowRect(scaler, out NativeMethods.RECT sr))
                {
                    var rect = new PixelRect(sr.Left, sr.Top, sr.Right - sr.Left, sr.Bottom - sr.Top);
                    if (!rect.IsEmpty)
                        return rect;
                }
            }

            IntPtr game = FindGameWindow(processNames);
            if (game == IntPtr.Zero || IsIconic(game))
                return null;
            if (!GetClientRect(game, out NativeMethods.RECT cr))
                return null;

            var origin = new POINT { X = 0, Y = 0 };
            if (!ClientToScreen(game, ref origin))
                return null;

            var area = new PixelRect(origin.X, origin.Y, cr.Right - cr.Left, cr.Bottom - cr.Top);
            return area.IsEmpty ? null : area;
        }

        /// <summary>게임 창. 지금 앞에 있는 창이 게임이면 그 창(클라를 여러 개 띄운 경우), 아니면 이름으로 찾은 첫 창.</summary>
        public static IntPtr FindGameWindow(string? processNames)
        {
            string[] names = (processNames ?? string.Empty)
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i].EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    names[i] = names[i][..^4];
            }

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground != IntPtr.Zero && IsGameWindow(foreground, names))
                return foreground;

            foreach (string name in names)
            {
                Process[] processes;
                try { processes = Process.GetProcessesByName(name); }
                catch { continue; }

                IntPtr found = IntPtr.Zero;
                foreach (Process process in processes)
                {
                    try
                    {
                        IntPtr hwnd = process.MainWindowHandle;
                        if (found == IntPtr.Zero && hwnd != IntPtr.Zero && NativeMethods.IsWindowVisible(hwnd))
                            found = hwnd;
                    }
                    catch { }
                    finally { process.Dispose(); }
                }
                if (found != IntPtr.Zero)
                    return found;
            }

            return IntPtr.Zero;
        }

        private static bool IsGameWindow(IntPtr hwnd, string[] names)
        {
            try
            {
                NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid != 0)
                {
                    using Process process = Process.GetProcessById((int)pid);
                    foreach (string name in names)
                    {
                        if (string.Equals(process.ProcessName, name, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch { }

            // 프로세스 이름을 못 얻으면(권한) 창 제목으로 본다. 게임 클라이언트 제목은 "Talesweaver Client Version …"이다
            int length = NativeMethods.GetWindowTextLength(hwnd);
            if (length <= 0)
                return false;
            var title = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hwnd, title, title.Capacity);
            return title.ToString().Contains("talesweaver", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>화면의 그 자리를 찍어 얼린(Freeze) 그림으로 돌려준다. 어느 스레드에서 불러도 된다.</summary>
        public static BitmapSource Capture(PixelRect rect)
        {
            if (rect.IsEmpty)
                throw new ArgumentException("빈 영역은 찍을 수 없습니다.", nameof(rect));

            IntPtr screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero)
                throw new InvalidOperationException("화면 DC를 얻지 못했습니다.");

            IntPtr memDc = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            try
            {
                memDc = CreateCompatibleDC(screenDc);
                bitmap = CreateCompatibleBitmap(screenDc, rect.Width, rect.Height);
                if (memDc == IntPtr.Zero || bitmap == IntPtr.Zero)
                    throw new InvalidOperationException("캡처용 비트맵을 만들지 못했습니다.");

                IntPtr old = SelectObject(memDc, bitmap);
                bool ok = BitBlt(memDc, 0, 0, rect.Width, rect.Height, screenDc, rect.X, rect.Y, SRCCOPY | CAPTUREBLT);
                SelectObject(memDc, old);
                if (!ok)
                    throw new InvalidOperationException("화면 복사(BitBlt)가 실패했습니다.");

                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                // 화면 비트맵의 알파 칸은 0으로 차 있을 수 있다 — 그대로 쓰면 투명하게 그려지므로 알파 없는 형식으로 바꾼다
                var opaque = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
                opaque.Freeze();
                return opaque;
            }
            finally
            {
                if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
                if (memDc != IntPtr.Zero) DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        /// <summary>
        /// 너비·높이가 주어진 그림 안에서 퍼센트 영역이 차지하는 픽셀 자리 (그림 왼쪽 위 기준).
        /// <see cref="Crop"/>과 화면에서 그 자리만 바로 찍을 때가 같은 자리를 쓰도록 계산을 한 곳에 둔다.
        /// </summary>
        public static PixelRect RegionRect(int imageWidth, int imageHeight, PercentRegion region)
        {
            int x = (int)Math.Round(imageWidth * region.Left / 100.0);
            int y = (int)Math.Round(imageHeight * region.Top / 100.0);
            int width = (int)Math.Round(imageWidth * region.Width / 100.0);
            int height = (int)Math.Round(imageHeight * region.Height / 100.0);

            x = Math.Clamp(x, 0, Math.Max(0, imageWidth - 1));
            y = Math.Clamp(y, 0, Math.Max(0, imageHeight - 1));
            width = Math.Min(width, imageWidth - x);
            height = Math.Min(height, imageHeight - y);
            return new PixelRect(x, y, width, height);
        }

        /// <summary>그림에서 퍼센트로 적은 자리를 잘라 얼린 그림으로 돌려준다. 자를 자리가 1픽셀도 안 되면 원본 그대로.</summary>
        public static BitmapSource Crop(BitmapSource image, PercentRegion region)
        {
            PixelRect rect = RegionRect(image.PixelWidth, image.PixelHeight, region);
            if (rect.IsEmpty)
                return image;

            var cropped = new CroppedBitmap(image, new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height));
            cropped.Freeze();
            return cropped;
        }

        /// <summary>
        /// 같은 자리를 이어 찍은 그림들을 픽셀(색 칸)마다 중앙값으로 합친다. 제자리에 있는 것(공지 글·배경)은 남고,
        /// 장마다 자리가 바뀌는 것(떠오르는 대미지 숫자·이펙트)은 절반 넘는 장에서 그 픽셀을 덮지 않는 한 빠진다.
        /// 크기가 첫 장과 다른 그림은 건너뛴다. 한 장뿐이면 그 장을 그대로 돌려준다.
        /// </summary>
        public static BitmapSource MedianComposite(IReadOnlyList<BitmapSource> frames)
        {
            if (frames == null || frames.Count == 0)
                throw new ArgumentException("합칠 그림이 없습니다.", nameof(frames));

            int width = frames[0].PixelWidth;
            int height = frames[0].PixelHeight;
            int stride = width * 3;
            var buffers = new List<byte[]>(frames.Count);
            foreach (BitmapSource frame in frames)
            {
                if (frame.PixelWidth != width || frame.PixelHeight != height)
                    continue;
                BitmapSource bgr = frame.Format == PixelFormats.Bgr24 ? frame : new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
                var pixels = new byte[stride * height];
                bgr.CopyPixels(pixels, stride, 0);
                buffers.Add(pixels);
            }

            if (buffers.Count == 1)
                return frames[0];

            int count = buffers.Count;
            var result = new byte[stride * height];
            Span<byte> values = count <= 64 ? stackalloc byte[count] : new byte[count];
            for (int i = 0; i < result.Length; i++)
            {
                for (int f = 0; f < count; f++)
                    values[f] = buffers[f][i];

                // 장 수가 적어 삽입 정렬이면 충분하다
                for (int a = 1; a < count; a++)
                {
                    byte key = values[a];
                    int b = a - 1;
                    while (b >= 0 && values[b] > key)
                    {
                        values[b + 1] = values[b];
                        b--;
                    }
                    values[b + 1] = key;
                }

                result[i] = (count & 1) == 1
                    ? values[count / 2]
                    : (byte)((values[count / 2 - 1] + values[count / 2] + 1) / 2);
            }

            BitmapSource composite = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, result, stride);
            composite.Freeze();
            return composite;
        }

        /// <summary>그림을 PNG 파일로 쓴다. 폴더가 없으면 만든다.</summary>
        public static void SavePng(BitmapSource image, string path)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
            encoder.Save(stream);
        }
    }
}
