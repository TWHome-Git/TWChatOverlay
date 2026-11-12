using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 설계자의 잔재 "운명 선고"가 시작되면 게임 화면에 뜬 발판 순서 공지를 찍어, 회차가 끝날 때까지 창에 띄워 둔다.
    ///
    /// 순서 공지는 화면에 3초쯤만 뜨고 채팅 로그에는 남지 않는다 (로그에는 "▶ 운명 선고 N회차 : 발판 K개 (제한 시간 S초)"만).
    /// 로그 줄과 화면 공지는 서버에서 같은 순간에 오므로, 그 줄을 본 직후(설정한 지연 뒤) 화면을 찍으면 공지가 담긴다.
    /// 찍은 그림은 설정한 영역(퍼센트)만 잘라 보여 주고, 원본은 Logs/DoomSentence 에 남겨 영역을 맞출 때 쓴다.
    ///
    /// 줄 판독은 <see cref="DoomSentenceLogParser"/>, 화면 찍기는 <see cref="ScreenCaptureHelper"/>가 한다.
    /// 실시간 줄만 넘겨받는다 (MainWindow.LogProcessing) — 시작할 때 지난 로그를 다시 읽으며 찍지 않게.
    /// </summary>
    public sealed class DoomSentenceCaptureService
    {
        /// <summary>자를 영역 기본값 (왼쪽,위,너비,높이 %). 설정이 비었거나 잘못 적혔을 때도 이 값을 쓴다.</summary>
        public const string DefaultRegionText = "20,10,60,35";
        public const int DefaultDelayMilliseconds = 250;

        // 다 밟았을 때 창을 조금 더 보여 준 뒤 닫는다
        private static readonly TimeSpan CompletedLinger = TimeSpan.FromSeconds(1.5);
        private const int KeptCaptureFiles = 20;

        private DoomSentenceCaptureWindow? _window;
        private int _generation;        // 회차마다 올린다: 늦게 끝난 캡처가 다음 회차 창을 덮지 않게
        private volatile bool _roundActive;

        public void Observe(string formattedText)
        {
            DoomSentenceLogParser.Line line = DoomSentenceLogParser.Parse(formattedText);
            switch (line.Kind)
            {
                case DoomSentenceLogParser.LineKind.RoundStart:
                    BeginRound(line.Round, line.Total, line.Seconds);
                    break;
                case DoomSentenceLogParser.LineKind.Step when _roundActive:
                    OnStep(line.Done, line.Total);
                    break;
                case DoomSentenceLogParser.LineKind.Triggered when _roundActive:
                    EndRound();
                    break;
            }
        }

        private void BeginRound(int round, int total, int seconds)
        {
            int generation = Interlocked.Increment(ref _generation);
            DateTime startedUtc = DateTime.UtcNow;
            // 바로 켜 둔다: 같은 묶음으로 들어온 다음 줄(밟음 · 발동)이 아래 예약보다 먼저 판독될 수 있다
            _roundActive = true;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != Volatile.Read(ref _generation))
                    return;

                ChatSettings? settings = GetSharedSettings();
                bool disabled = settings != null && !settings.ShowDoomSentenceCapture;
                // 트레이 최소화 중에는 알림 창을 띄우지 않는다
                if (disabled || AppServices.Get<TrayAllWindowsService>().IsTrayed)
                {
                    _roundActive = false;
                    return;
                }

                // 앞 회차 창이 아직 떠 있으면 숨긴다 — 공지 자리에 겹쳐 있으면 같이 찍히기 때문이다
                if (_window is { IsLoaded: true, IsVisible: true })
                    _window.HideForCapture();

                int delay = settings?.DoomSentenceCaptureDelayMilliseconds ?? DefaultDelayMilliseconds;
                string regionText = settings?.DoomSentenceCaptureRegion ?? DefaultRegionText;
                bool save = settings?.SaveDoomSentenceCapture ?? true;
                string? processNames = settings?.TopmostGuardProcessNames;

                _ = CaptureAndShowAsync(generation, round, total, seconds, startedUtc, delay, regionText, save, processNames);
            }));
        }

        private async Task CaptureAndShowAsync(int generation, int round, int total, int seconds, DateTime startedUtc,
            int delayMilliseconds, string regionText, bool save, string? processNames)
        {
            BitmapSource? shown = null;
            string? note = null;

            try
            {
                if (delayMilliseconds > 0)
                    await Task.Delay(delayMilliseconds).ConfigureAwait(false);

                // 화면 복사와 PNG 쓰기는 UI 스레드 밖에서 한다
                (shown, note) = await Task.Run(() => CaptureNow(round, regionText, save, processNames)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Doom sentence capture failed.", ex);
                note = "화면을 찍지 못했습니다";
            }

            await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != Volatile.Read(ref _generation) || !_roundActive)
                    return; // 그 사이 회차가 끝났거나 다음 회차가 시작됐다

                ChatSettings? settings = GetSharedSettings();
                DateTime deadlineUtc = startedUtc.AddSeconds(Math.Max(1, seconds));
                EnsureWindow(settings).ShowCapture(shown, round, total, deadlineUtc, note);
            }));
        }

        private static (BitmapSource? Shown, string? Note) CaptureNow(int round, string regionText, bool save, string? processNames)
        {
            ScreenCaptureHelper.PixelRect? area = ScreenCaptureHelper.ResolveGameArea(processNames, ScalerTopmostGuard.MagpieWindowClass);
            if (area == null)
                return (null, "게임 창을 찾지 못했습니다");

            BitmapSource full = ScreenCaptureHelper.Capture(area.Value);

            if (save)
            {
                try
                {
                    string directory = LogStoragePaths.DoomSentenceDirectory;
                    string path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}_r{round}.png");
                    ScreenCaptureHelper.SavePng(full, path);
                    PruneOldCaptures(directory);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to save doom sentence capture.", ex);
                }
            }

            if (!ScreenCaptureHelper.PercentRegion.TryParse(regionText, out ScreenCaptureHelper.PercentRegion region))
                ScreenCaptureHelper.PercentRegion.TryParse(DefaultRegionText, out region);

            return (ScreenCaptureHelper.Crop(full, region), null);
        }

        /// <summary>원본 캡처는 최근 것만 남긴다 (한 장이 2~3MB라 쌓이면 금방 커진다).</summary>
        private static void PruneOldCaptures(string directory)
        {
            foreach (string old in Directory.EnumerateFiles(directory, "*.png")
                         .OrderByDescending(f => f, StringComparer.Ordinal)
                         .Skip(KeptCaptureFiles))
            {
                try { File.Delete(old); } catch { }
            }
        }

        private void OnStep(int done, int total)
        {
            int generation = Volatile.Read(ref _generation);
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (generation != Volatile.Read(ref _generation))
                    return;

                var window = _window;
                if (window != null && window.IsLoaded)
                    window.UpdateProgress(done, total);

                if (total > 0 && done >= total)
                {
                    // 다 밟았다: 잠깐 보여 주고 닫는다
                    _roundActive = false;
                    window?.CloseAfter(CompletedLinger);
                }
            }));
        }

        private void EndRound()
        {
            _roundActive = false;
            Interlocked.Increment(ref _generation); // 아직 안 끝난 캡처가 있으면 버린다
            Close();
        }

        /// <summary>잠금 해제 모드에서 위치·크기를 잡을 수 있게 예시로 띄운다.</summary>
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
                // 진행 중인 회차의 창은 미리보기를 끝낸다고 닫지 않는다
                var window = _window;
                if (window == null || !window.IsPreviewMode)
                    return;
                CloseWindow();
            }));
        }

        public void Close()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(CloseWindow));
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

        private DoomSentenceCaptureWindow EnsureWindow(ChatSettings? settings)
        {
            if (_window != null && _window.IsLoaded)
                return _window;

            var window = new DoomSentenceCaptureWindow(settings);
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

        /// <summary>캡처 창이 떠 있으면 설정에 저장된 위치/크기로 옮긴다. 설정 전체 교체(프로필 불러오기) 뒤에 쓴다.</summary>
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
