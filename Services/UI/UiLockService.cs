using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 잠금 해제 모드(Unlock Mode): 오버레이 창 이동은 이 모드에서만 허용됩니다.
    /// 모드가 켜지면 화면 전체가 살짝 어두워지며 격자가 표시되고(클릭 통과),
    /// 주 모니터 상단 중앙에 안내/완료 배너가 뜹니다. 모드를 끝내면 모든 창이 잠깁니다.
    /// </summary>
    public static class UiLockService
    {
        private static BackdropWindow? _backdrop;
        private static BannerWindow? _banner;
        private static HighlightWindow? _highlight;
        private static InspectorWindow? _inspector;
        private static Window? _selected;

        public static bool IsUnlocked { get; private set; }

        /// <summary>자석 스냅 모드. 시작 시 설정에서 로드되고 배너의 토글로 바뀐다.</summary>
        public static bool SnapEnabled { get; set; }

        private static bool _isAdjustingSelected; // 인스펙터發 이동/크기 변경 중 (스냅 제외)
        private static bool _isSnappingSelected;  // 스냅 적용 중 재진입 방지

        /// <summary>잠금 해제 모드의 보조 UI(격자/배너/하이라이트/인스펙터)인지 — 스냅 대상에서 제외.</summary>
        public static bool IsUnlockChrome(Window window)
            => window is BackdropWindow or BannerWindow or HighlightWindow or InspectorWindow;

        /// <summary>현재 선택된(편집 중인) 창. 잠금 해제 모드에서만 값이 있다.</summary>
        public static Window? SelectedWindow => _selected;

        /// <summary>모드 변경 시 발생. 인자 = 새 잠금 해제 상태.</summary>
        public static event Action<bool>? UnlockChanged;

        /// <summary>넛지/크기 입력으로 창 위치·크기가 바뀐 뒤 발생. 창별 저장 로직을 연결한다.</summary>
        public static event Action<Window>? WindowAdjusted;

        public static void Toggle() => Set(!IsUnlocked);

        public static void Set(bool unlocked)
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
        public static void Select(Window? window)
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

                AppLogger.Info($"Unlock select -> {GetFriendlyName(window)}.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Unlock selection UI failed.", ex);
            }
        }

        private static void Selected_Closed(object? sender, EventArgs e) => ClearSelection();

        private static void Selected_BoundsChanged(object? sender, EventArgs e)
        {
            if (_selected == null) return;

            // 자석 모드: 선택한 창을 드래그하는 동안 다른 창 가장자리에 붙인다.
            // 인스펙터의 넛지/좌표 입력(_isAdjustingSelected)은 1px 조정을 방해하므로 제외.
            if (SnapEnabled && !_isAdjustingSelected && !_isSnappingSelected)
            {
                _isSnappingSelected = true;
                try { ChatWindowHub.TryApplyMagneticSnap(_selected); }
                catch { }
                finally { _isSnappingSelected = false; }
            }

            _highlight?.SyncTo(_selected);
            _inspector?.RefreshValues();
        }

        private static void Selected_SizeChanged(object? sender, SizeChangedEventArgs e)
            => Selected_BoundsChanged(sender, EventArgs.Empty);

        private static void DetachSelectionHandlers()
        {
            if (_selected == null) return;
            _selected.Closed -= Selected_Closed;
            _selected.LocationChanged -= Selected_BoundsChanged;
            _selected.SizeChanged -= Selected_SizeChanged;
            _selected = null;
        }

        private static void ClearSelection()
        {
            DetachSelectionHandlers();
            // 선택 UI도 대기시키지 않고 닫는다 — 다음 선택 때 새로 만든다
            CloseQuietly(ref _highlight);
            CloseQuietly(ref _inspector);
        }

        /// <summary>보조 창을 닫고 참조를 비운다. 이미 닫혔거나 예외가 나도 무시.</summary>
        private static void CloseQuietly<T>(ref T? window) where T : Window
        {
            var target = window;
            window = null;
            if (target == null) return;
            try { target.Close(); } catch { }
        }

        internal static void NudgeSelected(int dx, int dy)
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
        internal static void MoveSelected(double? left, double? top)
        {
            var window = _selected;
            if (window == null) return;

            _isAdjustingSelected = true;
            try
            {
                if (left.HasValue)
                    window.Left = ClampToScreen(left.Value, SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenWidth, window.ActualWidth);
                if (top.HasValue)
                    window.Top = ClampToScreen(top.Value, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenHeight, window.ActualHeight);
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

        /// <summary>창이 화면 밖으로 완전히 사라지지 않도록 최소 VisibleMargin 만큼은 남긴다.</summary>
        private static double ClampToScreen(double value, double screenStart, double screenLength, double windowLength)
        {
            const double VisibleMargin = 40;
            double size = windowLength > 0 ? windowLength : VisibleMargin;
            double lower = screenStart - Math.Max(0, size - VisibleMargin);
            double upper = screenStart + screenLength - VisibleMargin;
            return upper < lower ? lower : Math.Max(lower, Math.Min(upper, value));
        }

        internal static void ResizeSelected(double? width, double? height)
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
        internal static void SetSelectedOpacity(double percent)
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
        private static (Func<Models.ChatSettings, double> Get, Action<Models.ChatSettings, double> Set)? GetFontAccessor(Window window)
            => window switch
            {
                TWChatOverlay.Views.DungeonCountDisplayWindow => (s => s.DungeonCountDisplayFontSize, (s, v) => s.DungeonCountDisplayFontSize = v),
                TWChatOverlay.Views.ShoutToastWindow => (s => s.ShoutToastFontSize, (s, v) => s.ShoutToastFontSize = v),
                TWChatOverlay.Views.ExperienceAlertWindow => (s => s.ExperienceAlertFontSize, (s, v) => s.ExperienceAlertFontSize = v),
                TWChatOverlay.Views.ItemDropHelperWindow => (s => s.ItemDropToastFontSize, (s, v) => s.ItemDropToastFontSize = v),
                TWChatOverlay.Views.MessengerEtaToastWindow => (s => s.MessengerEtaFontSize, (s, v) => s.MessengerEtaFontSize = v),
                TWChatOverlay.Views.BossAlertToastWindow => (s => s.BossAlertToastFontSize, (s, v) => s.BossAlertToastFontSize = v),
                _ => ((Func<Models.ChatSettings, double>, Action<Models.ChatSettings, double>)?)null,
            };

        internal static double? GetSelectedFontSize()
        {
            var window = _selected;
            var accessor = window == null ? null : GetFontAccessor(window);
            var settings = ToastPresentationHelper.FindSharedSettings();
            if (accessor == null || settings == null) return null;
            return accessor.Value.Get(settings);
        }

        /// <summary>인스펙터 슬라이더로 선택된 알림창의 폰트 크기를 지정·저장하고 즉시 반영한다.</summary>
        internal static void SetSelectedFontSize(double size)
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
                "AbaddonRoadSummaryWindow" => "어밴던로드 통계",
                "BossAlertToastWindow" => "필드 보스 알림창",
                "ExpTrackerWindow" => "경험치 추적창",
                "ExperienceAlertWindow" => "경험치 누적 알림",
                "DungeonCountDisplayWindow" => "던전 카운터",
                "SubAddonWindow" => "에토스 방향 안내",
                "ItemDropHelperWindow" => "아이템 드롭 알림",
                "BuffTrackerHelperWindow" => "버프 추적",
                "BuffTrackerWindow" => "버프 추적",
                "ShoutToastWindow" => "외치기 팝업",
                "MessengerEtaToastWindow" => "1:1 대화 에타 표시",
                "RecaptureSupplyWindow" => "탈환 보급 알림",
                _ => string.IsNullOrWhiteSpace(window.Title) ? window.GetType().Name : window.Title,
            };
        }

        /// <summary>WS_EX_TRANSPARENT + WS_EX_NOACTIVATE: 모든 마우스 입력이 아래 창으로 통과.</summary>
        private static void ApplyClickThrough(Window window)
        {
            try
            {
                var handle = new WindowInteropHelper(window).Handle;
                const int GWL_EXSTYLE = -20;
                const int WS_EX_TRANSPARENT = 0x00000020;
                const int WS_EX_NOACTIVATE = 0x08000000;
                const int WS_EX_TOOLWINDOW = 0x00000080;
                int ex = NativeMethods.GetWindowLong(handle, GWL_EXSTYLE);
                NativeMethods.SetWindowLong(handle, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Click-through setup failed.", ex);
            }
        }

        /// <summary>
        /// 백드롭보다 우리 오버레이 창/배너가 위에 오도록 Topmost 밴드 최상단으로 재삽입한다.
        /// (백드롭이 나중에 Show되면 같은 밴드 최상단에 끼어들어 격자가 오버레이 위에 그려짐)
        /// </summary>
        private static void RaiseOverlaysAboveBackdrop()
        {
            var app = Application.Current;
            if (app == null) return;

            const int SWP_NOMOVE = 0x0002;
            const int SWP_NOSIZE = 0x0001;
            const int SWP_NOACTIVATE = 0x0010;
            var HWND_TOPMOST = new IntPtr(-1);

            foreach (Window window in app.Windows)
            {
                if (ReferenceEquals(window, _backdrop) || !window.IsVisible)
                    continue;

                var handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero)
                    continue;

                NativeMethods.SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        /// <summary>
        /// 전체 화면 격자 배경. 배경은 완전 투명이라 뒤 화면이 그대로 보이고,
        /// 최상단(게임 위)에 떠서 배치 기준 격자가 보인다. 클릭은 아래 창으로 통과.
        /// </summary>
        private sealed class BackdropWindow : Window
        {
            public BackdropWindow()
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent; // 기본값(흰색)이 깔리면 화면 전체가 불투명해짐
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                Left = SystemParameters.VirtualScreenLeft;
                Top = SystemParameters.VirtualScreenTop;
                Width = SystemParameters.VirtualScreenWidth;
                Height = SystemParameters.VirtualScreenHeight;
                IsHitTestVisible = false;
                Focusable = false;

                var root = new Grid();
                // 배경은 완전 투명, 격자만 표시
                root.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Opacity = 0.5,
                    Fill = CreateGridBrush()
                });
                Content = root;

                SourceInitialized += (_, _) => ApplyClickThrough(this);
            }

            private static DrawingBrush CreateGridBrush()
            {
                var line = Color.FromArgb(0x70, 0x0C, 0xD2, 0x9D);
                var pen = new Pen(new SolidColorBrush(line), 2);
                var drawing = new GeometryDrawing
                {
                    Pen = pen,
                    Geometry = new GeometryGroup
                    {
                        Children =
                        {
                            new LineGeometry(new Point(0, 0), new Point(32, 0)),
                            new LineGeometry(new Point(0, 0), new Point(0, 32)),
                        }
                    }
                };
                var brush = new DrawingBrush(drawing)
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, 32, 32),
                    ViewportUnits = BrushMappingMode.Absolute,
                };
                brush.Freeze();
                return brush;
            }
        }

        /// <summary>주 모니터 상단 중앙의 안내 + 완료 버튼 배너.</summary>
        private sealed class BannerWindow : Window
        {
            public BannerWindow()
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                SizeToContent = SizeToContent.WidthAndHeight;

                var banner = new Border
                {
                    Padding = new Thickness(14, 8, 10, 8),
                    CornerRadius = new CornerRadius(3),
                    BorderThickness = new Thickness(1),
                };
                banner.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
                banner.SetResourceReference(Border.BorderBrushProperty, "OverlayAccentBorderBrush");

                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                var text = new TextBlock
                {
                    Text = "잠금 해제 모드 — 창을 클릭해 선택하고 드래그해 배치하세요",
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 12, 0),
                };
                text.SetResourceReference(TextBlock.ForegroundProperty, "OverlayInfoTextBrush");
                panel.Children.Add(text);

                var snapButton = new Button
                {
                    Padding = new Thickness(12, 4, 12, 4),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand,
                    Foreground = Brushes.White,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                snapButton.SetResourceReference(FrameworkElement.StyleProperty, "SimButtonStyle");
                void RefreshSnapButton() =>
                    snapButton.Content = SnapEnabled ? "자석 ON" : "자석 OFF";
                RefreshSnapButton();
                snapButton.Click += (_, _) =>
                {
                    SnapEnabled = !SnapEnabled;
                    RefreshSnapButton();
                    try
                    {
                        var settings = ToastPresentationHelper.FindSharedSettings();
                        if (settings != null)
                        {
                            settings.WindowSnapEnabled = SnapEnabled;
                            ConfigService.SaveDeferred(settings);
                        }
                    }
                    catch { }
                };
                IsVisibleChanged += (_, _) => RefreshSnapButton();
                panel.Children.Add(snapButton);

                var doneButton = new Button
                {
                    Content = "완료",
                    Padding = new Thickness(16, 4, 16, 4),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand,
                    Foreground = Brushes.White,
                };
                doneButton.SetResourceReference(FrameworkElement.StyleProperty, "SimButtonStyle");
                doneButton.Click += (_, _) => Set(false);
                panel.Children.Add(doneButton);

                banner.Child = panel;
                Content = banner;

                // 주 모니터 상단 중앙 배치 (크기 확정 후)
                Loaded += (_, _) => PositionTopCenter();
                SizeChanged += (_, _) => PositionTopCenter();

                PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.Escape) Set(false);
                };
            }

            private void PositionTopCenter()
            {
                double width = ActualWidth > 0 ? ActualWidth : 320;
                Left = (SystemParameters.PrimaryScreenWidth - width) / 2.0;
                Top = 0;
            }
        }

        /// <summary>
        /// 선택된 창 위에 겹쳐 표시되는 하이라이트 테두리(클릭 통과).
        /// 대상보다 4px 크게 그려서 z-order 다툼과 무관하게 테두리가 항상 보인다.
        /// </summary>
        private sealed class HighlightWindow : Window
        {
            private const double Inflate = 4;

            public HighlightWindow()
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                IsHitTestVisible = false;
                Focusable = false;

                Content = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x0C, 0xD2, 0x9D)),
                    BorderThickness = new Thickness(2),
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(Color.FromArgb(0x16, 0x0C, 0xD2, 0x9D)),
                };

                SourceInitialized += (_, _) => ApplyClickThrough(this);
            }

            public void SyncTo(Window target)
            {
                try
                {
                    Left = target.Left - Inflate;
                    Top = target.Top - Inflate;
                    Width = Math.Max(16, target.ActualWidth + Inflate * 2);
                    Height = Math.Max(16, target.ActualHeight + Inflate * 2);
                }
                catch { }
            }
        }

        /// <summary>
        /// 선택된 창에 부착되는 편집 UI. 1px 이동 버튼이 창의 각 변 가운데(밖)에 붙고,
        /// 정보 패널(이름/좌표/가로·세로 입력)은 창 상단에 — 창이 화면 위쪽에 붙어 있으면
        /// 하단에 — 표시된다. 투명 영역 클릭은 아래(대상 창)로 통과한다.
        /// </summary>
        private sealed class InspectorWindow : Window
        {
            private Window? _target;
            private readonly Canvas _canvas;
            private readonly Border _panel;
            private readonly System.Windows.Controls.Primitives.RepeatButton _btnUp;
            private readonly System.Windows.Controls.Primitives.RepeatButton _btnDown;
            private readonly System.Windows.Controls.Primitives.RepeatButton _btnLeft;
            private readonly System.Windows.Controls.Primitives.RepeatButton _btnRight;
            private readonly TextBox _widthBox;
            private readonly TextBox _heightBox;
            private readonly TextBox _xBox;
            private readonly TextBox _yBox;
            private readonly Slider _opacitySlider;
            private readonly TextBlock _opacityText;
            private bool _suppressOpacityEvent;
            private readonly StackPanel _fontRow;
            private readonly Slider _fontSlider;
            private readonly TextBlock _fontText;
            private bool _suppressFontEvent;

            private const double SideZone = 34;   // 좌우 버튼이 차지하는 폭
            private const double VertZone = 180;  // 상하 버튼 + 정보 패널이 차지하는 높이
            private const double PanelWidth = 248;
            private const double ButtonW = 26;
            private const double ButtonH = 24;
            private const double Gap = 6;

            private static readonly Color PanelBg = Color.FromRgb(0x10, 0x16, 0x14);
            private static readonly Color FieldBg = Color.FromRgb(0x0D, 0x12, 0x10);
            private static readonly Color BorderCol = Color.FromRgb(0x2A, 0x33, 0x2E);
            private static readonly Color TextCol = Color.FromRgb(0xE8, 0xEA, 0xE9);
            private static readonly Color SubTextCol = Color.FromRgb(0x8C, 0x91, 0x97);
            private static readonly Color Mint = Color.FromRgb(0x0C, 0xD2, 0x9D);

            public InspectorWindow()
            {
                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;

                // 가로/세로 크기 + X/Y 좌표 입력 (2행 x 4열)
                _widthBox = CreateNumberBox(ApplySizeFromBoxes);
                _heightBox = CreateNumberBox(ApplySizeFromBoxes);
                _xBox = CreateNumberBox(ApplyPositionFromBoxes);
                _yBox = CreateNumberBox(ApplyPositionFromBoxes);

                var fieldPanel = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
                for (int i = 0; i < 4; i++)
                    fieldPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                fieldPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                fieldPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                fieldPanel.Children.Add(CreateFieldLabel("가로", 0, 0, 0));
                fieldPanel.Children.Add(CreateFieldLabel("세로", 1, 0, 0));
                fieldPanel.Children.Add(CreateFieldLabel("X", 0, 2, 14));
                fieldPanel.Children.Add(CreateFieldLabel("Y", 1, 2, 14));
                fieldPanel.Children.Add(PlaceField(_widthBox, 0, 1));
                fieldPanel.Children.Add(PlaceField(_heightBox, 1, 1));
                fieldPanel.Children.Add(PlaceField(_xBox, 0, 3));
                fieldPanel.Children.Add(PlaceField(_yBox, 1, 3));

                // 투명도 슬라이더 (10~100%) + 우측 % 표시
                _opacitySlider = new Slider
                {
                    Minimum = 10,
                    Maximum = 100,
                    Value = 100,
                    Width = 138,
                    TickFrequency = 1,
                    IsSnapToTickEnabled = true,
                    Focusable = false,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0),
                };
                _opacityText = new TextBlock
                {
                    Text = "100%",
                    FontSize = 11,
                    Width = 36,
                    TextAlignment = TextAlignment.Right,
                    Foreground = new SolidColorBrush(TextCol),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _opacitySlider.ValueChanged += (_, e) =>
                {
                    _opacityText.Text = $"{(int)Math.Round(e.NewValue)}%";
                    if (_suppressOpacityEvent) return;
                    SetSelectedOpacity(e.NewValue);
                };

                var opacityRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 7, 0, 0),
                };
                opacityRow.Children.Add(new TextBlock
                {
                    Text = "투명도",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(SubTextCol),
                    VerticalAlignment = VerticalAlignment.Center,
                });
                opacityRow.Children.Add(_opacitySlider);
                opacityRow.Children.Add(_opacityText);

                // 폰트 크기 슬라이더 (지원 창에서만 표시) — 투명도 아래
                _fontSlider = new Slider
                {
                    Minimum = 10,
                    Maximum = 40,
                    Value = 16,
                    Width = 138,
                    TickFrequency = 1,
                    IsSnapToTickEnabled = true,
                    Focusable = false,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 8, 0),
                };
                _fontText = new TextBlock
                {
                    Text = "16",
                    FontSize = 11,
                    Width = 36,
                    TextAlignment = TextAlignment.Right,
                    Foreground = new SolidColorBrush(TextCol),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _fontSlider.ValueChanged += (_, e) =>
                {
                    _fontText.Text = $"{(int)Math.Round(e.NewValue)}";
                    if (_suppressFontEvent) return;
                    SetSelectedFontSize(e.NewValue);
                };

                _fontRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 7, 0, 0),
                };
                _fontRow.Children.Add(new TextBlock
                {
                    Text = "폰트",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(SubTextCol),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(9, 0, 0, 0),
                });
                _fontRow.Children.Add(_fontSlider);
                _fontRow.Children.Add(_fontText);

                var root = new StackPanel();
                root.Children.Add(fieldPanel);
                root.Children.Add(opacityRow);
                root.Children.Add(_fontRow);

                _panel = new Border
                {
                    Width = PanelWidth,
                    Padding = new Thickness(12, 8, 12, 9),
                    CornerRadius = new CornerRadius(4),
                    BorderThickness = new Thickness(1),
                    Background = new SolidColorBrush(Color.FromArgb(0xF2, PanelBg.R, PanelBg.G, PanelBg.B)),
                    BorderBrush = new SolidColorBrush(BorderCol),
                    Child = root,
                };

                _btnUp = CreateNudgeButton("▲", 0, -1);
                _btnDown = CreateNudgeButton("▼", 0, 1);
                _btnLeft = CreateNudgeButton("◀", -1, 0);
                _btnRight = CreateNudgeButton("▶", 1, 0);

                _canvas = new Canvas();
                _canvas.Children.Add(_btnUp);
                _canvas.Children.Add(_btnDown);
                _canvas.Children.Add(_btnLeft);
                _canvas.Children.Add(_btnRight);
                _canvas.Children.Add(_panel);
                Content = _canvas;

                PreviewKeyDown += (_, e) =>
                {
                    if (e.Key == Key.Escape) Set(false);
                };
            }

            private System.Windows.Controls.Primitives.RepeatButton CreateNudgeButton(string glyph, int dx, int dy)
            {
                var button = new System.Windows.Controls.Primitives.RepeatButton
                {
                    Content = glyph,
                    Width = ButtonW,
                    Height = ButtonH,
                    FontSize = 10,
                    Cursor = Cursors.Hand,
                    Focusable = false,
                    Delay = 400,
                    Interval = 45,
                    Foreground = new SolidColorBrush(TextCol),
                    Background = new SolidColorBrush(FieldBg),
                    BorderBrush = new SolidColorBrush(BorderCol),
                    ToolTip = "1px 이동 (길게 누르면 반복)",
                };
                button.Click += (_, _) => NudgeSelected(dx, dy);
                return button;
            }

            private TextBlock CreateFieldLabel(string text, int row, int column, double leftMargin)
            {
                var label = new TextBlock
                {
                    Text = text,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(SubTextCol),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(leftMargin, 0, 8, 0),
                };
                return PlaceField(label, row, column);
            }

            private static T PlaceField<T>(T element, int row, int column) where T : UIElement
            {
                Grid.SetRow(element, row);
                Grid.SetColumn(element, column);
                return element;
            }

            private TextBox CreateNumberBox(Action commit)
            {
                var box = new TextBox
                {
                    Width = 64,
                    Height = 22,
                    Margin = new Thickness(0, 2, 0, 2),
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Foreground = new SolidColorBrush(TextCol),
                    Background = new SolidColorBrush(FieldBg),
                    BorderBrush = new SolidColorBrush(BorderCol),
                    CaretBrush = new SolidColorBrush(Mint),
                };
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        commit();
                        e.Handled = true;
                    }
                };
                box.LostFocus += (_, _) => commit();
                return box;
            }

            public void SetTarget(Window target)
            {
                _target = target;
                // SizeToContent 창은 내용이 크기를 결정하므로 수치 입력을 막는다
                bool canResize = target.SizeToContent == SizeToContent.Manual;
                _widthBox.IsEnabled = canResize;
                _heightBox.IsEnabled = canResize;
                double fieldOpacity = canResize ? 1.0 : 0.45;
                _widthBox.Opacity = fieldOpacity;
                _heightBox.Opacity = fieldOpacity;

                SyncOpacitySlider(target);
                SyncFontSlider();
                RefreshValues();
            }

            private void SyncFontSlider()
            {
                double? size = GetSelectedFontSize();
                _fontRow.Visibility = size.HasValue ? Visibility.Visible : Visibility.Collapsed;
                if (!size.HasValue) return;

                _suppressFontEvent = true;
                try
                {
                    _fontSlider.Value = Math.Max(10, Math.Min(40, Math.Round(size.Value)));
                    _fontText.Text = $"{(int)Math.Round(size.Value)}";
                }
                finally
                {
                    _suppressFontEvent = false;
                }
            }

            private void SyncOpacitySlider(Window target)
            {
                _suppressOpacityEvent = true;
                try
                {
                    double percent = Math.Max(10, Math.Min(100, Math.Round(target.Opacity * 100)));
                    _opacitySlider.Value = percent;
                    _opacityText.Text = $"{(int)percent}%";
                }
                finally
                {
                    _suppressOpacityEvent = false;
                }
            }

            public void RefreshValues()
            {
                var target = _target;
                if (target == null) return;

                try
                {
                    if (!_xBox.IsKeyboardFocused)
                        _xBox.Text = ((int)Math.Round(target.Left)).ToString();
                    if (!_yBox.IsKeyboardFocused)
                        _yBox.Text = ((int)Math.Round(target.Top)).ToString();
                    if (!_widthBox.IsKeyboardFocused)
                        _widthBox.Text = ((int)Math.Round(target.ActualWidth)).ToString();
                    if (!_heightBox.IsKeyboardFocused)
                        _heightBox.Text = ((int)Math.Round(target.ActualHeight)).ToString();

                    RefreshLayout(target);
                }
                catch { }
            }

            /// <summary>대상 창의 위치·크기에 맞춰 이 창을 감싸도록 재배치한다.</summary>
            private void RefreshLayout(Window target)
            {
                double tw = Math.Max(16, target.ActualWidth);
                double th = Math.Max(16, target.ActualHeight);

                // 대상 창이 정보 패널보다 좁으면 인스펙터 창을 좌우로 더 넓혀 패널이 잘리지 않게 한다
                double extra = Math.Max(0, (PanelWidth + 8 - (tw + SideZone * 2)) / 2);
                double originX = SideZone + extra;

                Left = target.Left - originX;
                Top = target.Top - VertZone;
                Width = tw + originX * 2;
                Height = th + VertZone * 2;

                // 상하좌우 버튼: 각 변의 가운데, 창 바깥쪽에 붙인다
                Canvas.SetLeft(_btnLeft, originX - ButtonW - 4);
                Canvas.SetTop(_btnLeft, VertZone + th / 2 - ButtonH / 2);
                Canvas.SetLeft(_btnRight, originX + tw + 4);
                Canvas.SetTop(_btnRight, VertZone + th / 2 - ButtonH / 2);
                Canvas.SetLeft(_btnUp, originX + tw / 2 - ButtonW / 2);
                Canvas.SetTop(_btnUp, VertZone - ButtonH - 4);
                Canvas.SetLeft(_btnDown, originX + tw / 2 - ButtonW / 2);
                Canvas.SetTop(_btnDown, VertZone + th + 4);

                // 정보 패널: 기본은 창 상단(▲ 버튼 위), 화면 위쪽에 붙어 있으면 하단(▼ 버튼 아래)
                double panelHeight = _panel.ActualHeight > 0 ? _panel.ActualHeight : 140;
                bool showBelow = target.Top - (ButtonH + Gap * 2 + panelHeight) < SystemParameters.VirtualScreenTop + 4;

                double panelLeft = originX + tw / 2 - PanelWidth / 2;
                panelLeft = Math.Max(2, Math.Min(Width - PanelWidth - 2, panelLeft));
                Canvas.SetLeft(_panel, panelLeft);
                Canvas.SetTop(_panel, showBelow
                    ? VertZone + th + 4 + ButtonH + Gap
                    : VertZone - ButtonH - 4 - Gap - panelHeight);

                RaiseToTop();
            }

            /// <summary>
            /// 다른 오버레이 창들이 Topmost 밴드 최상단으로 끼어들어도
            /// 인스펙터(좌표·크기 패널)는 항상 그 위에 보이도록 재삽입한다.
            /// </summary>
            private void RaiseToTop()
            {
                try
                {
                    var handle = new WindowInteropHelper(this).Handle;
                    if (handle == IntPtr.Zero)
                        return;

                    const int SWP_NOMOVE = 0x0002;
                    const int SWP_NOSIZE = 0x0001;
                    const int SWP_NOACTIVATE = 0x0010;
                    NativeMethods.SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
                catch { }
            }

            private void ApplyPositionFromBoxes()
            {
                var target = _target;
                if (target == null) return;

                double? left = double.TryParse(_xBox.Text, out double x) ? x : null;
                double? top = double.TryParse(_yBox.Text, out double y) ? y : null;
                if (left == null && top == null)
                {
                    RefreshValues();
                    return;
                }

                MoveSelected(left, top);

                // 화면 밖으로 나가지 않게 보정될 수 있으므로 실제 값으로 되돌려 보여준다
                _xBox.Text = ((int)Math.Round(target.Left)).ToString();
                _yBox.Text = ((int)Math.Round(target.Top)).ToString();
                RefreshValues();
            }

            private void ApplySizeFromBoxes()
            {
                if (_target == null || !_widthBox.IsEnabled) return;

                double? width = double.TryParse(_widthBox.Text, out double w) ? w : null;
                double? height = double.TryParse(_heightBox.Text, out double h) ? h : null;
                if (width == null && height == null)
                {
                    RefreshValues();
                    return;
                }

                ResizeSelected(width, height);
                RefreshValues();
            }

        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, int flags);

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            public struct WINDOWPOS
            {
                public IntPtr hwnd;
                public IntPtr hwndInsertAfter;
                public int x;
                public int y;
                public int cx;
                public int cy;
                public int flags;
            }
        }
    }
}
