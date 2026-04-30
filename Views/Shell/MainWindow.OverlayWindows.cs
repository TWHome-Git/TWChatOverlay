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
        #region Overlay Windows

        public void ToggleOverlayVisibility()
        {
            _isOverlayVisible = !_isOverlayVisible;
            OverlayVisibilityChanged?.Invoke(this, _isOverlayVisible);

            if (_stickyService != null)
            {
                _stickyService.SetForceHidden(!_isOverlayVisible);
                if (_isOverlayVisible)
                {
                    _stickyService.UpdatePositionImmediately();
                }
            }

            if (_isOverlayVisible)
            {
                if (!IsVisible)
                {
                    Show();
                }

                Opacity = 1;
                IsHitTestVisible = true;
                Visibility = Visibility.Visible;
                CompleteInitialPresentation();
            }
            else
            {
                IsHitTestVisible = false;
                Visibility = Visibility.Collapsed;
                Opacity = 0;
            }

            PersistSettings();
        }

        private void ShowDailyWeeklyWindow()
        {
            bool shouldScanHistoricalLogs = false;

            if (_dailyWeeklyContentOverlay == null || !_dailyWeeklyContentOverlay.IsLoaded)
            {
                _dailyWeeklyContentOverlay = new DailyWeeklyContentWindow(_settings);
                shouldScanHistoricalLogs = true;
                _dailyWeeklyContentOverlay.Closed += (_, _) =>
                {
                    _dailyWeeklyContentOverlay = null;
                    try { DailyWeeklyVisibilityChanged?.Invoke(this, false); } catch { }
                };
            }

            if (!_dailyWeeklyContentOverlay.IsVisible)
            {
                _dailyWeeklyContentOverlay.Owner = this;
                ApplyStoredPosition(_dailyWeeklyContentOverlay, _settings.DailyWeeklyContentOverlayLeft, _settings.DailyWeeklyContentOverlayTop);
                _dailyWeeklyContentOverlay.WindowStartupLocation = WindowStartupLocation.Manual;
                _dailyWeeklyContentOverlay.Show();
            }

            _dailyWeeklyContentOverlay.Activate();
            if (shouldScanHistoricalLogs)
            {
                _ = _dailyWeeklyContentOverlay.ScanHistoricalLogsAsync();
            }
            try { DailyWeeklyVisibilityChanged?.Invoke(this, true); } catch { }
        }

        private void CloseDailyWeeklyWindow()
        {
            try
            {
                _dailyWeeklyContentOverlay?.Close();
            }
            catch { }
            finally
            {
                _dailyWeeklyContentOverlay = null;
                try { DailyWeeklyVisibilityChanged?.Invoke(this, false); } catch { }
            }
        }

        public void ToggleDailyWeeklyContentWindow()
        {
            _settings.ShowDailyWeeklyContentOverlay = !_settings.ShowDailyWeeklyContentOverlay;
            PersistSettings();
        }

        public void ShowItemCalendarWindow()
        {
            if (_itemCalendarWindow == null || !_itemCalendarWindow.IsLoaded)
            {
                _itemCalendarWindow = new ItemCalendarWindow(_settings, _logAnalysisService);
                _itemCalendarWindow.Closed += (_, _) =>
                {
                    _settings.ItemCalendarWindowLeft = _itemCalendarWindow?.Left;
                    _settings.ItemCalendarWindowTop = _itemCalendarWindow?.Top;
                    PersistSettings();
                    _itemCalendarWindow = null;
                    try { ItemCalendarVisibilityChanged?.Invoke(this, false); } catch { }
                };
                AppLogger.Info("Created item calendar window instance.");
            }

            if (!_itemCalendarWindow.IsVisible)
            {
                _itemCalendarWindow.Owner = this;
                ApplyStoredPosition(_itemCalendarWindow, _settings.ItemCalendarWindowLeft, _settings.ItemCalendarWindowTop);
                _itemCalendarWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                _itemCalendarWindow.Show();
            }

            _itemCalendarWindow.Activate();
            if (!_itemCalendarWindow.IsMonthLoaded(DateTime.Today))
                _ = _itemCalendarWindow.LoadCurrentMonthAsync();
            try { ItemCalendarVisibilityChanged?.Invoke(this, true); } catch { }
        }

        public void ToggleItemCalendarWindow()
        {
            if (_itemCalendarWindow?.IsVisible == true)
            {
                try
                {
                    _itemCalendarWindow.Close();
                }
                catch { }
                return;
            }

            ShowItemCalendarWindow();
        }

        public void ShowAbaddonRoadSummaryWindow(bool previewMode = false, bool restartLifetime = true)
        {
            if (!_settings.ShowAbaddonRoadSummaryWindow)
            {
                if (_abaddonRoadSummaryWindow != null)
                {
                    try { _abaddonRoadSummaryWindow.Close(); } catch { }
                }
                return;
            }

            if (_abaddonRoadSummaryWindow == null || !_abaddonRoadSummaryWindow.IsLoaded)
            {
                _abaddonRoadSummaryWindow = new AbaddonRoadSummaryWindow(_settings, _logAnalysisService);
                _abaddonRoadSummaryWindow.Closed += (_, _) =>
                {
                    _settings.AbaddonRoadSummaryWindowLeft = _abaddonRoadSummaryWindow?.Left;
                    _settings.AbaddonRoadSummaryWindowTop = _abaddonRoadSummaryWindow?.Top;
                    PersistSettings();
                    _abaddonRoadSummaryWindow = null;
                };
                AppLogger.Info("Created AbaddonRoad summary window instance.");
            }

            if (!_abaddonRoadSummaryWindow.IsVisible)
            {
                _abaddonRoadSummaryWindow.Owner = this;
                ApplyStoredPosition(_abaddonRoadSummaryWindow, _settings.AbaddonRoadSummaryWindowLeft, _settings.AbaddonRoadSummaryWindowTop);
                _abaddonRoadSummaryWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                _abaddonRoadSummaryWindow.Show();
            }

            _abaddonRoadSummaryWindow.SetPreviewMode(previewMode);
            if (!previewMode && restartLifetime)
            {
                _abaddonRoadSummaryWindow.StartAutoClose(_settings.AbaddonRoadCountAlertDurationSeconds);
            }

            _abaddonRoadSummaryWindow.Topmost = true;
            _abaddonRoadSummaryWindow.Activate();
            TopmostWindowHelper.BringToTopmost(_abaddonRoadSummaryWindow);
            _ = _abaddonRoadSummaryWindow.LoadCurrentWeekAsync();
        }

        public void RefreshAbaddonRoadSummaryWindow()
        {
            if (!_settings.ShowAbaddonRoadSummaryWindow)
                return;

            if (_abaddonRoadSummaryWindow?.IsVisible != true)
                return;

            _ = _abaddonRoadSummaryWindow.LoadCurrentWeekAsync();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                if (_dailyWeeklyContentOverlay?.IsVisible == true)
                    _dailyWeeklyContentOverlay.Hide();
                if (_itemCalendarWindow?.IsVisible == true)
                    _itemCalendarWindow.Hide();
                if (_abaddonRoadSummaryWindow?.IsVisible == true)
                    _abaddonRoadSummaryWindow.Hide();
                return;
            }

            if (_settings.ShowDailyWeeklyContentOverlay && _canShowAuxiliaryWindows)
            {
                if (_dailyWeeklyContentOverlay != null && !_dailyWeeklyContentOverlay.IsVisible)
                    _dailyWeeklyContentOverlay.Show();
            }

            if (_itemCalendarWindow != null && !_itemCalendarWindow.IsVisible)
                _itemCalendarWindow.Show();

            if (_settings.ShowAbaddonRoadSummaryWindow &&
                _abaddonRoadSummaryWindow != null &&
                !_abaddonRoadSummaryWindow.IsVisible)
            {
                if (_isSettingsPositionMode)
                {
                    ShowAbaddonRoadSummaryWindow(previewMode: true, restartLifetime: false);
                }
                else if (_abaddonRoadSummaryWindow.IsAutoClosePending)
                {
                    ShowAbaddonRoadSummaryWindow(previewMode: false, restartLifetime: false);
                }
            }
        }

        #endregion
    }
}
