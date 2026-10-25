using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TWChatOverlay.Views.Unlock;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 잠금 해제 모드(Unlock Mode): 오버레이 창 이동은 이 모드에서만 허용됩니다.
    /// 모드가 켜지면 화면 전체가 살짝 어두워지며 격자가 표시되고(클릭 통과),
    /// 주 모니터 상단 중앙에 안내/완료 배너가 뜹니다. 모드를 끝내면 모든 창이 잠깁니다.
    /// 보조 창(격자·배너·하이라이트·인스펙터)은 Views/Overlays/Unlock에 있고, 이 서비스는 상태·선택·z-순서만 든다.
    /// </summary>
    public sealed class UiLockService
    {
        private BackdropWindow? _backdrop;
        private BannerWindow? _banner;
        private HighlightWindow? _highlight;
        private InspectorWindow? _inspector;
        private Window? _selected;

        public bool IsUnlocked { get; private set; }

        /// <summary>자석 스냅 모드. 시작 시 설정에서 로드되고 배너의 토글로 바뀐다.</summary>
        public bool SnapEnabled { get; set; }

        private bool _isAdjustingSelected; // 인스펙터發 이동/크기 변경 중 (스냅 제외)
        private bool _isSnappingSelected;  // 스냅 적용 중 재진입 방지

        /// <summary>잠금 해제 모드의 보조 UI(격자/배너/하이라이트/인스펙터)인지 — 스냅 대상에서 제외.</summary>
        public static bool IsUnlockChrome(Window window)
            => window is BackdropWindow or BannerWindow or HighlightWindow or InspectorWindow;

        /// <summary>모드 변경 시 발생. 인자 = 새 잠금 해제 상태.</summary>
        public event Action<bool>? UnlockChanged;

        /// <summary>설정 화면에서 잠금 해제로 들어온 경우, 종료 시 설정 창으로 복귀하기 위한 플래그.</summary>
        public bool ReturnToSettingsOnLock { get; set; }

        /// <summary>복귀 플래그를 읽고 초기화한다.</summary>
        public bool ConsumeReturnToSettings()
        {
            bool value = ReturnToSettingsOnLock;
            ReturnToSettingsOnLock = false;
            return value;
        }

        /// <summary>넛지/크기 입력으로 창 위치·크기가 바뀐 뒤 발생. 창별 저장 로직을 연결한다.</summary>
        public event Action<Window>? WindowAdjusted;

        public void Toggle() => Set(!IsUnlocked);

        public void Set(bool unlocked)
        {
            if (IsUnlocked == unlocked) return;
            IsUnlocked = unlocked;

            try
            {
                if (unlocked)
                {
                    _backdrop ??= new BackdropWindow();
                    _banner ??= new BannerWindow();
                    _backdrop.Show();
                    _banner.Show();
                    RaiseOverlaysAboveBackdrop();
                }
                else
                {
                    ClearSelection();
                    // 격자 백드롭은 가상 화면 전체 크기의 투명 창이라 숨겨두면 그만큼 메모리를 계속 차지한다.
                    // 잠금 해제가 끝나면 닫고, 다음 진입 때 새로 만든다.
                    CloseQuietly(ref _banner);
                    CloseQuietly(ref _backdrop);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Unlock overlay toggle failed.", ex);
            }

            AppLogger.Info($"UI unlock mode -> {unlocked}.");
            UnlockChanged?.Invoke(unlocked);
        }

        /// <summary>
        /// 잠금 해제 모드에서 창을 선택한다(드래그 시작 시 각 창의 핸들러가 호출).
        /// 선택된 창은 하이라이트 테두리와 인스펙터(1px 이동/크기 조절)로 편집한다.
        /// </summary>
        public void Select(Window? window)
        {
            if (!IsUnlocked || window == null) return;
            if (ReferenceEquals(_selected, window))
            {
                _inspector?.RefreshValues();
                return;
            }

            DetachSelectionHandlers();
            _selected = window;
            window.Closed += Selected_Closed;
            window.LocationChanged += Selected_BoundsChanged;
            window.SizeChanged += Selected_SizeChanged;

            try
            {
                _highlight ??= new HighlightWindow();
                _highlight.SyncTo(window);
                _highlight.Show();

                _inspector ??= new InspectorWindow();
                _inspector.SetTarget(window);
                _inspector.Show();
                // 선택할 때 한 번만 맨 위로 올린다 (Show 전에는 핸들이 없어 SetTarget 안에서는 올릴 수 없다).
                // 이후 다른 창이 덮으면 ReassertZOrder가 BringToFront로 다시 올린다.
                _inspector.BringToFront();

                AppLogger.Info($"Unlock select -> {GetFriendlyName(window)}.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Unlock selection UI failed.", ex);
            }
        }

        private void Selected_Closed(object? sender, EventArgs e) => ClearSelection();

        private void Selected_BoundsChanged(object? sender, EventArgs e)
        {
            if (_selected == null) return;

            // 자석 모드: 선택한 창을 드래그하는 동안 다른 창 가장자리에 붙인다.
            // 인스펙터의 넛지/좌표 입력(_isAdjustingSelected)은 1px 조정을 방해하므로 제외.
            if (SnapEnabled && !_isAdjustingSelected && !_isSnappingSelected)
            {
                _isSnappingSelected = true;
                try { AppServices.Get<ChatWindowHub>().TryApplyMagneticSnap(_selected); }
                catch { }
                finally { _isSnappingSelected = false; }
            }

            _highlight?.SyncTo(_selected);
            _inspector?.RefreshValues();
        }

        private void Selected_SizeChanged(object? sender, SizeChangedEventArgs e)
            => Selected_BoundsChanged(sender, EventArgs.Empty);

        private void DetachSelectionHandlers()
        {
            if (_selected == null) return;
            _selected.Closed -= Selected_Closed;
            _selected.LocationChanged -= Selected_BoundsChanged;
            _selected.SizeChanged -= Selected_SizeChanged;
            _selected = null;
        }

        private void ClearSelection()
        {
            DetachSelectionHandlers();
            // 선택 UI도 대기시키지 않고 닫는다 — 다음 선택 때 새로 만든다
            CloseQuietly(ref _highlight);
            CloseQuietly(ref _inspector);
        }

        /// <summary>보조 창을 닫고 참조를 비운다. 이미 닫혔거나 예외가 나도 무시.</summary>
        private void CloseQuietly<T>(ref T? window) where T : Window
        {
            var target = window;
            window = null;
            if (target == null) return;
            try { target.Close(); } catch { }
        }

        internal void NudgeSelected(int dx, int dy)
        {
            var window = _selected;
            if (window == null) return;

            _isAdjustingSelected = true;
            try
            {
                window.Left += dx;
                window.Top += dy;
                WindowAdjusted?.Invoke(window);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Unlock nudge failed.", ex);
            }
            finally
            {
                _isAdjustingSelected = false;
            }
        }

        /// <summary>인스펙터 좌표 입력으로 선택된 창을 지정 위치로 옮긴다.</summary>
        internal void MoveSelected(double? left, double? top)
        {
            var window = _selected;
            if (window == null) return;

            _isAdjustingSelected = true;
            try
            {
                // 창이 화면 밖으로 완전히 사라지지 않게 가상 화면 안으로 보정한다 (ScreenBoundsHelper, 40px는 남긴다)
                var (clampedLeft, clampedTop) = ScreenBoundsHelper.ClampToVirtualScreen(
                    left ?? window.Left, top ?? window.Top, window.ActualWidth, window.ActualHeight);
                if (left.HasValue)
                    window.Left = clampedLeft;
                if (top.HasValue)
                    window.Top = clampedTop;
                WindowAdjusted?.Invoke(window);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Unlock move failed.", ex);
            }
            finally
            {
                _isAdjustingSelected = false;
            }
        }

        internal void ResizeSelected(double? width, double? height)
        {
            var window = _selected;
            if (window == null) return;

            _isAdjustingSelected = true;
            try
            {
                if (width.HasValue)
                    window.Width = ClampSize(width.Value, window.MinWidth, window.MaxWidth);
                if (height.HasValue)
                    window.Height = ClampSize(height.Value, window.MinHeight, window.MaxHeight);
                WindowAdjusted?.Invoke(window);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Unlock resize failed.", ex);
            }
            finally
            {
                _isAdjustingSelected = false;
            }
        }

        private static double ClampSize(double value, double min, double max)
        {
            double lower = Math.Max(40, double.IsNaN(min) ? 0 : min);
            double upper = double.IsInfinity(max) || double.IsNaN(max) || max <= 0 ? 4096 : max;
            return Math.Max(lower, Math.Min(upper, value));
        }

        /// <summary>
        /// 창별 투명도 저장 키 (서브 채팅창은 슬롯 구분).
        /// 잠금 해제에서 보이는 미리보기(도우미) 창은 실제 창과 같은 키를 써서
        /// 미리보기에서 조절한 값이 실제 창에도 적용되게 한다.
        /// </summary>
        private static string GetOpacityKey(Window window)
            => window switch
            {
                TWChatOverlay.Views.ChatCloneWindow clone => $"ChatCloneWindow{clone.Slot}",
                TWChatOverlay.Views.BuffTrackerHelperWindow => nameof(TWChatOverlay.Views.BuffTrackerWindow),
                TWChatOverlay.Views.ItemDropHelperWindow => nameof(TWChatOverlay.Views.ItemDropToastWindow),
                _ => window.GetType().Name,
            };

        /// <summary>저장된 창별 투명도(10~100%)가 있으면 적용한다. 창 생성/표시 시 호출.</summary>
        public static void ApplyStoredOpacity(Window window)
        {
            if (window == null) return;

            try
            {
                var settings = ToastPresentationHelper.FindSharedSettings();
                if (settings?.WindowOpacityPercents != null &&
                    settings.WindowOpacityPercents.TryGetValue(GetOpacityKey(window), out double percent))
                {
                    // 표시 애니메이션이 Opacity를 잡고 있으면 직접 설정이 무시되므로 해제
                    window.BeginAnimation(UIElement.OpacityProperty, null);
                    window.Opacity = Math.Max(0.1, Math.Min(1.0, percent / 100.0));
                }
            }
            catch { }
        }

        /// <summary>인스펙터 슬라이더로 선택된 창의 투명도를 지정하고 창별로 저장한다.</summary>
        internal void SetSelectedOpacity(double percent)
        {
            var window = _selected;
            if (window == null) return;

            percent = Math.Max(10.0, Math.Min(100.0, Math.Round(percent)));
            try
            {
                // 토스트류는 표시할 때 Opacity 애니메이션을 걸어두는데,
                // 애니메이션이 잡고 있는 동안에는 직접 값이 무시되므로 먼저 해제한다
                window.BeginAnimation(UIElement.OpacityProperty, null);
                window.Opacity = percent / 100.0;

                string key = GetOpacityKey(window);

                var settings = ToastPresentationHelper.FindSharedSettings();
                if (settings != null)
                {
                    settings.WindowOpacityPercents[key] = percent;
                    ConfigService.SaveDeferred(settings);
                }

                // 같은 키를 공유하는 다른 창(미리보기 뒤에 숨은 실제 창 등)에도 즉시 반영
                foreach (Window other in Application.Current.Windows)
                {
                    if (ReferenceEquals(other, window) || GetOpacityKey(other) != key)
                        continue;

                    other.BeginAnimation(UIElement.OpacityProperty, null);
                    other.Opacity = percent / 100.0;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Unlock opacity change failed.", ex);
            }
        }

        /// <summary>선택된 창이 폰트 크기 조절을 지원하면 (읽기, 쓰기) 접근자를 돌려준다.</summary>
        private (Func<Models.ChatSettings, double> Get, Action<Models.ChatSettings, double> Set)? GetFontAccessor(Window window)
            => window switch
            {
                TWChatOverlay.Views.DungeonCountDisplayWindow => (s => s.DungeonCountDisplayFontSize, (s, v) => s.DungeonCountDisplayFontSize = v),
                TWChatOverlay.Views.ShoutToastWindow => (s => s.ShoutToastFontSize, (s, v) => s.ShoutToastFontSize = v),
                TWChatOverlay.Views.ExperienceAlertWindow => (s => s.ExperienceAlertFontSize, (s, v) => s.ExperienceAlertFontSize = v),
                TWChatOverlay.Views.ItemDropHelperWindow => (s => s.ItemDropToastFontSize, (s, v) => s.ItemDropToastFontSize = v),
                TWChatOverlay.Views.MessengerEtaToastWindow => (s => s.MessengerEtaFontSize, (s, v) => s.MessengerEtaFontSize = v),
                TWChatOverlay.Views.BossAlertToastWindow => (s => s.BossAlertToastFontSize, (s, v) => s.BossAlertToastFontSize = v),
                TWChatOverlay.Views.ContentTimerWindow => (s => s.ContentTimerFontSize, (s, v) => s.ContentTimerFontSize = v),
                _ => ((Func<Models.ChatSettings, double>, Action<Models.ChatSettings, double>)?)null,
            };

        internal double? GetSelectedFontSize()
        {
            var window = _selected;
            var accessor = window == null ? null : GetFontAccessor(window);
            var settings = ToastPresentationHelper.FindSharedSettings();
            if (accessor == null || settings == null) return null;
            return accessor.Value.Get(settings);
        }

        /// <summary>인스펙터 슬라이더로 선택된 알림창의 폰트 크기를 지정·저장하고 즉시 반영한다.</summary>
        internal void SetSelectedFontSize(double size)
        {
            var window = _selected;
            if (window == null) return;

            var accessor = GetFontAccessor(window);
            var settings = ToastPresentationHelper.FindSharedSettings();
            if (accessor == null || settings == null) return;

            size = Math.Max(10.0, Math.Min(40.0, Math.Round(size)));
            try
            {
                accessor.Value.Set(settings, size);
                ConfigService.SaveDeferred(settings);

                switch (window)
                {
                    case TWChatOverlay.Views.DungeonCountDisplayWindow d: d.SetFontSize(size); break;
                    case TWChatOverlay.Views.ShoutToastWindow s: s.SetFontSize(size); break;
                    case TWChatOverlay.Views.ExperienceAlertWindow e: e.SetFontSize(size); break;
                    case TWChatOverlay.Views.ItemDropHelperWindow i: i.SetFontSize(size); break;
                    case TWChatOverlay.Views.MessengerEtaToastWindow m: m.SetFontSize(size); break;
                    case TWChatOverlay.Views.BossAlertToastWindow b: b.SetFontSize(size); break;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Unlock font size change failed.", ex);
            }
        }

        private static string GetFriendlyName(Window window)
        {
            if (window is TWChatOverlay.Views.ChatCloneWindow clone)
                return $"서브 채팅창 {clone.Slot}";

            return window.GetType().Name switch
            {
                "MainWindow" => "채팅창",
                "AbandonRoadSummaryWindow" => "어밴던로드 통계",
                "BossAlertToastWindow" => "필드 보스 알림창",
                "ExpTrackerWindow" => "경험치 추적창",
                "ExperienceAlertWindow" => "경험치 누적 알림",
                "DungeonCountDisplayWindow" => "던전 도우미",
                "DungeonAlertWindow" => "던전 특수 알림",
                "SubAddonWindow" => "에토스 방향 안내",
                "ItemDropHelperWindow" => "아이템 드롭 알림",
                "BuffTrackerHelperWindow" => "버프 추적",
                "BuffTrackerWindow" => "버프 추적",
                "ShoutToastWindow" => "외치기 팝업",
                "MessengerEtaToastWindow" => "1:1 대화 에타 표시",
                "ToastStackPreviewWindow" => string.IsNullOrWhiteSpace(window.Title) ? "알림 표시 위치" : window.Title,
                "RecaptureSupplyWindow" => "보급품 탈환",
                "TreasurySummaryWindow" => "보물창고 통계",
                _ => string.IsNullOrWhiteSpace(window.Title) ? window.GetType().Name : window.Title,
            };
        }

        /// <summary>
        /// 잠금 해제 중 다른 프로그램의 topmost 창(예: Magpie 확대 창)이 격자·오버레이를 덮었을 때
        /// 격자 → 오버레이·배너 → 인스펙터 순서로 다시 올린다. 잠금 상태면 아무것도 하지 않는다.
        /// </summary>
        public void ReassertZOrder()
        {
            if (!IsUnlocked)
                return;

            try
            {
                if (_backdrop?.IsVisible == true)
                    TopmostWindowHelper.BringToTopmost(_backdrop);
                RaiseOverlaysAboveBackdrop();
                _inspector?.BringToFront();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to reassert unlock z-order.", ex);
            }
        }

        /// <summary>
        /// 백드롭보다 우리 오버레이 창/배너가 위에 오도록 Topmost 밴드 최상단으로 재삽입한다.
        /// (백드롭이 나중에 Show되면 같은 밴드 최상단에 끼어들어 격자가 오버레이 위에 그려짐)
        /// </summary>
        private void RaiseOverlaysAboveBackdrop()
        {
            var app = Application.Current;
            if (app == null) return;

            foreach (Window window in app.Windows)
            {
                if (ReferenceEquals(window, _backdrop) || !window.IsVisible)
                    continue;

                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                    continue;

                NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        }
    }
}
