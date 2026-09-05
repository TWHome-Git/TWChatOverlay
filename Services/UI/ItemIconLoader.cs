using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 아이템 아이콘 로더 — (1) 가장자리에서 이어지는 흰색/투명 배경을 투명 처리하고
    /// (2) 스프라이트 주변의 투명 여백을 잘라내, 어떤 원본이든 표시 박스를 균일하게 채운다.
    /// 스프라이트 내부의 흰색(하이라이트)은 가장자리와 연결되지 않으므로 보존된다.
    /// 결과는 URI별로 캐시된다.
    /// </summary>
    public static class ItemIconLoader
    {
        private const byte AlphaThreshold = 8;    // 이 값 이하의 알파는 여백으로 간주
        private const byte WhiteThreshold = 235;  // RGB가 모두 이 값 이상이면 흰 배경 후보

        private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.Ordinal);

        public static ImageSource? LoadTrimmed(string? uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return null;
            return Cache.GetOrAdd(uri, static u => CreateTrimmed(u));
        }

        private static ImageSource? CreateTrimmed(string uri)
        {
            try
            {
                var source = new BitmapImage(new Uri(uri, UriKind.Absolute));
                var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                if (width <= 0 || height <= 0)
                    return source;

                int stride = width * 4;
                var pixels = new byte[height * stride];
                converted.CopyPixels(pixels, stride, 0);

                RemoveEdgeConnectedBackground(pixels, width, height, stride);

                // 남은 불투명 픽셀의 경계 사각형 계산
                int minX = width, minY = height, maxX = -1, maxY = -1;
                for (int y = 0; y < height; y++)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < width; x++)
                    {
                        if (pixels[rowOffset + x * 4 + 3] <= AlphaThreshold)
                            continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }

                if (maxX < 0)
                    return source; // 전부 배경이면 원본 유지

                var cleaned = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
                var rect = new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
                BitmapSource result = rect.Width == width && rect.Height == height
                    ? cleaned
                    : new CroppedBitmap(cleaned, rect);
                if (result.CanFreeze) result.Freeze();
                return result;
            }
            catch
            {
                try { return new BitmapImage(new Uri(uri, UriKind.Absolute)); }
                catch { return null; }
            }
        }

        /// <summary>가장자리에서 연결된 배경(투명 또는 흰색에 가까운 픽셀)을 투명 처리한다.</summary>
        private static void RemoveEdgeConnectedBackground(byte[] pixels, int width, int height, int stride)
        {
            var visited = new bool[width * height];
            var queue = new Queue<int>();

            void TryEnqueue(int x, int y)
            {
                if (x < 0 || y < 0 || x >= width || y >= height)
                    return;
                int index = y * width + x;
                if (visited[index])
                    return;
                int p = y * stride + x * 4;
                byte b = pixels[p];
                byte g = pixels[p + 1];
                byte r = pixels[p + 2];
                byte a = pixels[p + 3];
                bool isTransparent = a <= AlphaThreshold;
                bool isWhite = r >= WhiteThreshold && g >= WhiteThreshold && b >= WhiteThreshold;
                if (!isTransparent && !isWhite)
                    return;
                visited[index] = true;
                if (isWhite)
                    pixels[p + 3] = 0; // 흰 배경 → 투명
                queue.Enqueue(index);
            }

            for (int x = 0; x < width; x++)
            {
                TryEnqueue(x, 0);
                TryEnqueue(x, height - 1);
            }
            for (int y = 0; y < height; y++)
            {
                TryEnqueue(0, y);
                TryEnqueue(width - 1, y);
            }

            while (queue.Count > 0)
            {
                int index = queue.Dequeue();
                int cx = index % width;
                int cy = index / width;
                TryEnqueue(cx - 1, cy);
                TryEnqueue(cx + 1, cy);
                TryEnqueue(cx, cy - 1);
                TryEnqueue(cx, cy + 1);
            }
        }
    }
}
