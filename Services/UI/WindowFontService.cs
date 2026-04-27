using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    public static class WindowFontService
    {
        public static FontFamily ResolveCurrentFont()
        {
            try
            {
                MainWindow? mainWindow = Application.Current?.Windows
                    .OfType<MainWindow>()
                    .FirstOrDefault();

                if (mainWindow?.CurrentFont != null)
                    return mainWindow.CurrentFont;
            }
            catch { }

            try
            {
                ChatSettings settings = ConfigService.Load();
                return FontService.GetFont(settings.FontFamily);
            }
            catch { }

            return new FontFamily("Malgun Gothic");
        }

        public static void Apply(Window window)
        {
            if (window == null)
                return;

            window.FontFamily = ResolveCurrentFont();
        }

        public static void Apply(FrameworkElement element)
        {
            if (element == null)
                return;

            element.SetCurrentValue(TextElement.FontFamilyProperty, ResolveCurrentFont());
        }
    }
}
