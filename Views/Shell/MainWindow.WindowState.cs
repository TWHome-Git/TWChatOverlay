using System;
using System.Windows;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class MainWindow
    {
        private void PersistSettings()
        {
            ConfigService.SaveDeferred(_settings);
        }

        private void PersistCurrentMainWindowPosition()
        {
            SyncMarginsFromWindowPosition(this.Left, this.Top);
            _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);

            try
            {
                _settings.SavePreset(
                    _settings.LastSelectedPresetNumber,
                    this.Left,
                    this.Top,
                    _settings.LineMarginLeft,
                    _settings.LineMargin);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to persist main window position to preset.", ex);
            }

            PersistSettings();
        }

        public void SetSettingsPositionMode(bool isEnabled)
        {
            if (_isSettingsPositionMode == isEnabled)
                return;

            _isSettingsPositionMode = isEnabled;
            ApplyPositionModeWindows();
            RefreshExpTrackerWindow();
        }

        public void SetAddonPositionMode(bool isEnabled)
        {
            if (_isAddonPositionMode == isEnabled)
                return;

            _isAddonPositionMode = isEnabled;
            if (!isEnabled)
            {
                CloseAddonPositionPreviewWindows(savePositions: true, restoreNormalWindows: true);
            }
            ApplyPositionModeWindows();
            RefreshExpTrackerWindow();
        }

        public void SetAddonPositionPreviewTabIndex(int tabIndex)
        {
            int normalized = tabIndex < 0 ? -1 : tabIndex;
            if (_addonPositionPreviewTabIndex == normalized)
                return;

            _addonPositionPreviewTabIndex = normalized;

            if (_isAddonPositionMode && !_isWizardChatPositionMode)
            {
                ShowSettingsPositionWindows();
            }
        }

        private void ApplyPositionModeWindows()
        {
            if (_isSettingsPositionMode || _isAddonPositionMode)
            {
                ShowSettingsPositionWindows();
            }
            else
            {
                HideSettingsPositionWindows();
            }
        }

        public void SetWizardChatPositionMode(bool isEnabled)
        {
            _isWizardChatPositionMode = isEnabled;
            SetSettingsPositionMode(isEnabled);
            ApplyWizardChatPositionUi(isEnabled);
        }

        public void ShowWizardStepPreviewWindows(int stepIndex)
        {
            try
            {
                ExperienceAlertWindowService.Close();
                DungeonCountDisplayWindowService.ClosePositionPreview(_settings);
                ShoutToastService.ClosePositionPreview(_settings);
                MessengerEtaToastService.ClosePositionPreview(_settings);
                SubAddonWindow.Instance?.ApplyPositionPreviewVisibility(false);
                ItemDropHelperWindow.Instance?.Hide();
                BuffTrackerHelperWindow.Instance?.Hide();
                _AbandonRoadSummaryWindow?.Hide();
            }
            catch { }

            try
            {
                switch (stepIndex)
                {
                    case 3:
                        ShoutToastService.ShowPositionPreview(_settings, force: true);
                        break;
                    case 5:
                        ExperienceAlertWindowService.ShowPositionPreview(_settings, force: true);
                        break;
                    case 6:
                        DungeonCountDisplayWindowService.ShowPositionPreview(_settings, force: true);
                        ShowAbandonRoadSummaryWindow(previewMode: true, restartLifetime: false, activateWindow: false, forcePreview: true);
                        var etosHelper = SubAddonWindow.Instance ?? CreateSubAddonWindow();
                        etosHelper?.ApplyPositionPreviewVisibility(true);
                        break;
                    case 7:
                        var itemHelper = ItemDropHelperWindow.Instance ?? CreateItemDropHelperWindow();
                        if (itemHelper != null)
                        {
                            ApplyStoredPosition(itemHelper, _settings.ItemDropWindowLeft, _settings.ItemDropWindowTop);
                            if (!itemHelper.IsVisible)
                                itemHelper.Show();
                        }
                        break;
                    case 8:
                        var buffHelper = BuffTrackerHelperWindow.Instance ?? CreateBuffTrackerHelperWindow();
                        if (buffHelper != null)
                        {
                            ApplyStoredPosition(buffHelper, _settings.BuffTrackerWindowLeft, _settings.BuffTrackerWindowTop);
                            if (!buffHelper.IsVisible)
                                buffHelper.Show();
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to show wizard step preview windows.", ex);
            }
        }

        private void ApplyWizardChatPositionUi(bool enabled)
        {
            if (enabled)
            {
                try
                {
                    if (!IsVisible)
                        Show();

                    Opacity = 1;
                    IsHitTestVisible = true;
                    Visibility = Visibility.Visible;

                    SettingsDisplay.Visibility = Visibility.Collapsed;
                    if (LogDisplay != null)
                        LogDisplay.Visibility = Visibility.Visible;

                    DragBar.Visibility = Visibility.Visible;
                    DragBarRow.Height = new GridLength(25);

                    _stickyService?.SetPositionTrackingEnabled(false);
                }
                catch { }
            }
            else
            {
                try
                {
                    DragBar.Visibility = Visibility.Collapsed;
                    DragBarRow.Height = new GridLength(0);
                    _stickyService?.SetPositionTrackingEnabled(true);
                    _stickyService?.UpdatePositionImmediately();
                }
                catch { }
            }
        }

        private void ShowSettingsPositionWindows()
        {
            if (_isWizardChatPositionMode)
            {
                try
                {
                    ExperienceAlertWindowService.Close();
                    DungeonCountDisplayWindowService.ClosePositionPreview(_settings);
                    ShoutToastService.ClosePositionPreview(_settings);
                    MessengerEtaToastService.ClosePositionPreview(_settings);
                    if (_dailyWeeklyContentOverlay?.IsVisible == true)
                        _dailyWeeklyContentOverlay.Hide();
                    SubAddonWindow.Instance?.Hide();
                    ItemDropHelperWindow.Instance?.Hide();
                    BuffTrackerHelperWindow.Instance?.Hide();
                    _AbandonRoadSummaryWindow?.Hide();
                }
                catch { }
                return;
            }

            try { ApplyDailyWeeklyWindowVisibility(); } catch { }

            if (_isSettingsPositionMode)
            {
                ShoutToastService.ShowPositionPreview(_settings, force: true);
                MessengerEtaToastService.ShowPositionPreview(_settings, force: true);
            }
            else
            {
                CloseNonAddonPositionPreviewWindows(savePositions: true);
            }

            if (_isAddonPositionMode)
            {
                ShowAddonPositionPreviewForSelectedTab();
            }
        }

        private void HideSettingsPositionWindows()
        {
            if (_isWizardChatPositionMode)
            {
                _isWizardChatPositionMode = false;
                return;
            }

            CloseNonAddonPositionPreviewWindows(savePositions: true);
            CloseAddonPositionPreviewWindows(savePositions: true, restoreNormalWindows: true);
            try { ApplyDailyWeeklyWindowVisibility(); } catch { }
        }

        private void CloseNonAddonPositionPreviewWindows(bool savePositions)
        {
            if (savePositions)
            {
                ShoutToastService.SaveCurrentPosition(_settings);
                MessengerEtaToastService.SaveCurrentPosition(_settings);
            }

            ShoutToastService.ClosePositionPreview(_settings);
            MessengerEtaToastService.ClosePositionPreview(_settings);
        }

        private void ShowAddonPositionPreviewForSelectedTab()
        {
            CloseAddonPositionPreviewWindows(savePositions: true, restoreNormalWindows: false);

            switch (_addonPositionPreviewTabIndex)
            {
                case 1:
                    ExperienceAlertWindowService.ShowPositionPreview(_settings, force: true);
                    break;
                case 2:
                    DungeonCountDisplayWindowService.ShowPositionPreview(_settings, force: true);
                    ShowAbandonRoadSummaryWindow(previewMode: true, restartLifetime: false, activateWindow: false, forcePreview: true);
                    if (_AbandonRoadSummaryWindow != null)
                        _AbandonRoadSummaryWindow.Topmost = true;

                    var etosHelper = SubAddonWindow.Instance ?? CreateSubAddonWindow();
                    etosHelper?.ApplyPositionPreviewVisibility(true);
                    break;
                case 3:
                    var itemHelper = ItemDropHelperWindow.Instance ?? CreateItemDropHelperWindow();
                    if (itemHelper != null)
                    {
                        ApplyStoredPosition(itemHelper, _settings.ItemDropWindowLeft, _settings.ItemDropWindowTop);
                        if (!itemHelper.IsVisible)
                            itemHelper.Show();
                    }
                    break;
                case 4:
                    var buffHelper = BuffTrackerHelperWindow.Instance ?? CreateBuffTrackerHelperWindow();
                    if (buffHelper != null)
                    {
                        ApplyStoredPosition(buffHelper, _settings.BuffTrackerWindowLeft, _settings.BuffTrackerWindowTop);
                        if (!buffHelper.IsVisible)
                            buffHelper.Show();
                    }
                    break;
            }
        }

        private void CloseAddonPositionPreviewWindows(bool savePositions, bool restoreNormalWindows)
        {
            if (savePositions)
            {
                ExperienceAlertWindowService.SaveCurrentPosition(_settings);
                DungeonCountDisplayWindowService.SaveCurrentPosition(_settings);

                if (_AbandonRoadSummaryWindow != null)
                {
                    try
                    {
                        _settings.AbandonRoadSummaryWindowLeft = _AbandonRoadSummaryWindow.Left;
                        _settings.AbandonRoadSummaryWindowTop = _AbandonRoadSummaryWindow.Top;
                    }
                    catch { }
                }
            }

            ExperienceAlertWindowService.Close();
            DungeonCountDisplayWindowService.ClosePositionPreview(_settings);
            SubAddonWindow.Instance?.Hide();
            ItemDropHelperWindow.Instance?.Hide();
            BuffTrackerHelperWindow.Instance?.Hide();

            if (restoreNormalWindows)
            {
                ApplySubAddonWindowSettings();
                ApplyItemDropHelperWindowSettings();
                ApplyBuffTrackerHelperWindowSettings();
                PersistSettings();
            }

            if (_AbandonRoadSummaryWindow != null)
            {
                try
                {
                    _AbandonRoadSummaryWindow.Close();
                }
                catch { }
            }
        }

        private static void ApplyStoredPosition(Window window, double? left, double? top)
        {
            if (left.HasValue)
                window.Left = left.Value;
            if (top.HasValue)
                window.Top = top.Value;
        }

        private void SyncMarginsFromWindowPosition(double windowLeft, double windowTop)
        {
            _settings.LineMarginLeft = windowLeft;
            _settings.LineMargin = windowTop;
        }

        private SubAddonWindow? CreateSubAddonWindow()
        {
            try
            {
                var helper = new SubAddonWindow
                {
                    Left = _settings.SubAddonWindowLeft ?? (SystemParameters.WorkArea.Width - 290),
                    Top = _settings.SubAddonWindowTop ?? 10
                };
                helper.ApplyPinnedVisibility();
                return helper;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to create SubAddonWindow for Eclipse alert.", ex);
                return null;
            }
        }

        private ItemDropHelperWindow? CreateItemDropHelperWindow()
        {
            try
            {
                return new ItemDropHelperWindow
                {
                    Left = _settings.ItemDropWindowLeft ?? ((SystemParameters.WorkArea.Width - 420) / 2),
                    Top = _settings.ItemDropWindowTop ?? 42
                };
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to create ItemDropHelperWindow.", ex);
                return null;
            }
        }

        private BuffTrackerWindow? CreateBuffTrackerWindow()
        {
            try
            {
                return new BuffTrackerWindow(_buffTrackerService, _settings)
                {
                    Left = _settings.BuffTrackerWindowLeft ?? 10,
                    Top = _settings.BuffTrackerWindowTop ?? 42
                };
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to create BuffTrackerWindow.", ex);
                return null;
            }
        }

        private BuffTrackerHelperWindow? CreateBuffTrackerHelperWindow()
        {
            try
            {
                return new BuffTrackerHelperWindow
                {
                    Left = _settings.BuffTrackerWindowLeft ?? 10,
                    Top = _settings.BuffTrackerWindowTop ?? 42
                };
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to create BuffTrackerHelperWindow.", ex);
                return null;
            }
        }

        private void ApplySubAddonWindowSettings()
        {
            try
            {
                var helper = SubAddonWindow.Instance ?? CreateSubAddonWindow();
                if (helper == null)
                {
                    return;
                }

                if (_settings.SubAddonWindowLeft.HasValue)
                {
                    helper.Left = _settings.SubAddonWindowLeft.Value;
                }

                if (_settings.SubAddonWindowTop.HasValue)
                {
                    helper.Top = _settings.SubAddonWindowTop.Value;
                }

                helper.ApplyPinnedVisibility();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to apply SubAddonWindow settings.", ex);
            }
        }

        private void ApplyItemDropHelperWindowSettings()
        {
            try
            {
                if (!_isAddonPositionMode && !_settings.ShowItemDropHelperWindow && ItemDropHelperWindow.Instance == null)
                    return;

                var helper = ItemDropHelperWindow.Instance ?? CreateItemDropHelperWindow();
                if (helper == null)
                    return;

                if (_settings.ItemDropWindowLeft.HasValue)
                    helper.Left = _settings.ItemDropWindowLeft.Value;
                if (_settings.ItemDropWindowTop.HasValue)
                    helper.Top = _settings.ItemDropWindowTop.Value;

                if (_isAddonPositionMode || _settings.ShowItemDropHelperWindow)
                {
                    if (!helper.IsVisible)
                        helper.Show();
                }
                else if (helper.IsVisible)
                {
                    helper.Hide();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to apply ItemDropHelperWindow settings.", ex);
            }
        }

        private void ApplyBuffTrackerWindowSettings()
        {
            try
            {
                if (!_settings.EnableBuffTrackerAlert && BuffTrackerWindow.Instance == null)
                    return;

                var window = BuffTrackerWindow.Instance ?? CreateBuffTrackerWindow();
                if (window == null)
                    return;

                if (_settings.BuffTrackerWindowLeft.HasValue)
                    window.Left = _settings.BuffTrackerWindowLeft.Value;
                if (_settings.BuffTrackerWindowTop.HasValue)
                    window.Top = _settings.BuffTrackerWindowTop.Value;

                if (_canShowAuxiliaryWindows)
                {
                    window.ApplyVisibility();
                }
                else if (window.IsVisible)
                {
                    window.Hide();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to apply BuffTrackerWindow settings.", ex);
            }
        }

        private void ApplyDailyWeeklyWindowVisibility()
        {
            try
            {
                bool shouldShow = !_isWizardChatPositionMode &&
                                  _settings.ShowDailyWeeklyContentOverlay &&
                                  true;

                if (shouldShow)
                {
                    if (_dailyWeeklyContentOverlay == null || !_dailyWeeklyContentOverlay.IsLoaded)
                    {
                        ShowDailyWeeklyWindow();
                        return;
                    }

                    if (!_dailyWeeklyContentOverlay.IsVisible)
                        _dailyWeeklyContentOverlay.Show();
                }
                else if (_dailyWeeklyContentOverlay?.IsVisible == true)
                {
                    CloseDailyWeeklyWindow();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to apply DailyWeekly window visibility.", ex);
            }
        }

        private bool CanShowAbandonRoadSummaryWindow(bool previewMode)
        {
            if (previewMode || _isAddonPositionMode)
                return true;

            if (!_isOverlayVisible)
                return false;

            if (WindowState == WindowState.Minimized)
                return false;

            if (!_canShowAuxiliaryWindows)
                return false;

            if (Visibility != Visibility.Visible || Opacity <= 0)
                return false;

            return true;
        }

        private void ApplyAbandonRoadSummaryWindowVisibility()
        {
            try
            {
                if (_AbandonRoadSummaryWindow == null)
                    return;

                if (_isAddonPositionMode)
                {
                    ShowAbandonRoadSummaryWindow(previewMode: true, restartLifetime: false);
                    return;
                }

                bool canShow = _settings.ShowAbandonRoadSummaryWindow && CanShowAbandonRoadSummaryWindow(previewMode: false);
                if (!canShow)
                {
                    if (_AbandonRoadSummaryWindow.IsVisible)
                        _AbandonRoadSummaryWindow.Hide();
                    return;
                }

                if (!_AbandonRoadSummaryWindow.IsVisible && _AbandonRoadSummaryWindow.IsAutoClosePending)
                {
                    ShowAbandonRoadSummaryWindow(previewMode: false, restartLifetime: false);
                    return;
                }

                if (_AbandonRoadSummaryWindow.IsVisible)
                {
                    bool shouldTopmost = Topmost;
                    _AbandonRoadSummaryWindow.Topmost = shouldTopmost;
                    if (shouldTopmost)
                        TopmostWindowHelper.BringToTopmost(_AbandonRoadSummaryWindow);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to apply Abandon summary window visibility.", ex);
            }
        }

        private void ApplyBuffTrackerHelperWindowSettings()
        {
            try
            {
                if (!_isAddonPositionMode && !_settings.ShowBuffTrackerWindow && BuffTrackerHelperWindow.Instance == null)
                    return;

                var helper = BuffTrackerHelperWindow.Instance ?? CreateBuffTrackerHelperWindow();
                if (helper == null)
                    return;

                if (_settings.BuffTrackerWindowLeft.HasValue)
                    helper.Left = _settings.BuffTrackerWindowLeft.Value;
                if (_settings.BuffTrackerWindowTop.HasValue)
                    helper.Top = _settings.BuffTrackerWindowTop.Value;

                if (_isAddonPositionMode || _settings.ShowBuffTrackerWindow)
                {
                    if (!helper.IsVisible)
                        helper.Show();
                }
                else if (helper.IsVisible)
                {
                    helper.Hide();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to apply BuffTrackerHelperWindow settings.", ex);
            }
        }
    }
}
