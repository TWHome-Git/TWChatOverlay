using System;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class ShoutToastWindow : Window
    {
        private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
        private readonly DispatcherTimer _lifetimeTimer;
        private ChatSettings _settings;
        private bool _isClosing;
        private bool _isPreviewMode;

        public ShoutToastWindow(string message, FontFamily fontFamily, ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            InitializeComponent();
            FontFamily = fontFamily;
            ToastText.FontFamily = fontFamily;
            ToastText.Text = NormalizeMessage(message);
            LocationChanged += (_, _) => SyncPositionToSettings();

            _lifetimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _lifetimeTimer.Tick += (_, _) =>
            {
                _lifetimeTimer.Stop();
                StartCloseAnimation();
            };
        }

        public void SetSettings(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public void SetPreviewMode(bool isPreview)
        {
            _isPreviewMode = isPreview;
            RefreshMousePassthroughStyle(forceInteractive: isPreview);
        }

        public void SetMessage(string message)
        {
            ToastText.Text = NormalizeMessage(message);
        }

        public void ShowAnimated(double targetLeft, double targetTop, int durationSeconds = 5)
        {
            _isPreviewMode = false;
            RefreshMousePassthroughStyle(forceInteractive: false);

            Left = targetLeft;
            Top = targetTop - 18;
            Opacity = 0;
            Show();
            TopmostWindowHelper.BringToTopmost(this);

            BeginAnimation(TopProperty, new DoubleAnimation
            {
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });

            BeginAnimation(OpacityProperty, new DoubleAnimation
            {
                To = 1,
                Duration = TimeSpan.FromMilliseconds(180)
            });

            _lifetimeTimer.Stop();
            _lifetimeTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, Math.Min(5, durationSeconds)));
            _lifetimeTimer.Start();
        }

        public void ShowPreview(double targetLeft, double targetTop)
        {
            _isPreviewMode = true;
            RefreshMousePassthroughStyle(forceInteractive: true);

            Left = targetLeft;
            Top = targetTop;
            Opacity = 1;
            if (!IsVisible)
                Show();

            Visibility = Visibility.Visible;
            TopmostWindowHelper.BringToTopmost(this);
        }

        public void MoveTo(double targetTop)
        {
            BeginAnimation(TopProperty, new DoubleAnimation
            {
                To = targetTop,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            RefreshMousePassthroughStyle(forceInteractive: _isPreviewMode);
        }

        protected override void OnClosed(EventArgs e)
        {
            _lifetimeTimer.Stop();
            SyncPositionToSettings();
            base.OnClosed(e);
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isPreviewMode || e.ButtonState != MouseButtonState.Pressed)
                return;

            try
            {
                DragMove();
            }
            catch { }
            finally
            {
                SyncPositionToSettings();
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
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }

        private void SyncPositionToSettings()
        {
            if (!_isPreviewMode)
                return;

            try
            {
                _settings.ShoutToastWindowLeft = Left;
                _settings.ShoutToastWindowTop = Top;
                ConfigService.SaveDeferred(_settings);
                ShoutToastService.NotifyPreviewPositionChanged();
            }
            catch { }
        }

        private void RefreshMousePassthroughStyle(bool forceInteractive = false)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                int nextStyle = forceInteractive
                    ? (exStyle & ~NativeMethods.WS_EX_TRANSPARENT) | NativeMethods.WS_EX_TOOLWINDOW
                    : exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW;
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, nextStyle);
            }
            catch { }
        }

        private static string NormalizeMessage(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string decoded = WebUtility.HtmlDecode(text).Replace("&nbsp;", " ");
            decoded = HtmlTagRegex.Replace(decoded, " ");
            decoded = Regex.Replace(decoded, @"\s+", " ").Trim();
            return decoded;
        }
    }
}
