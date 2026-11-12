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
    /// 2배로 키웠을 때 네 낱말을 정확히 읽었다 (원래 크기에서는 "슬픔"을 "술품"으로 읽음). 그래서 늘 2배로 키워서 넘긴다.
    /// 한글 인식기는 Windows 설정 > 언어에 한국어가 깔려 있어야 한다 (없으면 <see cref="IsAvailable"/>가 false).
    /// 인식에는 0.05초쯤 든다. 낱말을 발판으로 맞추는 일은 <see cref="DoomSentenceOrderParser"/>가 한다.
    /// </summary>
    public static class DoomSentenceOcr
    {
        /// <summary>읽은 순서와, 인식기가 돌려준 글 그대로 (기록용).</summary>
        public sealed record Result(IReadOnlyList<DoomPad> Order, string RawText);

        private const double UpscaleFactor = 2.0;

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
        public static async Task<Result?> RecognizeAsync(BitmapSource image)
        {
            OcrEngine? engine = ResolveEngine();
            if (engine == null || image == null)
                return null;

            try
            {
                using SoftwareBitmap bitmap = ToSoftwareBitmap(image);
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

        /// <summary>WPF 그림을 2배로 키워 인식기가 받는 SoftwareBitmap(Bgra8)으로 옮긴다.</summary>
        private static SoftwareBitmap ToSoftwareBitmap(BitmapSource image)
        {
            double factor = UpscaleFactor;
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
