using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 앱에서 사용하는 폰트 로드 및 목록 제공 기능을 담당합니다.
    /// </summary>
    public static class FontService
    {
        private static readonly string FontDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Font");
        private static readonly string UserFontPath = Path.Combine(FontDirectory, "UserDefine.ttf");

        /// <summary>
        /// 설정된 폰트 이름에 따라 적절한 FontFamily 객체를 반환합니다.
        /// </summary>
        public static FontFamily GetFont(string fontFamilyName)
        {
            string normalized = (fontFamilyName ?? string.Empty).Trim();

            if (normalized == "사용자 설정")
            {
                if (File.Exists(UserFontPath))
                {
                    try
                    {
                        var fontFamilies = Fonts.GetFontFamilies(new Uri(UserFontPath));
                        if (fontFamilies.Count > 0)
                        {
                            return fontFamilies.First();
                        }
                    }
                    catch
                    {
                        return new FontFamily("Malgun Gothic");
                    }
                }
            }
            else if (!string.IsNullOrWhiteSpace(normalized))
            {
                try
                {
                    return new FontFamily(normalized);
                }
                catch
                {
                    return new FontFamily("Malgun Gothic");
                }
            }

            return new FontFamily("Malgun Gothic");
        }

        /// <summary>
        /// 사용 가능한 폰트 목록을 반환합니다.
        /// </summary>
        public static List<string> GetAvailableFonts()
        {
            return new List<string> { "나눔고딕", "굴림", "사용자 설정" };
        }
    }
}
