using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.Views.Addons;

namespace TWChatOverlay.Views
{
    public partial class SubAddonWindow : Window
    {
        private readonly DispatcherTimer _hideTimer;
        public static SubAddonWindow? Instance { get; private set; }

        public SubAddonWindow()
        {
            InitializeComponent();
            WindowFontService.Apply(this);
            Instance = this;
            LocationChanged += (_, _) => SyncWindowPositionToLiveSettings();

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _hideTimer.Tick += (_, _) =>
            {
                _hideTimer.Stop();
                HideAlert();
            };

            EnsureEclipseAddonView();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            RefreshMousePassthroughStyle();
        }

        public void ShowAddonContent(FrameworkElement? view, string? title = null)
        {
            if (title != null)
            {
                Title = title;
                WindowTitleText.Text = title;
            }

            HostContent.Child = view;
            UpdateHintVisibility();
        }

        public void ClearAddonContent()
        {
            HostContent.Child = null;
        }

        public void ShowEtosDirection(string? imagePath)
        {
            if (!IsEtosAlertEnabled())
            {
                HideAlert();
                return;
            }

            var view = EnsureEclipseAddonView();
            view.ShowEtosDirection(imagePath);

            if (!IsVisible)
            {
                Show();
            }

            Visibility = Visibility.Visible;
            TopmostWindowHelper.BringToTopmost(this);
            UpdateHintVisibility();

            _hideTimer.Stop();
            if (!IsPinnedVisible())
            {
                _hideTimer.Start();
            }
        }

        public void HideAlert()
        {
            try
            {
                if (IsPinnedVisible())
                {
                    ShowPinnedState();
                    return;
                }

                _hideTimer.Stop();
                Hide();
            }
            catch { }
        }

        public void ApplyPinnedVisibility()
        {
            RefreshMousePassthroughStyle();

            if (IsPinnedVisible())
            {
                ShowPinnedState();
                return;
            }

            Hide();
        }

        public void ApplyPositionPreviewVisibility(bool isPreviewing)
        {
            RefreshMousePassthroughStyle(forceInteractive: isPreviewing);

            if (isPreviewing)
            {
                _hideTimer.Stop();
                EnsureEclipseAddonView();
                if (!IsVisible)
                {
                    Show();
                }

                Visibility = Visibility.Visible;
                TopmostWindowHelper.BringToTopmost(this);
                UpdateHintVisibility();
                return;
            }

            ApplyPinnedVisibility();
        }

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
            {
                return;
            }

            try
            {
                DragMove();
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }

            base.OnClosed(e);
        }

        private EclipseAddonView EnsureEclipseAddonView()
        {
            if (HostContent.Child is EclipseAddonView existing)
            {
                return existing;
            }

            var view = new EclipseAddonView();
            ShowAddonContent(view, "에토스 방향 안내");
            return view;
        }

        private void ShowPinnedState()
        {
            _hideTimer.Stop();
            RefreshMousePassthroughStyle();
            var view = EnsureEclipseAddonView();
            if (view != null && !IsVisible)
            {
                Show();
            }

            Visibility = Visibility.Visible;
            TopmostWindowHelper.BringToTopmost(this);
            UpdateHintVisibility();
        }

        private void UpdateHintVisibility()
        {
            WindowHintText.Visibility = IsPinnedVisible()
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private static bool IsEtosAlertEnabled()
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow mainWindow && mainWindow.DataContext is ChatSettings settings)
                {
                    return settings.ShowEtosDirectionAlert;
                }
            }

            return true;
        }

        private static bool IsPinnedVisible()
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow mainWindow && mainWindow.DataContext is ChatSettings settings)
                {
                    return settings.ShowEtosHelperWindow;
                }
            }

            return false;
        }

        private void SyncWindowPositionToLiveSettings()
        {
            try
            {
                foreach (Window window in Application.Current.Windows)
                {
                    if (window is MainWindow mainWindow && mainWindow.DataContext is ChatSettings settings)
                    {
                        settings.SubAddonWindowLeft = Left;
                        settings.SubAddonWindowTop = Top;
                        break;
                    }
                }
            }
            catch { }
        }

        private void RefreshMousePassthroughStyle(bool forceInteractive = false)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                int nextStyle = forceInteractive || IsPinnedVisible()
                    ? (exStyle & ~NativeMethods.WS_EX_TRANSPARENT) | NativeMethods.WS_EX_TOOLWINDOW
                    : exStyle | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW;
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, nextStyle);
            }
            catch { }
        }
    }
}
