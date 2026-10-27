using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using TWChatOverlay.Services;
using TWChatOverlay.Models;

namespace TWChatOverlay.Views
{
    public partial class MemoOverlayWindow : OverlayWindowBase
    {
        private MemoTextOnlyWindow? _textOnly;
        private bool _isOverlayMode;
        private ChatSettings? _settings;
        private bool _isStateReady;
        private bool _isApplyingLoadedState;
        private string _selectedColorKey = "White";
        private readonly bool _restoreSavedMode;
        // 글 전용 창은 편집 창의 글 상자 자리에 뜬다. 그 차이(편집 창 기준)를 기억해 두고 위치는 늘 편집 창 기준으로 저장한다.
        private Vector _textOnlyOffset;
        public event EventHandler? EditorModeChanged;

        /// <param name="restoreSavedMode">true면 앱 시작 복원 — 마지막으로 쓰던 모드(글만 보기/편집)로 띄운다. 메뉴로 열면 편집 모드.</param>
        public MemoOverlayWindow(ChatSettings? sharedSettings = null, bool restoreSavedMode = false)
        {
            InitializeComponent();
            _settings = sharedSettings;
            _restoreSavedMode = restoreSavedMode;
            Loaded += MemoOverlayWindow_Loaded;
            Closing += MemoOverlayWindow_Closing;
            MemoTextBox.TextChanged += MemoTextBox_TextChanged;
            Application.Current.Exit += Current_Exit;
            Closed += MemoOverlayWindow_Closed;
        }

        // 메모 창은 잠금 모드와 무관하게 항상 이동 가능. 위치는 베이스 경로(PersistBounds)로, 글·서식은 PersistState로 저장한다.
        protected override bool KeepBelowSettingsHost => false;
        protected override bool DragRequiresUnlock => false;
        protected override ChatSettings? ResolveSettings() => _settings ??= ResolveSharedSettings();

        /// <summary>
        /// 편집 창 자리를 저장한다. 글만 보기 중에도 숨은 편집 창이 글 전용 창을 따라 움직이므로 같은 값이다.
        /// (예전에는 모드에 따라 글 전용 창 자리를 저장해, 다음에 편집 창이 그 자리에 뜨며 제목줄 높이만큼씩 밀렸다.)
        /// </summary>
        protected override bool PersistBounds(ChatSettings settings)
        {
            if (!_isStateReady || _isApplyingLoadedState)
                return false;

            settings.MemoOverlayWindowLeft = Left;
            settings.MemoOverlayWindowTop = Top;
            return true;
        }

        protected override (double? Left, double? Top, double? Width, double? Height)? StoredBounds(ChatSettings settings)
            => (settings.MemoOverlayWindowLeft, settings.MemoOverlayWindowTop, null, null);

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => TryBeginDrag(e);

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            try { _textOnly?.Close(); } catch { }
            Close();
        }

        /// <summary>
        /// 설정에 저장된 위치로 옮긴다 (복원 경로). 글만 보기 모드면 글 전용 창도 편집 창 기준 같은 차이만큼 옮긴다.
        /// </summary>
        public override void ApplyStoredBounds(ChatSettings? settings = null)
        {
            settings ??= ResolveSettings();
            if (settings == null)
                return;

            base.ApplyStoredBounds(settings);

            if (_isOverlayMode && _textOnly?.IsVisible == true)
            {
                _textOnly.Left = Left + _textOnlyOffset.X;
                _textOnly.Top = Top + _textOnlyOffset.Y;
            }
        }

        private void MemoOverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _isApplyingLoadedState = true;
            _settings ??= ResolveSharedSettings();
            ApplyStoredBounds(_settings);
            MemoTextBox.Text = _settings.MemoOverlayText ?? string.Empty;
            FontSizeSlider.Value = _settings.MemoOverlayFontSize <= 0 ? 20.0 : _settings.MemoOverlayFontSize;
            MemoTextBox.FontWeight = _settings.MemoOverlayBold ? FontWeights.Bold : FontWeights.Normal;
            MemoTextBox.FontStyle = _settings.MemoOverlayItalic ? FontStyles.Italic : FontStyles.Normal;
            _selectedColorKey = string.IsNullOrWhiteSpace(_settings.MemoOverlayColorKey) ? "White" : _settings.MemoOverlayColorKey;
            ApplyColorByKey(_selectedColorKey);
            SelectColorCombo(_selectedColorKey);
            OpacitySlider.Value = _settings.MemoOverlayBackgroundOpacity;
            _isOverlayMode = false;
            ShowEditorMode();

            _isApplyingLoadedState = false;
            _isStateReady = true;
            _settings.MemoOverlayIsOpen = true;
            EditorModeChanged?.Invoke(this, EventArgs.Empty);

            // 시작 복원: 글만 보기로 끝냈으면 글 상자 배치가 끝난 뒤 글 전용 창으로 바꾼다
            if (_restoreSavedMode && _settings.MemoOverlayTextOnlyMode)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (IsLoaded && !_isOverlayMode)
                        OverlayMode_Click(this, new RoutedEventArgs());
                }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
        }

        private void MemoOverlayWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // 사용자가 닫은 것만 '꺼짐'으로 남긴다. 앱 종료로 닫히면 열린 상태 그대로 두어 다음 실행 때 다시 뜬다.
            if (_isStateReady && _settings != null && !AppServices.Get<ChatWindowHub>().IsShuttingDown)
                _settings.MemoOverlayIsOpen = false;
            PersistState(immediate: true);
        }
        private void MemoOverlayWindow_Closed(object? sender, EventArgs e)
        {
            try { Application.Current.Exit -= Current_Exit; } catch { }
            try { MemoTextBox.TextChanged -= MemoTextBox_TextChanged; } catch { }
            try { Loaded -= MemoOverlayWindow_Loaded; } catch { }
            try { Closing -= MemoOverlayWindow_Closing; } catch { }
            try { Closed -= MemoOverlayWindow_Closed; } catch { }
        }
        private void Current_Exit(object? sender, ExitEventArgs e) => PersistState(immediate: true);

        private void MemoTextBox_TextChanged(object sender, TextChangedEventArgs e) => PersistState();

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _textOnly?.SetBackgroundOpacity(e.NewValue);
            PersistState();
        }

        private void MemoStyle_Changed() => PersistState();

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
                    // 숨은 편집 창이 같은 차이를 두고 따라가면 베이스의 LocationChanged 저장 경로가 편집 창 자리를 저장한다
                    if (_isOverlayMode && _textOnly?.IsVisible == true)
                    {
                        Left = _textOnly.Left - _textOnlyOffset.X;
                        Top = _textOnly.Top - _textOnlyOffset.Y;
                    }
                };
                _textOnly.Closed += (_, _) =>
                {
                    if (_isOverlayMode)
                        PersistState();

                    _textOnly = null;
                };
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
            _textOnlyOffset = new Vector(dipPoint.X - Left, dipPoint.Y - Top);

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

        public void ToggleModeFromMenu()
        {
            if (_isApplyingLoadedState)
                return;
            OverlayMode_Click(this, new RoutedEventArgs());
        }

        /// <summary>
        /// 메모 상태를 설정에 반영한다. 글자 입력·드래그처럼 잦은 변경은 지연 저장으로 모으고,
        /// 창 닫기·앱 종료처럼 마지막 기회인 경로만 <paramref name="immediate"/>로 즉시 저장한다.
        /// </summary>
        private void PersistState(bool immediate = false)
        {
            if (!_isStateReady || _isApplyingLoadedState)
                return;

            _settings ??= ResolveSharedSettings();
            if (_settings == null)
                return;

            string text = MemoTextBox?.Text ?? string.Empty;
            PersistBounds(_settings); // 위치는 베이스 경로와 같은 규칙으로 (즉시 저장 때 최신 자리가 함께 가도록)
            _settings.MemoOverlayText = text;
            _settings.MemoOverlayTextOnlyMode = _isOverlayMode;
            _settings.MemoOverlayFontSize = MemoTextBox?.FontSize ?? 20.0;
            _settings.MemoOverlayBold = MemoTextBox?.FontWeight == FontWeights.Bold;
            _settings.MemoOverlayItalic = MemoTextBox?.FontStyle == FontStyles.Italic;
            _settings.MemoOverlayColorKey = _selectedColorKey;
            _settings.MemoOverlayBackgroundOpacity = OpacitySlider?.Value ?? 0;
            if (immediate)
                ConfigService.Save(_settings);
            else
                ConfigService.SaveDeferred(_settings);
        }

        private static ChatSettings ResolveSharedSettings() => AppServices.Get<ChatSettings>();

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (MemoTextBox == null) return;
            MemoTextBox.FontSize = e.NewValue;
            MemoStyle_Changed();
        }

        private void BtnBold_Click(object sender, RoutedEventArgs e)
        {
            if (MemoTextBox == null) return;
            MemoTextBox.FontWeight = MemoTextBox.FontWeight == FontWeights.Bold ? FontWeights.Normal : FontWeights.Bold;
            MemoStyle_Changed();
        }

        private void BtnItalic_Click(object sender, RoutedEventArgs e)
        {
            if (MemoTextBox == null) return;
            MemoTextBox.FontStyle = MemoTextBox.FontStyle == FontStyles.Italic ? FontStyles.Normal : FontStyles.Italic;
            MemoStyle_Changed();
        }

        private void ColorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MemoTextBox == null || ColorCombo == null)
                return;

            _selectedColorKey = (ColorCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "White";
            ApplyColorByKey(_selectedColorKey);
            MemoStyle_Changed();
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

