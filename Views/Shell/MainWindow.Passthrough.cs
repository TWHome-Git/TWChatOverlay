using System;
using System.Windows;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 메인 채팅창의 "휠로 깨어나는 클릭 통과" 연결. 동작은 <see cref="ChatWheelPassthroughService"/> 참고.
    /// 평소(잠금)에는 창 아래 게임 버튼이 눌리고, 휠을 굴리면 잠시 창이 마우스를 받다가 유휴 시간 뒤 다시 통과된다.
    /// </summary>
    public partial class MainWindow : IWheelPassthroughTarget
    {
        Window IWheelPassthroughTarget.Window => this;

        /// <summary>
        /// 설정이 켜져 있고, 잠금 상태이고, 위치 편집(설정 탭·추가 기능 미리보기·초기 마법사)이 아닐 때만 자동 통과한다.
        /// 서브 채팅창도 같은 기준을 쓴다.
        /// </summary>
        public bool IsChatAutoPassthroughAllowed
        {
            get
            {
                if (!_settings.ChatPassthroughEnabled)
                    return false;
                if (AppServices.Get<UiLockService>().IsUnlocked)
                    return false;
                if (_isSettingsPositionMode || _isAddonPositionMode || _isWizardChatPositionMode || _isInitialSetupWizardRunning)
                    return false;
                return true;
            }
        }

        bool IWheelPassthroughTarget.IsAutoPassthroughAllowed => IsChatAutoPassthroughAllowed;

        void IWheelPassthroughTarget.ScrollByWheel(int delta)
        {
            ChatDisplay?.ScrollByWheelDelta(delta);
        }

        void IWheelPassthroughTarget.OnInteractiveChanged(bool interactive)
        {
            // 통과 중에는 MouseEnter가 오지 않아 탭이 안 뜬다 — 깨어날 때 보여 준다
            if (interactive)
                ShowMainTabsTemporarily();
        }

        void IWheelPassthroughTarget.OnInteractiveActivity()
        {
            // 상호작용 중 창 위 입력이 있으면 탭 자동 숨김도 같이 미룬다 (클릭 가능한데 탭만 사라지는 일이 없게)
            ShowMainTabsTemporarily();
        }

        /// <summary>편집 모드가 바뀐 뒤 부른다. 메인·서브 채팅창의 통과 상태를 다시 맞춘다.</summary>
        private void RefreshChatPassthrough()
        {
            try { AppServices.TryGet<ChatWheelPassthroughService>()?.RefreshAll(); }
            catch (Exception ex) { AppLogger.Warn("Failed to refresh chat passthrough.", ex); }
        }
    }
}
