using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>보급품 탈환 지도 창. 잠금 해제 모드에서 창 어디를 잡아도 이동하고, 가장자리로 크기를 조절한다 (잠금 중에도 가능).</summary>
    public partial class RecaptureSupplyWindow : OverlayWindowBase
    {
        public RecaptureSupplyWindow(string imagePath)
        {
            InitializeComponent();
            EnableEdgeResize(thickness: 6); // 네이티브 테두리(CanResize)에 6px 히트 영역을 덧씌운다
            // 잠금 해제 모드에서는 창 어디를 잡아도 선택+드래그 가능
            PreviewMouseLeftButtonDown += (_, e) => TryBeginDrag(e, markHandled: true);
            LoadImage(imagePath);
        }

        protected override bool PersistBounds(ChatSettings settings)
        {
            if (!IsVisible)
                return false;

            settings.RecaptureSupplyWindowLeft = Left;
            settings.RecaptureSupplyWindowTop = Top;
            if (ActualWidth > 0)
                settings.RecaptureSupplyWindowWidth = ActualWidth;
            if (ActualHeight > 0)
                settings.RecaptureSupplyWindowHeight = ActualHeight;
            return true;
        }

        protected override (double? Left, double? Top, double? Width, double? Height)? StoredBounds(ChatSettings settings)
            => (settings.RecaptureSupplyWindowLeft, settings.RecaptureSupplyWindowTop,
                settings.RecaptureSupplyWindowWidth, settings.RecaptureSupplyWindowHeight);

        /// <summary>저장된 자리가 없으면 화면 가운데에서 시작한다.</summary>
        public override void ApplyStoredBounds(ChatSettings? settings = null)
        {
            settings ??= ResolveSettings();
            if (settings == null)
                return;
            base.ApplyStoredBounds(settings);
            if (!(settings.RecaptureSupplyWindowLeft.HasValue && settings.RecaptureSupplyWindowTop.HasValue))
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        private void LoadImage(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                    throw new FileNotFoundException("Recapture supply image cache was missing.", imagePath);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.EndInit();
                bitmap.Freeze();
                SupplyImage.Source = bitmap;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load recapture supply image.", ex);
                Close();
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => TryBeginDrag(e);

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
