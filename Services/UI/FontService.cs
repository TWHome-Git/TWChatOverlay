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

        // 사용자 폰트 파일: UserDefine.ttf / .otf / .ttc 중 존재하는 첫 파일 사용 (기본 미제공)
        private static readonly string[] UserFontCandidates =
        {
            Path.Combine(FontDirectory, "UserDefine.ttf"),
            Path.Combine(FontDirectory, "UserDefine.otf"),
            Path.Combine(FontDirectory, "UserDefine.ttc"),
        };

        // 번들 기본 글꼴: 표시 이름 → (Font\ 폴더의 파일, 폰트 패밀리명). 전부 재배포 허용 무료 폰트.
        // 주의: Fonts.GetFontFamilies(파일URI)는 파일이 아니라 폴더 전체의 패밀리를 돌려주므로
        //       반드시 패밀리명을 지정해 "./#패밀리" 방식으로 만들어야 한다.
        private static readonly Dictionary<string, (string File, string Family)> BundledFonts = new()
        {
            ["쿠키런"] = ("CookieRun-Regular.ttf", "CookieRun"),            // 데브시스터즈 무료
            ["프리텐다드"] = ("Pretendard-Regular.otf", "Pretendard"),       // SIL OFL
            ["나눔스퀘어라운드"] = ("NanumSquareRoundB.ttf", "NanumSquareRound"), // SIL OFL (네이버)
            ["G마켓 산스"] = ("GmarketSansTTFMedium.ttf", "Gmarket Sans TTF"),   // 무료 배포 (지마켓)
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
                var userFont = TryLoadUserFont();
                if (userFont != null)
                    return userFont;
            }
            else if (BundledFonts.TryGetValue(normalized, out var bundled))
            {
                var loaded = TryLoadBundledFont(bundled.File, bundled.Family);
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

        /// <summary>Font 폴더의 파일을 패밀리명을 지정해 로드한다 (없거나 실패하면 null).</summary>
        private static FontFamily? TryLoadBundledFont(string fileName, string familyName)
        {
            try
            {
                if (!File.Exists(Path.Combine(FontDirectory, fileName)))
                    return null;

                var baseUri = new Uri(FontDirectory + Path.DirectorySeparatorChar);
                return new FontFamily(baseUri, $"./#{familyName}, Malgun Gothic");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>UserDefine 파일에서 패밀리명을 읽어 로드한다. (폴더에 다른 번들 폰트가 있어도 정확히 이 파일을 쓴다)</summary>
        private static FontFamily? TryLoadUserFont()
        {
            string? path = FindUserFontPath();
            if (path == null)
                return null;

            try
            {
                var glyphTypeface = new GlyphTypeface(new Uri(path));
                var names = glyphTypeface.FamilyNames;
                string? familyName =
                    (names.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("ko-KR"), out string? ko) ? ko : null)
                    ?? (names.TryGetValue(System.Globalization.CultureInfo.GetCultureInfo("en-US"), out string? en) ? en : null)
                    ?? names.Values.FirstOrDefault();
                if (string.IsNullOrWhiteSpace(familyName))
                    return null;

                var baseUri = new Uri(FontDirectory + Path.DirectorySeparatorChar);
                return new FontFamily(baseUri, $"./#{familyName}, Malgun Gothic");
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
            return new List<string> { "나눔고딕", "굴림", "쿠키런", "프리텐다드", "나눔스퀘어라운드", "G마켓 산스", "사용자 설정" };
        }
    }
}
