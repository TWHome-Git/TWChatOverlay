using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 놀라움의 격리실 가면 기믹에서 부숴야 할 가면을 큰 그림으로 보여 준다 (보스가 쓴 가면의 상성 가면).
    /// 즐거움(폭발 예고)일 때는 터지기까지 남은 시간을 같이 센다. "파훼 성공" 줄이 오거나 유지 시간이 지나면 닫힌다.
    ///
    /// 보기만 하는 창이라 잠금 상태에서는 클릭이 아래 게임으로 통과한다. 잠금 해제 모드에서 끌어 옮길 수 있고,
    /// 크기는 설정의 표시 배율로 정한다. 띄우고 닫는 것은 <see cref="MaskGimmickService"/>가 한다.
    /// </summary>
    public partial class MaskGimmickWindow : ToastWindowBase
    {
        // 게임에서 가져온 가면 그림 (TextureFile 16628 의 0 · 6 · 12 · 18번 프레임, 31x37 안팎)
        private static readonly Dictionary<DoomPad, string> MaskImageFiles = new()
        {
            [DoomPad.Joy] = "mask_joy.png",           // 핑크
            [DoomPad.Sorrow] = "mask_sorrow.png",     // 보라
            [DoomPad.Pleasure] = "mask_pleasure.png", // 파랑
            [DoomPad.Anger] = "mask_anger.png",       // 빨강
        };

        private static readonly Dictionary<DoomPad, BitmapImage> MaskImageCache = new();

        // 표시 배율 100%일 때 가면 그림 높이(DIP). 원본이 37px 안팎이라 3배쯤이다
        private const double BaseMaskHeight = 111;

        private readonly ChatSettings? _settings;
        private readonly DispatcherTimer _countdownTimer;
        private DateTime? _explosionDeadlineUtc;

        /// <summary>위치 조정 미리보기로 떠 있는지. 미리보기는 저절로 닫히지 않는다.</summary>
        public bool IsPreviewMode { get; private set; }

        public MaskGimmickWindow(ChatSettings? settings)
        {
            _settings = settings;
            InitializeComponent();

            // 잠금 해제 모드에서는 창 어디를 잡아도 선택+드래그 가능
            PreviewMouseLeftButtonDown += (_, e) => TryBeginDrag(e, markHandled: true);

            _countdownTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(200) };
            _countdownTimer.Tick += (_, _) => UpdateRemain();
        }

        protected override ChatSettings? ResolveSettings() => _settings;
        // 자리를 사용자가 정하는 창이라 알림이지만 LocationChanged·닫힘에 저장한다
        protected override bool PersistBoundsOnChange => true;
        protected override bool PersistBoundsOnClose => true;
        protected override bool PassthroughWhenLocked => true;

        private static BitmapImage? LoadMaskImage(DoomPad pad)
        {
            if (MaskImageCache.TryGetValue(pad, out BitmapImage? cached))
                return cached;
            if (!MaskImageFiles.TryGetValue(pad, out string? file))
                return null;

            try
            {
                var image = new BitmapImage(new Uri($"pack://application:,,,/Data/images/Masks/{file}", UriKind.Absolute));
                image.Freeze();
                MaskImageCache[pad] = image;
                return image;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load mask image '{file}'.", ex);
                return null;
            }
        }

        /// <summary>부술 가면을 띄우고 N초 뒤 닫히게 한다. 이미 떠 있으면 내용만 바꾸고 시간을 다시 센다.</summary>
        /// <param name="wearing">보스가 쓴 가면 (모르면 null).</param>
        /// <param name="explosionDeadlineUtc">즐거움 폭발 시각 (폭발 예고가 없으면 null).</param>
        public void ShowTarget(DoomPad target, DoomPad? wearing, DateTime? explosionDeadlineUtc, int durationSeconds, int scalePercent)
        {
            IsPreviewMode = false;
            Apply(target, wearing, explosionDeadlineUtc, scalePercent);
            Reveal();
            StartAutoClose(durationSeconds);
        }

        /// <summary>잠금 해제 모드에서 위치를 잡을 수 있게 예시로 띄운다. 저절로 닫히지 않는다.</summary>
        public void ShowPreview()
        {
            IsPreviewMode = true;
            CancelAutoClose();
            Apply(DoomPad.Joy, DoomPad.Sorrow, null, ResolveSettings()?.MaskGimmickScalePercent ?? 100);
            Reveal();
        }

        private void Apply(DoomPad target, DoomPad? wearing, DateTime? explosionDeadlineUtc, int scalePercent)
        {
            double scale = Math.Clamp(scalePercent <= 0 ? 100 : scalePercent, 50, 300) / 100.0;

            WearingText.Text = wearing.HasValue ? $"보스: {DoomSentenceOrderParser.NameOf(wearing.Value)} 가면" : "격리실 가면";
            MaskImage.Source = LoadMaskImage(target);
            MaskImage.Height = Math.Round(BaseMaskHeight * scale);

            TargetText.Text = $"{DoomSentenceOrderParser.NameOf(target)} 가면 파괴";
            TargetText.FontSize = Math.Round(20 * scale);
            TargetText.Foreground = new SolidColorBrush(DoomPadIcon.ColorOf(target));

            _explosionDeadlineUtc = explosionDeadlineUtc;
            if (explosionDeadlineUtc.HasValue)
            {
                RemainText.Visibility = Visibility.Visible;
                UpdateRemain();
                _countdownTimer.Start();
            }
            else
            {
                _countdownTimer.Stop();
                RemainText.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateRemain()
        {
            if (!_explosionDeadlineUtc.HasValue)
                return;

            double remain = (_explosionDeadlineUtc.Value - DateTime.UtcNow).TotalSeconds;
            if (remain <= 0)
            {
                RemainText.Text = "폭발!";
                _countdownTimer.Stop();
                return;
            }
            RemainText.Text = $"폭발까지 {(int)Math.Ceiling(remain)}초";
        }

        private void Reveal()
        {
            ResetPresentation();
            if (!IsVisible)
                Show();
            BringToFront();
        }

        protected override void OnAutoCloseElapsed()
        {
            if (IsPreviewMode)
                return;
            CloseWithFade();
        }

        protected override void OnClosed(EventArgs e)
        {
            _countdownTimer.Stop();
            base.OnClosed(e);
        }

        private void Content_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => TryBeginDrag(e);

        protected override bool PersistBounds(ChatSettings settings)
        {
            if (!IsVisible)
                return false;

            // 크기는 내용이 정하므로 왼쪽 위 자리만 남긴다
            settings.MaskGimmickWindowLeft = Left;
            settings.MaskGimmickWindowTop = Top;
            return true;
        }

        protected override (double? Left, double? Top, double? Width, double? Height)? StoredBounds(ChatSettings settings)
            => (settings.MaskGimmickWindowLeft, settings.MaskGimmickWindowTop, null, null);
    }
}
