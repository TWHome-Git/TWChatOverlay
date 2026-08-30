using System;
using System.Linq;
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

        /// <summary>
        /// 잠금 해제 모드 진입/종료 시 위치 조정 대상 창들을 일괄 표시/복원한다.
        /// 대상: 채팅창(+서브), 어밴던로드 주간 합계, 경험치 누적 알림, 경험치 추적창,
        /// 던전 카운터, 에토스 방향 안내, 아이템 드롭 알림, 버프 추적, 외치기 팝업, 1:1 대화 에타 표시.
        /// </summary>
        private void OnUiUnlockChanged(bool unlocked)
        {
            try
            {
                if (unlocked)
                {
                    ShowUnlockPositionWindows();
                }
                else
                {
                    CloseUnlockPositionWindows();

                    // 설정 화면의 [잠금 해제 모드] 버튼으로 들어온 경우 설정 창으로 복귀
                    if (UiLockService.ConsumeReturnToSettings())
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            try
                            {
                                System.Windows.Application.Current.Windows
                                    .OfType<MenuWindow>()
                                    .FirstOrDefault()?
                                    .OpenSettingsFromExternal();
                            }
                            catch (Exception ex)
                            {
                                AppLogger.Warn("Failed to reopen settings after unlock.", ex);
                            }
                        }));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to toggle unlock position windows.", ex);
            }
        }

        private void ShowUnlockPositionWindows()
        {
            // 어밴던로드 주간 합계
            ShowAbandonRoadSummaryWindow(previewMode: true, restartLifetime: false, activateWindow: false, forcePreview: true);

            // 통합 알림 스택 앵커 (외치기·던전 카운터·누적 경험치·아이템·필드 보스)
            ToastStackService.ShowPositionPreview(_settings);

            // 경험치 추적창
            ShowExpTrackerWindow();

            // 에토스 방향 안내
            var etosHelper = SubAddonWindow.Instance ?? CreateSubAddonWindow();
            etosHelper?.ApplyPositionPreviewVisibility(true);

            // 버프 추적 위치 — 모든 버프가 켜진 최대 크기 미리보기(도우미)로 배치한다
            // (실제 버프창은 잠금 해제 동안 스스로 숨는다)
            var buffHelper = BuffTrackerHelperWindow.Instance ?? CreateBuffTrackerHelperWindow();
            if (buffHelper != null)
            {
                ApplyStoredPosition(buffHelper, _settings.BuffTrackerWindowLeft, _settings.BuffTrackerWindowTop);
                if (!buffHelper.IsVisible)
                    buffHelper.Show();
            }

            // 1:1 대화 에타 표시 위치
            MessengerEtaToastService.ShowPositionPreview(_settings, force: true);

            // 보급품 탈환 미니 지도 위치/크기
            RecaptureSupplyAlertService.ShowPositionPreview(_settings, force: true);
        }

        /// <summary>인스펙터의 넛지/크기 입력으로 메인 창이 조정되면 즉시 저장한다.</summary>
        private void OnUnlockWindowAdjusted(Window window)
        {
            if (!ReferenceEquals(window, this))
                return;

            try
            {
                _settings.WindowWidth = Width;
                _settings.WindowHeight = Height;
                PersistCurrentMainWindowPosition();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to persist main window bounds after unlock adjustment.", ex);
            }
        }

        private void CloseUnlockPositionWindows()
        {
            // 위치 저장 후 미리보기 종료, 각 창의 원래 표시 상태로 복원
            ToastStackService.ClosePositionPreview();
            MessengerEtaToastService.ClosePositionPreview(_settings);
            RecaptureSupplyAlertService.ClosePositionPreview();
            CloseAddonPositionPreviewWindows(savePositions: true, restoreNormalWindows: true);
            RefreshExpTrackerWindow();
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
                    SubAddonWindow.Instance?.Hide();
                    ItemDropHelperWindow.Instance?.Close();
                    BuffTrackerHelperWindow.Instance?.Close();
                    try { _AbandonRoadSummaryWindow?.Close(); } catch { }
                }
                catch { }
                return;
            }

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

            // 인덱스 = 내비*10 + 서브탭. 선택된 서브 탭과 관련된 창만 미리보기로 띄운다.
            int nav = _addonPositionPreviewTabIndex / 10;
            int sub = _addonPositionPreviewTabIndex % 10;

            // 각 창은 해당 기능이 활성화(토글 ON)된 경우에만 미리보기를 띄운다.
            // 토스트류(외치기·던전 카운터·누적 경험치·아이템·필드 보스)는 통합 알림 스택 앵커 하나로 표시.
            switch (nav)
            {
                case 1: // 경험치 추적: 일반 탭 + 누적 알림 켜짐
                    if (sub == 0 && _settings.EnableExperienceLimitAlert)
                        ToastStackService.ShowPositionPreview(_settings);
                    break;
                case 2: // 던전 도우미
                    switch (sub)
                    {
                        case 2: // 이클립스: 에토스 방향 + 보급품 탈환 미니 지도
                            if (_settings.ShowEtosDirectionAlert)
                            {
                                var etosHelper = SubAddonWindow.Instance ?? CreateSubAddonWindow();
                                etosHelper?.ApplyPositionPreviewVisibility(true);
                            }
                            if (_settings.ShowRecaptureSupplyMap)
                                RecaptureSupplyAlertService.ShowPositionPreview(_settings, force: true);
                            break;
                        case 3: // 어밴던로드: 알림 앵커 + 통계 창
                            if (_settings.EnableAbandonRoadCountAlert)
                                ToastStackService.ShowPositionPreview(_settings);
                            if (_settings.ShowAbandonRoadSummaryWindow)
                            {
                                ShowAbandonRoadSummaryWindow(previewMode: true, restartLifetime: false, activateWindow: false, forcePreview: true);
                                if (_AbandonRoadSummaryWindow != null)
                                    _AbandonRoadSummaryWindow.Topmost = true;
                            }
                            break;
                        case 4: // 갈망하는 즐거움: 알림 앵커
                            if (_settings.EnableCravingPleasureCountAlert)
                                ToastStackService.ShowPositionPreview(_settings);
                            break;
                            // 0(룬·테시스)·1(어비스)는 소리 알림뿐이라 창 없음
                    }
                    break;
                case 3: // 아이템 알림: 획득 알림 탭 + 획득 알림 켜짐
                    if (sub == 0 && _settings.ShowItemDropAlert)
                        ToastStackService.ShowPositionPreview(_settings);
                    break;
                case 4: // 버프 추적 켜짐
                    if (_settings.EnableBuffTrackerAlert)
                    {
                        var buffHelper = BuffTrackerHelperWindow.Instance ?? CreateBuffTrackerHelperWindow();
                        if (buffHelper != null)
                        {
                            ApplyStoredPosition(buffHelper, _settings.BuffTrackerWindowLeft, _settings.BuffTrackerWindowTop);
                            if (!buffHelper.IsVisible)
                                buffHelper.Show();
                        }
                    }
                    break;
                case 5: // 필드 보스: 알림 앵커
                    ToastStackService.ShowPositionPreview(_settings);
                    break;
            }
        }

        private void CloseAddonPositionPreviewWindows(bool savePositions, bool restoreNormalWindows)
        {
            if (savePositions)
            {
                ToastStackService.SaveCurrentPosition(_settings);

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

            ToastStackService.ClosePositionPreview();
            RecaptureSupplyAlertService.ClosePositionPreview();
            SubAddonWindow.Instance?.Hide();
            ItemDropHelperWindow.Instance?.Close();
            BuffTrackerHelperWindow.Instance?.Close();

            if (restoreNormalWindows)
            {
                ApplySubAddonWindowSettings();
                ApplyItemDropHelperWindowSettings();
                ApplyBuffTrackerHelperWindowSettings();
                ApplyBuffTrackerWindowSettings(); // 잠금 해제 동안 닫혀 있던 실제 버프창 복원
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
                else
                {
                    helper.Close(); // 대기 중인 창을 유지하지 않는다 (메모리)
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
                // 창은 실제로 보여줄 때만 만든다 — 대기용 창을 상주시키지 않는다 (메모리)
                bool shouldShow = _settings.EnableBuffTrackerAlert && _buffTrackerService.HasAnyActiveBuffs;
                if (BuffTrackerWindow.Instance == null && !shouldShow)
                    return;

                var window = BuffTrackerWindow.Instance ?? CreateBuffTrackerWindow();
                if (window == null)
                    return;

                if (_settings.BuffTrackerWindowLeft.HasValue)
                    window.Left = _settings.BuffTrackerWindowLeft.Value;
                if (_settings.BuffTrackerWindowTop.HasValue)
                    window.Top = _settings.BuffTrackerWindowTop.Value;

                // Buff tracker visibility is managed independently from the main chat overlay.
                window.ApplyVisibility();
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
                bool shouldShow = _settings.ShowDailyWeeklyContentOverlay;

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
                else if (_dailyWeeklyContentOverlay != null)
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
                    try { _AbandonRoadSummaryWindow.Close(); } catch { } // 사용하지 않을 땐 닫아 메모리 회수
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

                // Keep the helper window in sync with its own setting, not the main overlay.
                if (_isAddonPositionMode || _settings.ShowBuffTrackerWindow)
                {
                    if (!helper.IsVisible)
                        helper.Show();
                }
                else
                {
                    helper.Close(); // 대기 중인 창을 유지하지 않는다 (메모리)
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to apply BuffTrackerHelperWindow settings.", ex);
            }
        }
    }
}
