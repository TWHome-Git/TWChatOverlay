using System;
using System.Collections.Concurrent;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// pack:// 아이콘(시드·경험의 정수·아이템·버프 등)을 URI별로 한 번만 만들어 Freeze해 두고 공유한다.
    /// 창·행·호출마다 새 BitmapImage를 만들던 곳에서 쓴다. 트리밍이 필요한 아이템 아이콘은 ItemIconLoader를 쓴다.
    /// 로드에 실패하면 예외를 그대로 던지고 캐시에 남기지 않는다(호출부가 기존처럼 try/catch로 처리).
    /// </summary>
    public static class PackImageCache
    {
        private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new(StringComparer.Ordinal);

        public static ImageSource Get(string uri) => Cache.GetOrAdd(uri, static u => Create(u));

        private static BitmapImage Create(string uri)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(uri, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }
}
