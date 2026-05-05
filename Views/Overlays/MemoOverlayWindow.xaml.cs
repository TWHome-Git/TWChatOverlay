using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Interop;
using TWChatOverlay.Services;
using TWChatOverlay.Models;

namespace TWChatOverlay.Views
{
    public partial class MemoOverlayWindow : Window
    {
        private MemoTextOnlyWindow? _textOnly;
        private bool _isOverlayMode;
        private ChatSettings? _settings;
        private bool _isStateReady;
        private bool _isApplyingLoadedState;
        private string _selectedColorKey = "White";
        public event EventHandler? EditorModeChanged;

        public MemoOverlayWindow(ChatSettings? sharedSettings = null)
        {
            InitializeComponent();
            WindowFontService.Apply(this);
            _settings = sharedSettings;
            Loaded += MemoOverlayWindow_Loaded;
            Closing += MemoOverlayWindow_Closing;
            LocationChanged += MemoOverlayWindow_LocationChanged;
            SizeChanged += MemoOverlayWindow_SizeChanged;
            MemoTextBox.TextChanged += MemoTextBox_TextChanged;
            Application.Current.Exit += Current_Exit;
        }

        private void MemoOverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isApplyingLoadedState = true;
            _settings ??= ResolveSharedSettings();
            if (_settings.MemoOverlayWindowLeft.HasValue && _settings.MemoOverlayWindowTop.HasValue)
            {
                Left = _settings.MemoOverlayWindowLeft.Value;
                Top = _settings.MemoOverlayWindowTop.Value;
            }
            MemoTextBox.Text = _settings.MemoOverlayText ?? string.Empty;
            FontSizeSlider.Value = _settings.MemoOverlayFontSize <= 0 ? 20.0 : _settings.MemoOverlayFontSize;
            MemoTextBox.FontWeight = _settings.MemoOverlayBold ? FontWeights.Bold : FontWeights.Normal;
            MemoTextBox.FontStyle = _settings.MemoOverlayItalic ? FontStyles.Italic : FontStyles.Normal;
            _selectedColorKey = string.IsNullOrWhiteSpace(_settings.MemoOverlayColorKey) ? "White" : _settings.MemoOverlayColorKey;
            ApplyColorByKey(_selectedColorKey);
            SelectColorCombo(_selectedColorKey);
            _isOverlayMode = false;
            ShowEditorMode();
            if (BtnOverlayMode != null)
                BtnOverlayMode.Content = "편집 모드";

            _isApplyingLoadedState = false;
            _isStateReady = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isApplyingLoadedState = true;
                try
                {
                    _isOverlayMode = true;
                    ShowTextOnly();
                    EditorModeChanged?.Invoke(this, EventArgs.Empty);
                    PersistState();
                }
                finally
                {
                    _isApplyingLoadedState = false;
                }
            }), System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        private void MemoOverlayWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            PersistState();
        }
        private void Current_Exit(object? sender, ExitEventArgs e) => PersistState();

        private void MemoOverlayWindow_LocationChanged(object? sender, System.EventArgs e) => PersistState();
        private void MemoOverlayWindow_SizeChanged(object sender, SizeChangedEventArgs e) => PersistState();
        private void MemoTextBox_TextChanged(object sender, TextChangedEventArgs e) => PersistState();

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _textOnly?.SetBackgroundOpacity(e.NewValue);
            PersistState();
        }

        private void OverlayMode_Click(object sender, RoutedEventArgs e)
        {
            if (_isApplyingLoadedState)
                return;

            if (_isOverlayMode)
            {
                _textOnly?.Hide();
                Show();
                Activate();
                _isOverlayMode = false;
                BtnOverlayMode.Content = "오버레이 모드";
                PersistState();
                EditorModeChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ShowTextOnly();
                _isOverlayMode = true;
                BtnOverlayMode.Content = "편집 모드";
                PersistState();
                EditorModeChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private void ShowTextOnly()
        {
            if (_textOnly == null || !_textOnly.IsLoaded)
            {
                _textOnly = new MemoTextOnlyWindow();
                _textOnly.LocationChanged += (_, _) =>
                {
                    if (_isOverlayMode && _textOnly.IsVisible)
                    {
                        Left = _textOnly.Left;
                        Top = _textOnly.Top;
                        PersistState();
                    }
                };
                _textOnly.Closed += (_, _) => _textOnly = null;
            }

            if (MemoTextBox == null)
                return;

            _textOnly.SetText(MemoTextBox.Text);
            _textOnly.SetStyle(MemoTextBox.Foreground, MemoTextBox.FontSize, MemoTextBox.FontWeight, MemoTextBox.FontStyle);
            _textOnly.SetBackgroundOpacity(OpacitySlider?.Value ?? 0);

            Point devicePoint = MemoTextBox.PointToScreen(new Point(0, 0));
            Matrix toDip = Matrix.Identity;
            if (PresentationSource.FromVisual(this)?.CompositionTarget is CompositionTarget target)
                toDip = target.TransformFromDevice;
            Point dipPoint = toDip.Transform(devicePoint);

            _textOnly.Left = dipPoint.X;
            _textOnly.Top = dipPoint.Y;
            _textOnly.Width = MemoTextBox.ActualWidth;
            _textOnly.Height = MemoTextBox.ActualHeight;
            _textOnly.Topmost = false;
            _textOnly.Topmost = true;
            _textOnly.Show();
            Hide();
        }

        public void ShowEditorMode()
        {
            _textOnly?.Hide();
            Show();
            Activate();
            _isOverlayMode = false;
            if (BtnOverlayMode != null)
                BtnOverlayMode.Content = "오버레이 모드";
            EditorModeChanged?.Invoke(this, EventArgs.Empty);
        }

        public bool IsEditorModeVisible => IsVisible && !_isOverlayMode;
        public bool IsOverlayMode => _isOverlayMode;
        public bool IsAnyModeVisible => IsVisible || (_textOnly?.IsVisible == true);

        public void ToggleModeFromMenu()
        {
            if (_isApplyingLoadedState)
                return;
            OverlayMode_Click(this, new RoutedEventArgs());
        }

        public void ShowCurrentModeFromMenu()
        {
            if (_isOverlayMode)
            {
                ShowTextOnly();
                if (BtnOverlayMode != null)
                    BtnOverlayMode.Content = "편집 모드";
            }
            else
            {
                ShowEditorMode();
            }
        }

        private void PersistState()
        {
            if (!_isStateReady || _isApplyingLoadedState)
                return;

            string text = MemoTextBox?.Text ?? string.Empty;
            ChatSettings snapshot = ConfigService.Load();
            double left = (_isOverlayMode && _textOnly?.IsVisible == true) ? _textOnly.Left : Left;
            double top = (_isOverlayMode && _textOnly?.IsVisible == true) ? _textOnly.Top : Top;
            snapshot.MemoOverlayWindowLeft = left;
            snapshot.MemoOverlayWindowTop = top;
            snapshot.MemoOverlayText = text;
            snapshot.MemoOverlayTextOnlyMode = true;
            snapshot.MemoOverlayFontSize = MemoTextBox?.FontSize ?? 20.0;
            snapshot.MemoOverlayBold = MemoTextBox?.FontWeight == FontWeights.Bold;
            snapshot.MemoOverlayItalic = MemoTextBox?.FontStyle == FontStyles.Italic;
            snapshot.MemoOverlayColorKey = _selectedColorKey;
            ConfigService.Save(snapshot);
            _settings = snapshot;
        }

        private static ChatSettings ResolveSharedSettings()
        {
            foreach (Window w in Application.Current.Windows)
            {
                if (w is MainWindow main && main.DataContext is ChatSettings shared)
                    return shared;
            }
            return ConfigService.Load();
        }

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MemoTextBox == null) return;
            MemoTextBox.FontSize = e.NewValue;
        }

        private void BtnBold_Click(object sender, RoutedEventArgs e)
        {
            if (MemoTextBox == null) return;
            MemoTextBox.FontWeight = MemoTextBox.FontWeight == FontWeights.Bold ? FontWeights.Normal : FontWeights.Bold;
        }

        private void BtnItalic_Click(object sender, RoutedEventArgs e)
        {
            if (MemoTextBox == null) return;
            MemoTextBox.FontStyle = MemoTextBox.FontStyle == FontStyles.Italic ? FontStyles.Normal : FontStyles.Italic;
        }

        private void ColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MemoTextBox == null || ColorCombo == null)
                return;

            _selectedColorKey = (ColorCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "White";
            ApplyColorByKey(_selectedColorKey);
        }

        private void ApplyColorByKey(string key)
        {
            MemoTextBox.Foreground = key switch
            {
                "Yellow" => Brushes.Gold,
                "Cyan" => Brushes.DeepSkyBlue,
                "Pink" => Brushes.HotPink,
                "Lime" => Brushes.LimeGreen,
                _ => Brushes.White
            };
        }

        private void SelectColorCombo(string key)
        {
            if (ColorCombo == null)
                return;

            foreach (var item in ColorCombo.Items)
            {
                if (item is ComboBoxItem c && string.Equals(c.Content?.ToString(), key, StringComparison.Ordinal))
                {
                    ColorCombo.SelectedItem = c;
                    return;
                }
            }
        }
    }
}
