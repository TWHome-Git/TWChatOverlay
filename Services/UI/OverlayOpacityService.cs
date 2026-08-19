using System;
using System.Windows;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 오버레이 창 배경 불투명도를 앱 전역 리소스 브러시의 알파값으로 적용합니다.
    /// 모든 오버레이 창이 DynamicResource로 배경 브러시를 참조하므로 즉시 반영됩니다.
    /// 텍스트/아이콘은 영향을 받지 않아 가독성이 유지됩니다.
    /// </summary>
    public static class OverlayOpacityService
    {
        // (리소스 키, 기본 색상) — 알파만 교체하고 RGB는 유지.
        // AllowsTransparency=True인 오버레이 창들이 쓰는 브러시만 대상으로 한다.
        // (PrimaryBackgroundBrush는 메뉴/서브메뉴 등 불투명 창이 사용하므로 제외)
        private static readonly (string Key, Color BaseColor)[] Targets =
        {
            ("OverlayWindowBackgroundBrush", Color.FromRgb(0x1E, 0x1E, 0x1E)),
            ("OverlayPanelBackgroundBrush",  Color.FromRgb(0x1A, 0x1A, 0x1B)),
        };

        public static void Apply(double opacityPercent)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                double clamped = Math.Clamp(opacityPercent, 20.0, 100.0);
                byte alpha = (byte)Math.Round(clamped / 100.0 * 255.0);

                foreach (var (key, baseColor) in Targets)
                {
                    var brush = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
                    brush.Freeze();
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
