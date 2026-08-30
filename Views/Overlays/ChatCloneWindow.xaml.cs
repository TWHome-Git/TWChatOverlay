using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
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
        private bool _isResizingWindow;
        private readonly DispatcherTimer _tabAutoHideTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };

        public event PropertyChangedEventHandler? PropertyChanged;

        public int Slot => _slot;

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

            return TryCreateAndShow(settings, null);
        }

        public static bool TryRestore(ChatSettings settings, int slot)
        {
            if (slot < 1 || slot > 2)
                return false;

            return TryCreateAndShow(settings, slot);
        }

        private static bool TryCreateAndShow(ChatSettings settings, int? preferredSlot)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));

            ChatCloneWindow? window = null;
            try
            {
                window = new ChatCloneWindow(settings, preferredSlot);
                window.Show();
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to open chat clone window.", ex);
                try { window?.Close(); } catch { }
                return false;
            }
        }

        public ChatCloneWindow(ChatSettings settings, int? preferredSlot = null)
        {
            InitializeComponent();
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));

            int? slot = ChatWindowHub.RegisterClone(preferredSlot);
            if (slot == null)
            {
                if (preferredSlot.HasValue)
                    throw new InvalidOperationException($"Chat window slot {preferredSlot.Value} is not available.");

                throw new InvalidOperationException("No chat window slot is available.");
            }

            _slot = slot.Value;

            DataContext = _settings;
            Closed += ChatCloneWindow_Closed;
            LocationChanged += (_, _) => HandleLocationChanged();
            _settings.PropertyChanged += Settings_PropertyChanged;
            ChatWindowHub.BuffersChanged += ChatWindowHub_BuffersChanged;
            IdTagService.IdTagsChanged += IdTagService_Changed;
            BlacklistService.BlacklistChanged += IdTagService_Changed;
            AttachToMainWindow();
            Activated += (_, _) => Dispatcher.BeginInvoke(new Action(EnsureCloneTopmost), DispatcherPriority.Background);
            Deactivated += (_, _) => Dispatcher.BeginInvoke(new Action(EnsureCloneTopmost), DispatcherPriority.Background);
            StateChanged += (_, _) => Dispatcher.BeginInvoke(new Action(EnsureCloneTopmost), DispatcherPriority.Background);
            IsVisibleChanged += (_, _) => Dispatcher.BeginInvoke(new Action(EnsureCloneTopmost), DispatcherPriority.Background);
            _tabAutoHideTimer.Tick += (_, _) => HideCloneTabs();

            _currentTabTag = NormalizeTabTag(GetStoredTabTag());

            UiLockService.ApplyStoredOpacity(this);
            ApplySizeFromMainWindow();
            ApplyDefaultFontSettings();
            ApplyEffectiveFont();
            ApplyStoredPosition();
            ApplyDefaultPositionIfNeeded();
            ApplyTabState(_currentTabTag, persistSettings: false, refreshLogDisplay: false);
        }

        private void ChatCloneWindow_Closed(object? sender, EventArgs e)
        {
            try
            {
                if (!ChatWindowHub.IsShuttingDown)
                    SaveOpenStateToSettings(false);
            }
            catch
            {
            }

            try
            {
                SaveSizeToSettings();
            }
            catch
            {
            }

            try
            {
                SavePositionToSettings();
            }
            catch
            {
            }

            try
            {
                SaveTabStateToSettings();
                ConfigService.Save(_settings);
            }
            catch
            {
            }

            ChatWindowHub.BuffersChanged -= ChatWindowHub_BuffersChanged;
            IdTagService.IdTagsChanged -= IdTagService_Changed;
            BlacklistService.BlacklistChanged -= IdTagService_Changed;
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

            _mainWindow.StateChanged += MainWindow_StateChanged;
            _mainWindow.IsVisibleChanged += MainWindow_IsVisibleChanged;
            _mainWindow.Activated += MainWindow_ActivatedOrDeactivated;
            _mainWindow.Deactivated += MainWindow_ActivatedOrDeactivated;
        }

        private void DetachFromMainWindow()
        {
            if (_mainWindow == null)
                return;

            _mainWindow.StateChanged -= MainWindow_StateChanged;
            _mainWindow.IsVisibleChanged -= MainWindow_IsVisibleChanged;
            _mainWindow.Activated -= MainWindow_ActivatedOrDeactivated;
            _mainWindow.Deactivated -= MainWindow_ActivatedOrDeactivated;
            _mainWindow = null;
        }

        private void MainWindow_ActivatedOrDeactivated(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(SyncVisibilityWithMainWindow), DispatcherPriority.Background);
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

            bool wasVisible = IsVisible;
            ApplySizeFromMainWindow();

            bool shouldBeVisible =
                _mainWindow.IsOverlayVisible &&
                _mainWindow.WindowState != WindowState.Minimized &&
                _mainWindow.Visibility == Visibility.Visible;

            if (shouldBeVisible)
            {
                if (!wasVisible)
                {
                    Show();
                    ShowCloneTabsTemporarily();
                    RefreshLogDisplay();
                }
            }
            else if (IsVisible)
            {
                Hide();
            }

            bool shouldTopmost = _mainWindow.Topmost;
            Topmost = shouldTopmost;
            if (shouldTopmost)
                EnsureCloneTopmost();
        }

        internal void RefreshVisibilityFromMainWindow()
        {
            SyncVisibilityWithMainWindow();
        }

        private void HandleLocationChanged()
        {
            if (_isResizingWindow)
                return;

            SyncPositionToSettings();
        }

        private void ChatWindowHub_BuffersChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() => RefreshLogDisplay()), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>아이디 태그/블랙리스트 파일이 바뀌면 표기 접미사가 달라지므로 다시 그린다.</summary>
        private void IdTagService_Changed()
        {
            Dispatcher.BeginInvoke(new Action(() => RefreshLogDisplay(forceRebuild: true)), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 빈 이름 = 설정 전체 교체(프로필/초기화/마법사) — 폰트·탭·문서를 모두 다시 맞춘다
            if (string.IsNullOrEmpty(e.PropertyName))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnPropertyChanged(nameof(FollowMainFont));
                    OnPropertyChanged(nameof(SelectedFontFamily));
                    OnPropertyChanged(nameof(SelectedFontSize));
                    string normalizedTag = NormalizeTabTag(GetStoredTabTag());
                    if (!string.Equals(_currentTabTag, normalizedTag, StringComparison.Ordinal))
                        ApplyTabState(normalizedTag, persistSettings: false, refreshLogDisplay: false);
                    ApplyEffectiveFont(); // 초기화 후엔 RefreshLogDisplay(forceRebuild)까지 수행
                }), System.Windows.Threading.DispatcherPriority.Background);
                return;
            }

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
            else if (e.PropertyName == GetTabTagSettingPropertyName())
            {
                string normalizedTabTag = NormalizeTabTag(GetStoredTabTag());
                if (!string.Equals(_currentTabTag, normalizedTabTag, StringComparison.Ordinal))
                {
                    Dispatcher.BeginInvoke(new Action(() => ApplyTabState(normalizedTabTag, persistSettings: false, refreshLogDisplay: false)),
                        System.Windows.Threading.DispatcherPriority.Background);
                }
            }
            else if (e.PropertyName != null && e.PropertyName.StartsWith("Show", StringComparison.Ordinal))
            {
                Dispatcher.BeginInvoke(new Action(() => RefreshLogDisplay(forceRebuild: true)), System.Windows.Threading.DispatcherPriority.Background);
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
                RefreshLogDisplay(forceRebuild: true);
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

        // 증분 렌더 상태: 마지막으로 그린 시점의 버퍼 버전
        private long _renderedTotalAppended;
        private int _renderedGeneration = -1;
        private string? _renderedTabTag;

        /// <summary>
        /// 평상시(버퍼에 줄 추가)에는 새 줄만 덧붙이고, 탭/폰트/필터 변경이나
        /// 버퍼 전체 교체 시에만 문서를 전체 재구축한다.
        /// </summary>
        private void RefreshLogDisplay(bool forceRebuild = false)
        {
            var logDisplay = ChatDisplay.LogDisplayControl;
            if (logDisplay == null)
                return;

            var store = ChatWindowHub.SharedLogBuffers;
            bool canAppend = !forceRebuild
                && string.Equals(_renderedTabTag, _currentTabTag, StringComparison.Ordinal)
                && _renderedGeneration >= 0;

            IReadOnlyList<LogParser.ParseResult>? delta = null;
            long totalAppended;
            int generation;
            if (canAppend)
            {
                delta = store.GetLogsAppendedAfter(
                    _currentTabTag, _renderedTotalAppended, _renderedGeneration,
                    out totalAppended, out generation);
                if (delta != null && delta.Count == 0)
                {
                    // 새 줄 없음 — 그릴 것도 없다
                    _renderedTotalAppended = totalAppended;
                    return;
                }
            }
            else
            {
                (totalAppended, generation) = store.GetVersion(_currentTabTag);
            }

            bool shouldAutoScroll = ChatDisplay.IsAutoScrollEnabled;
            logDisplay.BeginChange();
            try
            {
                if (delta != null)
                {
                    foreach (var log in delta)
                    {
                        _renderer.AddLog(logDisplay.Document, log, _settings, CurrentFont, CurrentFontSize, false, false);
                    }

                    // 버퍼 상한만큼만 유지 — 오래된 문단은 앞에서 제거 (Count는 O(n)이라 한 번만 센다)
                    var blocks = logDisplay.Document.Blocks;
                    int maxBlocks = store.MaxCountPerTab;
                    int blockCount = blocks.Count;
                    while (blockCount > maxBlocks && blocks.FirstBlock != null)
                    {
                        blocks.Remove(blocks.FirstBlock);
                        blockCount--;
                    }
                }
                else
                {
                    (totalAppended, generation) = store.GetVersion(_currentTabTag);
                    logDisplay.Document.Blocks.Clear();
                    foreach (var log in store.GetLogs(_currentTabTag))
                    {
                        _renderer.AddLog(logDisplay.Document, log, _settings, CurrentFont, CurrentFontSize, false, false);
                    }
                }
            }
            finally
            {
                logDisplay.EndChange();
                if (shouldAutoScroll)
                    logDisplay.ScrollToEnd();
            }

            _renderedTotalAppended = totalAppended;
            _renderedGeneration = generation;
            _renderedTabTag = _currentTabTag;
        }

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton btn || btn.Tag == null)
                return;

            string tabTag = btn.Tag.ToString() ?? string.Empty;
            ApplyTabState(tabTag);
        }

        private void ApplyTabState(string tabTag, bool persistSettings = true, bool refreshLogDisplay = true)
        {
            string normalizedTabTag = NormalizeTabTag(tabTag);
            _currentTabTag = normalizedTabTag;

            if (persistSettings)
                SaveTabStateToSettings(normalizedTabTag);

            UpdateTabSelection(normalizedTabTag);

            if (refreshLogDisplay)
                RefreshLogDisplay();
        }

        private void UpdateTabSelection(string tabTag)
        {
            if (MainTabPanel == null)
                return;

            foreach (var radioButton in FindVisualDescendants<RadioButton>(MainTabPanel))
            {
                bool isSelected = string.Equals(radioButton.Tag?.ToString(), tabTag, StringComparison.Ordinal);
                if (radioButton.IsChecked != isSelected)
                    radioButton.IsChecked = isSelected;
            }
        }

        private string GetStoredTabTag()
            => _slot == 1 ? _settings.ChatCloneWindow1TabTag : _settings.ChatCloneWindow2TabTag;

        private string GetTabTagSettingPropertyName()
            => _slot == 1 ? nameof(ChatSettings.ChatCloneWindow1TabTag) : nameof(ChatSettings.ChatCloneWindow2TabTag);

        private void SaveTabStateToSettings(string? tabTag = null)
        {
            string normalized = NormalizeTabTag(tabTag ?? _currentTabTag);

            if (_slot == 1)
                _settings.ChatCloneWindow1TabTag = normalized;
            else
                _settings.ChatCloneWindow2TabTag = normalized;

            ConfigService.SaveDeferred(_settings);
        }

        private void SaveOpenStateToSettings(bool isOpen)
        {
            if (_slot == 1)
                _settings.ChatCloneWindow1IsOpen = isOpen;
            else
                _settings.ChatCloneWindow2IsOpen = isOpen;

            ConfigService.SaveDeferred(_settings);
        }

        private static string NormalizeTabTag(string? tabTag)
        {
            if (string.Equals(tabTag, "General", StringComparison.OrdinalIgnoreCase))
                return "General";
            if (string.Equals(tabTag, "Team", StringComparison.OrdinalIgnoreCase))
                return "Team";
            if (string.Equals(tabTag, "Club", StringComparison.OrdinalIgnoreCase))
                return "Club";
            if (string.Equals(tabTag, "Shout", StringComparison.OrdinalIgnoreCase))
                return "Shout";
            if (string.Equals(tabTag, "System", StringComparison.OrdinalIgnoreCase))
                return "System";

            return "General";
        }

        private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null)
                yield break;

            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                    yield return match;

                foreach (T descendant in FindVisualDescendants<T>(child))
                    yield return descendant;
            }
        }

        private void CloseChat_Click(object sender, RoutedEventArgs e)
        {
            if (!ConfirmDialogWindow.Confirm(this, "서브 채팅창을 닫을까요?", "닫기", "취소"))
                return;

            Close();
        }

        /// <summary>잠금 해제 모드에서는 창의 아무 곳이나 잡고 드래그해 이동할 수 있다.</summary>
        private void UnlockDrag_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!UiLockService.IsUnlocked) return;
            UiLockService.Select(this);
            if (e.ButtonState != MouseButtonState.Pressed) return;

            try
            {
                DragMove();
            }
            catch { }
            finally
            {
                ChatWindowHub.TryApplyMagneticSnap(this);
                SyncPositionToSettings();
            }
            e.Handled = true;
        }

        private void TopResize_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (Height - e.VerticalChange >= MinHeight)
            {
                Height -= e.VerticalChange;
                Top += e.VerticalChange;
                SaveSizeToSettings();
                SyncPositionToSettings();
            }
        }

        private void LeftResize_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (Width - e.HorizontalChange >= MinWidth)
            {
                Width -= e.HorizontalChange;
                Left += e.HorizontalChange;
                SaveSizeToSettings();
                SyncPositionToSettings();
            }
        }

        private void RightResize_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            if (Width + e.HorizontalChange >= MinWidth)
            {
                Width += e.HorizontalChange;
                SaveSizeToSettings();
                SyncPositionToSettings();
            }
        }

        private void ResizeThumb_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isResizingWindow = true;
        }

        private void ResizeThumb_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _isResizingWindow = false;
            ChatWindowHub.TryApplyMagneticSnap(this);
            SyncPositionToSettings();
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
            if (!UiLockService.IsUnlocked)
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
            if (!UiLockService.IsUnlocked)
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
            SaveOpenStateToSettings(true);
            ApplySizeFromMainWindow();
            ApplyTabState(_currentTabTag, persistSettings: false, refreshLogDisplay: false);
            RefreshLogDisplay();
            SyncVisibilityWithMainWindow();
            ShowCloneTabsTemporarily();
        }

        private void MainBorder_MouseEnter(object sender, MouseEventArgs e)
            => ShowCloneTabsTemporarily();

        private void MainBorder_MouseMove(object sender, MouseEventArgs e)
            => ShowCloneTabsTemporarily();

        private void ShowCloneTabsTemporarily()
        {
            if (MainTabBackground == null || MainTabPanel == null)
                return;

            MainTabBackground.Visibility = Visibility.Visible;
            MainTabPanel.Visibility = Visibility.Visible;
            _tabAutoHideTimer.Stop();
            _tabAutoHideTimer.Start();
        }

        private void HideCloneTabs()
        {
            if (MainBorder?.IsMouseOver == true)
            {
                _tabAutoHideTimer.Stop();
                _tabAutoHideTimer.Start();
                return;
            }

            _tabAutoHideTimer.Stop();
            if (MainTabBackground == null || MainTabPanel == null)
                return;

            MainTabBackground.Visibility = Visibility.Collapsed;
            MainTabPanel.Visibility = Visibility.Collapsed;
        }

        private void ApplySizeFromMainWindow()
        {
            MainWindow? mainWindow = _mainWindow ?? Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            double? storedWidth = _slot == 1 ? _settings.ChatCloneWindow1Width : _settings.ChatCloneWindow2Width;
            double? storedHeight = _slot == 1 ? _settings.ChatCloneWindow1Height : _settings.ChatCloneWindow2Height;

            if (storedWidth.HasValue && storedWidth.Value > 0)
                Width = Math.Max(MinWidth, storedWidth.Value);
            else if (mainWindow != null && mainWindow.Width > 0)
                Width = Math.Max(MinWidth, mainWindow.Width);

            if (storedHeight.HasValue && storedHeight.Value > 0)
                Height = Math.Max(MinHeight, storedHeight.Value);
            else if (mainWindow != null && mainWindow.Height > 0)
                Height = Math.Max(MinHeight, mainWindow.Height);
        }

        private void SaveSizeToSettings()
        {
            double width = Math.Max(MinWidth, GetCurrentWindowWidth());
            double height = Math.Max(MinHeight, GetCurrentWindowHeight());

            if (width <= 0 || height <= 0)
                return;

            if (_slot == 1)
            {
                _settings.ChatCloneWindow1Width = width;
                _settings.ChatCloneWindow1Height = height;
            }
            else if (_slot == 2)
            {
                _settings.ChatCloneWindow2Width = width;
                _settings.ChatCloneWindow2Height = height;
            }

            ConfigService.SaveDeferred(_settings);
        }

        private double GetCurrentWindowWidth()
        {
            if (!double.IsNaN(Width) && Width > 0)
                return Width;

            if (ActualWidth > 0)
                return ActualWidth;

            return 0;
        }

        private double GetCurrentWindowHeight()
        {
            if (!double.IsNaN(Height) && Height > 0)
                return Height;

            if (ActualHeight > 0)
                return ActualHeight;

            return 0;
        }

        private void EnsureCloneTopmost()
        {
            if (!IsVisible || _mainWindow == null || !_mainWindow.IsVisible || _mainWindow.WindowState == WindowState.Minimized)
                return;

            try
            {
                if (!_mainWindow.Topmost)
                    return;

                TopmostWindowHelper.EnsureTopmost(this);
            }
            catch
            {
            }
        }
    }
}
