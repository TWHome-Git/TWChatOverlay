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
        private static readonly Regex ShoutPrefixRegex = new(@"^\[\d{2}:\d{2}:\d{2}\]\s*[^:]+?\s*:\s*", RegexOptions.Compiled);
        private const double ScreenEdgePadding = 16;
        private readonly DispatcherTimer _lifetimeTimer;
        private readonly DispatcherTimer _foregroundTimer;
        private ChatSettings _settings;
        private bool _isClosing;
        private bool _isPreviewMode;
        private bool? _lastAppliedTopmostState;

        public ShoutToastWindow(string message, FontFamily fontFamily, ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            InitializeComponent();
            FontFamily = fontFamily;
            ToastText.FontFamily = fontFamily;
            ApplyToastSettings();
            ToastText.Text = NormalizeMessage(message);
            ApplyLayoutConstraints();

            _lifetimeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _lifetimeTimer.Tick += (_, _) =>
            {
                _lifetimeTimer.Stop();
                StartCloseAnimation();
            };

            _foregroundTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120)
            };
            _foregroundTimer.Tick += (_, _) => ApplyForegroundTopmostState();
        }

        public void SetSettings(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            ApplyToastSettings();
        }

        public void SetPreviewMode(bool isPreview)
        {
            _isPreviewMode = isPreview;
            RefreshMousePassthroughStyle(forceInteractive: isPreview);
            if (isPreview)
            {
                _foregroundTimer.Stop();
                ApplyTopmostState(true);
            }
            else if (IsVisible)
            {
                ApplyForegroundTopmostState();
                _foregroundTimer.Start();
            }
        }

        public void SetMessage(string message)
        {
            ToastText.Text = NormalizeMessage(message);
            ApplyLayoutConstraints();
        }

        public void ShowAnimated(double targetLeft, double targetTop, int durationSeconds = 5)
        {
            _isPreviewMode = false;
            RefreshMousePassthroughStyle(forceInteractive: false);

            Left = targetLeft;
            Top = targetTop - 18;
            Opacity = 0;
            Show();
            UpdateLayout();
            Left = ClampLeftToWorkArea(targetLeft);
            ApplyForegroundTopmostState();

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
            _lifetimeTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, Math.Min(300, durationSeconds)));
            _lifetimeTimer.Start();
            _foregroundTimer.Start();
        }

        public void ShowPreview(double targetLeft, double targetTop)
        {
            _isPreviewMode = true;
            RefreshMousePassthroughStyle(forceInteractive: true);
            BeginAnimation(TopProperty, null);
            BeginAnimation(LeftProperty, null);

            Left = targetLeft;
            Top = targetTop;
            Opacity = 1;
            if (!IsVisible)
                Show();

            Visibility = Visibility.Visible;
            UpdateLayout();
            Left = ClampLeftToWorkArea(targetLeft);
            ApplyTopmostState(true);
        }

        public void MoveTo(double targetTop)
        {
            if (_isPreviewMode)
            {
                BeginAnimation(TopProperty, null);
                Top = targetTop;
                return;
            }

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
            _foregroundTimer.Stop();
            SyncPositionToSettings(saveImmediately: true);
            base.OnClosed(e);
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isPreviewMode || e.ButtonState != MouseButtonState.Pressed)
                return;

            try
            {
                BeginAnimation(TopProperty, null);
                BeginAnimation(LeftProperty, null);
                DragMove();
            }
            catch { }
            finally
            {
                SyncPositionToSettings(saveImmediately: true);
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

        public void SaveCurrentPosition()
        {
            SyncPositionToSettings(saveImmediately: true);
        }

        private void SyncPositionToSettings(bool saveImmediately)
        {
            if (!_isPreviewMode)
                return;

            try
            {
                _settings.ShoutToastWindowLeft = Left;
                _settings.ShoutToastWindowTop = Top;
                if (saveImmediately)
                    ConfigService.Save(_settings);
                else
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

        private void ApplyToastSettings()
        {
            ToastText.FontSize = _settings.ShoutToastFontSize;
            ApplyLayoutConstraints();
        }

        private void ApplyForegroundTopmostState()
        {
            if (_isPreviewMode)
            {
                ApplyTopmostState(true);
                return;
            }

            ApplyTopmostState(OverlayHelper.IsForegroundAllowedOverlayWindow());
        }

        private void ApplyTopmostState(bool shouldTopmost)
        {
            if (_lastAppliedTopmostState == shouldTopmost)
                return;

            _lastAppliedTopmostState = shouldTopmost;

            try
            {
                Topmost = shouldTopmost;
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                NativeMethods.SetWindowPos(
                    hwnd,
                    shouldTopmost ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE |
                    NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_NOOWNERZORDER);
            }
            catch { }
        }

        private void ApplyLayoutConstraints()
        {
            var area = SystemParameters.WorkArea;
            MaxWidth = Math.Max(MinWidth, area.Width - (ScreenEdgePadding * 2));
            ToastText.MaxWidth = Math.Max(100, MaxWidth - 52);
        }

        private double ClampLeftToWorkArea(double requestedLeft)
        {
            var area = SystemParameters.WorkArea;
            double width = ActualWidth > 0 ? ActualWidth : MinWidth;
            double minLeft = area.Left + ScreenEdgePadding;
            double maxLeft = area.Right - width - ScreenEdgePadding;
            if (maxLeft < minLeft)
                return minLeft;

            return Math.Max(minLeft, Math.Min(maxLeft, requestedLeft));
        }

        private static string NormalizeMessage(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string decoded = WebUtility.HtmlDecode(text).Replace("&nbsp;", " ");
            decoded = HtmlTagRegex.Replace(decoded, " ");
            decoded = Regex.Replace(decoded, @"\s+", " ").Trim();
            decoded = ShoutPrefixRegex.Replace(decoded, string.Empty).Trim();
            return decoded;
        }
    }
}
