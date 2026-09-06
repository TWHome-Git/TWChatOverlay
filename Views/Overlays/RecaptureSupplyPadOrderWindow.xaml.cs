using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 보급품 탈환에서 경보 장치 해제 문구가 뜨면 밟아야 할 발판 색 세 개를 순서대로 보여준다.
    /// 지정한 시간이 지나면 저절로 닫히고, X를 눌러도 닫힌다.
    /// 잠금 해제 모드에서는 끌어서 옮기고 가장자리를 잡아 크기를 바꿀 수 있다. 발판은 창 크기에 맞춰 함께 커진다.
    /// </summary>
    public partial class RecaptureSupplyPadOrderWindow : Window
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

        private const int ResizeBorderThickness = 6;
        private const int WM_NCHITTEST = 0x0084;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;

        private readonly DispatcherTimer _lifetimeTimer;
        private readonly ChatSettings? _settings;
        private bool _isPreviewMode;
        private bool _isClosing;

        public RecaptureSupplyPadOrderWindow(ChatSettings? settings)
        {
            _settings = settings;
            InitializeComponent();
            SettingsHostZOrder.Register(this); // 설정 창이 열려 있으면 그 아래로 표시
            WindowFontService.Apply(this);

            _lifetimeTimer = new DispatcherTimer();
            _lifetimeTimer.Tick += (_, _) =>
            {
                _lifetimeTimer.Stop();
                StartCloseAnimation();
            };

            SourceInitialized += (_, _) =>
            {
                try
                {
                    HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc);
                }
                catch { }
            };

            // 잠금 해제 모드에서는 창 어디를 잡아도 선택+드래그 가능
            PreviewMouseLeftButtonDown += UnlockDrag_PreviewMouseLeftButtonDown;
            LocationChanged += (_, _) => PersistBoundsDeferred();
            SizeChanged += (_, _) => PersistBoundsDeferred();
        }

        /// <summary>발판 색을 순서대로 채우고 N초 뒤 닫히게 한다. 이미 떠 있으면 내용만 바꾸고 시간을 다시 센다.</summary>
        public void ShowOrder(IReadOnlyList<string> colors, string phrase, int durationSeconds)
        {
            _isPreviewMode = false;
            ApplyColors(colors);
            PhraseText.Text = phrase;

            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            if (!IsVisible)
                Show();
            TopmostWindowHelper.BringToTopmost(this);

            _lifetimeTimer.Stop();
            _lifetimeTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, Math.Min(300, durationSeconds)));
            _lifetimeTimer.Start();
        }

        /// <summary>잠금 해제 모드에서 위치·크기를 잡을 수 있게 예시 순서로 띄운다. 저절로 닫히지 않는다.</summary>
        public void ShowPreview()
        {
            _isPreviewMode = true;
            _lifetimeTimer.Stop();
            ApplyColors(new[] { "파", "노", "빨" });
            PhraseText.Text = "위치·크기 조정 미리보기";
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            if (!IsVisible)
                Show();
            TopmostWindowHelper.BringToTopmost(this);
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

        private void StartCloseAnimation()
        {
            if (_isPreviewMode || _isClosing)
                return;

            _isClosing = true;
            var fade = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            };
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }

        private void UnlockDrag_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!UiLockService.IsUnlocked) return;
            UiLockService.Select(this);
            if (e.ButtonState != MouseButtonState.Pressed)
                return;

            try
            {
                DragMove();
            }
            catch
            {
            }

            PersistBoundsDeferred();
            e.Handled = true;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!UiLockService.IsUnlocked) return;
            UiLockService.Select(this);
            try
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            }
            catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _lifetimeTimer.Stop();
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _lifetimeTimer.Stop();
            PersistBounds(saveImmediately: true);
            base.OnClosed(e);
        }

        /// <summary>가장자리 6px를 잡으면 크기 조절. 잠금 상태에서는 그냥 창 안쪽으로 취급해 실수로 늘어나지 않게 한다.</summary>
        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            try
            {
                if (msg != WM_NCHITTEST || !UiLockService.IsUnlocked)
                    return IntPtr.Zero;

                Point screenPoint = new(
                    unchecked((short)(lParam.ToInt64() & 0xFFFF)),
                    unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF)));
                Point localPoint = PointFromScreen(screenPoint);

                bool left = localPoint.X <= ResizeBorderThickness;
                bool right = localPoint.X >= ActualWidth - ResizeBorderThickness;
                bool top = localPoint.Y <= ResizeBorderThickness;
                bool bottom = localPoint.Y >= ActualHeight - ResizeBorderThickness;

                if (left && top) { handled = true; return (IntPtr)HTTOPLEFT; }
                if (right && top) { handled = true; return (IntPtr)HTTOPRIGHT; }
                if (left && bottom) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
                if (right && bottom) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
                if (left) { handled = true; return (IntPtr)HTLEFT; }
                if (right) { handled = true; return (IntPtr)HTRIGHT; }
                if (top) { handled = true; return (IntPtr)HTTOP; }
                if (bottom) { handled = true; return (IntPtr)HTBOTTOM; }
            }
            catch { }

            return IntPtr.Zero;
        }

        private void PersistBoundsDeferred()
        {
            if (!IsLoaded || !IsVisible)
                return;
            PersistBounds(saveImmediately: false);
        }

        private void PersistBounds(bool saveImmediately)
        {
            if (_settings == null)
                return;

            try
            {
                _settings.RecaptureSupplyPadOrderWindowLeft = Left;
                _settings.RecaptureSupplyPadOrderWindowTop = Top;
                if (ActualWidth > 0)
                    _settings.RecaptureSupplyPadOrderWindowWidth = ActualWidth;
                if (ActualHeight > 0)
                    _settings.RecaptureSupplyPadOrderWindowHeight = ActualHeight;
                if (saveImmediately)
                    ConfigService.Save(_settings);
                else
                    ConfigService.SaveDeferred(_settings);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save recapture supply pad order window bounds.", ex);
            }
        }
    }
}
