using System;
using System.Text.RegularExpressions;
using System.Windows;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 신뢰의 격리실(설계자의 잔재) 반사 구간 음성 알림 (2026-11-12 사용자 요청).
    /// 보스는 신뢰(공격 구간)와 배신(받는 피해 90% 감소 + 공격하면 피해를 되돌려 받는 구간)을 번갈아 바꾼다.
    ///
    ///   ▶ 배신 : 받는 피해가 약 90% 줄고, 공격할 때마다 피해를 되돌려 받습니다.     → "반사 조심" 소리 (Reflection)
    ///   ▶ 섬광 5줄기가 날아오니 공격을 멈추고 봉인 기둥 뒤로 피하십시오.            (같은 순간에 오는 줄 — 소리는 위 줄에서 한 번만)
    ///   ▶ 설계자의 잔재가 신뢰 상태가 되었습니다. 지금 공격하십시오!               → "반사 끝" 소리 (ReflectionEnd)
    ///
    /// 소리는 어비스 반사 패턴 알림과 같은 파일 · 같은 볼륨 설정(AlertChannel.Reflection)을 쓴다.
    /// 방 안내 줄("[배신] 피해가 약 90% 줄고…")은 "배신 :" 꼴이 아니라 걸리지 않는다. 실시간 줄만 넘겨받는다.
    /// </summary>
    public sealed class TrustGimmickAlertService
    {
        private static readonly Regex BetrayalRegex = new(@"배신\s*:\s*받는\s*피해", RegexOptions.Compiled);
        private static readonly Regex TrustRegex = new(@"신뢰\s*상태가\s*되었습니다", RegexOptions.Compiled);

        // 같은 줄이 겹쳐 들어와도 소리는 한 번만
        private static readonly TimeSpan RepeatGuard = TimeSpan.FromSeconds(3);
        private DateTime _lastStartUtc = DateTime.MinValue;
        private DateTime _lastEndUtc = DateTime.MinValue;

        public enum LineKind { None, ReflectionStart, ReflectionEnd }

        public static LineKind Parse(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return LineKind.None;
            if (!text.Contains("배신", StringComparison.Ordinal) && !text.Contains("신뢰", StringComparison.Ordinal))
                return LineKind.None;

            if (BetrayalRegex.IsMatch(text))
                return LineKind.ReflectionStart;
            if (TrustRegex.IsMatch(text))
                return LineKind.ReflectionEnd;
            return LineKind.None;
        }

        public void Observe(string formattedText)
        {
            LineKind kind = Parse(formattedText);
            if (kind == LineKind.None)
                return;

            ChatSettings? settings = AppServices.TryGet<ChatSettings>();
            if (settings != null && !settings.EnableTrustReflectionAlert)
                return;

            DateTime now = DateTime.UtcNow;
            if (kind == LineKind.ReflectionStart)
            {
                if (now - _lastStartUtc < RepeatGuard)
                    return;
                _lastStartUtc = now;
                Play("Reflection.wav");
            }
            else
            {
                if (now - _lastEndUtc < RepeatGuard)
                    return;
                _lastEndUtc = now;
                Play("ReflectionEnd.wav");
            }
        }

        private static void Play(string fileName)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    AppServices.Get<NotificationService>().PlayAlert(fileName, AlertChannel.Reflection);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to play trust gimmick alert '{fileName}'.", ex);
                }
            }));
        }
    }
}
