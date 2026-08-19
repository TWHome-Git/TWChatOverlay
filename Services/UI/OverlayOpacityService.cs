using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 오버레이 창 배경 불투명도를 앱 전역 리소스 브러시의 알파값으로 적용합니다.
    /// 창 루트 배경뿐 아니라 창 내부를 넓게 채우는 패널/카드/셸 배경 브러시까지 함께 조절해야
    /// 실제로 투명해 보입니다. 텍스트/아이콘/테두리는 영향을 받지 않아 가독성이 유지됩니다.
    /// </summary>
    public static class OverlayOpacityService
    {
        /// <summary>
        /// (리소스 키, 기본 알파 0~255) — 100%일 때의 알파. RGB는 현재 리소스 값을 그대로 유지한다.
        /// 배경 면적을 채우는 브러시만 대상으로 하며, 각 브러시의 기본 알파에 비례해서 조절한다.
        /// (예: 원래 반투명이던 브러시는 더 투명해지고, 불투명이던 브러시는 슬라이더 값 그대로)
        /// </summary>
        private static readonly (string Key, byte BaseAlpha)[] Targets =
        {
            ("OverlayWindowBackgroundBrush",     0xF5),
            ("OverlayPanelBackgroundBrush",      0xEE),
            ("OverlayShellBackgroundBrush",      0xFF),
            ("OverlaySurfaceBackgroundBrush",    0xFF),
            ("OverlaySurfaceAltBackgroundBrush", 0xFF),
            ("OverlayCardBackgroundBrush",       0xFF),
            ("OverlayHeaderBackgroundBrush",     0xFF),
            ("OverlayDragBarBackgroundBrush",    0xFF),
        };

        // 원본 RGB를 최초 1회 캡처 (알파만 바꾸고 색상은 보존)
        private static readonly Dictionary<string, Color> _baseColors = new();

        public static void Apply(double opacityPercent)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                double clamped = Math.Clamp(opacityPercent, 20.0, 100.0);
                double factor = clamped / 100.0;

                foreach (var (key, baseAlpha) in Targets)
                {
                    if (!_baseColors.TryGetValue(key, out var baseColor))
                    {
                        if (app.TryFindResource(key) is SolidColorBrush existing)
                            baseColor = existing.Color;
                        else
                            continue;
                        _baseColors[key] = baseColor;
                    }

                    byte alpha = (byte)Math.Round(baseAlpha * factor);
                    var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
                    brush.Freeze();

                    // 최상위 리소스 딕셔너리에 덮어써서 병합 딕셔너리(Styles.xaml) 값을 가린다.
                    // DynamicResource 참조는 키 변경 알림을 받아 즉시 다시 그린다.
                    app.Resources[key] = brush;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to apply overlay opacity ({opacityPercent}%).", ex);
            }
        }
    }
}
