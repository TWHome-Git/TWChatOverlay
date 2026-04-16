namespace TWChatOverlay.Services
{
    /// <summary>
    /// 탭 선택 상태에 따른 화면 표시 상태 값입니다.
    /// </summary>
    public readonly record struct TabDisplayState(bool IsLogVisible, bool IsSettingsVisible, bool IsItemVisible, bool IsSettingsTab);

    /// <summary>
    /// 탭 태그를 기반으로 화면 표시 상태를 계산합니다.
    /// </summary>
    public sealed class TabDisplayStateResolver
    {
        /// <summary>
        /// 선택된 탭 태그에 맞는 표시 상태를 반환합니다.
        /// </summary>
        public TabDisplayState Resolve(string tabTag)
        {
            bool isSettings = tabTag == "Settings";
            bool isItem = tabTag == "Item";
            bool isLogVisible = !isSettings;

            return new TabDisplayState(
                IsLogVisible: isLogVisible,
                IsSettingsVisible: isSettings,
                IsItemVisible: isItem,
                IsSettingsTab: isSettings);
        }
    }
}
