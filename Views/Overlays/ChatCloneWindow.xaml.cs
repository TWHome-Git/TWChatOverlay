using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class ChatCloneWindow : Window, INotifyPropertyChanged
    {
        private readonly ChatSettings _settings;
        private readonly LogDocumentRenderer _renderer = new(200);
        private readonly int _slot;
        private MainWindow? _mainWindow;
        private string _currentTabTag = "General";
        private bool _isInitialized;
        private bool _isSettingsMode;
        private bool _isApplyingSnap;

        public event PropertyChangedEventHandler? PropertyChanged;

        public static readonly DependencyProperty CurrentFontProperty =
            DependencyProperty.Register(nameof(CurrentFont), typeof(FontFamily), typeof(ChatCloneWindow));

        public FontFamily CurrentFont
        {
            get => (FontFamily)GetValue(CurrentFontProperty);
            set => SetValue(CurrentFontProperty, value);
        }

        public static readonly DependencyProperty CurrentFontSizeProperty =
            DependencyProperty.Register(nameof(CurrentFontSize), typeof(double), typeof(ChatCloneWindow));

        public double CurrentFontSize
        {
            get => (double)GetValue(CurrentFontSizeProperty);
            set => SetValue(CurrentFontSizeProperty, value);
        }

        public IReadOnlyList<string> AvailableFonts => _settings.AvailableFonts;

        public bool FollowMainFont
        {
            get => GetFollowMainFont();
            set
            {
                if (GetFollowMainFont() == value)
                    return;

                SetFollowMainFont(value);
                OnPropertyChanged();
                ApplyEffectiveFont();
            }
        }

        public string SelectedFontFamily
        {
            get => GetSelectedFontFamily();
            set
            {
                string next = value ?? string.Empty;
                if (GetSelectedFontFamily() == next)
                    return;

                SetSelectedFontFamily(next);
                OnPropertyChanged();
                ApplyEffectiveFont();
            }
        }

        public double SelectedFontSize
        {
            get => GetSelectedFontSize();
            set
            {
                double next = Math.Max(10.0, Math.Min(28.0, value));
                if (Math.Abs(GetSelectedFontSize() - next) < 0.001)
                    return;

                SetSelectedFontSize(next);
                OnPropertyChanged();
                ApplyEffectiveFont();
            }
        }

        public static bool TryOpen(ChatSettings settings)
        {
            if (!ChatWindowHub.CanOpenClone)
                return false;

            var window = new ChatCloneWindow(settings);
            window.Show();
            window.Activate();
            return true;
        }

        public ChatCloneWindow(ChatSettings settings)
        {
            InitializeComponent();
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            int? slot = ChatWindowHub.RegisterClone();
            if (slot == null)
                throw new InvalidOperationException("No chat window slot is available.");

            _slot = slot.Value;

            DataContext = _settings;
            Closed += ChatCloneWindow_Closed;
            LocationChanged += (_, _) => HandleLocationChanged();
            _settings.PropertyChanged += Settings_PropertyChanged;
            ChatWindowHub.BuffersChanged += ChatWindowHub_BuffersChanged;
            AttachToMainWindow();

            ApplySizeFromMainWindow();
            ApplyDefaultFontSettings();
            ApplyEffectiveFont();
            ApplyStoredPosition();
            ApplyDefaultPositionIfNeeded();
            SetSettingsMode(false);
        }

        private void ChatCloneWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                SavePositionToSettings();
            }
            catch
            {
            }

            ChatWindowHub.BuffersChanged -= ChatWindowHub_BuffersChanged;
            _settings.PropertyChanged -= Settings_PropertyChanged;
            DetachFromMainWindow();
            ChatWindowHub.UnregisterClone(_slot);
        }

        private void AttachToMainWindow()
        {
            _mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (_mainWindow == null)
                return;

            if (Owner == null)
                Owner = _mainWindow;

            _mainWindow.OverlayVisibilityChanged += MainWindow_OverlayVisibilityChanged;
            _mainWindow.StateChanged += MainWindow_StateChanged;
            _mainWindow.IsVisibleChanged += MainWindow_IsVisibleChanged;
        }

        private void DetachFromMainWindow()
        {
            if (_mainWindow == null)
                return;

            _mainWindow.OverlayVisibilityChanged -= MainWindow_OverlayVisibilityChanged;
            _mainWindow.StateChanged -= MainWindow_StateChanged;
            _mainWindow.IsVisibleChanged -= MainWindow_IsVisibleChanged;
            _mainWindow = null;
        }

        private void MainWindow_OverlayVisibilityChanged(object? sender, bool isVisible)
        {
            Dispatcher.BeginInvoke(new Action(SyncVisibilityWithMainWindow), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(SyncVisibilityWithMainWindow), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(SyncVisibilityWithMainWindow), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void SyncVisibilityWithMainWindow()
        {
            if (_mainWindow == null)
                return;

            ApplySizeFromMainWindow();

            bool shouldBeVisible =
                _mainWindow.IsVisible &&
                _mainWindow.WindowState != WindowState.Minimized &&
                _mainWindow.IsOverlayVisible &&
                _mainWindow.Visibility == Visibility.Visible;

            if (shouldBeVisible)
            {
                if (!IsVisible)
                    Show();
            }
            else if (IsVisible)
            {
                Hide();
            }
        }

        private void HandleLocationChanged()
        {
            if (_isApplyingSnap)
                return;

            if (_isSettingsMode)
            {
                _isApplyingSnap = true;
                try
                {
                    if (ChatWindowHub.TryApplyMagneticSnap(this))
                        ApplySizeFromMainWindow();
                }
                finally
                {
                    _isApplyingSnap = false;
                }
            }

            SyncPositionToSettings();
        }

        private void ChatWindowHub_BuffersChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(RefreshLogDisplay), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatSettings.FontFamily) ||
                e.PropertyName == nameof(ChatSettings.FontSize) ||
                e.PropertyName == nameof(ChatSettings.ChatCloneWindow1FollowMainFont) ||
                e.PropertyName == nameof(ChatSettings.ChatCloneWindow1FontFamily) ||
                e.PropertyName == nameof(ChatSettings.ChatCloneWindow1FontSize) ||
                e.PropertyName == nameof(ChatSettings.ChatCloneWindow2FollowMainFont) ||
                e.PropertyName == nameof(ChatSettings.ChatCloneWindow2FontFamily) ||
                e.PropertyName == nameof(ChatSettings.ChatCloneWindow2FontSize))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnPropertyChanged(nameof(FollowMainFont));
                    OnPropertyChanged(nameof(SelectedFontFamily));
                    OnPropertyChanged(nameof(SelectedFontSize));
                    ApplyEffectiveFont();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
            else if (e.PropertyName != null && e.PropertyName.StartsWith("Show", StringComparison.Ordinal))
            {
                Dispatcher.BeginInvoke(new Action(RefreshLogDisplay), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void ApplyDefaultFontSettings()
        {
            if (FollowMainFont)
            {
                if (string.IsNullOrWhiteSpace(SelectedFontFamily))
                    SelectedFontFamily = _settings.FontFamily;

                if (SelectedFontSize <= 0)
                    SelectedFontSize = _settings.FontSize;
            }
        }

        private void ApplyEffectiveFont()
        {
            FontFamily effectiveFont;
            double effectiveSize;

            if (FollowMainFont)
            {
                effectiveFont = ResolveMainFont();
                effectiveSize = ResolveMainFontSize();
            }
            else
            {
                effectiveFont = FontService.GetFont(SelectedFontFamily);
                effectiveSize = SelectedFontSize > 0 ? SelectedFontSize : ResolveMainFontSize();
            }

            CurrentFont = effectiveFont;
            CurrentFontSize = effectiveSize;

            if (_isInitialized)
                RefreshLogDisplay();
        }

        private FontFamily ResolveMainFont()
        {
            MainWindow? mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (mainWindow?.CurrentFont != null)
                return mainWindow.CurrentFont;

            return FontService.GetFont(_settings.FontFamily);
        }

        private double ResolveMainFontSize()
        {
            MainWindow? mainWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (mainWindow != null && mainWindow.CurrentFontSize > 0)
                return mainWindow.CurrentFontSize;

            return _settings.FontSize > 0 ? _settings.FontSize : 17.0;
        }

        private void RefreshLogDisplay()
        {
            var logDisplay = ChatDisplay.LogDisplayControl;
            if (logDisplay == null)
                return;

            bool shouldAutoScroll = ChatDisplay.IsAutoScrollEnabled;
            logDisplay.BeginChange();
            try
            {
                logDisplay.Document.Blocks.Clear();
                foreach (var log in ChatWindowHub.SharedLogBuffers.GetLogs(_currentTabTag))
                {
                    _renderer.AddLog(logDisplay.Document, log, _settings, CurrentFont, CurrentFontSize, false, false);
                }
            }
            finally
            {
                logDisplay.EndChange();
                logDisplay.InvalidateMeasure();
                logDisplay.InvalidateVisual();
                logDisplay.UpdateLayout();
                if (shouldAutoScroll)
                    logDisplay.ScrollToEnd();
            }
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton btn || btn.Tag == null)
                return;

            string tabTag = btn.Tag.ToString() ?? string.Empty;
            if (string.Equals(tabTag, "Settings", StringComparison.Ordinal))
            {
                SetSettingsMode(true);
                return;
            }

            _currentTabTag = tabTag;
            SetSettingsMode(false);
            RefreshLogDisplay();
        }

        private void SetSettingsMode(bool isSettingsMode)
        {
            if (_isSettingsMode == isSettingsMode)
                return;

            _isSettingsMode = isSettingsMode;
            DragBar.Visibility = isSettingsMode ? Visibility.Visible : Visibility.Collapsed;
            DragBarRow.Height = isSettingsMode ? new GridLength(25) : new GridLength(0);
            ChatDisplay.Visibility = isSettingsMode ? Visibility.Collapsed : Visibility.Visible;
            SettingsScrollViewer.Visibility = isSettingsMode ? Visibility.Visible : Visibility.Collapsed;
        }

        private void CloseChat_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch
                {
                }
                finally
                {
                    ChatWindowHub.TryApplyMagneticSnap(this);
                    SyncPositionToSettings();
                }
            }
        }

        private void TopResize_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (Height - e.VerticalChange >= MinHeight)
            {
                Height -= e.VerticalChange;
                Top += e.VerticalChange;
                ChatWindowHub.TryApplyMagneticSnap(this);
                SyncPositionToSettings();
            }
        }

        private void LeftResize_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (Width - e.HorizontalChange >= MinWidth)
            {
                Width -= e.HorizontalChange;
                Left += e.HorizontalChange;
                ChatWindowHub.TryApplyMagneticSnap(this);
                SyncPositionToSettings();
            }
        }

        private void RightResize_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (Width + e.HorizontalChange >= MinWidth)
            {
                Width += e.HorizontalChange;
                ChatWindowHub.TryApplyMagneticSnap(this);
                SyncPositionToSettings();
            }
        }

        private void ApplyStoredPosition()
        {
            if (_slot == 1)
            {
                if (_settings.ChatCloneWindow1Left.HasValue)
                    Left = _settings.ChatCloneWindow1Left.Value;
                if (_settings.ChatCloneWindow1Top.HasValue)
                    Top = _settings.ChatCloneWindow1Top.Value;
            }
            else if (_slot == 2)
            {
                if (_settings.ChatCloneWindow2Left.HasValue)
                    Left = _settings.ChatCloneWindow2Left.Value;
                if (_settings.ChatCloneWindow2Top.HasValue)
                    Top = _settings.ChatCloneWindow2Top.Value;
            }
        }

        private void ApplyDefaultPositionIfNeeded()
        {
            bool hasStoredPosition = _slot == 1
                ? _settings.ChatCloneWindow1Left.HasValue && _settings.ChatCloneWindow1Top.HasValue
                : _settings.ChatCloneWindow2Left.HasValue && _settings.ChatCloneWindow2Top.HasValue;

            if (hasStoredPosition)
                return;

            Window ownerWindow = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() ?? Application.Current.MainWindow ?? this;
            const double gap = 8.0;
            Rect workArea = SystemParameters.WorkArea;
            double cloneHeight = Height > 0 ? Height : ownerWindow.Height;
            double left = Math.Max(workArea.Left, Math.Min(ownerWindow.Left, workArea.Right - Width));
            double top = ownerWindow.Top - cloneHeight - gap;

            if (_slot == 2 && ChatWindowHub.OpenCloneSlots.Any(slot => slot == 1))
            {
                top -= cloneHeight + gap;
            }

            top = Math.Max(workArea.Top, top);

            Left = left;
            Top = top;
        }

        private void SavePositionToSettings()
        {
            if (!_isSettingsMode)
                return;

            if (_slot == 1)
            {
                _settings.ChatCloneWindow1Left = Left;
                _settings.ChatCloneWindow1Top = Top;
            }
            else if (_slot == 2)
            {
                _settings.ChatCloneWindow2Left = Left;
                _settings.ChatCloneWindow2Top = Top;
            }

            ConfigService.SaveDeferred(_settings);
        }

        private void SyncPositionToSettings()
        {
            if (!_isSettingsMode)
                return;

            SavePositionToSettings();
        }

        private bool GetFollowMainFont()
            => _slot == 1 ? _settings.ChatCloneWindow1FollowMainFont : _settings.ChatCloneWindow2FollowMainFont;

        private void SetFollowMainFont(bool value)
        {
            if (_slot == 1)
                _settings.ChatCloneWindow1FollowMainFont = value;
            else
                _settings.ChatCloneWindow2FollowMainFont = value;

            ConfigService.SaveDeferred(_settings);
        }

        private string GetSelectedFontFamily()
        {
            string value = _slot == 1 ? _settings.ChatCloneWindow1FontFamily : _settings.ChatCloneWindow2FontFamily;
            if (string.IsNullOrWhiteSpace(value))
                return _settings.FontFamily;

            return value;
        }

        private void SetSelectedFontFamily(string value)
        {
            if (_slot == 1)
                _settings.ChatCloneWindow1FontFamily = value;
            else
                _settings.ChatCloneWindow2FontFamily = value;

            ConfigService.SaveDeferred(_settings);
        }

        private double GetSelectedFontSize()
        {
            double? value = _slot == 1 ? _settings.ChatCloneWindow1FontSize : _settings.ChatCloneWindow2FontSize;
            return value ?? _settings.FontSize;
        }

        private void SetSelectedFontSize(double value)
        {
            if (_slot == 1)
                _settings.ChatCloneWindow1FontSize = value;
            else
                _settings.ChatCloneWindow2FontSize = value;

            ConfigService.SaveDeferred(_settings);
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
                return;

            _isInitialized = true;
            ApplySizeFromMainWindow();
            RefreshLogDisplay();
            Topmost = true;
            SyncVisibilityWithMainWindow();
            if (IsVisible)
                Activate();
        }

        private void SettingsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            double offset = SettingsScrollViewer.VerticalOffset - Math.Sign(e.Delta) * 48.0;
            SettingsScrollViewer.ScrollToVerticalOffset(Math.Max(0.0, offset));
            e.Handled = true;
        }

        private void ApplySizeFromMainWindow()
        {
            MainWindow? mainWindow = _mainWindow ?? Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            if (mainWindow == null)
                return;

            if (mainWindow.Width > 0)
                Width = mainWindow.Width;

            if (mainWindow.Height > 0)
                Height = mainWindow.Height;
        }
    }
}
