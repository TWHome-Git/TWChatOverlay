using System;
using System.Collections.Concurrent;
using System.Windows.Media;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 채팅 카테고리와 설정 색상 문자열을 WPF 브러시로 변환합니다.
    /// 줄마다(조각마다) 호출되는 핫패스이므로 색상 문자열별로 Frozen 브러시를 캐시합니다.
    /// </summary>
    public static class ChatBrushResolver
    {
        // 설정에 등장하는 색상 문자열은 수십 종 수준이라 캐시가 무한히 자라지 않는다
        private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>색상 문자열(#RRGGBB)을 고정(Frozen) 브러시로. 잘못된 값이면 흰색.</summary>
        public static SolidColorBrush ToBrush(string? hex)
        {
            if (string.IsNullOrEmpty(hex))
                return Brushes.White;

            return Cache.GetOrAdd(hex, static key =>
            {
                try
                {
                    if (ColorConverter.ConvertFromString(key) is Color color)
                    {
                        var brush = new SolidColorBrush(color);
                        brush.Freeze();
                        return brush;
                    }
                }
                catch
                {
                    // 잘못된 색상 문자열 — 흰색 폴백을 캐시해 반복 예외를 막는다
                }

                return Brushes.White;
            });
        }

        /// <summary>클럽 보스 공지 줄은 동기화가 꺼져 있으면 전용 색을 쓴다.</summary>
        public static SolidColorBrush Resolve(ChatSettings settings, ChatCategory category, bool isClubBossMessage)
        {
            if (isClubBossMessage && !settings.ClubBossColorSync)
                return ToBrush(settings.ClubBossColor);

            return Resolve(settings, category);
        }

        public static SolidColorBrush Resolve(ChatSettings settings, ChatCategory category)
        {
            string hex = category switch
            {
                ChatCategory.System or ChatCategory.System2 or ChatCategory.System3 => settings.SystemColor,
                ChatCategory.Team => settings.TeamColor,
                ChatCategory.Club => settings.ClubColor,
                ChatCategory.Shout => settings.ShoutColor,
                _ => settings.NormalColor
            };

            return ToBrush(hex);
        }
    }
}
