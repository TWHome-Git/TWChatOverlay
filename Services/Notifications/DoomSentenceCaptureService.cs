using System;
using System.Collections.Generic;
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
    /// 찍은 그림은 설정한 영역(퍼센트)만 잘라 찍힌 크기 그대로(표시 배율 100%) 보여 주고, 원본은 Logs/DoomSentence 에 남겨 영역을 맞출 때 쓴다.
    ///
    /// 연속 캡처(시험, 2026-11-12): 한 장만 찍으면 대미지 숫자·이름표가 공지 글을 덮는다. 공지는 제자리에 있고 숫자는 움직이므로
    /// 공지가 떠 있는 동안 여러 장을 찍어 픽셀별 중앙값으로 합치면 움직이는 것이 빠진다 (<see cref="ScreenCaptureHelper.MedianComposite"/>).
    /// 첫 장은 바로 보여 주고, 장이 모일 때마다 합친 그림으로 바꾼다. 장마다 잘라 낸 그림과 합친 그림도 저장해 글자 인식이 되는지 살펴본다.
    ///
    /// 줄 판독은 <see cref="DoomSentenceLogParser"/>, 화면 찍기는 <see cref="ScreenCaptureHelper"/>가 한다.
    /// 실시간 줄만 넘겨받는다 (MainWindow.LogProcessing) — 시작할 때 지난 로그를 다시 읽으며 찍지 않게.
    /// </summary>
    public sealed class DoomSentenceCaptureService
    {
        /// <summary>
        /// 자를 영역 기본값 (왼쪽,위,너비,높이 %). 설정이 비었거나 잘못 적혔을 때도 이 값을 쓴다.
        /// 2026-11-12 실제 1회차 캡처(게임 1920x1080, 확대 화면 2576x1449)로 잰 자리: 공지 글은 가로 40~60% · 세로 20~25%에 뜬다.
        /// 발판 8개일 때는 순서가 두 줄이라 아래로 여유를 뒀다.
        /// </summary>
        public const string DefaultRegionText = "34,17,32,15";
        public const int DefaultDelayMilliseconds = 250;

        // 다 밟았을 때 창을 조금 더 보여 준 뒤 닫는다
        private static readonly TimeSpan CompletedLinger = TimeSpan.FromSeconds(1.5);
        // 원본 한 장 + 잘라 낸 장들 + 합친 그림이 회차마다 쌓인다 (연속 6장이면 8개). 20회차쯤 남긴다
        private const int KeptCaptureFiles = 160;
        // 합친 그림은 이만큼 모인 뒤부터 보여 준다 (두 장의 중앙값은 평균이라 겹쳐 보일 뿐이다)
        private const int MinFramesForComposite = 3;

        private DoomSentenceCaptureWindow? _window;
        private int _generation;        // 회차마다 올린다: 늦게 끝난 캡처가 다음 회차 창을 덮지 않게
        private volatile bool _roundActive;

        /// <summary>한 회차의 캡처 설정 (UI 스레드에서 읽어 백그라운드로 넘긴다).</summary>
        private sealed record CaptureOptions(int DelayMilliseconds, string RegionText, bool Save, string? ProcessNames, int BurstFrames, int BurstIntervalMilliseconds);

        /// <summary>첫 장을 찍은 결과. Shown 이 null 이면 못 찍은 것이고 Note 에 까닭이 있다.</summary>
        private sealed record FirstCapture(BitmapSource? Shown, string? Note, ScreenCaptureHelper.PixelRect CropRect, string? FilePrefix);

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

                var options = new CaptureOptions(
                    settings?.DoomSentenceCaptureDelayMilliseconds ?? DefaultDelayMilliseconds,
                    settings?.DoomSentenceCaptureRegion ?? DefaultRegionText,
                    settings?.SaveDoomSentenceCapture ?? true,
                    settings?.TopmostGuardProcessNames,
                    settings?.DoomSentenceCaptureBurstFrames ?? 1,
                    settings?.DoomSentenceCaptureBurstIntervalMilliseconds ?? 350);

                _ = CaptureAndShowAsync(generation, round, total, seconds, startedUtc, options);
            }));
        }

        private bool IsCurrent(int generation) => generation == Volatile.Read(ref _generation) && _roundActive;

        private async Task CaptureAndShowAsync(int generation, int round, int total, int seconds, DateTime startedUtc, CaptureOptions options)
        {
            FirstCapture first;
            try
            {
                if (options.DelayMilliseconds > 0)
                    await Task.Delay(options.DelayMilliseconds).ConfigureAwait(false);

                // 화면 복사와 PNG 쓰기는 UI 스레드 밖에서 한다
                first = await Task.Run(() => CaptureFirst(round, options)).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Doom sentence capture failed.", ex);
                first = new FirstCapture(null, "화면을 찍지 못했습니다", default, null);
            }

            bool canBurst = false;
            await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsCurrent(generation))
                    return; // 그 사이 회차가 끝났거나 다음 회차가 시작됐다

                ChatSettings? settings = GetSharedSettings();
                DateTime deadlineUtc = startedUtc.AddSeconds(Math.Max(1, seconds));
                DoomSentenceCaptureWindow window = EnsureWindow(settings);
                window.ShowCapture(first.Shown, round, total, deadlineUtc, first.Note, settings?.DoomSentenceCaptureScalePercent ?? 100);

                // 이 창이 공지 자리를 덮고 있으면 다음 장부터는 창이 찍힌다. 창을 캡처에서 빼는 데 성공했거나 겹치지 않을 때만 이어 찍는다
                canBurst = first.Shown != null && (window.IsExcludedFromCapture || !window.OverlapsScreenRect(first.CropRect));
            }));

            if (!canBurst || options.BurstFrames <= 1)
                return;

            try
            {
                await CaptureBurstAsync(generation, round, first, options).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Doom sentence burst capture failed.", ex);
            }
        }

        /// <summary>공지가 떠 있는 동안 같은 자리를 이어 찍고, 모일 때마다 합친 그림으로 창을 바꾼다.</summary>
        private async Task CaptureBurstAsync(int generation, int round, FirstCapture first, CaptureOptions options)
        {
            var frames = new List<BitmapSource> { first.Shown! };
            BitmapSource? composite = null;

            for (int index = 2; index <= options.BurstFrames; index++)
            {
                await Task.Delay(options.BurstIntervalMilliseconds).ConfigureAwait(false);
                if (!IsCurrent(generation))
                    break;

                int frameNumber = index;
                BitmapSource? merged = await Task.Run(() =>
                {
                    BitmapSource frame = ScreenCaptureHelper.Capture(first.CropRect);
                    frames.Add(frame);
                    if (first.FilePrefix != null)
                        TrySave(frame, $"{first.FilePrefix}_f{frameNumber}.png");
                    return frames.Count >= MinFramesForComposite ? ScreenCaptureHelper.MedianComposite(frames) : null;
                }).ConfigureAwait(false);

                if (merged == null)
                    continue;
                composite = merged;

                await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!IsCurrent(generation))
                        return;
                    var window = _window;
                    if (window != null && window.IsLoaded)
                        window.UpdateImage(merged);
                }));
            }

            if (composite != null && first.FilePrefix != null)
            {
                BitmapSource last = composite;
                await Task.Run(() => TrySave(last, $"{first.FilePrefix}_median.png")).ConfigureAwait(false);
            }
        }

        private static FirstCapture CaptureFirst(int round, CaptureOptions options)
        {
            ScreenCaptureHelper.PixelRect? area = ScreenCaptureHelper.ResolveGameArea(options.ProcessNames, ScalerTopmostGuard.MagpieWindowClass);
            if (area == null)
                return new FirstCapture(null, "게임 창을 찾지 못했습니다", default, null);

            BitmapSource full = ScreenCaptureHelper.Capture(area.Value);

            if (!ScreenCaptureHelper.PercentRegion.TryParse(options.RegionText, out ScreenCaptureHelper.PercentRegion region))
                ScreenCaptureHelper.PercentRegion.TryParse(DefaultRegionText, out region);

            // 이어 찍을 때는 화면에서 이 자리만 바로 찍는다 (전체를 찍으면 장마다 0.1초가 든다)
            ScreenCaptureHelper.PixelRect inner = ScreenCaptureHelper.RegionRect(area.Value.Width, area.Value.Height, region);
            var cropRect = new ScreenCaptureHelper.PixelRect(area.Value.X + inner.X, area.Value.Y + inner.Y, inner.Width, inner.Height);
            BitmapSource cropped = ScreenCaptureHelper.Crop(full, region);

            string? prefix = null;
            if (options.Save)
            {
                string directory = LogStoragePaths.DoomSentenceDirectory;
                prefix = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}_r{round}");
                TrySave(full, prefix + ".png");
                if (options.BurstFrames > 1)
                    TrySave(cropped, prefix + "_f1.png");
                PruneOldCaptures(directory);
            }

            return new FirstCapture(cropped, null, cropRect, prefix);
        }

        private static void TrySave(BitmapSource image, string path)
        {
            try
            {
                ScreenCaptureHelper.SavePng(image, path);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to save doom sentence capture '{Path.GetFileName(path)}'.", ex);
            }
        }

        /// <summary>저장한 그림은 최근 것만 남긴다 (원본 한 장이 6MB쯤이라 쌓이면 금방 커진다).</summary>
        private static void PruneOldCaptures(string directory)
        {
            try
            {
                foreach (string old in Directory.EnumerateFiles(directory, "*.png")
                             .OrderByDescending(f => f, StringComparer.Ordinal)
                             .Skip(KeptCaptureFiles))
                {
                    try { File.Delete(old); } catch { }
                }
            }
            catch { }
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

        /// <summary>캡처 창이 떠 있으면 설정에 저장된 위치로 옮긴다. 설정 전체 교체(프로필 불러오기) 뒤에 쓴다.</summary>
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
