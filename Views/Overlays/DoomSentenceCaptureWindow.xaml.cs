using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 설계자의 잔재 "운명 선고" 때 밟을 발판 순서를 큰 화살표 아이콘으로 보여 준다 (2026-11-12 사용자: 스크린샷 말고,
    /// 둥근 색 버튼에 화살표가 있는 그림처럼 방향을 크게, 순서대로). 순서는 게임 화면의 공지를 찍어 글자 인식으로 읽은 것이고
    /// (<see cref="DoomSentenceCaptureService"/>), 제목줄에 회차 · 밟은 수 · 남은 시간을 같이 적는다.
    ///
    /// 방향과 색(게임의 가면 색): 기쁨 ← 핑크 · 슬픔 ↑ 보라 · 즐거움 ↓ 파랑 · 분노 → 빨강.
    /// 로그의 "발판을 밟았습니다 (i / n)"에 맞춰 밟은 것은 흐리게, 다음에 밟을 것은 금색 테로 강조한다.
    /// 순서를 끝내 못 읽으면 찍은 화면을 대신 보여 준다 (아무것도 없는 것보다는 낫다).
    ///
    /// 보기만 하는 창이라 잠금 상태에서는 클릭이 아래 게임으로 통과한다 (발판을 밟으러 그 자리를 눌러야 할 수 있다).
    /// 잠금 해제 모드에서는 끌어서 옮길 수 있다. 크기는 내용에 맞춰지고 설정의 표시 배율로 키우고 줄인다. 저장하는 것은 왼쪽 위 자리뿐이다.
    /// </summary>
    public partial class DoomSentenceCaptureWindow : ToastWindowBase
    {
        // 제한 시간이 지난 뒤에도 "발동" 줄이 늦게 올 수 있어 조금 더 둔다. 줄이 오면 서비스가 바로 닫는다
        private static readonly TimeSpan LingerAfterDeadline = TimeSpan.FromSeconds(3);

        // 표시 배율 100%일 때 아이콘 지름(DIP)과 한 줄에 놓는 수 (게임 공지도 넷씩 끊어 적는다)
        private const double BaseIconSize = 84;
        private const int IconsPerRow = 4;

        private readonly ChatSettings? _settings;
        private readonly DispatcherTimer _countdownTimer;
        private DateTime _deadlineUtc;
        private BitmapSource? _image;
        private int _scalePercent = 100;
        private IReadOnlyList<DoomPad> _order = Array.Empty<DoomPad>();
        private int _done;

        /// <summary>위치 조정 미리보기로 떠 있는지. 미리보기는 저절로 닫히지 않는다.</summary>
        public bool IsPreviewMode { get; private set; }

        /// <summary>
        /// 이 창이 지금 화면 캡처에서 빠져 있는지. 빠져 있으면 공지 자리 위에 놓여 있어도 이어 찍는 장에 이 창이 찍히지 않는다.
        /// 빠져 있는 동안에는 다른 캡처 도구(녹화·스크린샷)에도 안 나오므로, 서비스가 순서를 읽는 2초쯤만 빼고 다시 돌려놓는다
        /// (2026-11-12 사용자: 화살표가 왜 스샷에 안 찍히나).
        /// </summary>
        public bool IsExcludedFromCapture { get; private set; }

        /// <summary>이 창을 화면 캡처에서 빼거나(true) 다시 찍히게(false) 한다. 창 핸들이 아직 없으면 아무것도 하지 않는다.</summary>
        public void SetCaptureExcluded(bool excluded)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            bool ok = ScreenCaptureHelper.SetCaptureExclusion(hwnd, excluded);
            if (ok)
                IsExcludedFromCapture = excluded;
            else if (excluded)
                IsExcludedFromCapture = false; // 빼지 못했다: 서비스가 겹침을 보고 이어 찍을지 정한다
        }

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

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IsExcludedFromCapture = ScreenCaptureHelper.ExcludeFromCapture(new WindowInteropHelper(this).Handle);
            if (!IsExcludedFromCapture)
                AppLogger.Debug("Doom sentence window could not be excluded from capture; burst capture runs only when it does not cover the notice.");
        }

        /// <summary>이 창이 화면의 그 자리(픽셀)와 겹치는지. 겹치면 그 자리를 찍을 때 이 창이 같이 찍힌다.</summary>
        public bool OverlapsScreenRect(ScreenCaptureHelper.PixelRect rect)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero || !IsVisible || rect.IsEmpty)
                return false;
            if (!NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT own))
                return true; // 모르면 겹친다고 보고 이어 찍지 않는다

            return own.Left < rect.X + rect.Width && own.Right > rect.X &&
                   own.Top < rect.Y + rect.Height && own.Bottom > rect.Y;
        }

        /// <summary>회차 정보를 채우고 제한 시간이 끝날 때까지 띄운다. 순서는 읽히는 대로 <see cref="ShowOrder"/>로 들어온다.</summary>
        /// <param name="deadlineUtc">제한 시간이 끝나는 시각 (로그 줄을 본 시각 + 제한 시간).</param>
        /// <param name="scalePercent">표시 배율. 100이면 아이콘 지름 84.</param>
        /// <param name="status">순서가 아직 없는 동안 보여 줄 한 줄 ("순서 읽는 중…", "게임 창을 찾지 못했습니다").</param>
        public void ShowRound(int round, int total, DateTime deadlineUtc, int scalePercent, string status)
        {
            IsPreviewMode = false;
            _deadlineUtc = deadlineUtc;
            _scalePercent = Math.Clamp(scalePercent <= 0 ? 100 : scalePercent, 50, 300);
            _order = Array.Empty<DoomPad>();
            _done = 0;
            _image = null;

            TitleText.Text = $"운명 선고 {round}회차 · 발판 {total}개";
            ProgressText.Text = total > 0 ? $"0 / {total}" : string.Empty;
            OrderPanel.Visibility = Visibility.Collapsed;
            CaptureImage.Visibility = Visibility.Collapsed;
            SetStatus(status);
            UpdateRemain();

            Reveal();
            _countdownTimer.Start();

            TimeSpan lifetime = deadlineUtc - DateTime.UtcNow + LingerAfterDeadline;
            StartAutoClose(lifetime < LingerAfterDeadline ? LingerAfterDeadline : lifetime);
        }

        /// <summary>읽은 순서를 화살표로 보여 준다. 더 믿을 만한 결과가 나오면 다시 불러 바꾼다.</summary>
        /// <param name="confirmed">두 번 이어 같은 순서가 읽혔거나 마지막 결과인지. 아니면 "확인 중"을 같이 적는다.</param>
        public void ShowOrder(IReadOnlyList<DoomPad> order, bool confirmed)
        {
            if (IsPreviewMode || order == null || order.Count == 0)
                return;

            _order = order.ToList();
            CaptureImage.Visibility = Visibility.Collapsed;
            BuildOrderIcons();
            SetStatus(confirmed ? null : "확인 중…");
        }

        /// <summary>순서를 끝내 못 읽었을 때 찍은 화면을 대신 보여 준다.</summary>
        public void ShowFallbackImage(BitmapSource? image, string note)
        {
            if (IsPreviewMode)
                return;

            _order = Array.Empty<DoomPad>();
            OrderPanel.Visibility = Visibility.Collapsed;
            _image = image;
            CaptureImage.Source = image;
            CaptureImage.Visibility = image != null ? Visibility.Visible : Visibility.Collapsed;
            ApplyImageSize();
            SetStatus(note);
        }

        /// <summary>밟은 수를 고친다 (로그의 "발판을 밟았습니다. (done / total)").</summary>
        public void UpdateProgress(int done, int total)
        {
            if (IsPreviewMode || total <= 0)
                return;

            ProgressText.Text = $"{done} / {total}";
            _done = done;
            if (_order.Count > 0)
                BuildOrderIcons();
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

        /// <summary>다음 회차 화면을 찍는 동안 이 창이 같이 찍히지 않게 잠깐 숨긴다. 찍은 뒤 ShowRound가 다시 띄운다.</summary>
        public void HideForCapture()
        {
            if (IsPreviewMode)
                return;
            CancelAutoClose();
            _countdownTimer.Stop();
            Hide();
        }

        /// <summary>잠금 해제 모드에서 위치를 잡을 수 있게 예시 순서로 띄운다. 저절로 닫히지 않는다.</summary>
        public void ShowPreview()
        {
            IsPreviewMode = true;
            CancelAutoClose();
            _countdownTimer.Stop();

            ChatSettings? settings = ResolveSettings();
            _scalePercent = Math.Clamp(settings?.DoomSentenceCaptureScalePercent ?? 100, 50, 300);
            _order = new[] { DoomPad.Joy, DoomPad.Sorrow, DoomPad.Pleasure, DoomPad.Anger, DoomPad.Sorrow, DoomPad.Joy };
            _done = 1;
            _image = null;

            TitleText.Text = "운명 선고 2회차 · 발판 6개";
            ProgressText.Text = "1 / 6";
            RemainText.Text = "13초";
            CaptureImage.Visibility = Visibility.Collapsed;
            BuildOrderIcons();
            SetStatus("위치 조정 미리보기 (크기는 설정의 표시 배율)");
            Reveal();
        }

        private void SetStatus(string? text)
        {
            StatusText.Text = text ?? string.Empty;
            StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        #region 화살표 아이콘

        private void BuildOrderIcons()
        {
            double size = Math.Round(BaseIconSize * _scalePercent / 100.0);
            double gap = Math.Round(size * 0.1);

            OrderPanel.Children.Clear();
            OrderPanel.MaxWidth = IconsPerRow * (size + gap * 2);
            for (int i = 0; i < _order.Count; i++)
            {
                bool done = i < _done;
                bool current = i == _done;
                OrderPanel.Children.Add(DoomPadIcon.Create(_order[i], i + 1, size, gap, done, current));
            }
            OrderPanel.Visibility = Visibility.Visible;
        }

        #endregion

        /// <summary>
        /// (못 읽었을 때 대신 보여 주는) 찍은 화면을 화면 픽셀 기준으로 놓는다: 찍힌 픽셀 수 ÷ 이 창이 있는 모니터의 DPI 배율 × 표시 배율.
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

            // 크기는 내용이 정하므로 왼쪽 위 자리만 남긴다
            settings.DoomSentenceCaptureWindowLeft = Left;
            settings.DoomSentenceCaptureWindowTop = Top;
            return true;
        }

        protected override (double? Left, double? Top, double? Width, double? Height)? StoredBounds(ChatSettings settings)
            => (settings.DoomSentenceCaptureWindowLeft, settings.DoomSentenceCaptureWindowTop, null, null);
    }
}
