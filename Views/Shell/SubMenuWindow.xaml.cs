using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch { }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SubMenuWindow_Loaded(object sender, RoutedEventArgs e)
            => ApplyStoredPosition();

        /// <summary>설정에 저장된 위치로 옮긴다. 시작 시와 설정 전체 교체(프로필 불러오기) 뒤에 쓴다.</summary>
        public void ApplyStoredPosition()
        {
            try
            {
                var settings = GetSharedSettings();
                if (settings.SubMenuWindowLeft.HasValue && settings.SubMenuWindowTop.HasValue)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    // 저장된 위치가 화면 밖(모니터 구성 변경 등)이면 창 전체가 가상 화면 안에 들도록 보정한다 (여백 = 창 크기)
                    var area = ScreenBoundsHelper.GetVirtualDesktop();
                    Left = ScreenBoundsHelper.ClampAxis(settings.SubMenuWindowLeft.Value, area.Left, area.Width, Width, visibleMargin: Width);
                    Top = ScreenBoundsHelper.ClampAxis(settings.SubMenuWindowTop.Value, area.Top, area.Height, Height, visibleMargin: Height);
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

        private static Models.ChatSettings GetSharedSettings() => Services.AppServices.Get<Models.ChatSettings>();

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
                        Foreground = ThemeBrushes.Get("OverlaySubtleTextBrush"),
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
                    settingsView.ApplyHostLayout();
                }

                HostContent.Child = view;
            });
        }
    }
}
