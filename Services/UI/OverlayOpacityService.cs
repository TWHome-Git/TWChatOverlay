using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 오버레이 창 배경 불투명도를 리소스 브러시의 알파값으로 적용합니다.
    ///
    /// - 메뉴에서 따로 여는 창(달력·컨텐츠·어밴던)은 그룹별 값을 따로 저장하고,
    ///   Window.Resources에 브러시를 덮어써 앱 전역 리소스를 가리는 방식으로 개별 적용합니다.
    /// - 메인/서브 채팅창과 자동으로 뜨는 창(트래커·토스트 등)은 통합 값 하나로 관리하며,
    ///   앱 전역 리소스에 적용합니다.
    ///
    /// 텍스트/아이콘/테두리는 영향을 받지 않아 가독성이 유지됩니다.
    /// </summary>
    public static class OverlayOpacityService
    {
        /// <summary>메인·서브 채팅창과 자동으로 뜨는 창을 함께 관리하는 통합 값.</summary>
        public const string GroupShared = "Shared";
        public const string GroupContent = "Content";
        public const string GroupCalendar = "Calendar";
        public const string GroupAbaddon = "Abaddon";

        /// <summary>그룹 정의: 키, 표시명, 해당 창 타입 이름 목록.</summary>
        private sealed class GroupDefinition
        {
            public string Key = string.Empty;
            public string DisplayName = string.Empty;
            public string[] WindowTypes = Array.Empty<string>();
        }

        /// <summary>창에서 직접 조절하는 그룹. 여기 없는 창은 모두 통합 값(GroupShared)을 따른다.</summary>
        private static readonly GroupDefinition[] Groups =
        {
            new GroupDefinition
            {
                Key = GroupContent, DisplayName = "컨텐츠",
                WindowTypes = new[] { "DailyWeeklyContentWindow" },
            },
            new GroupDefinition
            {
                Key = GroupCalendar, DisplayName = "달력",
                WindowTypes = new[] { "ItemCalendarWindow" },
            },
            new GroupDefinition
            {
                Key = GroupAbaddon, DisplayName = "어밴던",
                WindowTypes = new[] { "AbandonRoadSummaryWindow" },
            },
        };

        /// <summary>
        /// 배경 면적을 채우는 리소스 브러시 키 목록. RGB는 현재 리소스 값을 그대로 유지하고
        /// 알파만 슬라이더 값(%)에 그대로 매핑한다. 100%면 알파 255 = 뒤가 전혀 비치지 않는다.
        /// </summary>
        private static readonly string[] Targets =
        {
            "OverlayWindowBackgroundBrush",
            "OverlayPanelBackgroundBrush",
            "OverlayShellBackgroundBrush",
            "OverlaySurfaceBackgroundBrush",
            "OverlaySurfaceAltBackgroundBrush",
            "OverlayCardBackgroundBrush",
            "OverlayHeaderBackgroundBrush",
            "OverlayDragBarBackgroundBrush",
        };

        // 원본 RGB를 최초 1회 캡처 (알파만 바꾸고 색상은 보존)
        private static readonly Dictionary<string, Color> _baseColors = new();

        private static ChatSettings? _settings;
        private static bool _classHandlerRegistered;

        /// <summary>그룹 값이 바뀌면 발생한다. 인자는 그룹 키, null이면 전체 변경(설정 초기화 등).</summary>
        public static event Action<string?>? GroupOpacityChanged;

        /// <summary>설정 초기화처럼 여러 그룹이 한꺼번에 바뀐 뒤 창의 슬라이더를 갱신시킨다.</summary>
        public static void NotifyAllGroupsChanged() => GroupOpacityChanged?.Invoke(null);

        /// <summary>설정을 연결하고, 이후 열리는 모든 창에 그룹별 불투명도가 자동 적용되도록 등록한다.</summary>
        public static void Initialize(ChatSettings settings)
        {
            _settings = settings;

            // 앱 전역 리소스는 통합 값을 따른다. (메인·서브 채팅창 + 자동으로 뜨는 창)
            Apply(settings.OverlayOpacityPercent);

            if (!_classHandlerRegistered)
            {
                EventManager.RegisterClassHandler(
                    typeof(Window),
                    FrameworkElement.LoadedEvent,
                    new RoutedEventHandler(OnWindowLoaded));
                _classHandlerRegistered = true;
            }

            ApplyToOpenWindows();
        }

        /// <summary>그룹 표시명. 알 수 없는 키면 키를 그대로 돌려준다.</summary>
        public static string GetDisplayName(string groupKey)
            => Groups.FirstOrDefault(g => g.Key == groupKey)?.DisplayName ?? groupKey;

        /// <summary>그룹에 저장된 불투명도(%). 저장값이 없으면 통합 값을 따른다.</summary>
        public static double GetGroupOpacity(string groupKey)
        {
            if (_settings == null) return 100.0;
            if (string.IsNullOrEmpty(groupKey) || groupKey == GroupShared)
                return _settings.OverlayOpacityPercent;
            return _settings.GetOverlayOpacity(groupKey);
        }

        /// <summary>그룹 불투명도를 저장하고 해당 그룹의 열린 창에 즉시 반영한다.</summary>
        public static void SetGroupOpacity(string groupKey, double opacityPercent)
        {
            if (_settings == null || string.IsNullOrEmpty(groupKey)) return;

            if (groupKey == GroupShared)
            {
                if (Math.Abs(_settings.OverlayOpacityPercent - opacityPercent) < 0.001) return;
                _settings.OverlayOpacityPercent = opacityPercent;
                Apply(_settings.OverlayOpacityPercent);
            }
            else
            {
                if (!_settings.SetOverlayOpacity(groupKey, opacityPercent)) return;
                ApplyToOpenWindows(groupKey);
            }

            GroupOpacityChanged?.Invoke(groupKey);
            ConfigService.SaveDeferred(_settings);
        }

        /// <summary>열린 창 전체(또는 지정 그룹)에 저장된 불투명도를 다시 적용한다.</summary>
        public static void ApplyToOpenWindows(string? groupKey = null)
        {
            var app = Application.Current;
            if (app == null) return;

            foreach (Window window in app.Windows)
            {
                var group = ResolveGroup(window);
                if (group == null) continue;
                if (groupKey != null && group.Key != groupKey) continue;

                ApplyToWindow(window, GetGroupOpacity(group.Key));
            }
        }

        /// <summary>앱 전역 리소스에 적용한다. (통합 값을 따르는 모든 창)</summary>
        public static void Apply(double opacityPercent)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                byte alpha = ToAlpha(opacityPercent);

                foreach (var key in Targets)
                {
                    if (!TryGetBaseColor(app, key, out var baseColor)) continue;

                    // 최상위 리소스 딕셔너리에 덮어써서 병합 딕셔너리(Styles.xaml) 값을 가린다.
                    // DynamicResource 참조는 키 변경 알림을 받아 즉시 다시 그린다.
                    app.Resources[key] = CreateBrush(alpha, baseColor);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to apply overlay opacity ({opacityPercent}%).", ex);
            }
        }

        /// <summary>지정한 창에만 적용한다. Window.Resources가 앱 전역 리소스를 가린다.</summary>
        public static void ApplyToWindow(Window window, double opacityPercent)
        {
            if (window == null) return;

            try
            {
                var app = Application.Current;
                if (app == null) return;

                byte alpha = ToAlpha(opacityPercent);

                foreach (var key in Targets)
                {
                    if (!TryGetBaseColor(app, key, out var baseColor)) continue;
                    window.Resources[key] = CreateBrush(alpha, baseColor);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to apply overlay opacity to {window.GetType().Name} ({opacityPercent}%).", ex);
            }
        }

        /// <summary>창 타입으로 그룹 키를 찾는다. 없으면 null.</summary>
        public static string? ResolveGroupKey(Window window) => ResolveGroup(window)?.Key;

        private static GroupDefinition? ResolveGroup(Window? window)
        {
            if (window == null) return null;
            string typeName = window.GetType().Name;
            return Groups.FirstOrDefault(g => Array.IndexOf(g.WindowTypes, typeName) >= 0);
        }

        private static void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Window window) return;

            var group = ResolveGroup(window);
            if (group == null) return;

            ApplyToWindow(window, GetGroupOpacity(group.Key));
        }

        private static byte ToAlpha(double opacityPercent)
        {
            double clamped = Math.Clamp(opacityPercent, 20.0, 100.0);
            return (byte)Math.Clamp((int)Math.Round(255.0 * (clamped / 100.0)), 0, 255);
        }

        private static SolidColorBrush CreateBrush(byte alpha, Color baseColor)
        {
            var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            brush.Freeze();
            return brush;
        }

        private static bool TryGetBaseColor(Application app, string key, out Color baseColor)
        {
            if (_baseColors.TryGetValue(key, out baseColor)) return true;

            if (app.TryFindResource(key) is SolidColorBrush existing)
            {
                baseColor = existing.Color;
                _baseColors[key] = baseColor;
                return true;
            }

            baseColor = default;
            return false;
        }
    }
}
