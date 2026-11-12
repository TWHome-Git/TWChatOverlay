using System;
using System.Windows;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 놀라움의 격리실(설계자의 잔재) 가면 기믹: 보스가 감정 가면을 쓰면 부숴야 할 상성 가면을 그림으로 띄운다
    /// (2026-11-12 사용자: 슬픔 → 기쁨 가면, 기쁨 → 슬픔 가면, 즐거움(폭발) → 분노 가면, 분노 → 즐거움 가면 보여주기).
    ///
    /// 어느 가면을 부술지는 채팅 로그에 그대로 적혀 나오므로 화면을 찍지 않는다 (<see cref="MaskGimmickLogParser"/>).
    /// "파훼 성공" 줄이 오면 닫고, 줄이 안 와도(오답 · 폭발) 유지 시간이 지나면 닫힌다. 즐거움은 로그의 폭발까지 남은 초를 같이 센다.
    /// 실시간 줄만 넘겨받는다 (MainWindow.LogProcessing).
    /// </summary>
    public sealed class MaskGimmickService
    {
        private const int DefaultDurationSeconds = 20;
        // "가면으로"/"폭발" 줄 바로 뒤에 "…가면을 파괴" 줄이 온다. 이보다 오래된 앞줄은 다른 판의 것으로 보고 쓰지 않는다
        private static readonly TimeSpan ContextWindow = TimeSpan.FromSeconds(5);

        private MaskGimmickWindow? _window;
        private DoomPad? _wearing;
        private DateTime _wearingSeenUtc;
        private DateTime? _explosionDeadlineUtc;

        public void Observe(string formattedText)
        {
            MaskGimmickLogParser.Line line = MaskGimmickLogParser.Parse(formattedText);
            switch (line.Kind)
            {
                case MaskGimmickLogParser.LineKind.Wearing:
                    _wearing = line.Pad;
                    _wearingSeenUtc = DateTime.UtcNow;
                    _explosionDeadlineUtc = null;
                    break;

                case MaskGimmickLogParser.LineKind.Explosion:
                    _wearing = DoomPad.Pleasure;
                    _wearingSeenUtc = DateTime.UtcNow;
                    _explosionDeadlineUtc = DateTime.UtcNow.AddSeconds(Math.Max(1, line.Seconds));
                    break;

                case MaskGimmickLogParser.LineKind.Target:
                    ShowTarget(line.Pad);
                    break;

                case MaskGimmickLogParser.LineKind.Cleared:
                    _wearing = null;
                    _explosionDeadlineUtc = null;
                    Close();
                    break;
            }
        }

        private void ShowTarget(DoomPad target)
        {
            bool fresh = DateTime.UtcNow - _wearingSeenUtc <= ContextWindow;
            DoomPad? wearing = fresh ? _wearing : null;
            DateTime? explosionDeadlineUtc = fresh ? _explosionDeadlineUtc : null;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                ChatSettings? settings = GetSharedSettings();
                if (settings != null && !settings.ShowMaskGimmickAlert)
                    return;
                if (AppServices.Get<TrayAllWindowsService>().IsTrayed)
                    return; // 트레이 최소화 중에는 알림 창을 띄우지 않는다

                int seconds = settings?.MaskGimmickDurationSeconds ?? DefaultDurationSeconds;
                if (explosionDeadlineUtc.HasValue)
                {
                    // 폭발 예고가 있으면 터질 때까지(와 조금 더) 띄워 둔다
                    int untilExplosion = (int)Math.Ceiling((explosionDeadlineUtc.Value - DateTime.UtcNow).TotalSeconds) + 2;
                    seconds = Math.Max(seconds, untilExplosion);
                }

                EnsureWindow(settings).ShowTarget(target, wearing, explosionDeadlineUtc, seconds, settings?.MaskGimmickScalePercent ?? 100);
            }));
        }

        /// <summary>잠금 해제 모드에서 위치를 잡을 수 있게 예시로 띄운다.</summary>
        public void ShowPositionPreview(ChatSettings settings)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                EnsureWindow(settings).ShowPreview();
            }));
        }

        public void ClosePositionPreview()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 진행 중인 안내는 미리보기를 끝낸다고 닫지 않는다
                var window = _window;
                if (window == null || !window.IsPreviewMode)
                    return;
                CloseWindow();
            }));
        }

        public void Close()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                // 잠금 해제 미리보기는 로그 줄("파훼 성공")로 닫지 않는다
                if (_window is { IsPreviewMode: true })
                    return;
                CloseWindow();
            }));
        }

        private void CloseWindow()
        {
            try
            {
                _window?.Close();
            }
            catch { }
            finally
            {
                _window = null;
            }
        }

        private MaskGimmickWindow EnsureWindow(ChatSettings? settings)
        {
            if (_window != null && _window.IsLoaded)
                return _window;

            var window = new MaskGimmickWindow(settings);
            _window = window;
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                    _window = null;
            };

            if (settings != null)
                window.ApplyStoredBounds(settings);

            return window;
        }

        /// <summary>가면 창이 떠 있으면 설정에 저장된 위치로 옮긴다. 설정 전체 교체(프로필 불러오기) 뒤에 쓴다.</summary>
        public void ApplyStoredBounds(ChatSettings settings)
        {
            if (settings == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                var window = _window;
                if (window == null || !window.IsLoaded)
                    return;
                window.ApplyStoredBounds(settings);
            }));
        }

        private static ChatSettings? GetSharedSettings() => AppServices.TryGet<ChatSettings>();
    }
}
