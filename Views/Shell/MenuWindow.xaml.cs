using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class MenuWindow : Window
    {
        private Button? _activeSubmenuButton;
        private readonly System.Windows.Threading.DispatcherTimer _menuAutoHideTimer;
        private bool _isPinned; // 아이콘 클릭으로 고정 — 자동 접힘 없이 상시 표시
        private MainWindow? _subscribedMainWindow;
        private readonly TrayIconService _notifyIcon;
        private static ShoutReplayWindow? _shoutReplayWindow;
        private static MemoOverlayWindow? _memoWindow;

        public MenuWindow()
        {
            InitializeComponent();
            WindowFontService.Apply(this);
            _notifyIcon = CreateNotifyIcon();
            LocationChanged += MenuWindow_LocationChanged;

            // 채팅창 탭과 같은 방식: 마우스가 올라오면 펼치고, 벗어난 채 3초가 지나면 접는다
            _menuAutoHideTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _menuAutoHideTimer.Tick += (_, _) => CollapseMenuBody();
            MouseEnter += (_, _) => ExpandMenuBodyTemporarily();
            MouseMove += (_, _) => ExpandMenuBodyTemporarily();
            ExpandMenuBodyTemporarily();

            try
            {
                var settings = GetSharedSettings();
                if (settings.MenuWindowLeft.HasValue && settings.MenuWindowTop.HasValue)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = settings.MenuWindowLeft.Value;
                    Top = settings.MenuWindowTop.Value;
                }

                ApplyMenuOrientation(settings.MenuWindowHorizontal);
                ApplyPinned(settings.MenuWindowPinned, persist: false);
                settings.PropertyChanged += SharedSettings_PropertyChanged;
            }
            catch (Exception ex) { AppLogger.Warn("Failed to restore menu window position.", ex); }

            try
            {
                var main = Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
                if (main != null)
                {
                    _subscribedMainWindow = main;
                    try { main.OverlayVisibilityChanged += Main_OverlayVisibilityChanged; } catch { }
                    try { main.DailyWeeklyVisibilityChanged += Main_DailyWeeklyVisibilityChanged; } catch { }
                    try { main.ItemCalendarVisibilityChanged += Main_ItemCalendarVisibilityChanged; } catch { }
                    try { SetButtonActive(BtnChat, main.IsOverlayVisible); } catch { }
                    try { SetButtonActive(BtnDailyWeekly, main.IsDailyWeeklyVisible); } catch { }
                    try { SetButtonActive(BtnCalendar, main.IsItemCalendarVisible); } catch { }
                }
            }
            catch (Exception ex) { AppLogger.Warn("Failed to subscribe menu window to main window state.", ex); }

            UiLockService.UnlockChanged += OnUnlockChanged;
            TrayAllWindowsService.TrayStateChanged += OnTrayStateChanged;
            ApplyMinimizeHighlight(TrayAllWindowsService.IsTrayed);
            AppLogger.Info("Menu window initialized.");
        }

        private void MenuWindow_LocationChanged(object? sender, EventArgs e)
        {
            try
            {
                PersistMenuWindowPosition();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to persist menu window position after move.", ex);
            }
        }

        private void Main_OverlayVisibilityChanged(object? sender, bool isVisible)
        {
            try { SetButtonActive(BtnChat, isVisible); } catch { }
        }

        private void Main_DailyWeeklyVisibilityChanged(object? sender, bool isVisible)
        {
            try { SetButtonActive(BtnDailyWeekly, isVisible); } catch { }
        }

        private void Main_ItemCalendarVisibilityChanged(object? sender, bool isVisible)
        {
            try { SetButtonActive(BtnCalendar, isVisible); } catch { }
        }

        /// <summary>잠금 해제 상태에 맞춰 자물쇠 아이콘(잠김 E72E / 열림 E785)과 하이라이트를 갱신한다.</summary>
        private void OnUnlockChanged(bool unlocked)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    UnlockGlyph.Text = unlocked ? "" : "";
                    SetButtonActive(BtnUnlock, unlocked);
                });
            }
            catch { }
        }

        private void SharedSettings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Models.ChatSettings.MenuWindowHorizontal))
                return;

            try
            {
                Dispatcher.Invoke(() => ApplyMenuOrientation(GetSharedSettings().MenuWindowHorizontal));
            }
            catch { }
        }

        /// <summary>메뉴 바를 세로형/가로형으로 전환한다.</summary>
        private void ApplyMenuOrientation(bool horizontal)
        {
            if (RootPanel == null || MenuBody == null || ButtonsGrid == null) return;

            // 양축 SizeToContent는 WindowStyle=None+DPI 조합에서 크기를 잘못 계산하므로
            // 방향별로 한 축만 자동, 다른 축은 고정한다
            if (horizontal)
            {
                SizeToContent = SizeToContent.Width;
                Height = 44;
                Width = double.NaN;
            }
            else
            {
                SizeToContent = SizeToContent.Height;
                Width = 44;
                Height = double.NaN;
            }

            RootPanel.Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
            MenuBody.Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical;
            ButtonsGrid.Rows = horizontal ? 1 : 9;
            ButtonsGrid.Columns = horizontal ? 9 : 1;

            if (horizontal)
            {
                // 가로형: 최소화 버튼을 세로쓰기(최/소/화)로 좁고 길게
                MinimizeText.Text = "최\n소\n화";
                BtnMinimize.Width = 18;
                BtnMinimize.Height = double.NaN;
                BtnMinimize.HorizontalAlignment = HorizontalAlignment.Center;
                BtnMinimize.VerticalAlignment = VerticalAlignment.Stretch;
                BtnMinimize.Margin = new Thickness(2, 2, 4, 2);
            }
            else
            {
                MinimizeText.Text = "최소화";
                BtnMinimize.Width = double.NaN;
                BtnMinimize.Height = 18;
                BtnMinimize.HorizontalAlignment = HorizontalAlignment.Stretch;
                BtnMinimize.VerticalAlignment = VerticalAlignment.Center;
                BtnMinimize.Margin = new Thickness(2, 2, 2, 4);
            }
        }

        /// <summary>메뉴 몸통을 펼치고 자동 접힘 타이머를 다시 건다.</summary>
        private void ExpandMenuBodyTemporarily()
        {
            if (MenuBody == null) return;

            MenuBody.Visibility = Visibility.Visible;
            _menuAutoHideTimer.Stop();
            _menuAutoHideTimer.Start();
        }

        /// <summary>마우스가 벗어난 상태면 메뉴 몸통을 접는다. 아직 올라가 있으면 타이머만 연장.</summary>
        private void CollapseMenuBody()
        {
            if (_isPinned)
            {
                _menuAutoHideTimer.Stop();
                return;
            }

            if (IsMouseOver)
            {
                _menuAutoHideTimer.Stop();
                _menuAutoHideTimer.Start();
                return;
            }

            _menuAutoHideTimer.Stop();
            if (MenuBody != null)
                MenuBody.Visibility = Visibility.Collapsed;
        }

        /// <summary>접힌 상태에서도 아이콘을 잡고 끌면 메뉴 바를 옮길 수 있다.</summary>
        private void AppIcon_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            double startLeft = Left, startTop = Top;
            try
            {
                DragMove();
            }
            catch { }

            // 창이 움직이지 않았으면 드래그가 아니라 클릭 → 고정 토글
            bool moved = Math.Abs(Left - startLeft) > 0.5 || Math.Abs(Top - startTop) > 0.5;
            if (!moved)
                ApplyPinned(!_isPinned, persist: true);
        }

        /// <summary>고정 상태를 적용한다. 고정되면 아이콘 테두리가 강조되고 메뉴가 상시 표시된다.</summary>
        private void ApplyPinned(bool pinned, bool persist)
        {
            _isPinned = pinned;

            if (AppIconArea != null)
            {
                // 활성 메뉴 버튼(SetButtonActive)과 동일한 하이라이트: 테마 민트 2px 테두리만
                if (pinned)
                    AppIconArea.SetResourceReference(Border.BorderBrushProperty, "OverlayAccentBorderBrush");
                else
                    AppIconArea.BorderBrush = Brushes.Transparent;
            }

            if (pinned)
            {
                _menuAutoHideTimer.Stop();
                if (MenuBody != null)
                    MenuBody.Visibility = Visibility.Visible;
            }
            else
            {
                ExpandMenuBodyTemporarily(); // 해제 직후부터 자동 접힘 카운트 시작
            }

            if (!persist) return;
            try
            {
                var settings = GetSharedSettings();
                settings.MenuWindowPinned = pinned;
                ConfigService.SaveDeferred(settings);
            }
            catch (Exception ex) { AppLogger.Warn("Failed to persist menu pin state.", ex); }
        }

        protected override void OnClosed(System.EventArgs e)
        {
            try { _menuAutoHideTimer.Stop(); } catch { }
            try { GetSharedSettings().PropertyChanged -= SharedSettings_PropertyChanged; } catch { }
            try { TrayAllWindowsService.TrayStateChanged -= OnTrayStateChanged; } catch { }
            try
            {
                _notifyIcon.Dispose();
            }
            catch { }

            base.OnClosed(e);
            try
            {
                try
                {
                    UiLockService.UnlockChanged -= OnUnlockChanged;
                    if (_subscribedMainWindow != null)
                    {
                        _subscribedMainWindow.OverlayVisibilityChanged -= Main_OverlayVisibilityChanged;
                        _subscribedMainWindow.DailyWeeklyVisibilityChanged -= Main_DailyWeeklyVisibilityChanged;
                        _subscribedMainWindow.ItemCalendarVisibilityChanged -= Main_ItemCalendarVisibilityChanged;
                    }
                }
                catch { }
                PersistMenuWindowPosition();
            }
            catch { }
        }

        private static Models.ChatSettings GetSharedSettings()
        {
            try
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is MainWindow main && main.DataContext is Models.ChatSettings shared)
                        return shared;
                }
            }
            catch
            {
            }

            return ConfigService.Load();
        }

        private static MainWindow? GetMainWindow()
        {
            try
            {
                return Application.Current.Windows.OfType<MainWindow>().FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private void OpenChild_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn)
            {
                AppLogger.Warn("OpenChild_Click received a non-button sender. Falling back to submenu.");
                OpenSubMenuFallback();
                return;
            }

            AppLogger.Info($"Menu action requested: {btn.Name}.");

            switch (btn.Name)
            {
                case "BtnWebDb":
                    OpenTwPage();
                    break;
                case "BtnUnlock":
                    UiLockService.Toggle();
                    break;
                case "BtnChat":
                    OpenChat();
                    break;
                case "BtnDailyWeekly":
                    try
                    {
                        foreach (Window w in Application.Current.Windows)
                        {
                            if (w is MainWindow mainWindow)
                            {
                                try
                                {
                                    mainWindow.ToggleDailyWeeklyContentWindow();
                                }
                                catch { }
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        try { MessageBox.Show($"DailyWeekly 창을 열 수 없습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
                    }
                    break;
                case "BtnCalendar":
                    try
                    {
                        foreach (Window w in Application.Current.Windows)
                        {
                            if (w is MainWindow mainWindow)
                            {
                                try
                                {
                                    mainWindow.ToggleItemCalendarWindow();
                                }
                                catch { }
                                return;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        try { MessageBox.Show($"달력 창을 열 수 없습니다:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
                    }
                    break;
                case "BtnShoutReplay":
                    OpenShoutReplay();
                    break;
                case "BtnMemo":
                    OpenMemo();
                    break;
                case "BtnSettings":
                    OpenSettings();
                    break;
                case "BtnExit":
                    AppLogger.Warn("Exit requested from menu window.");
                    ChatWindowHub.BeginShutdown();
                    Application.Current.Shutdown();
                    break;
                default:
                    OpenSubMenuFallback();
                    break;
            }
        }

        private void OpenSubMenuFallback()
        {
            var child = new SubMenuWindow();
            child.Owner = (Window?)GetMainWindow() ?? this;
            child.Show();
            AppLogger.Info("Opened fallback submenu window.");
        }

        /// <summary>
        /// 에타 순위/장비 DB/계산기/시뮬레이터는 웹 홈페이지(TWPage)로 통합되었습니다.
        /// 기본 브라우저로 홈페이지를 엽니다.
        /// </summary>
        private static void OpenTwPage()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Services.RemoteEndpoints.TwPageUrl,
                    UseShellExecute = true
                });
                AppLogger.Info("Opened TWPage in default browser.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to open TWPage in browser.", ex);
                try { MessageBox.Show($"홈페이지를 열 수 없습니다:\n{Services.RemoteEndpoints.TwPageUrl}", "오류", MessageBoxButton.OK, MessageBoxImage.Error); } catch { }
            }
        }


        private void OpenChat()
        {
            try
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is MainWindow main)
                    {
                        try { main.ToggleOverlayVisibility(); }
                        catch (Exception ex) { AppLogger.Warn("Failed to toggle chat overlay from menu.", ex); }
                        return;
                    }
                }
            }
            catch (Exception ex) { AppLogger.Warn("Failed to find main window for chat toggle.", ex); }

            var chat = new ChatView();
            ShowAddonViewWindow(chat, "Chat", BtnChat);
        }


        private void OpenShoutReplay()
        {
            if (_shoutReplayWindow != null && _shoutReplayWindow.IsLoaded && _shoutReplayWindow.IsVisible)
            {
                _shoutReplayWindow.Close();
                return;
            }

            if (_shoutReplayWindow == null || !_shoutReplayWindow.IsLoaded)
            {
                _shoutReplayWindow = new ShoutReplayWindow(GetSharedSettings())
                {
                    Owner = (Window?)GetMainWindow() ?? this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                _shoutReplayWindow.Closed += (_, _) =>
                {
                    _shoutReplayWindow = null;
                    SetButtonActive(BtnShoutReplay, false);
                };
            }
            _shoutReplayWindow.Show();
            _shoutReplayWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                _shoutReplayWindow.Activate();
                _shoutReplayWindow.Focus();
                SetButtonActive(BtnShoutReplay, true);
            }), System.Windows.Threading.DispatcherPriority.Input);
        }

        private void OpenMemo()
        {
            if (_memoWindow == null || !_memoWindow.IsLoaded)
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is MemoOverlayWindow existingMemo && existingMemo.IsLoaded)
                    {
                        _memoWindow = existingMemo;
                        _memoWindow.EditorModeChanged -= MemoWindow_EditorModeChanged;
                        _memoWindow.EditorModeChanged += MemoWindow_EditorModeChanged;
                        _memoWindow.IsVisibleChanged -= MemoWindow_IsVisibleChanged;
                        _memoWindow.IsVisibleChanged += MemoWindow_IsVisibleChanged;
                        break;
                    }
                }
            }

            if (_memoWindow == null || !_memoWindow.IsLoaded)
            {
                _memoWindow = new MemoOverlayWindow(GetSharedSettings())
                {
                    Owner = (Window?)GetMainWindow() ?? this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                _memoWindow.EditorModeChanged += MemoWindow_EditorModeChanged;
                _memoWindow.IsVisibleChanged += MemoWindow_IsVisibleChanged;
                _memoWindow.Closed += (_, _) =>
                {
                    _memoWindow = null;
                    SetButtonActive(BtnMemo, false);
                };
                _memoWindow.Show();
                _memoWindow.Activate();
                SetButtonActive(BtnMemo, _memoWindow.IsEditorModeVisible);
                return;
            }

            if (_memoWindow.IsOverlayMode)
            {
                _memoWindow.ShowEditorMode();
            }
            else
            {
                _memoWindow.ToggleModeFromMenu();
            }
            SetButtonActive(BtnMemo, _memoWindow.IsEditorModeVisible);
        }

        private void MemoWindow_EditorModeChanged(object? sender, EventArgs e)
        {
            if (sender is MemoOverlayWindow memo)
                SetButtonActive(BtnMemo, memo.IsEditorModeVisible);
        }

        private void MemoWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is MemoOverlayWindow memo)
                SetButtonActive(BtnMemo, memo.IsEditorModeVisible);
        }

        /// <summary>잠금 해제 종료 후 설정 복귀 등 외부에서 설정 창을 열 때 사용.</summary>
        internal void OpenSettingsFromExternal() => OpenSettings();

        private void OpenSettings()
        {
            try
            {
                SubMenuWindow? existingHost = FindSubMenuHost();
                if (existingHost != null && existingHost.IsVisible && string.Equals(existingHost.Title, "설정", StringComparison.Ordinal))
                {
                    SetMainSettingsPositionMode(false);
                    SetMainAddonPositionMode(false);
                    existingHost.Close();
                    return;
                }

                var settingsView = new SettingsView();
                try
                {
                    foreach (Window win2 in Application.Current.Windows)
                    {
                        if (win2 is MainWindow main)
                        {
                            settingsView.DataContext = main.SettingsViewModelInstance;
                            settingsView.OnlyChatMode = false;
                            break;
                        }
                    }
                }
                catch { }

                SubMenuWindow? host = existingHost;

                if (host == null)
                {
                    host = new SubMenuWindow();
                    host.Owner = (Window?)GetMainWindow() ?? this;
                    host.Show();
                }

                host.Show();
                host.ShowHostContent(settingsView, "설정");
                // 설정 콘텐츠 폭(내비 168 + 본문 620 + 여백/스크롤바)에 맞춰 잘림 없이 폭 고정
                host.Width = 892;
                SetMainAddonPositionMode(false);
                SetMainSettingsPositionMode(false);

                try { if (_activeSubmenuButton != null) SetButtonActive(_activeSubmenuButton, false); } catch { }
                SetButtonActive(BtnSettings, true);
                _activeSubmenuButton = BtnSettings;

                AttachHostClosedHandler(host);

                try
                {
                    if (settingsView is SettingsView sv)
                        sv.SetCompactMode(false);
                }
                catch { }
            }
            catch
            {
                foreach (Window win in Application.Current.Windows)
                {
                    if (win is MainWindow main)
                    {
                        break;
                    }
                }
            }
        }

        private void ShowAddonViewWindow(FrameworkElement content, string title, Button? buttonToActivate)
        {
            SubMenuWindow? host = FindSubMenuHost();

            if (host != null && host.IsVisible && string.Equals(host.Title, title, StringComparison.Ordinal))
            {
                AppLogger.Info($"Closing hosted view '{title}' from repeated command.");
                host.Close();
                return;
            }

            if (host == null)
            {
                host = new SubMenuWindow();
                host.Owner = (Window?)GetMainWindow() ?? this;
                host.Show();
            }

            try
            {
                host.Show();
                host.ShowHostContent(content, title);
                host.Width = 1100; // 설정(892)에서 재사용될 수 있으므로 기본 폭 복원
                bool isAddonSettingsView = false; // 추가 기능은 설정 창으로 통합됨
                SetMainAddonPositionMode(isAddonSettingsView);
                SetMainSettingsPositionMode(false);

                try { if (_activeSubmenuButton != null) SetButtonActive(_activeSubmenuButton, false); } catch { }

                if (buttonToActivate != null)
                {
                    SetButtonActive(buttonToActivate, true);
                    _activeSubmenuButton = buttonToActivate;
                }

                AttachHostClosedHandler(host);
            }
            catch
            {
                AppLogger.Warn($"Primary host window unavailable for '{title}'. Falling back to standalone window.");
                var window = new Window()
                {
                    Title = title,
                    Content = content,
                    Width = 560,
                    Height = 420,
                    Owner = (Window?)GetMainWindow() ?? this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                WindowFontService.Apply(window);
                WindowFontService.Apply(content);
                window.Show();
            }
        }

        private SubMenuWindow? FindSubMenuHost()
        {
            foreach (Window w in Application.Current.Windows)
            {
                if (w is SubMenuWindow s)
                {
                    return s;
                }
            }

            return null;
        }

        private void AttachHostClosedHandler(SubMenuWindow host)
        {
            host.Closed -= Host_Closed;
            host.Closed += Host_Closed;
        }

        private void Host_Closed(object? sender, EventArgs e)
        {
            try { SetMainSettingsPositionMode(false); } catch { }
            try { SetMainAddonPositionMode(false); } catch { }
            try { if (_activeSubmenuButton != null) SetButtonActive(_activeSubmenuButton, false); } catch { }
            _activeSubmenuButton = null;
        }

        private static void SetMainSettingsPositionMode(bool isEnabled)
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow main)
                {
                    main.SetSettingsPositionMode(isEnabled);
                    return;
                }
            }
        }

        private static void SetMainAddonPositionMode(bool isEnabled)
        {
            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow main)
                {
                    main.SetAddonPositionMode(isEnabled);
                    return;
                }
            }
        }

        private void SetButtonActive(Button btn, bool active)
        {
            if (btn == null) return;
            try
            {
                // 고정된 앱 아이콘과 같은 하이라이트 (테마 민트 2px)
                btn.BorderThickness = active ? new Thickness(2) : new Thickness(0);
                if (active)
                    btn.SetResourceReference(Control.BorderBrushProperty, "OverlayAccentBorderBrush");
                else
                    btn.BorderBrush = Brushes.Transparent;
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private TrayIconService CreateNotifyIcon()
        {
            // WinForms NotifyIcon 대신 Shell_NotifyIcon 직접 구현 (WinForms 어셈블리 로드 제거)
            var tray = new TrayIconService("TWChatOverlay", new[]
            {
                new TrayIconService.MenuItem("열기", RestoreFromTray),
                new TrayIconService.MenuItem("모든 창 숨기기", TrayAllWindowsService.HideAll),
                new TrayIconService.MenuItem("종료", () =>
                {
                    ChatWindowHub.BeginShutdown();
                    Application.Current.Shutdown();
                }),
            });
            tray.DoubleClick += RestoreFromTray;
            return tray;
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            // 이미 최소화 상태면 한 번 더 눌러 이전 상태로 복원
            if (TrayAllWindowsService.IsTrayed)
                RestoreFromTray();
            else
                MinimizeToTray();
        }

        private void OnTrayStateChanged(bool trayed)
        {
            try { Dispatcher.Invoke(() => ApplyMinimizeHighlight(trayed)); } catch { }
        }

        /// <summary>최소화 상태에서는 [최소화] 버튼을 활성 버튼과 같은 민트 테두리로 강조하고 복원 안내로 바꾼다.</summary>
        private void ApplyMinimizeHighlight(bool trayed)
        {
            if (BtnMinimize == null) return;

            if (trayed)
            {
                BtnMinimize.SetResourceReference(Control.BorderBrushProperty, "OverlayAccentBorderBrush");
                BtnMinimize.BorderThickness = new Thickness(2);
                BtnMinimize.ToolTip = "최소화 상태 — 한 번 더 누르면 모든 창 복원";
            }
            else
            {
                BtnMinimize.SetResourceReference(Control.BorderBrushProperty, "ControlBorderBrush");
                BtnMinimize.BorderThickness = new Thickness(1);
                BtnMinimize.ToolTip = "모든 창 트레이로 최소화";
            }
        }

        /// <summary>메뉴 바를 제외한 모든 창을 트레이로 숨깁니다.</summary>
        private void MinimizeToTray()
        {
            PersistMenuWindowPosition();
            TrayAllWindowsService.HideAll();
        }

        /// <summary>트레이에서 모든 창을 복원합니다. (트레이 아이콘 더블클릭 / '열기' 메뉴)</summary>
        private void RestoreFromTray()
        {
            Dispatcher.Invoke(() =>
            {
                if (TrayAllWindowsService.IsTrayed)
                {
                    TrayAllWindowsService.RestoreAll();
                }

                if (!IsVisible)
                {
                    Show();
                }

                WindowState = WindowState.Normal;
            });
        }

        private void PersistMenuWindowPosition()
        {
            try
            {
                foreach (Window w in Application.Current.Windows)
                {
                    if (w is MainWindow main && main.DataContext is Models.ChatSettings appSettings)
                    {
                        appSettings.MenuWindowLeft = Left;
                        appSettings.MenuWindowTop = Top;
                        ConfigService.SaveDeferred(appSettings);
                        return;
                    }
                }
            }
            catch
            {
            }

            var settings = GetSharedSettings();
            settings.MenuWindowLeft = Left;
            settings.MenuWindowTop = Top;
            ConfigService.SaveDeferred(settings);
        }
    }
}
