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

            ApplyAbandonRoadSummaryWindowVisibility();
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

        public void ShowAbandonRoadSummaryWindow(bool previewMode = false, bool restartLifetime = true, bool activateWindow = true)
        {
            if (!_settings.ShowAbandonRoadSummaryWindow)
            {
                if (_AbandonRoadSummaryWindow != null)
                {
                    try { _AbandonRoadSummaryWindow.Close(); } catch { }
                }
                return;
            }

            if (!CanShowAbandonRoadSummaryWindow(previewMode))
            {
                if (_AbandonRoadSummaryWindow?.IsVisible == true)
                    _AbandonRoadSummaryWindow.Hide();
                return;
            }

            if (_AbandonRoadSummaryWindow == null || !_AbandonRoadSummaryWindow.IsLoaded)
            {
                _AbandonRoadSummaryWindow = new AbandonRoadSummaryWindow(_settings, _logAnalysisService);
                _AbandonRoadSummaryWindow.Closed += (_, _) =>
                {
                    _settings.AbandonRoadSummaryWindowLeft = _AbandonRoadSummaryWindow?.Left;
                    _settings.AbandonRoadSummaryWindowTop = _AbandonRoadSummaryWindow?.Top;
                    PersistSettings();
                    _AbandonRoadSummaryWindow = null;
                };
                AppLogger.Info("Created AbandonRoad summary window instance.");
            }

            bool wasVisible = _AbandonRoadSummaryWindow.IsVisible;
            if (!wasVisible)
            {
                _AbandonRoadSummaryWindow.Owner = this;
                ApplyStoredPosition(_AbandonRoadSummaryWindow, _settings.AbandonRoadSummaryWindowLeft, _settings.AbandonRoadSummaryWindowTop);
                _AbandonRoadSummaryWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                _AbandonRoadSummaryWindow.ShowActivated = activateWindow;
                _AbandonRoadSummaryWindow.Show();
            }

            _AbandonRoadSummaryWindow.SetPreviewMode(previewMode);
            if (!previewMode && restartLifetime)
            {
                _AbandonRoadSummaryWindow.StartAutoClose(_settings.AbandonRoadCountAlertDurationSeconds);
            }

            bool shouldTopmost = _isSettingsPositionMode || _settings.AlwaysVisible || Topmost;
            _AbandonRoadSummaryWindow.Topmost = shouldTopmost;
            if (activateWindow)
            {
                if (shouldTopmost)
                    TopmostWindowHelper.BringToTopmost(_AbandonRoadSummaryWindow);
            }
            // Avoid reloading from disk on every realtime popup refresh.
            // Realtime pipeline already pushes in-memory weekly deltas to this window.
            if (!wasVisible)
                _ = _AbandonRoadSummaryWindow.LoadCurrentWeekAsync();
        }

        public void RefreshAbandonRoadSummaryWindow()
        {
            if (!_settings.ShowAbandonRoadSummaryWindow)
                return;

            if (_AbandonRoadSummaryWindow?.IsVisible != true)
                return;

            _ = _AbandonRoadSummaryWindow.LoadCurrentWeekAsync();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                if (_dailyWeeklyContentOverlay?.IsVisible == true)
                    _dailyWeeklyContentOverlay.Hide();
                if (_itemCalendarWindow?.IsVisible == true)
                    _itemCalendarWindow.Hide();
                if (_AbandonRoadSummaryWindow?.IsVisible == true)
                    _AbandonRoadSummaryWindow.Hide();
                return;
            }

            if (_settings.ShowDailyWeeklyContentOverlay && _canShowAuxiliaryWindows)
            {
                if (_dailyWeeklyContentOverlay != null && !_dailyWeeklyContentOverlay.IsVisible)
                    _dailyWeeklyContentOverlay.Show();
            }

            if (_itemCalendarWindow != null && !_itemCalendarWindow.IsVisible)
                _itemCalendarWindow.Show();

            ApplyAbandonRoadSummaryWindowVisibility();
        }

        #endregion
    }
}
