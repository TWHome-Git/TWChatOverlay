using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

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
                Debug.WriteLine($"[EclipseAddonView] Failed to load resource '{imagePath}': {ex.GetType().Name}: {ex.Message}");
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
                    Debug.WriteLine($"[EclipseAddonView] Fallback filesystem load failed: {ex2.GetType().Name}: {ex2.Message}");
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

        private static BitmapImage CreateBitmap(Uri uri)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = uri;
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            return bitmap;
        }
    }
}
