using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class DungeonCountDisplayWindow : Window
    {
        private readonly DispatcherTimer _closeTimer;
        private ChatSettings _settings;
        private bool _isClosing;
        private bool _isDragging;

        public DungeonCountDisplayWindow(string message, FontFamily fontFamily, int durationSeconds, ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            InitializeComponent();
            FontFamily = fontFamily;
            MessageTextBlock.FontFamily = fontFamily;
            MessageTextBlock.Text = message;
            LocationChanged += (_, _) => SyncPositionToSettings(notify: false);

            _closeTimer = new DispatcherTimer
            {
                Interval = durationSeconds > 0
                    ? TimeSpan.FromSeconds(Math.Max(1, Math.Min(300, durationSeconds)))
                    : TimeSpan.Zero
            };
            _closeTimer.Tick += (_, _) =>
            {
                _closeTimer.Stop();
                CloseAnimated();
            };
        }

        public void SetSettings(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            ApplyToolWindowStyle();
        }

        public void UpdateDisplay(string message, int durationSeconds)
        {
            _isClosing = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            MessageTextBlock.Text = message;
            _closeTimer.Stop();
            if (durationSeconds > 0)
            {
                _closeTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, Math.Min(300, durationSeconds)));
                _closeTimer.Start();
            }
            BringToFront();
        }

        public void ShowDisplay(double left, double top)
        {
            Left = left;
            Top = top;
            Show();
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
            if (_closeTimer.Interval > TimeSpan.Zero)
                _closeTimer.Start();
        }

        public void MoveTo(double top)
        {
            BeginAnimation(TopProperty, new DoubleAnimation(Top, top, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            });
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            try
            {
                ApplyToolWindowStyle();
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            SyncPositionToSettings(notify: true);
            base.OnClosed(e);
        }

        private void ApplyToolWindowStyle()
        {
            IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            exStyle |= NativeMethods.WS_EX_TOOLWINDOW;
            exStyle = _settings.ShowDungeonCountDisplayWindow
                ? exStyle & ~NativeMethods.WS_EX_TRANSPARENT
                : exStyle | NativeMethods.WS_EX_TRANSPARENT;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle);
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_settings.ShowDungeonCountDisplayWindow || e.ButtonState != MouseButtonState.Pressed)
                return;

            _isDragging = true;
            try { DragMove(); } catch { }
            finally
            {
                _isDragging = false;
                SyncPositionToSettings(notify: true);
            }
        }

        private void CloseAnimated()
        {
            if (_isClosing)
                return;

            _isClosing = true;
            var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
            animation.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, animation);
        }

        private void BringToFront()
        {
            Topmost = false;
            Topmost = true;
        }

        private void SyncPositionToSettings(bool notify)
        {
            if (!_settings.ShowDungeonCountDisplayWindow || !IsVisible)
                return;

            _settings.DungeonCountDisplayWindowLeft = Left;
            _settings.DungeonCountDisplayWindowTop = Top;

            if (_isDragging || notify)
                ConfigService.SaveDeferred(_settings);
        }
    }
}
