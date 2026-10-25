using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Addons
{
    public partial class EclipseAddonView : UserControl
    {
        public EclipseAddonView()
        {
            InitializeComponent();
        }

        public void ShowEtosDirection(string? imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
            {
                DirectionImage.Source = null;
                DirectionImage.Visibility = Visibility.Collapsed;
                ArrowText.Text = "?";
                return;
            }

            string resourcePath = imagePath.Replace('\\', '/').TrimStart('/');
            try
            {
                string asm = Assembly.GetExecutingAssembly().GetName().Name ?? string.Empty;
                var uri = new Uri($"pack://application:,,,/{asm};component/{resourcePath}", UriKind.Absolute);
                DirectionImage.Source = CreateBitmap(uri);
                DirectionImage.Visibility = Visibility.Visible;
                ArrowText.Text = ResolveArrow(resourcePath);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[EclipseAddonView] Failed to load resource '{imagePath}'.", ex);
                try
                {
                    string fsPath = Path.GetFullPath(resourcePath);
                    if (File.Exists(fsPath))
                    {
                        DirectionImage.Source = CreateBitmap(new Uri(fsPath, UriKind.Absolute));
                        DirectionImage.Visibility = Visibility.Visible;
                        ArrowText.Text = ResolveArrow(resourcePath);
                        return;
                    }
                }
                catch (Exception ex2)
                {
                    AppLogger.Warn("[EclipseAddonView] Fallback filesystem load failed.", ex2);
                }

                DirectionImage.Source = null;
                DirectionImage.Visibility = Visibility.Collapsed;
                ArrowText.Text = "!";
            }
        }

        private static string ResolveArrow(string resourcePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(resourcePath).ToUpperInvariant();
            return fileName switch
            {
                "N" => "\u2191",
                "NE" => "\u2197",
                "E" => "\u2192",
                "SE" => "\u2198",
                "S" => "\u2193",
                "SW" => "\u2199",
                "W" => "\u2190",
                "NW" => "\u2196",
                _ => "?"
            };
        }

        // 방향 이미지는 8장뿐이고 표시 크기는 72 DIP 고정 — 2배 DPI까지 대비해 144px로 디코딩해 재사용
        private const int DecodePixelWidth = 144;
        private static readonly Dictionary<string, BitmapImage> BitmapCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object BitmapCacheLock = new();

        private static BitmapImage CreateBitmap(Uri uri)
        {
            string key = uri.AbsoluteUri;
            lock (BitmapCacheLock)
            {
                if (BitmapCache.TryGetValue(key, out var cached))
                    return cached;
            }

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.DecodePixelWidth = DecodePixelWidth;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();

            lock (BitmapCacheLock)
            {
                BitmapCache[key] = bitmap;
            }
            return bitmap;
        }
    }
}
