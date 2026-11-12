using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 설계자의 잔재 "운명 선고"가 시작되면 게임 화면에 뜬 발판 순서 공지를 찍어 글자 인식으로 읽고,
    /// 회차가 끝날 때까지 큰 화살표로 순서를 띄워 둔다 (<see cref="DoomSentenceCaptureWindow"/>).
    ///
    /// 순서 공지는 화면에 3초쯤만 뜨고 채팅 로그에는 남지 않는다 (로그에는 "▶ 운명 선고 N회차 : 발판 K개 (제한 시간 S초)"만).
    /// 로그 줄과 화면 공지는 서버에서 같은 순간에 오므로, 그 줄을 본 직후(설정한 지연 뒤) 화면을 찍으면 공지가 담긴다.
    ///
    /// 한 장만 찍으면 대미지 숫자·이름표가 공지 글을 덮어 인식이 되다 말다 한다. 공지는 제자리에 있고 숫자는 움직이므로
    /// 공지가 떠 있는 동안 여러 장을 찍어 픽셀별 중앙값으로 합치면 움직이는 것이 빠진다 (<see cref="ScreenCaptureHelper.MedianComposite"/>).
    /// 2026-11-12 실전 1회차: 첫 장은 못 읽었고, 둘째 장과 3~6장을 합친 그림은 모두 정확히 읽었다.
    /// 그래서 장이 들어올 때마다 다시 읽는다 (<see cref="DoomSentenceOcr"/>, 한 번에 0.05초쯤). 읽은 낱말 수가 로그의 발판 수와
    /// 같을 때만 보여 주고, 두 번 이어 같은 순서가 나오거나 마지막 장까지 가면 "확인 중" 표시를 뗀다.
    /// 끝내 못 읽으면(또는 한글 인식기가 없으면) 찍은 화면을 대신 보여 준다.
    ///
    /// 순서 창은 이어 찍는 2초쯤만 화면 캡처에서 빼고(자기 자신이 찍혀 공지를 가리지 않게) 다 읽으면 다시 찍히게 돌려놓는다.
    /// 찍은 그림은 읽고 나면 버린다. 설정에서 '캡처 원본 저장'을 켰을 때만(영역을 맞추거나 인식이 틀릴 때 살펴보는 용도)
    /// 원본 한 장 · 잘라 낸 장들 · 합친 그림과 인식 기록(_ocr.txt)을 Logs/DoomSentence 에 남긴다 (2026-11-12 사용자: 실제 동작에서는 저장하지 않는다).
    /// 줄 판독은 <see cref="DoomSentenceLogParser"/>, 화면 찍기는 <see cref="ScreenCaptureHelper"/>가 한다.
    /// 디버그 빌드에서는 순서를 읽은 뒤 화살표가 뜬 게임 화면을 한 장(_shot.png) 남긴다 — 릴리스 빌드에는 그 코드가 없다.
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
        // 원본 한 장 + 잘라 낸 장들 + 합친 그림 + 기록이 회차마다 쌓인다 (연속 6장이면 9개). 20회차쯤 남긴다
        private const int KeptCaptureFiles = 180;
        // 합친 그림은 이만큼 모인 뒤부터 쓴다 (두 장의 중앙값은 평균이라 겹쳐 보일 뿐이다)
        private const int MinFramesForComposite = 3;

        private DoomSentenceCaptureWindow? _window;
        private int _generation;        // 회차마다 올린다: 늦게 끝난 캡처가 다음 회차 창을 덮지 않게
        private volatile bool _roundActive;
        private volatile int _roundTotal;   // 이번 회차의 발판 수 (다 밟았을 때 n / n 으로 채운다)

        /// <summary>한 회차의 캡처 설정 (UI 스레드에서 읽어 백그라운드로 넘긴다).</summary>
        private sealed record CaptureOptions(int DelayMilliseconds, string RegionText, bool Save, string? ProcessNames, int BurstFrames, int BurstIntervalMilliseconds);

        /// <summary>첫 장을 찍은 결과. Shown 이 null 이면 못 찍은 것이고 Note 에 까닭이 있다.</summary>
        private sealed record FirstCapture(BitmapSource? Shown, string? Note, ScreenCaptureHelper.PixelRect CropRect, string? FilePrefix);

        /// <summary>한 회차에서 글자 인식을 이어 가며 쌓는 것.</summary>
        private sealed class Recognition
        {
            public List<DoomPad>? LastValid;
            public readonly StringBuilder Log = new();
        }

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
                case DoomSentenceLogParser.LineKind.Completed when _roundActive:
                    // 마지막 발판은 "밟았습니다 (n / n)" 줄이 없다 — 이 줄이 완료 신호다 (2026-11-12 사용자)
                    OnStep(_roundTotal, _roundTotal);
                    break;
            }
        }

        private void BeginRound(int round, int total, int seconds)
        {
            int generation = Interlocked.Increment(ref _generation);
            DateTime startedUtc = DateTime.UtcNow;
            // 바로 켜 둔다: 같은 묶음으로 들어온 다음 줄(밟음 · 발동)이 아래 예약보다 먼저 판독될 수 있다
            _roundActive = true;
            _roundTotal = total;

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
                    settings?.SaveDoomSentenceCapture ?? false,
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

            var burstClock = Stopwatch.StartNew(); // 이어 찍는 장들의 시각은 첫 장을 찍은 때부터 잰다
            bool canRead = first.Shown != null && DoomSentenceOcr.IsAvailable;
            bool canBurst = false;

            await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsCurrent(generation))
                    return; // 그 사이 회차가 끝났거나 다음 회차가 시작됐다

                ChatSettings? settings = GetSharedSettings();
                DateTime deadlineUtc = startedUtc.AddSeconds(Math.Max(1, seconds));
                DoomSentenceCaptureWindow window = EnsureWindow(settings);
                string status = first.Shown == null ? first.Note ?? "화면을 찍지 못했습니다" : "순서 읽는 중…";
                window.ShowRound(round, total, deadlineUtc, settings?.DoomSentenceCaptureScalePercent ?? 100, status);
                // 이어 찍는 동안에는 이 창이 찍히지 않게 캡처에서 뺀다 (새 창은 만들 때 이미 빠져 있다). 다 읽은 뒤 RestoreCaptureAsync 가 돌려놓는다
                window.SetCaptureExcluded(true);

                if (first.Shown != null && !canRead)
                    window.ShowFallbackImage(first.Shown, "한글 글자 인식기가 없어 찍은 화면을 보여 줍니다 (Windows 설정 > 언어에 한국어 추가)");

                // 이 창이 공지 자리를 덮고 있으면 다음 장부터는 창이 찍힌다. 창을 캡처에서 빼는 데 성공했거나 겹치지 않을 때만 이어 찍는다
                canBurst = first.Shown != null && (window.IsExcludedFromCapture || !window.OverlapsScreenRect(first.CropRect));
            }));

            if (!canRead || !IsCurrent(generation))
            {
                await RestoreCaptureAsync().ConfigureAwait(false);
                return;
            }

            var recognition = new Recognition();
            try
            {
                bool burst = canBurst && options.BurstFrames > 1;
                await ReadAndShowAsync(generation, first.Shown!, total, "1장", recognition, isLast: !burst).ConfigureAwait(false);

                BitmapSource best = first.Shown!;
                if (burst)
                    best = await CaptureBurstAsync(generation, total, first, options, recognition, burstClock).ConfigureAwait(false) ?? best;

                if (recognition.LastValid == null && IsCurrent(generation))
                {
                    BitmapSource fallback = best;
                    await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!IsCurrent(generation))
                            return;
                        var window = _window;
                        if (window != null && window.IsLoaded)
                            window.ShowFallbackImage(fallback, "순서를 읽지 못해 찍은 화면을 보여 줍니다");
                    }));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Doom sentence recognition failed.", ex);
            }
            finally
            {
                if (first.FilePrefix != null && recognition.Log.Length > 0)
                {
                    try { File.WriteAllText(first.FilePrefix + "_ocr.txt", recognition.Log.ToString(), Encoding.UTF8); } catch { }
                }
            }

            await RestoreCaptureAsync().ConfigureAwait(false);

#if DEBUG
            // 디버그 빌드: 화살표가 뜬 화면을 한 장 남긴다 (2026-11-12 사용자: 디버그 모드에선 화살표 나올 때 스샷 찍어서 보관)
            if (recognition.LastValid != null)
                await SaveDebugShotAsync(generation, round, options).ConfigureAwait(false);
#endif
        }

#if DEBUG
        /// <summary>
        /// 디버그 빌드 전용: 순서 창이 다시 캡처에 나오기를 잠깐 기다렸다가 게임 화면(순서 창이 그 밖에 걸쳐 있으면 그만큼 넓혀서)을
        /// 찍어 Logs/DoomSentence/…_shot.png 로 남긴다. 실제로 어떻게 보였는지 나중에 보려는 것이라 '캡처 원본 저장' 설정과 상관없이 남긴다.
        /// 릴리스 빌드에는 이 코드가 들어가지 않는다.
        /// </summary>
        private async Task SaveDebugShotAsync(int generation, int round, CaptureOptions options)
        {
            try
            {
                ScreenCaptureHelper.PixelRect? windowRect = null;
                await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var window = _window;
                    if (window != null && window.IsLoaded && IsCurrent(generation))
                        windowRect = window.GetScreenRect();
                }));
                if (windowRect == null)
                    return;

                // 창이 다시 찍히기 시작할 때까지 화면이 한두 번 갱신되기를 기다린다
                await Task.Delay(400).ConfigureAwait(false);
                if (!IsCurrent(generation))
                    return;

                ScreenCaptureHelper.PixelRect own = windowRect.Value;
                await Task.Run(() =>
                {
                    ScreenCaptureHelper.PixelRect area = ScreenCaptureHelper.ResolveGameArea(options.ProcessNames, ScalerTopmostGuard.MagpieWindowClass) ?? own;
                    int left = Math.Min(area.X, own.X);
                    int top = Math.Min(area.Y, own.Y);
                    int right = Math.Max(area.X + area.Width, own.X + own.Width);
                    int bottom = Math.Max(area.Y + area.Height, own.Y + own.Height);
                    BitmapSource shot = ScreenCaptureHelper.Capture(new ScreenCaptureHelper.PixelRect(left, top, right - left, bottom - top));

                    string directory = LogStoragePaths.DoomSentenceDirectory;
                    TrySave(shot, Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}_r{round}_shot.png"));
                    PruneOldCaptures(directory);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save doom sentence debug shot.", ex);
            }
        }
#endif

        /// <summary>
        /// 이어 찍기가 끝났다: 순서 창을 다시 화면 캡처에 나오게 돌려놓는다. 빼 둔 채로 두면 스크린샷 · 녹화에 화살표가 안 찍힌다.
        /// </summary>
        private async Task RestoreCaptureAsync()
        {
            try
            {
                await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    var window = _window;
                    if (window != null && window.IsLoaded)
                        window.SetCaptureExcluded(false);
                }));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to restore capture visibility of doom sentence window.", ex);
            }
        }

        /// <summary>
        /// 공지가 떠 있는 동안 같은 자리를 이어 찍고, 장이 들어올 때마다(3장부터는 합친 그림으로) 다시 읽는다.
        /// 돌려주는 것은 마지막으로 만든 그림 (못 읽었을 때 대신 보여 줄 것). 회차가 끝나면 그 자리에서 멈춘다.
        /// </summary>
        private async Task<BitmapSource?> CaptureBurstAsync(int generation, int total, FirstCapture first, CaptureOptions options,
            Recognition recognition, Stopwatch clock)
        {
            var frames = new List<BitmapSource> { first.Shown! };
            BitmapSource? latest = null;
            BitmapSource? composite = null;

            for (int index = 2; index <= options.BurstFrames; index++)
            {
                // 인식에 든 시간만큼 밀리지 않게, 첫 장부터 간격 × (장 번호 - 1) 시각에 맞춘다
                long wait = (long)options.BurstIntervalMilliseconds * (index - 1) - clock.ElapsedMilliseconds;
                if (wait > 0)
                    await Task.Delay((int)wait).ConfigureAwait(false);
                if (!IsCurrent(generation))
                    return latest;

                int frameNumber = index;
                latest = await Task.Run(() =>
                {
                    BitmapSource frame = ScreenCaptureHelper.Capture(first.CropRect);
                    frames.Add(frame);
                    if (first.FilePrefix != null)
                        TrySave(frame, $"{first.FilePrefix}_f{frameNumber}.png");
                    return frames.Count >= MinFramesForComposite ? ScreenCaptureHelper.MedianComposite(frames) : frame;
                }).ConfigureAwait(false);

                bool merged = frames.Count >= MinFramesForComposite;
                if (merged)
                    composite = latest;

                await ReadAndShowAsync(generation, latest, total, merged ? $"{frames.Count}장 합침" : $"{frameNumber}번째 장", recognition,
                    isLast: index == options.BurstFrames).ConfigureAwait(false);
            }

            if (composite != null && first.FilePrefix != null)
            {
                BitmapSource last = composite;
                await Task.Run(() => TrySave(last, $"{first.FilePrefix}_median.png")).ConfigureAwait(false);
            }
            return latest;
        }

        /// <summary>
        /// 그림 하나를 읽어, 낱말 수가 발판 수와 같으면 창에 순서를 띄운다. 앞서 읽은 순서와 같거나 마지막 장이면 확정으로 본다.
        /// </summary>
        private async Task ReadAndShowAsync(int generation, BitmapSource image, int total, string label, Recognition recognition, bool isLast)
        {
            DoomSentenceOcr.Result? result = await Task.Run(() => DoomSentenceOcr.RecognizeAsync(image)).ConfigureAwait(false);
            IReadOnlyList<DoomPad> order = result?.Order ?? Array.Empty<DoomPad>();
            bool valid = total > 0 && order.Count == total;

            recognition.Log.AppendLine($"[{DateTime.Now:HH:mm:ss.fff}] {label}: {(valid ? "OK" : $"낱말 {order.Count}개 (발판 {total}개)")} → {string.Join(" ", order.Select(p => DoomSentenceOrderParser.NameOf(p) + DoomSentenceOrderParser.ArrowOf(p)))}");
            if (result != null)
                recognition.Log.AppendLine("    " + result.RawText.Replace("\r", string.Empty).Replace("\n", "\n    "));

            if (!valid)
                return;

            bool confirmed = isLast || (recognition.LastValid != null && recognition.LastValid.SequenceEqual(order));
            var shown = order.ToList();
            recognition.LastValid = shown;

            await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsCurrent(generation))
                    return;
                var window = _window;
                if (window != null && window.IsLoaded)
                    window.ShowOrder(shown, confirmed);
            }));
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

        /// <summary>저장한 것은 최근 것만 남긴다 (원본 한 장이 6MB쯤이라 쌓이면 금방 커진다).</summary>
        private static void PruneOldCaptures(string directory)
        {
            try
            {
                foreach (string old in Directory.EnumerateFiles(directory)
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

        /// <summary>순서 창이 떠 있으면 설정에 저장된 위치로 옮긴다. 설정 전체 교체(프로필 불러오기) 뒤에 쓴다.</summary>
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
