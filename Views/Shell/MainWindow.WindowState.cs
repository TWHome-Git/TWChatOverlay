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

        public void SetSettingsPositionMode(bool isEnabled)
        {
            if (_isSettingsPositionMode == isEnabled)
                return;

            _isSettingsPositionMode = isEnabled;

            if (isEnabled)
            {
                ShowSettingsPositionWindows();
            }
            else
            {
                HideSettingsPositionWindows();
            }
        }

        private void ShowSettingsPositionWindows()
        {
            ExperienceAlertWindowService.ShowPositionPreview(_settings, force: true);
            DungeonCountDisplayWindowService.ShowPositionPreview(_settings, force: true);

            var etosHelper = SubAddonWindow.Instance ?? CreateSubAddonWindow();
            etosHelper?.ApplyPositionPreviewVisibility(true);

            var itemHelper = ItemDropHelperWindow.Instance ?? CreateItemDropHelperWindow();
            if (itemHelper != null)
            {
                ApplyStoredPosition(itemHelper, _settings.ItemDropWindowLeft, _settings.ItemDropWindowTop);
                if (!itemHelper.IsVisible)
                    itemHelper.Show();
            }

            var buffHelper = BuffTrackerHelperWindow.Instance ?? CreateBuffTrackerHelperWindow();
            if (buffHelper != null)
            {
                ApplyStoredPosition(buffHelper, _settings.BuffTrackerWindowLeft, _settings.BuffTrackerWindowTop);
                if (!buffHelper.IsVisible)
                    buffHelper.Show();
            }
        }

        private void HideSettingsPositionWindows()
        {
            ExperienceAlertWindowService.SaveCurrentPosition(_settings);
            DungeonCountDisplayWindowService.SaveCurrentPosition(_settings);
            ExperienceAlertWindowService.Close();
            DungeonCountDisplayWindowService.ClosePositionPreview(_settings);

            SubAddonWindow.Instance?.ApplyPositionPreviewVisibility(false);
            ApplyItemDropHelperWindowSettings();
            ApplyBuffTrackerHelperWindowSettings();
            PersistSettings();
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
            IntPtr gameHwnd = OverlayHelper.FindTalesWeaverWindow();
            if (gameHwnd == IntPtr.Zero) return;

            var rect = OverlayHelper.GetActualRect(gameHwnd);

            double dpiX = _stickyService?._dpiX > 0 ? _stickyService._dpiX : 1.0;
            double dpiY = _stickyService?._dpiY > 0 ? _stickyService._dpiY : 1.0;

            double gameWidth = (rect.Right - rect.Left) / dpiX;
            double gameHeight = (rect.Bottom - rect.Top) / dpiY;
            double gameLeft = rect.Left / dpiX;
            double gameTop = rect.Top / dpiY;

            double windowWidth = this.ActualWidth > 0 ? this.ActualWidth : _settings.WindowWidth;
            double windowHeight = this.ActualHeight > 0 ? this.ActualHeight : _settings.WindowHeight;

            double centerAlignedLeft = gameLeft + (gameWidth / 2.0) - (windowWidth / 2.0);
            double bottomAlignedTop = gameTop + gameHeight - windowHeight;

            _settings.LineMarginLeft = (windowLeft - centerAlignedLeft) * dpiX;
            _settings.LineMargin = (bottomAlignedTop - windowTop) * dpiY;
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
                if (!_isSettingsPositionMode && !_settings.ShowItemDropHelperWindow && ItemDropHelperWindow.Instance == null)
                    return;

                var helper = ItemDropHelperWindow.Instance ?? CreateItemDropHelperWindow();
                if (helper == null)
                    return;

                if (_settings.ItemDropWindowLeft.HasValue)
                    helper.Left = _settings.ItemDropWindowLeft.Value;
                if (_settings.ItemDropWindowTop.HasValue)
                    helper.Top = _settings.ItemDropWindowTop.Value;

                if (_isSettingsPositionMode || _settings.ShowItemDropHelperWindow)
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
                if (_dailyWeeklyContentOverlay == null)
                    return;

                if (_settings.ShowDailyWeeklyContentOverlay && _canShowAuxiliaryWindows)
                {
                    if (!_dailyWeeklyContentOverlay.IsVisible)
                    {
                        _dailyWeeklyContentOverlay.Show();
                    }
                }
                else if (_dailyWeeklyContentOverlay.IsVisible)
                {
                    _dailyWeeklyContentOverlay.Hide();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to apply DailyWeekly window visibility.", ex);
            }
        }

        private void ApplyBuffTrackerHelperWindowSettings()
        {
            try
            {
                if (!_isSettingsPositionMode && !_settings.ShowBuffTrackerWindow && BuffTrackerHelperWindow.Instance == null)
                    return;

                var helper = BuffTrackerHelperWindow.Instance ?? CreateBuffTrackerHelperWindow();
                if (helper == null)
                    return;

                if (_settings.BuffTrackerWindowLeft.HasValue)
                    helper.Left = _settings.BuffTrackerWindowLeft.Value;
                if (_settings.BuffTrackerWindowTop.HasValue)
                    helper.Top = _settings.BuffTrackerWindowTop.Value;

                if (_isSettingsPositionMode || _settings.ShowBuffTrackerWindow)
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
