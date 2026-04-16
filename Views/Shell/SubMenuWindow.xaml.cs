using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class SubMenuWindow : Window
    {
        public SubMenuWindow()
        {
            InitializeComponent();
            WindowFontService.Apply(this);
            Loaded += SubMenuWindow_Loaded;
            UseLayoutRounding = true;
            SnapsToDevicePixels = true;
        }

        private void SubMenuWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = GetSharedSettings();
                if (settings.SubMenuWindowLeft.HasValue && settings.SubMenuWindowTop.HasValue)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = settings.SubMenuWindowLeft.Value;
                    Top = settings.SubMenuWindowTop.Value;
                }
            }
            catch { }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            try
            {
                var settings = GetSharedSettings();
                settings.SubMenuWindowLeft = Left;
                settings.SubMenuWindowTop = Top;
                Services.ConfigService.SaveDeferred(settings);
            }
            catch { }
        }

        private static Models.ChatSettings GetSharedSettings()
        {
            try
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is MainWindow main && main.DataContext is Models.ChatSettings shared)
                    {
                        return shared;
                    }
                }
            }
            catch
            {
            }

            return Services.ConfigService.Load();
        }

        public void ShowHostContent(FrameworkElement? view, string? title = null)
        {
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(title))
                {
                    Title = title;
                    HeaderTitle.Text = title;
                }

                if (view == null)
                {
                    HostContent.Child = new TextBlock
                    {
                        Text = "표시할 화면이 없습니다.",
                        Foreground = new SolidColorBrush(Color.FromRgb(154, 164, 178)),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    return;
                }

                view.HorizontalAlignment = HorizontalAlignment.Stretch;
                view.VerticalAlignment = VerticalAlignment.Stretch;
                view.Margin = new Thickness(0);
                WindowFontService.Apply(view);

                if (view is Control control)
                {
                    control.SetCurrentValue(Control.BackgroundProperty, Brushes.Transparent);
                }

                if (view is SettingsView settingsView)
                {
                    settingsView.SetCompactMode(false);
                }

                HostContent.Child = view;
            });
        }

        public void ClearHostContent()
        {
            Dispatcher.Invoke(() => HostContent.Child = null);
        }
    }
}
