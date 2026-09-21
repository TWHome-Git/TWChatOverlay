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
    /// <summary>메뉴/버튼 클릭과 설정 변경(PropertyChanged) 라우팅</summary>
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
            // 설정 화면의 단순 설정은 ChatSettings에 직접 바인딩되어 ViewModel의 저장 경로를 거치지 않는다.
            // 원본이 바뀌면 여기서 디바운스 저장한다. (250ms 안에 몰리는 변경은 한 번만 쓴다)
            ConfigService.SaveDeferred(_settings);

            // 추가 기능 위치 미리보기 중 토글이 바뀌면 해당 탭의 창 표시를 다시 계산한다
            // (활성화하면 즉시 나타나고, 끄면 사라진다)
            if (_isAddonPositionMode && e.PropertyName != null && AddonPreviewToggleNames.Contains(e.PropertyName))
            {
                try { ShowSettingsPositionWindows(); } catch (Exception ex) { AppLogger.Warn("Addon preview refresh failed.", ex); }
            }

            Dispatcher.Invoke(() =>
            {
                if (e.PropertyName != null && SettingsChangeRoutes.TryGetValue(e.PropertyName, out Action? handler))
                    handler();
                else if (e.PropertyName != null && e.PropertyName.StartsWith("Show"))
                    RequestRefreshLogDisplay(); // 그 밖의 Show* (채팅 표시 필터류)는 채팅 표시만 다시 그린다

                PersistSettings();
            });
        }

        /// <summary>추가 기능 위치 미리보기 중 바뀌면 미리보기 창 표시를 다시 계산해야 하는 토글들.</summary>
        private static readonly HashSet<string> AddonPreviewToggleNames = new(StringComparer.Ordinal)
        {
            nameof(ChatSettings.EnableExperienceLimitAlert),
            nameof(ChatSettings.EnableAbandonRoadCountAlert),
            nameof(ChatSettings.EnableCravingPleasureCountAlert),
            nameof(ChatSettings.ShowAbandonRoadSummaryWindow),
            nameof(ChatSettings.ShowEtosDirectionAlert),
            nameof(ChatSettings.ShowRecaptureSupplyMap),
            nameof(ChatSettings.ShowItemDropAlert),
            nameof(ChatSettings.EnableBuffTrackerAlert),
        };

        private Dictionary<string, Action>? _settingsChangeRoutes;

        /// <summary>설정 프로퍼티 이름 → 반응. 한 프로퍼티는 한 핸들러만 갖는다 (표에 없는 Show*는 채팅 표시 갱신).</summary>
        private Dictionary<string, Action> SettingsChangeRoutes => _settingsChangeRoutes ??= BuildSettingsChangeRoutes();

        private Dictionary<string, Action> BuildSettingsChangeRoutes()
        {
            var routes = new Dictionary<string, Action>(StringComparer.Ordinal);
            void Map(Action action, params string[] names)
            {
                foreach (string name in names)
                    routes[name] = action;
            }

            // 채팅 표시
            Map(() => { ApplyInitialSettings(); RequestRefreshLogDisplay(); },
                nameof(ChatSettings.FontFamily), nameof(ChatSettings.FontSize));
            Map(() => _stickyService?.UpdatePositionImmediately(),
                nameof(ChatSettings.LineMargin), nameof(ChatSettings.LineMarginLeft));
            Map(OnMainWindowChatTabTagChanged, nameof(ChatSettings.MainWindowChatTabTag));

            // 부속 창 표시
            Map(ApplyDailyWeeklyWindowVisibility, nameof(ChatSettings.ShowDailyWeeklyContentOverlay));
            Map(OnEtosDirectionAlertSettingChanged, nameof(ChatSettings.ShowEtosDirectionAlert));
            Map(OnEtosHelperWindowSettingChanged, nameof(ChatSettings.ShowEtosHelperWindow));
            Map(ApplyItemDropHelperWindowSettings, nameof(ChatSettings.ShowItemDropHelperWindow));
            Map(RefreshExpTrackerWindow, nameof(ChatSettings.ShowExpTracker));
            Map(OnAbandonRoadSummaryWindowSettingChanged, nameof(ChatSettings.ShowAbandonRoadSummaryWindow));
            Map(OnDungeonCountDisplayWindowSettingChanged, nameof(ChatSettings.ShowDungeonCountDisplayWindow));

            // 알림
            Map(() => ToastStackService.RefreshPreviews(_settings), nameof(ChatSettings.UnifiedToastStack)); // 통합/분리 전환: 미리보기와 열린 알림 재배치
            Map(OnExperienceLimitAlertSettingChanged, nameof(ChatSettings.EnableExperienceLimitAlert));
            Map(OnExperienceLimitAlertWindowSettingChanged, nameof(ChatSettings.ShowExperienceLimitAlertWindow));
            Map(() => ExperienceAlertWindowService.RefreshState(_settings), nameof(ChatSettings.ExperienceLimitTotalExp));

            // 버프 추적
            Map(ApplyBuffTrackerWindowSettings, nameof(ChatSettings.EnableBuffTrackerAlert));
            Map(() => { ApplyBuffTrackerWindowSettings(); ApplyBuffTrackerHelperWindowSettings(); },
                nameof(ChatSettings.BuffTrackerWindowLeft), nameof(ChatSettings.BuffTrackerWindowTop));

            // 단축키
            Map(ApplyHotKeys,
                nameof(ChatSettings.ExitHotKey), nameof(ChatSettings.ToggleOverlayHotKey), nameof(ChatSettings.ToggleDailyWeeklyContentHotKey),
                nameof(ChatSettings.ToggleSettingsHotKey), nameof(ChatSettings.ToggleTrayAllHotKey), nameof(ChatSettings.ToggleUnlockHotKey));

            return routes;
        }

        private void OnMainWindowChatTabTagChanged()
        {
            string normalizedTabTag = NormalizeMainTabTag(_settings.MainWindowChatTabTag);
            if (!string.Equals(_currentTabTag, normalizedTabTag, StringComparison.Ordinal))
                ApplyMainTabState(normalizedTabTag, persistSettings: false, refreshLogDisplay: false);
        }

        private void OnEtosDirectionAlertSettingChanged()
        {
            if (!_settings.ShowEtosDirectionAlert)
            {
                if (_isInitialSetupWizardRunning)
                    SubAddonWindow.Instance?.ApplyPositionPreviewVisibility(true);
                else
                    SubAddonWindow.Instance?.HideAlert();
                return;
            }

            ApplyEtosHelperAfterToggleOn();
        }

        private void OnEtosHelperWindowSettingChanged()
        {
            if (!_settings.ShowEtosHelperWindow)
            {
                var helper = SubAddonWindow.Instance;
                if (helper != null)
                {
                    _settings.SubAddonWindowLeft = helper.Left;
                    _settings.SubAddonWindowTop = helper.Top;
                }

                ApplySubAddonWindowSettings();
                PersistSettings();
                return;
            }

            ApplyEtosHelperAfterToggleOn();
        }

        private void ApplyEtosHelperAfterToggleOn()
        {
            if (_isInitialSetupWizardRunning)
            {
                ApplySubAddonWindowSettings();
                SubAddonWindow.Instance?.ApplyPositionPreviewVisibility(true);
            }
            else if (!_isAddonPositionMode)
            {
                // 추가 기능 미리보기 중에는 미리보기 갱신이 표시를 관리한다
                // (여기서 ApplySubAddonWindowSettings를 부르면 방금 띄운 미리보기가 숨겨진다)
                ApplySubAddonWindowSettings();
            }
        }

        private void OnExperienceLimitAlertSettingChanged()
        {
            if (_settings.EnableExperienceLimitAlert && _settings.ShowExperienceLimitAlertWindow)
                ExperienceAlertWindowService.ShowPositionPreview(_settings);
            else if (!_settings.EnableExperienceLimitAlert && !_settings.ShowExperienceLimitAlertWindow)
                ExperienceAlertWindowService.Close();

            ExperienceAlertWindowService.RefreshState(_settings);
        }

        private void OnExperienceLimitAlertWindowSettingChanged()
        {
            if (_settings.ShowExperienceLimitAlertWindow)
                ExperienceAlertWindowService.ShowPositionPreview(_settings);
            else
                ExperienceAlertWindowService.Close();
        }

        private void OnDungeonCountDisplayWindowSettingChanged()
        {
            if (_settings.ShowDungeonCountDisplayWindow)
                AppServices.Get<DungeonCountDisplayWindowService>().ShowPositionPreview(_settings);
            else
                AppServices.Get<DungeonCountDisplayWindowService>().ClosePositionPreview(_settings);
        }

        private void OnAbandonRoadSummaryWindowSettingChanged()
        {
            if (_isInitialSetupWizardRunning)
            {
                ShowAbandonRoadSummaryWindow(previewMode: true, restartLifetime: false, activateWindow: false, forcePreview: true);
            }
            else if (_settings.ShowAbandonRoadSummaryWindow && _isAddonPositionMode)
            {
                ShowAbandonRoadSummaryWindow(previewMode: _isAddonPositionMode);
            }
            else if (_AbandonRoadSummaryWindow != null)
            {
                try { _AbandonRoadSummaryWindow.Close(); } catch { }
            }
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
