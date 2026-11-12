using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 운명 선고 공지 그림에서 발판 순서를 글자 인식(Windows 기본 인식기, Windows.Media.Ocr)으로 읽는다.
    ///
    /// 2026-11-12 시험: 한 장만 찍은 그림은 대미지 숫자가 글을 덮어 되다 말다 했고, 여러 장을 중앙값으로 합친 그림은
    /// 2배로 키웠을 때 네 낱말을 정확히 읽었다 (원래 크기에서는 "슬픔"을 "술품"으로 읽음). 그래서 늘 키워서 넘긴다 (<see cref="TargetTextScale"/>).
    /// 한글 인식기는 Windows 설정 > 언어에 한국어가 깔려 있어야 한다 (없으면 <see cref="IsAvailable"/>가 false).
    /// 인식에는 0.05초쯤 든다. 낱말을 발판으로 맞추는 일은 <see cref="DoomSentenceOrderParser"/>가 한다.
    /// </summary>
    public static class DoomSentenceOcr
    {
        /// <summary>읽은 순서와, 인식기가 돌려준 글 그대로 (기록용).</summary>
        public sealed record Result(IReadOnlyList<DoomPad> Order, string RawText);

        /// <summary>
        /// 인식기에 넘길 때 글자를 게임 해상도의 이만큼으로 키운다. 확대 화면(1.34배)을 2배로 키운 2.7배쯤에서 네 낱말이 정확히 읽혔고,
        /// 1.34배 그대로는 "슬픔"을 "술품"으로 읽었다. 확대 없이 찍은 그림(1배)은 그래서 3배 가까이 키운다.
        /// </summary>
        public const double TargetTextScale = 2.7;

        private static readonly object EngineLock = new();
        private static OcrEngine? _engine;
        private static bool _engineResolved;

        /// <summary>한글 인식기를 쓸 수 있는지.</summary>
        public static bool IsAvailable => ResolveEngine() != null;

        private static OcrEngine? ResolveEngine()
        {
            lock (EngineLock)
            {
                if (_engineResolved)
                    return _engine;

                try
                {
                    _engine = OcrEngine.TryCreateFromLanguage(new Language("ko"));
                    if (_engine == null)
                        AppLogger.Warn("Korean OCR recognizer is not installed; doom sentence order falls back to the captured image.");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to create OCR engine.", ex);
                    _engine = null;
                }
                _engineResolved = true;
                return _engine;
            }
        }

        /// <summary>그림(얼린 것)에서 순서를 읽는다. 인식기가 없거나 실패하면 null. 어느 스레드에서 불러도 된다.</summary>
        /// <param name="captureScale">그림이 게임 해상도의 몇 배로 찍혔는지 (확대 프로그램 배율, 안 쓰면 1). 여기에 맞춰 더 키운다.</param>
        public static async Task<Result?> RecognizeAsync(BitmapSource image, double captureScale = 1.0)
        {
            OcrEngine? engine = ResolveEngine();
            if (engine == null || image == null)
                return null;

            try
            {
                double upscale = Math.Clamp(TargetTextScale / Math.Max(0.25, captureScale), 1.0, 4.0);
                using SoftwareBitmap bitmap = ToSoftwareBitmap(image, upscale);
                OcrResult result = await engine.RecognizeAsync(bitmap);

                // 줄은 위에서 아래로, 낱말은 왼쪽에서 오른쪽으로
                var lines = result.Lines
                    .Where(line => line.Words.Count > 0)
                    .OrderBy(line => line.Words.Min(word => word.BoundingRect.Y))
                    .Select(line => (IReadOnlyList<string>)line.Words.OrderBy(word => word.BoundingRect.X).Select(word => word.Text).ToList())
                    .ToList();

                var raw = new StringBuilder();
                foreach (IReadOnlyList<string> words in lines)
                    raw.AppendLine(string.Join(" ", words));

                return new Result(DoomSentenceOrderParser.Parse(lines), raw.ToString().TrimEnd());
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Doom sentence OCR failed.", ex);
                return null;
            }
        }

        /// <summary>WPF 그림을 키워 인식기가 받는 SoftwareBitmap(Bgra8)으로 옮긴다.</summary>
        private static SoftwareBitmap ToSoftwareBitmap(BitmapSource image, double factor)
        {
            // 인식기가 받는 가장 큰 변(10000px)을 넘지 않게
            double longest = Math.Max(image.PixelWidth, image.PixelHeight) * factor;
            if (longest > OcrEngine.MaxImageDimension)
                factor *= OcrEngine.MaxImageDimension / longest;

            BitmapSource scaled = new TransformedBitmap(image, new ScaleTransform(factor, factor));
            var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth;
            int height = bgra.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);

            return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        }
    }
}
