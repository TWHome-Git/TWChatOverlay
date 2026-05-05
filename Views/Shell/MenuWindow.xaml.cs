using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TWChatOverlay.Services;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TWChatOverlay.Views
{
    public partial class MenuWindow : Window
    {
        private Button? _activeSubmenuButton;
        private MainWindow? _subscribedMainWindow;
        private readonly Forms.NotifyIcon _notifyIcon;
        private static ShoutReplayWindow? _shoutReplayWindow;
        private static MemoOverlayWindow? _memoWindow;
        private bool _memoFirstClickForceEditorPending = true;

        public MenuWindow()
        {
            InitializeComponent();
            WindowFontService.Apply(this);
            _notifyIcon = CreateNotifyIcon();
            LocationChanged += MenuWindow_LocationChanged;

            try
            {
                var settings = GetSharedSettings();
                if (settings.MenuWindowLeft.HasValue && settings.MenuWindowTop.HasValue)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = settings.MenuWindowLeft.Value;
                    Top = settings.MenuWindowTop.Value;
                }
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

        private void DragArea_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                DragMove();
            }
            catch (Exception ex) { AppLogger.Warn("Failed to persist menu window position.", ex); }
        }

        protected override void OnClosed(System.EventArgs e)
        {
            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch { }

            base.OnClosed(e);
            try
            {
                try
                {
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
                case "BtnEtaRanking":
                    OpenEtaRanking();
                    break;
                case "BtnChat":
                    OpenChat();
                    break;
                case "BtnCoefficient":
                    OpenCoefficientCalculator();
                    break;
                case "BtnDamageCalc":
                    OpenDamageCalculator();
                    break;
                case "BtnEquipmentDb":
                    OpenEquipmentDb();
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
                case "BtnAddon":
                    OpenAddon();
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
                case "BtnEncrypt":
                    OpenEncryptSimulator();
                    break;
                case "BtnExit":
                    AppLogger.Warn("Exit requested from menu window.");
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

        private void OpenEtaRanking()
        {
            var view = new Addons.EtaRankingView();
            ShowAddonViewWindow(view, "에타 순위", BtnEtaRanking);
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

        private void OpenCoefficientCalculator()
        {
            var view = new Addons.CoefficientCalculatorView();
            ShowAddonViewWindow(view, "계수 계산기", BtnCoefficient);
        }

        private void OpenDamageCalculator()
        {
            var view = new Addons.DamageCalculatorView();
            ShowAddonViewWindow(view, "Damage Calculator", null);
        }

        private void OpenEquipmentDb()
        {
            var view = new Addons.EquipmentDbView();
            ShowAddonViewWindow(view, "장비 DB", BtnEquipmentDb);
        }

        private void OpenEncryptSimulator()
        {
            var view = new Addons.EncryptSimulatorTabsView();
            ShowAddonViewWindow(view, "시뮬레이터", BtnEncrypt);
        }

        private void OpenAddon()
        {
            var view = new AddonView();
            ShowAddonViewWindow(view, "추가 기능", BtnAddon);
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

            if (_memoFirstClickForceEditorPending)
            {
                _memoFirstClickForceEditorPending = false;
                _memoWindow.ShowEditorMode();
                SetButtonActive(BtnMemo, true);
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

        private void OpenSettings()
        {
            try
            {
                SubMenuWindow? existingHost = FindSubMenuHost();
                if (existingHost != null && existingHost.IsVisible && string.Equals(existingHost.Title, "설정", StringComparison.Ordinal))
                {
                    SetMainSettingsPositionMode(false);
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
                SetMainSettingsPositionMode(true);

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

        private void SetButtonActive(Button btn, bool active)
        {
            if (btn == null) return;
            try
            {
                btn.BorderThickness = active ? new Thickness(2) : new Thickness(0);
                btn.BorderBrush = active ? Brushes.Cyan : Brushes.Transparent;
            }
            catch { }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private Forms.NotifyIcon CreateNotifyIcon()
        {
            var menu = new Forms.ContextMenuStrip();
            menu.Items.Add("열기", null, (_, _) => RestoreFromTray());
            menu.Items.Add("종료", null, (_, _) => Application.Current.Shutdown());

            Drawing.Icon trayIcon;
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                trayIcon = !string.IsNullOrWhiteSpace(exePath)
                    ? Drawing.Icon.ExtractAssociatedIcon(exePath) ?? Drawing.SystemIcons.Application
                    : Drawing.SystemIcons.Application;
            }
            catch
            {
                trayIcon = Drawing.SystemIcons.Application;
            }

            var notifyIcon = new Forms.NotifyIcon
            {
                Text = "TWChatOverlay",
                Visible = true,
                Icon = trayIcon,
                ContextMenuStrip = menu
            };

            notifyIcon.DoubleClick += (_, _) => RestoreFromTray();
            return notifyIcon;
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            MinimizeToTray();
        }

        private void MinimizeToTray()
        {
            PersistMenuWindowPosition();
            Hide();
        }

        private void RestoreFromTray()
        {
            Dispatcher.Invoke(() =>
            {
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
