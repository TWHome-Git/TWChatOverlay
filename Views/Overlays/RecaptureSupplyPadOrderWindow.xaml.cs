using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 보급품 탈환에서 경보 장치 해제 문구가 뜨면 밟아야 할 발판 색 세 개를 순서대로 보여준다.
    /// 지정한 시간이 지나면 저절로 닫히고, X를 눌러도 닫힌다.
    /// 잠금 해제 모드에서는 끌어서 옮기고 가장자리를 잡아 크기를 바꿀 수 있다. 발판은 창 크기에 맞춰 함께 커진다.
    /// </summary>
    public partial class RecaptureSupplyPadOrderWindow : ToastWindowBase
    {
        // 게임에서 잘라 온 발판 그림. 키는 서비스가 넘기는 한 글자 색 이름이다.
        private static readonly Dictionary<string, string> PadImageFiles = new()
        {
            ["파"] = "pad_blue.png",
            ["노"] = "pad_yellow.png",
            ["빨"] = "pad_red.png",
            ["검"] = "pad_black.png",
            ["흰"] = "pad_white.png",
        };

        // 한 번 읽은 그림은 다시 쓴다. 창이 판마다 여러 번 뜨기 때문이다.
        private static readonly Dictionary<string, BitmapImage> PadImageCache = new();

        private static BitmapImage? LoadPadImage(string key)
        {
            if (!PadImageFiles.TryGetValue(key, out string? file))
                return null;
            if (PadImageCache.TryGetValue(file, out BitmapImage? cached))
                return cached;

            try
            {
                var image = new BitmapImage(new Uri($"pack://application:,,,/Data/images/Pads/{file}", UriKind.Absolute));
                image.Freeze();
                PadImageCache[file] = image;
                return image;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load recapture supply pad image '{file}'.", ex);
                return null;
            }
        }

        private readonly ChatSettings? _settings;
        private bool _isPreviewMode;

        public RecaptureSupplyPadOrderWindow(ChatSettings? settings)
        {
            _settings = settings;
            InitializeComponent();

            // 가장자리 6px를 잡으면 크기 조절. 잠금 상태에서는 그냥 창 안쪽으로 취급해 실수로 늘어나지 않게 한다.
            EnableEdgeResize(thickness: 6, when: () => AppServices.Get<UiLockService>().IsUnlocked);

            // 잠금 해제 모드에서는 창 어디를 잡아도 선택+드래그 가능
            PreviewMouseLeftButtonDown += (_, e) => TryBeginDrag(e, markHandled: true);
        }

        protected override ChatSettings? ResolveSettings() => _settings;
        // 자리를 사용자가 정하는 창이라 알림이지만 LocationChanged·닫힘에 저장한다
        protected override bool PersistBoundsOnChange => true;
        protected override bool PersistBoundsOnClose => true;

        /// <summary>발판 색을 순서대로 채우고 N초 뒤 닫히게 한다. 이미 떠 있으면 내용만 바꾸고 시간을 다시 센다.</summary>
        public void ShowOrder(IReadOnlyList<string> colors, string phrase, int durationSeconds)
        {
            _isPreviewMode = false;
            ApplyColors(colors);
            PhraseText.Text = phrase;
            Reveal();
            StartAutoClose(durationSeconds);
        }

        /// <summary>잠금 해제 모드에서 위치·크기를 잡을 수 있게 예시 순서로 띄운다. 저절로 닫히지 않는다.</summary>
        public void ShowPreview()
        {
            _isPreviewMode = true;
            CancelAutoClose();
            ApplyColors(new[] { "파", "노", "빨" });
            PhraseText.Text = "위치·크기 조정 미리보기";
            Reveal();
        }

        private void Reveal()
        {
            ResetPresentation();
            if (!IsVisible)
                Show();
            BringToFront();
        }

        private void ApplyColors(IReadOnlyList<string> colors)
        {
            Image[] pads = { Pad1, Pad2, Pad3 };
            for (int i = 0; i < pads.Length; i++)
            {
                string key = i < colors.Count ? colors[i] : string.Empty;
                pads[i].Source = LoadPadImage(key);
            }
        }

        protected override void OnAutoCloseElapsed()
        {
            if (_isPreviewMode)
                return;
            CloseWithFade();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => TryBeginDrag(e);

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            CancelAutoClose();
            Close();
        }

        protected override bool PersistBounds(ChatSettings settings)
        {
            if (!IsVisible)
                return false;

            settings.RecaptureSupplyPadOrderWindowLeft = Left;
            settings.RecaptureSupplyPadOrderWindowTop = Top;
            if (ActualWidth > 0)
                settings.RecaptureSupplyPadOrderWindowWidth = ActualWidth;
            if (ActualHeight > 0)
                settings.RecaptureSupplyPadOrderWindowHeight = ActualHeight;
            return true;
        }

        protected override (double? Left, double? Top, double? Width, double? Height)? StoredBounds(ChatSettings settings)
            => (settings.RecaptureSupplyPadOrderWindowLeft, settings.RecaptureSupplyPadOrderWindowTop,
                settings.RecaptureSupplyPadOrderWindowWidth, settings.RecaptureSupplyPadOrderWindowHeight);
    }
}
