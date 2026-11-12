using System;
using System.Text.RegularExpressions;
using System.Windows;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 감정 격리실(설계자 · 설계자의 잔재) 패턴 음성 알림 (2026-11-12 사용자 요청). 로그 줄을 보면 음성 파일을 튼다.
    ///
    ///   레이저   설계자(의 잔재) : 그대들의 영혼까지 절단시켜주마!              → 전방 조심해 (EmotionFront.mp3)
    ///   자리 이동 설계자(의 잔재) : 공간의 저편에 너의 죽음이 기다리고 있다!      → 바닥 조심해 (EmotionFloor.mp3)
    ///   배신     ▶ 배신 : 받는 피해가 약 90% 줄고, 공격할 때마다 피해를 되돌려 받습니다.   → 광선 피해 (EmotionBeam.mp3)
    ///            (신뢰의 격리실. 같은 순간 "▶ 섬광 5줄기가 날아오니…"도 오지만 소리는 위 줄에서 한 번만.
    ///             처음엔 반사 끝("신뢰 상태가 되었습니다")에도 소리를 냈으나 사용자가 뺐다.)
    ///
    /// 보스 대사는 일반 채팅 줄이라 누가 같은 말을 적어도 걸리지 않게, 말한 이가 설계자 · 설계자의 잔재일 때만 본다
    /// (호출하는 쪽에서 시스템 줄 · 일반 줄만 넘긴다 — 팀 · 클럽 · 외치기는 오지 않는다).
    /// 같은 대사가 1초 간격으로 여러 번 찍히므로(레이저가 연달아 나갈 때) 같은 소리는 잠깐 동안 다시 틀지 않는다.
    /// 볼륨은 '감정 격리실 음성 볼륨'(AlertChannel.EmotionRoom). 실시간 줄만 넘겨받는다.
    /// </summary>
    public sealed class EmotionRoomVoiceAlertService
    {
        public enum Pattern
        {
            None,
            /// <summary>레이저: 전방 조심해.</summary>
            Laser,
            /// <summary>자리 이동: 바닥 조심해.</summary>
            Floor,
            /// <summary>신뢰의 격리실 배신(반사) 구간 시작: 광선 피해.</summary>
            Beam,
        }

        public const string LaserSound = "EmotionFront.mp3";
        public const string FloorSound = "EmotionFloor.mp3";
        public const string BeamSound = "EmotionBeam.mp3";

        // 줄 앞의 시각("[21시 13분 56초] ")은 있어도 없어도 된다. 말한 이는 "설계자" 또는 "설계자의 잔재"만
        private const string Speaker = @"^\s*(?:\[[^\]]*\]\s*)?설계자(?:의\s*잔재)?\s*:\s*";

        private static readonly Regex LaserRegex = new(Speaker + @"그대들의\s*영혼까지\s*절단시켜주마", RegexOptions.Compiled);
        private static readonly Regex FloorRegex = new(Speaker + @"공간의\s*저편에\s*너의\s*죽음이\s*기다리고\s*있다", RegexOptions.Compiled);
        private static readonly Regex BetrayalRegex = new(@"배신\s*:\s*받는\s*피해", RegexOptions.Compiled);

        // 같은 소리를 이 시간 안에 다시 틀지 않는다 (대사가 1초마다 연달아 찍혀도 음성이 겹치지 않게)
        private static readonly TimeSpan RepeatGuard = TimeSpan.FromSeconds(3);
        private readonly DateTime[] _lastPlayedUtc = new DateTime[4];

        /// <summary>줄이 어느 패턴인지. <paramref name="isSystemLog"/>가 true면 시스템 줄(배신), false면 채팅 줄(보스 대사)로 본다.</summary>
        public static Pattern Parse(string? text, bool isSystemLog)
        {
            if (string.IsNullOrEmpty(text))
                return Pattern.None;

            if (isSystemLog)
                return text.Contains("배신", StringComparison.Ordinal) && BetrayalRegex.IsMatch(text) ? Pattern.Beam : Pattern.None;

            if (!text.Contains("설계자", StringComparison.Ordinal))
                return Pattern.None;
            if (LaserRegex.IsMatch(text))
                return Pattern.Laser;
            if (FloorRegex.IsMatch(text))
                return Pattern.Floor;
            return Pattern.None;
        }

        public void Observe(string formattedText, bool isSystemLog)
        {
            Pattern pattern = Parse(formattedText, isSystemLog);
            if (pattern == Pattern.None)
                return;

            ChatSettings? settings = AppServices.TryGet<ChatSettings>();
            bool enabled = pattern switch
            {
                Pattern.Laser => settings?.EnableEmotionLaserAlert ?? true,
                Pattern.Floor => settings?.EnableEmotionFloorAlert ?? true,
                _ => settings?.EnableTrustReflectionAlert ?? true,
            };
            if (!enabled)
                return;

            DateTime now = DateTime.UtcNow;
            if (now - _lastPlayedUtc[(int)pattern] < RepeatGuard)
                return;
            _lastPlayedUtc[(int)pattern] = now;

            string file = pattern switch
            {
                Pattern.Laser => LaserSound,
                Pattern.Floor => FloorSound,
                _ => BeamSound,
            };
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    AppServices.Get<NotificationService>().PlayAlert(file, AlertChannel.EmotionRoom);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to play emotion room alert '{file}'.", ex);
                }
            }));
        }
    }
}
