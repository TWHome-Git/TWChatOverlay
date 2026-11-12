using System;
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
    /// 설계자의 잔재 "운명 선고" 때 게임 화면에 뜬 발판 순서 공지를 찍은 그림을 회차가 끝날 때까지 보여 준다.
    /// 제목줄에 회차 · 밟은 수 · 남은 시간을 같이 적는다.
    ///
    /// 보기만 하는 창이라 잠금 상태에서는 클릭이 아래 게임으로 통과한다 (발판을 밟으러 그 자리를 눌러야 할 수 있다).
    /// 잠금 해제 모드에서는 끌어서 옮길 수 있다. 크기는 사용자가 정하지 않는다 — 그림을 찍힌 픽셀 그대로(표시 배율 100%)
    /// 보여 주고 창이 그 크기에 맞춰진다 (2026-11-12 사용자: 창에 맞춰 줄이니 글이 너무 작다, 원본 크기로).
    /// 더 크게 · 작게 보려면 설정의 표시 배율을 바꾼다. 저장하는 것은 왼쪽 위 자리뿐이다.
    /// 띄우고 닫는 것은 <see cref="DoomSentenceCaptureService"/>가 한다.
    /// </summary>
    public partial class DoomSentenceCaptureWindow : ToastWindowBase
    {
        // 제한 시간이 지난 뒤에도 "발동" 줄이 늦게 올 수 있어 조금 더 둔다. 줄이 오면 서비스가 바로 닫는다
        private static readonly TimeSpan LingerAfterDeadline = TimeSpan.FromSeconds(3);

        private readonly ChatSettings? _settings;
        private readonly DispatcherTimer _countdownTimer;
        private DateTime _deadlineUtc;
        private BitmapSource? _image;
        private int _scalePercent = 100;

        /// <summary>위치·크기 조정 미리보기로 떠 있는지. 미리보기는 저절로 닫히지 않는다.</summary>
        public bool IsPreviewMode { get; private set; }

        public DoomSentenceCaptureWindow(ChatSettings? settings)
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

        /// <summary>찍은 그림과 회차 정보를 채우고 제한 시간이 끝날 때까지 띄운다. 이미 떠 있으면 내용만 바꾼다.</summary>
        /// <param name="image">잘라 낸 화면. 못 찍었으면 null (그때는 <paramref name="note"/>를 보여 준다).</param>
        /// <param name="deadlineUtc">제한 시간이 끝나는 시각 (로그 줄을 본 시각 + 제한 시간).</param>
        /// <param name="scalePercent">표시 배율. 100이면 찍힌 픽셀 그대로의 크기.</param>
        public void ShowCapture(BitmapSource? image, int round, int total, DateTime deadlineUtc, string? note, int scalePercent)
        {
            IsPreviewMode = false;
            _deadlineUtc = deadlineUtc;

            TitleText.Text = $"운명 선고 {round}회차 · 발판 {total}개";
            ProgressText.Text = total > 0 ? $"0 / {total}" : string.Empty;
            ApplyImage(image, note ?? "찍은 화면이 없습니다", scalePercent);
            UpdateRemain();

            Reveal();
            _countdownTimer.Start();

            TimeSpan lifetime = deadlineUtc - DateTime.UtcNow + LingerAfterDeadline;
            StartAutoClose(lifetime < LingerAfterDeadline ? LingerAfterDeadline : lifetime);
        }

        /// <summary>밟은 수를 고친다 (로그의 "발판을 밟았습니다. (done / total)").</summary>
        public void UpdateProgress(int done, int total)
        {
            if (IsPreviewMode || total <= 0)
                return;
            ProgressText.Text = $"{done} / {total}";
        }

        /// <summary>잠깐 더 보여 준 뒤 닫는다 (발판을 다 밟았을 때).</summary>
        public void CloseAfter(TimeSpan delay)
        {
            if (IsPreviewMode)
                return;
            _countdownTimer.Stop();
            RemainText.Text = "완료";
            StartAutoClose(delay);
        }

        /// <summary>다음 회차 화면을 찍는 동안 이 창이 같이 찍히지 않게 잠깐 숨긴다. 찍은 뒤 ShowCapture가 다시 띄운다.</summary>
        public void HideForCapture()
        {
            if (IsPreviewMode)
                return;
            CancelAutoClose();
            _countdownTimer.Stop();
            Hide();
        }

        /// <summary>잠금 해제 모드에서 위치·크기를 잡을 수 있게 예시로 띄운다. 저절로 닫히지 않는다.</summary>
        public void ShowPreview()
        {
            IsPreviewMode = true;
            CancelAutoClose();
            _countdownTimer.Stop();

            TitleText.Text = "운명 선고 2회차 · 발판 6개";
            ProgressText.Text = "1 / 6";
            RemainText.Text = "13초";
            ApplyImage(null, "위치 조정 미리보기\n운명 선고가 시작되면 게임 화면의 발판 순서 공지를 찍어 찍힌 크기 그대로 여기에 보여 줍니다.\n창 크기는 그림에 맞춰집니다 (설정의 표시 배율).", 100);
            Reveal();
        }

        private void ApplyImage(BitmapSource? image, string emptyNote, int scalePercent)
        {
            _image = image;
            _scalePercent = Math.Clamp(scalePercent <= 0 ? 100 : scalePercent, 50, 300);

            CaptureImage.Source = image;
            CaptureImage.Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Text = emptyNote;
            EmptyPanel.Visibility = image != null ? Visibility.Collapsed : Visibility.Visible;
            ApplyImageSize();
        }

        /// <summary>
        /// 그림을 화면 픽셀 기준으로 놓는다: 찍힌 픽셀 수 ÷ 이 창이 있는 모니터의 DPI 배율 × 표시 배율.
        /// 배율 100%에서는 화면 한 픽셀이 그림 한 픽셀이라 게임에서 보던 그 크기 그대로 나온다.
        /// </summary>
        private void ApplyImageSize()
        {
            BitmapSource? image = _image;
            if (image == null)
                return;

            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            double scale = _scalePercent / 100.0;
            CaptureImage.Width = image.PixelWidth / dpi.DpiScaleX * scale;
            CaptureImage.Height = image.PixelHeight / dpi.DpiScaleY * scale;
            // 1:1일 때는 보간 없이 픽셀 그대로, 늘리거나 줄일 때만 부드럽게
            RenderOptions.SetBitmapScalingMode(CaptureImage, _scalePercent == 100 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            ApplyImageSize(); // DPI가 다른 모니터로 옮겨도 화면 픽셀 기준 크기를 지킨다
        }

        private void UpdateRemain()
        {
            double remain = (_deadlineUtc - DateTime.UtcNow).TotalSeconds;
            if (remain <= 0)
            {
                RemainText.Text = "0초";
                _countdownTimer.Stop();
                return;
            }
            RemainText.Text = $"{(int)Math.Ceiling(remain)}초";
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

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => TryBeginDrag(e);

        protected override bool PersistBounds(ChatSettings settings)
        {
            if (!IsVisible)
                return false;

            // 크기는 그림이 정하므로 왼쪽 위 자리만 남긴다
            settings.DoomSentenceCaptureWindowLeft = Left;
            settings.DoomSentenceCaptureWindowTop = Top;
            return true;
        }

        protected override (double? Left, double? Top, double? Width, double? Height)? StoredBounds(ChatSettings settings)
            => (settings.DoomSentenceCaptureWindowLeft, settings.DoomSentenceCaptureWindowTop, null, null);
    }
}
