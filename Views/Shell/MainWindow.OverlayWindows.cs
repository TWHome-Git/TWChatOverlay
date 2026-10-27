using System;
using System.Linq;
using System.Windows;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>부속 창(일일/주간·달력·어밴던 통계·서브 채팅창·경험치 추적)의 생성과 표시, 최소화 시 정리</summary>
    public partial class MainWindow
    {
        // ── 이 부분 클래스가 주로 쓰는 상태 ──
        private DailyWeeklyContentWindow? _dailyWeeklyContentOverlay;
        private ItemCalendarWindow? _itemCalendarWindow;
        private AbandonRoadSummaryWindow? _AbandonRoadSummaryWindow;
        private bool _hasRestoredChatCloneWindows;
        private bool _hasRestoredMemoWindow;
        private bool _isOverlayVisible = true;
        /// <summary>
        /// Public read-only view of overlay visibility for other windows to subscribe to.
        /// </summary>
        public bool IsOverlayVisible => _isOverlayVisible;

        /// <summary>
        /// Raised when overlay visibility changes. The event argument is the new visibility value.
        /// </summary>
        public event EventHandler<bool>? OverlayVisibilityChanged;
        public event EventHandler<bool>? DailyWeeklyVisibilityChanged;
        public event EventHandler<bool>? ItemCalendarVisibilityChanged;

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
                ApplyMainTabState(_currentTabTag, persistSettings: false, refreshLogDisplay: false);
                ShowMainTabsTemporarily();
                RestoreSavedChatCloneWindows();
                SyncChatCloneWindowsVisibility();
                RequestRefreshLogDisplay(force: true);
                CompleteInitialPresentation();
            }
            else
            {
                IsHitTestVisible = false;
                Visibility = Visibility.Collapsed;
                Opacity = 0;
                SyncChatCloneWindowsVisibility();
            }

            ApplyAbandonRoadSummaryWindowVisibility();
            PersistSettings();
        }

        private void RestoreSavedChatCloneWindows()
        {
            if (_hasRestoredChatCloneWindows)
                return;

            _hasRestoredChatCloneWindows = true;

            if (_settings.ChatCloneWindow1IsOpen)
                ChatCloneWindow.TryRestore(_settings, 1);

            if (_settings.ChatCloneWindow2IsOpen)
                ChatCloneWindow.TryRestore(_settings, 2);
        }

        private void SyncChatCloneWindowsVisibility()
        {
            foreach (ChatCloneWindow clone in Application.Current.Windows.OfType<ChatCloneWindow>().ToList())
            {
                try
                {
                    clone.RefreshVisibilityFromMainWindow();
                }
                catch
                {
                }
            }
        }

        private void ShowDailyWeeklyWindow()
        {
            bool shouldScanHistoricalLogs = EnsureDailyWeeklyContentWindow();

            if (!_dailyWeeklyContentOverlay!.IsVisible)
            {
                _dailyWeeklyContentOverlay.Owner = this;
                _dailyWeeklyContentOverlay.WindowStartupLocation = WindowStartupLocation.Manual;
                _dailyWeeklyContentOverlay.ApplyStoredBounds(_settings);
                _dailyWeeklyContentOverlay.Show();
            }

            if (shouldScanHistoricalLogs)
            {
                _ = _dailyWeeklyContentOverlay.ScanHistoricalLogsAsync();
            }
            try { DailyWeeklyVisibilityChanged?.Invoke(this, true); } catch { }
        }

        private void DailyWeeklyContentOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if (sender is Window window)
                    DailyWeeklyVisibilityChanged?.Invoke(this, window.IsVisible);
            }
            catch { }
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
                    // 위치는 창이 닫히며 스스로 저장한다 (OverlayWindowBase.PersistBoundsOnClose)
                    _itemCalendarWindow = null;
                    try { ItemCalendarVisibilityChanged?.Invoke(this, false); } catch { }
                };
                AppLogger.Info("Created item calendar window instance.");
            }

            if (!_itemCalendarWindow.IsVisible)
            {
                _itemCalendarWindow.Owner = this;
                _itemCalendarWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                _itemCalendarWindow.ApplyStoredBounds(_settings);
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

        /// <param name="userRequested">
        /// 사용자가 직접 띄운 경우(일일/주간 창의 "어밴던로드 통계" 바로가기).
        /// 자동 표시 토글은 로그가 들어올 때 창을 띄울지를 정하는 설정이라, 직접 누른 경우에는 보지 않는다.
        /// </param>
        public void ShowAbandonRoadSummaryWindow(bool previewMode = false, bool restartLifetime = true, bool activateWindow = true, bool forcePreview = false, bool userRequested = false)
        {
            if (AppServices.Get<TrayAllWindowsService>().IsTrayed && !forcePreview)
                return; // 트레이 최소화 중에는 알림 창을 띄우지 않는다

            if (!_settings.ShowAbandonRoadSummaryWindow && !forcePreview && !userRequested)
            {
                if (_AbandonRoadSummaryWindow != null)
                {
                    try { _AbandonRoadSummaryWindow.Close(); } catch { }
                }
                return;
            }

            if (!CanShowAbandonRoadSummaryWindow(previewMode))
            {
                if (_AbandonRoadSummaryWindow != null)
                    try { _AbandonRoadSummaryWindow.Close(); } catch { } // 사용하지 않을 땐 닫아 메모리 회수
                return;
            }

            if (_AbandonRoadSummaryWindow == null || !_AbandonRoadSummaryWindow.IsLoaded)
            {
                _AbandonRoadSummaryWindow = new AbandonRoadSummaryWindow();
                _AbandonRoadSummaryWindow.Closed += (_, _) =>
                {
                    // 위치는 창이 닫히며 스스로 저장한다 (OverlayWindowBase.PersistBoundsOnClose)
                    _AbandonRoadSummaryWindow = null;
                };
                AppLogger.Info("Created AbandonRoad summary window instance.");
            }

            bool wasVisible = _AbandonRoadSummaryWindow.IsVisible;
            if (!wasVisible)
            {
                _AbandonRoadSummaryWindow.Owner = this;
                _AbandonRoadSummaryWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                _AbandonRoadSummaryWindow.ApplyStoredBounds(_settings);
                _AbandonRoadSummaryWindow.ShowActivated = activateWindow;
                _AbandonRoadSummaryWindow.Show();
            }

            _AbandonRoadSummaryWindow.SetPreviewMode(previewMode);
            if (!previewMode && restartLifetime)
            {
                _AbandonRoadSummaryWindow.StartAutoClose(_settings.AbandonRoadCountAlertDurationSeconds);
            }

            bool shouldTopmost = IsSettingsPositionMode || Topmost;
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

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                if (_itemCalendarWindow?.IsVisible == true)
                    _itemCalendarWindow.Hide();
                if (_AbandonRoadSummaryWindow != null)
                    try { _AbandonRoadSummaryWindow.Close(); } catch { } // 사용하지 않을 땐 닫아 메모리 회수
                if (_expTrackerWindow?.IsVisible == true)
                    _expTrackerWindow.Hide();
                return;
            }

            if (_settings.ShowDailyWeeklyContentOverlay)
            {
                if (_dailyWeeklyContentOverlay != null && !_dailyWeeklyContentOverlay.IsVisible)
                    _dailyWeeklyContentOverlay.Show();
            }

            if (_itemCalendarWindow != null && !_itemCalendarWindow.IsVisible)
                _itemCalendarWindow.Show();

            RefreshExpTrackerWindow();
            ApplyAbandonRoadSummaryWindowVisibility();
        }

        #endregion
    }
}
