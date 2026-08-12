using System;
using System.Windows;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 디자인 테마(구버전/신버전)를 런타임에 전환합니다.
    /// 구버전 = Styles.xaml 기본 팔레트, 신버전 = Theme.New.xaml 오버라이드 딕셔너리.
    /// 모든 창이 DynamicResource로 팔레트를 참조하므로 병합 딕셔너리 추가/제거만으로 즉시 반영됩니다.
    /// </summary>
    public static class ThemeService
    {
        public const int LegacyTheme = 1;
        public const int NewTheme = 2;

        private static readonly Uri NewThemeUri = new("pack://application:,,,/Resources/Themes/Theme.New.xaml");
        private static ResourceDictionary? _newThemeDictionary;

        public static void Apply(int themeVersion)
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                var merged = app.Resources.MergedDictionaries;

                if (themeVersion == LegacyTheme)
                {
                    if (_newThemeDictionary != null)
                        merged.Remove(_newThemeDictionary);
                }
                else
                {
                    _newThemeDictionary ??= new ResourceDictionary { Source = NewThemeUri };
                    if (!merged.Contains(_newThemeDictionary))
                        merged.Add(_newThemeDictionary);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to apply theme (version={themeVersion}).", ex);
            }
        }
    }
}
