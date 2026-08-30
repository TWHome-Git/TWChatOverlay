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

        // 사용자 폰트 파일: UserDefine.ttf / .otf / .ttc 중 존재하는 첫 파일 사용
        // (기본으로 쿠키런 Regular를 UserDefine.ttf로 배포한다)
        private static readonly string[] UserFontCandidates =
        {
            Path.Combine(FontDirectory, "UserDefine.ttf"),
            Path.Combine(FontDirectory, "UserDefine.otf"),
            Path.Combine(FontDirectory, "UserDefine.ttc"),
        };

        // 번들 기본 글꼴: 표시 이름 → Font\ 폴더의 파일 (모두 재배포 허용 무료 폰트)
        private static readonly Dictionary<string, string> BundledFontFiles = new()
        {
            ["프리텐다드"] = "Pretendard-Regular.otf",       // SIL OFL
            ["나눔스퀘어라운드"] = "NanumSquareRoundB.ttf",   // SIL OFL (네이버)
            ["G마켓 산스"] = "GmarketSansTTFMedium.ttf",      // 무료 배포 (지마켓)
        };

        private static string? FindUserFontPath()
        {
            foreach (string candidate in UserFontCandidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }

        /// <summary>
        /// 설정된 폰트 이름에 따라 적절한 FontFamily 객체를 반환합니다.
        /// </summary>
        public static FontFamily GetFont(string fontFamilyName)
        {
            string normalized = (fontFamilyName ?? string.Empty).Trim();

            if (normalized == "사용자 설정")
            {
                string? userFontPath = FindUserFontPath();
                if (userFontPath != null)
                {
                    var loaded = TryLoadFontFile(userFontPath);
                    if (loaded != null)
                        return loaded;
                }
            }
            else if (BundledFontFiles.TryGetValue(normalized, out string? bundledFile))
            {
                var loaded = TryLoadFontFile(Path.Combine(FontDirectory, bundledFile));
                if (loaded != null)
                    return loaded;
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

        private static FontFamily? TryLoadFontFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                var fontFamilies = Fonts.GetFontFamilies(new Uri(path));
                return fontFamilies.Count > 0 ? fontFamilies.First() : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 사용 가능한 폰트 목록을 반환합니다.
        /// </summary>
        public static List<string> GetAvailableFonts()
        {
            return new List<string> { "나눔고딕", "굴림", "프리텐다드", "나눔스퀘어라운드", "G마켓 산스", "사용자 설정" };
        }
    }
}
