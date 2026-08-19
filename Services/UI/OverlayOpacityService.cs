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

        public static void Apply(double opacityPercent)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                double clamped = Math.Clamp(opacityPercent, 20.0, 100.0);
                byte alpha = (byte)Math.Clamp((int)Math.Round(255.0 * (clamped / 100.0)), 0, 255);

                foreach (var key in Targets)
                {
                    if (!_baseColors.TryGetValue(key, out var baseColor))
                    {
                        if (app.TryFindResource(key) is SolidColorBrush existing)
                            baseColor = existing.Color;
                        else
                            continue;
                        _baseColors[key] = baseColor;
                    }

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
