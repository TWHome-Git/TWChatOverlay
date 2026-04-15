using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class MainWindow
    {
        private void ApplyHotKeys()
        {
            if (_hotKeyService == null) return;

            AppLogger.Info("Applying hotkey registrations.");

            _hotKeyService.Unregister(HotKeyService.EXIT_HOTKEY_ID);
            _hotKeyService.Unregister(HotKeyService.TOGGLE_OVERLAY_ID);
            _hotKeyService.Unregister(HotKeyService.TOGGLE_ADDON_ID);
            _hotKeyService.Unregister(HotKeyService.TOGGLE_ALWAYS_VISIBLE_ID);
            _hotKeyService.Unregister(HotKeyService.TOGGLE_DAILY_WEEKLY_CONTENT_ID);

            RegisterHotKeyOptional(HotKeyService.EXIT_HOTKEY_ID, _settings.ExitHotKey);
            RegisterHotKeyOptional(HotKeyService.TOGGLE_OVERLAY_ID, _settings.ToggleOverlayHotKey);
            RegisterHotKeyOptional(HotKeyService.TOGGLE_ADDON_ID, _settings.ToggleAddonHotKey);
            RegisterHotKeyOptional(HotKeyService.TOGGLE_ALWAYS_VISIBLE_ID, _settings.ToggleAlwaysVisibleHotKey);
            RegisterHotKeyOptional(HotKeyService.TOGGLE_DAILY_WEEKLY_CONTENT_ID, _settings.ToggleDailyWeeklyContentHotKey);

            _hotKeyService.Unregister(HotKeyService.TOGGLE_ETA_RANKING_ID);
            _hotKeyService.Unregister(HotKeyService.TOGGLE_COEFFICIENT_ID);
            _hotKeyService.Unregister(HotKeyService.TOGGLE_EQUIPMENTDB_ID);
            _hotKeyService.Unregister(HotKeyService.TOGGLE_ENCRYPT_ID);
            _hotKeyService.Unregister(HotKeyService.TOGGLE_SETTINGS_ID);

            RegisterHotKeyOptional(HotKeyService.TOGGLE_ETA_RANKING_ID, _settings.ToggleEtaRankingHotKey);
            RegisterHotKeyOptional(HotKeyService.TOGGLE_COEFFICIENT_ID, _settings.ToggleCoefficientHotKey);
            RegisterHotKeyOptional(HotKeyService.TOGGLE_EQUIPMENTDB_ID, _settings.ToggleEquipmentDbHotKey);
            RegisterHotKeyOptional(HotKeyService.TOGGLE_ENCRYPT_ID, _settings.ToggleEncryptHotKey);
            RegisterHotKeyOptional(HotKeyService.TOGGLE_SETTINGS_ID, _settings.ToggleSettingsHotKey);
        }

        private bool RegisterHotKeyOptional(int id, string? value)
        {
            if (_hotKeyService == null) return false;
            if (string.IsNullOrWhiteSpace(value)) return true;

            if (HotKeyService.TryParseHotKey(value, out uint modifiers, out uint vk))
            {
                return _hotKeyService.Register(id, modifiers, vk);
            }

            return false;
        }

        private void TriggerMenuButton(string buttonName)
        {
            foreach (Window win in Application.Current.Windows)
            {
                if (win is MenuWindow menu)
                {
                    menu.Dispatcher.Invoke(() =>
                    {
                        var button = menu.FindName(buttonName) as Button;
                        if (button == null)
                        {
                            AppLogger.Warn($"Menu button '{buttonName}' not found.");
                            return;
                        }

                        AppLogger.Debug($"Triggering menu button '{buttonName}' from hotkey.");
                        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    });
                    return;
                }
            }

            AppLogger.Warn($"Menu window not found while trying to trigger '{buttonName}'.");
        }

        private bool ShouldSuppressGlobalHotKeys()
        {
            foreach (Window window in Application.Current.Windows)
            {
                var settingsView = FindVisualChild<SettingsView>(window);
                if (settingsView?.IsHotkeyInteractionActive == true)
                {
                    return true;
                }
            }

            return false;
        }

        private static T? FindVisualChild<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null)
            {
                return null;
            }

            if (root is T match)
            {
                return match;
            }

            int childCount = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < childCount; i++)
            {
                T? child = FindVisualChild<T>(VisualTreeHelper.GetChild(root, i));
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }
    }
}
