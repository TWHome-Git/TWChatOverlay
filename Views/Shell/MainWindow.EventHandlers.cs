using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.Services.LogAnalysis;
using TWChatOverlay.ViewModels;

namespace TWChatOverlay.Views
{
    public partial class MainWindow
    {
        #region Event Handlers

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton btn || btn.Tag == null) return;

            _currentTabTag = btn.Tag.ToString() ?? string.Empty;
            AppLogger.Debug($"Switched log tab to '{_currentTabTag}'.");

            var displayState = _tabDisplayStateResolver.Resolve(_currentTabTag);
            var logDisplay = LogDisplay;

            if (logDisplay != null)
            {
                logDisplay.Visibility = displayState.IsLogVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            SettingsDisplay.Visibility = displayState.IsSettingsVisible ? Visibility.Visible : Visibility.Collapsed;

            bool isSettingsTab = displayState.IsSettingsTab;
            DragBar.Visibility = isSettingsTab ? Visibility.Visible : Visibility.Collapsed;
            DragBarRow.Height = isSettingsTab ? new GridLength(25) : new GridLength(0);
            SetSettingsPositionMode(isSettingsTab);

            if (_stickyService != null)
            {
                if (isSettingsTab)
                {
                    _stickyService.SetPositionTrackingEnabled(false);
                }
                else
                {
                    _stickyService.SetPositionTrackingEnabled(true);
                    _stickyService.UpdatePositionImmediately();
                }
            }

            if (logDisplay?.Visibility == Visibility.Visible) RequestRefreshLogDisplay();
        }

        private void AddChatWindow_Click(object sender, RoutedEventArgs e)
        {
            if (ChatCloneWindow.TryOpen(_settings))
                return;

            MessageBox.Show("채팅창은 최대 3개까지 열 수 있습니다.", "채팅창 제한", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MainBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            ShowMainTabsTemporarily();
        }

        private void MainBorder_MouseMove(object sender, MouseEventArgs e)
        {
            ShowMainTabsTemporarily();
        }

        private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                SyncMarginsFromWindowPosition(this.Left, this.Top);
                _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);
                ChatWindowHub.TryApplyMagneticSnap(this);
                PersistSettings();
            }
        }

        private void ResetExp_Click(object sender, RoutedEventArgs e)
        {
            _expService.Reset();
        }

        private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.PropertyName == nameof(_settings.FontFamily) || e.PropertyName == nameof(_settings.FontSize))
                {
                    ApplyInitialSettings();
                    RequestRefreshLogDisplay();
                }
                else if (e.PropertyName == nameof(_settings.LineMargin) || e.PropertyName == nameof(_settings.LineMarginLeft))
                {
                    _stickyService?.UpdatePositionImmediately();
                }
                else if (e.PropertyName == nameof(_settings.AlwaysVisible))
                {
                    _stickyService?.UpdatePositionImmediately();
                }
                else if (e.PropertyName == nameof(_settings.ShowDailyWeeklyContentOverlay))
                {
                    if (_settings.ShowDailyWeeklyContentOverlay)
                        ShowDailyWeeklyWindow();
                    else
                        CloseDailyWeeklyWindow();
                }
                else if (e.PropertyName == nameof(_settings.ShowEtosDirectionAlert) && !_settings.ShowEtosDirectionAlert)
                {
                    SubAddonWindow.Instance?.HideAlert();
                }
                else if (e.PropertyName == nameof(_settings.ShowEtosHelperWindow) && !_settings.ShowEtosHelperWindow)
                {
                    var helper = SubAddonWindow.Instance;
                    if (helper != null)
                    {
                        _settings.SubAddonWindowLeft = helper.Left;
                        _settings.SubAddonWindowTop = helper.Top;
                    }

                    ApplySubAddonWindowSettings();
                    PersistSettings();
                }
                else if (e.PropertyName == nameof(_settings.ShowEtosDirectionAlert) ||
                         e.PropertyName == nameof(_settings.ShowEtosHelperWindow))
                {
                    ApplySubAddonWindowSettings();
                }
                else if (e.PropertyName == nameof(_settings.ShowItemDropHelperWindow))
                {
                    ApplyItemDropHelperWindowSettings();
                }
                else if (e.PropertyName == nameof(_settings.ShowDailyWeeklyContentOverlay))
                {
                    ApplyDailyWeeklyWindowVisibility();
                }
                else if (e.PropertyName == nameof(_settings.ShowBuffTrackerWindow))
                {
                    ApplyBuffTrackerHelperWindowSettings();
                }
                else if (e.PropertyName == nameof(_settings.EnableBuffTrackerAlert))
                {
                    ApplyBuffTrackerWindowSettings();
                }
                else if (e.PropertyName == nameof(_settings.EnableExperienceLimitAlert))
                {
                    if (_settings.EnableExperienceLimitAlert && _settings.ShowExperienceLimitAlertWindow)
                        ExperienceAlertWindowService.ShowPositionPreview(_settings);
                    else if (!_settings.EnableExperienceLimitAlert && !_settings.ShowExperienceLimitAlertWindow)
                        ExperienceAlertWindowService.Close();
                }
                else if (e.PropertyName == nameof(_settings.ShowExperienceLimitAlertWindow))
                {
                    if (_settings.ShowExperienceLimitAlertWindow)
                        ExperienceAlertWindowService.ShowPositionPreview(_settings);
                    else
                        ExperienceAlertWindowService.Close();
                }
                else if (e.PropertyName == nameof(_settings.ShowDungeonCountDisplayWindow))
                {
                    if (_settings.ShowDungeonCountDisplayWindow)
                        DungeonCountDisplayWindowService.ShowPositionPreview(_settings);
                    else
                        DungeonCountDisplayWindowService.ClosePositionPreview(_settings);
                }
                else if (e.PropertyName == nameof(_settings.ShowAbaddonRoadSummaryWindow))
                {
                    if (_settings.ShowAbaddonRoadSummaryWindow && _isSettingsPositionMode)
                        ShowAbaddonRoadSummaryWindow(previewMode: _isSettingsPositionMode);
                    else if (_abaddonRoadSummaryWindow != null)
                    {
                        try { _abaddonRoadSummaryWindow.Close(); } catch { }
                    }
                }
                else if (e.PropertyName == nameof(_settings.BuffTrackerWindowLeft) ||
                         e.PropertyName == nameof(_settings.BuffTrackerWindowTop))
                {
                    ApplyBuffTrackerWindowSettings();
                    ApplyBuffTrackerHelperWindowSettings();
                }
                else if (e.PropertyName == nameof(_settings.ExitHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleOverlayHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleAddonHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleAlwaysVisibleHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleDailyWeeklyContentHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleEtaRankingHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleCoefficientHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleEquipmentDbHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleEncryptHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleSettingsHotKey))
                {
                    ApplyHotKeys();
                }
                else if (e.PropertyName != null && e.PropertyName.StartsWith("Show"))
                {
                    RequestRefreshLogDisplay();
                }

                PersistSettings();
            });
        }

        #endregion
    }
}
