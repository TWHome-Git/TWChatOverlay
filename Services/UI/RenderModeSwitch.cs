using System.Windows.Interop;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 창을 그리는 방식(그래픽 카드 / CPU)을 정한다. 설정(화면 > 오버레이 > 하드웨어 렌더링)이 바뀌거나 앱이 시작할 때 부른다.
    /// 프로세스 전체에 적용되고, 실행 중에 바꿔도 떠 있는 창이 바로 새 방식으로 그려진다.
    /// </summary>
    public static class RenderModeSwitch
    {
        /// <param name="useHardware">설정 값. true면 그래픽 카드로 그린다.</param>
        /// <param name="forcedSoftware">실행 인자나 환경 변수로 소프트웨어 렌더를 강제했으면 true — 설정보다 우선한다.</param>
        public static void Apply(bool useHardware, bool forcedSoftware)
        {
            RenderMode mode = forcedSoftware || !useHardware ? RenderMode.SoftwareOnly : RenderMode.Default;
            if (RenderOptions.ProcessRenderMode != mode)
                RenderOptions.ProcessRenderMode = mode;

            AppLogger.Info(mode == RenderMode.SoftwareOnly
                ? $"Render mode: software ({(forcedSoftware ? "forced by argument or environment" : "setting")})."
                : "Render mode: hardware (setting).");
        }
    }
}
