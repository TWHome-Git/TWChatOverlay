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

            string tabTag = btn.Tag.ToString() ?? string.Empty;
            AppLogger.Debug($"Switched log tab to '{tabTag}'.");
            ApplyMainTabState(tabTag);
        }

        private void AddChatWindow_Click(object sender, RoutedEventArgs e)
        {
            if (ChatCloneWindow.TryOpen(_settings))
                return;

            MessageBox.Show("채팅창은 최대 2개까지 열 수 있습니다.", "채팅창 제한", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void MainBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            ShowMainTabsTemporarily();
        }

        /// <summary>잠금 해제 모드에서는 창의 아무 곳이나 잡고 드래그해 이동할 수 있다. (레이아웃 변화 없음 → 위치 오차 없음)</summary>
        private void MainBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!UiLockService.IsUnlocked) return;
            UiLockService.Select(this);
            if (e.ButtonState != MouseButtonState.Pressed) return;

            try
            {
                DragMove();
                ChatWindowHub.TryApplyMagneticSnap(this);
                PersistCurrentMainWindowPosition();
            }
            catch { }
            e.Handled = true;
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
                ChatWindowHub.TryApplyMagneticSnap(this);
                PersistCurrentMainWindowPosition();
            }
        }

        private void ResetExp_Click(object sender, RoutedEventArgs e)
        {
            _expService.Reset();
        }

        private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 추가 기능 위치 미리보기 중 토글이 바뀌면 해당 탭의 창 표시를 다시 계산한다
            // (활성화하면 즉시 나타나고, 끄면 사라진다)
            if (_isAddonPositionMode && e.PropertyName is
                nameof(_settings.EnableExperienceLimitAlert) or
                nameof(_settings.EnableAbandonRoadCountAlert) or
                nameof(_settings.EnableCravingPleasureCountAlert) or
                nameof(_settings.ShowAbandonRoadSummaryWindow) or
                nameof(_settings.ShowEtosDirectionAlert) or
                nameof(_settings.ShowRecaptureSupplyMap) or
                nameof(_settings.ShowItemDropAlert) or
                nameof(_settings.EnableBuffTrackerAlert))
            {
                try { ShowSettingsPositionWindows(); } catch (Exception ex) { AppLogger.Warn("Addon preview refresh failed.", ex); }
            }

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
                else if (e.PropertyName == nameof(_settings.ShowDailyWeeklyContentOverlay))
                {
                    ApplyDailyWeeklyWindowVisibility();
                }
                else if (e.PropertyName == nameof(_settings.ShowEtosDirectionAlert) && !_settings.ShowEtosDirectionAlert)
                {
                    if (_isInitialSetupWizardRunning)
                        SubAddonWindow.Instance?.ApplyPositionPreviewVisibility(true);
                    else
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
                    if (_isInitialSetupWizardRunning)
                    {
                        ApplySubAddonWindowSettings();
                        SubAddonWindow.Instance?.ApplyPositionPreviewVisibility(true);
                    }
                    else if (!_isAddonPositionMode)
                    {
                        // 추가 기능 미리보기 중에는 위의 미리보기 갱신이 표시를 관리한다
                        // (여기서 ApplySubAddonWindowSettings를 부르면 방금 띄운 미리보기가 숨겨진다)
                        ApplySubAddonWindowSettings();
                    }
                }
                else if (e.PropertyName == nameof(_settings.ShowItemDropHelperWindow))
                {
                    ApplyItemDropHelperWindowSettings();
                }
                else if (e.PropertyName == nameof(_settings.ShowExpTracker))
                {
                    RefreshExpTrackerWindow();
                }
                else if (e.PropertyName == nameof(_settings.UnifiedToastStack))
                {
                    // 통합/분리 전환: 미리보기가 떠 있으면 새 모드로 다시 그리고, 열린 알림도 재배치
                    ToastStackService.RefreshPreviews(_settings);
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

                    ExperienceAlertWindowService.RefreshState(_settings);
                }
                else if (e.PropertyName == nameof(_settings.ShowExperienceLimitAlertWindow))
                {
                    if (_settings.ShowExperienceLimitAlertWindow)
                        ExperienceAlertWindowService.ShowPositionPreview(_settings);
                    else
                        ExperienceAlertWindowService.Close();
                }
                else if (e.PropertyName == nameof(_settings.ExperienceLimitTotalExp))
                {
                    ExperienceAlertWindowService.RefreshState(_settings);
                }
                else if (e.PropertyName == nameof(_settings.ShowDungeonCountDisplayWindow))
                {
                    if (_settings.ShowDungeonCountDisplayWindow)
                        DungeonCountDisplayWindowService.ShowPositionPreview(_settings);
                    else
                        DungeonCountDisplayWindowService.ClosePositionPreview(_settings);
                }
                else if (e.PropertyName == nameof(_settings.ShowAbandonRoadSummaryWindow))
                {
                    if (_isInitialSetupWizardRunning)
                    {
                        ShowAbandonRoadSummaryWindow(previewMode: true, restartLifetime: false, activateWindow: false, forcePreview: true);
                    }
                    else if (_settings.ShowAbandonRoadSummaryWindow && _isAddonPositionMode)
                        ShowAbandonRoadSummaryWindow(previewMode: _isAddonPositionMode);
                    else if (_AbandonRoadSummaryWindow != null)
                    {
                        try { _AbandonRoadSummaryWindow.Close(); } catch { }
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
                         e.PropertyName == nameof(_settings.ToggleDailyWeeklyContentHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleSettingsHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleTrayAllHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleUnlockHotKey))
                {
                    ApplyHotKeys();
                }
                else if (e.PropertyName == nameof(_settings.MainWindowChatTabTag))
                {
                    string normalizedTabTag = NormalizeMainTabTag(_settings.MainWindowChatTabTag);
                    if (!string.Equals(_currentTabTag, normalizedTabTag, StringComparison.Ordinal))
                        ApplyMainTabState(normalizedTabTag, persistSettings: false, refreshLogDisplay: false);
                }
                else if (e.PropertyName != null && e.PropertyName.StartsWith("Show"))
                {
                    RequestRefreshLogDisplay();
                }

                PersistSettings();
            });
        }

        private void ApplyMainTabState(string tabTag, bool persistSettings = true, bool refreshLogDisplay = true)
        {
            string normalizedTabTag = NormalizeMainTabTag(tabTag);
            _currentTabTag = normalizedTabTag;

            if (persistSettings && !string.Equals(_settings.MainWindowChatTabTag, normalizedTabTag, StringComparison.Ordinal))
            {
                _settings.MainWindowChatTabTag = normalizedTabTag;
                PersistSettings();
            }

            UpdateMainTabSelection(normalizedTabTag);

            var displayState = _tabDisplayStateResolver.Resolve(normalizedTabTag);
            var logDisplay = LogDisplay;

            if (logDisplay != null)
            {
                logDisplay.Visibility = displayState.IsLogVisible ? Visibility.Visible : Visibility.Collapsed;
            }

            bool isSettingsTab = displayState.IsSettingsTab;
            if (DragBar != null)
                DragBar.Visibility = isSettingsTab ? Visibility.Visible : Visibility.Collapsed;
            if (DragBarRow != null)
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

            if (refreshLogDisplay && logDisplay?.Visibility == Visibility.Visible)
                RequestRefreshLogDisplay();
        }

        private void UpdateMainTabSelection(string tabTag)
        {
            if (MainTabPanel == null)
                return;

            foreach (var radioButton in MainTabPanel.Children.OfType<RadioButton>())
            {
                bool isSelected = string.Equals(radioButton.Tag?.ToString(), tabTag, StringComparison.Ordinal);
                if (radioButton.IsChecked != isSelected)
                    radioButton.IsChecked = isSelected;
            }
        }

        private static string NormalizeMainTabTag(string? tabTag)
        {
            return LogTextClassifier.NormalizeMainTabTag(tabTag);
        }

        #endregion
    }
}
