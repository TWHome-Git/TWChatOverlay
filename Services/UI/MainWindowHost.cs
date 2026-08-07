using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// MainWindow(View)가 구현하는 계약. Services 계층이 구체적인 View 타입(MainWindow)에
    /// 직접 의존하지 않고 이 인터페이스에만 의존하도록 하여 계층 경계를 지킨다.
    /// </summary>
    public interface IMainWindowHost
    {
        FontFamily CurrentFont { get; }
        ChatSettings? HostSettings { get; }
        void RequestTopmostRefresh();
    }

    /// <summary>
    /// 현재 활성 MainWindow 호스트에 대한 전역 접근점. MainWindow가 생성 시 자신을 등록하고
    /// 종료 시 해제한다. Services는 창 목록을 스캔하는 대신 여기서 호스트를 얻는다.
    /// </summary>
    public static class MainWindowHost
    {
        public static IMainWindowHost? Current { get; set; }
    }

    /// <summary>
    /// 창 스냅(자석) 대상임을 표시하는 마커 인터페이스. MainWindow와 ChatCloneWindow가 구현한다.
    /// ChatWindowHub가 구체 View 타입 대신 이 마커로 스냅 대상을 식별한다.
    /// </summary>
    public interface ISnapTarget
    {
    }
}
