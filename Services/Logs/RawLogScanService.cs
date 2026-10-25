using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 스캔 중 소비자에게 전달되는 줄 하나. 원본 HTML(줄바꿈 병합 완료)과, 필요할 때 한 번만 만드는 정규화 텍스트를 든다.
    /// 소비자가 여럿이어도 태그 제거·디코드는 줄마다 한 번만 한다.
    /// </summary>
    public sealed class RawLogLine
    {
        private string? _plainText;
        private string? _normalizedText;

        public RawLogLine(RawLogFile file, string html)
        {
            File = file;
            Html = html;
        }

        /// <summary>이 줄이 들어 있던 파일.</summary>
        public RawLogFile File { get; }
        /// <summary>파일 날짜(= 줄의 날짜).</summary>
        public DateTime Date => File.Date;
        /// <summary>줄바꿈 병합이 끝난 원본 HTML 한 줄.</summary>
        public string Html { get; }
        /// <summary>태그만 뗀 텍스트(<see cref="RawChatLogReader.StripTags"/>).</summary>
        public string PlainText => _plainText ??= RawChatLogReader.StripTags(Html);
        /// <summary>태그→공백·디코드·공백 축약까지 한 텍스트(<see cref="RawChatLogReader.NormalizeText"/>). "[ 6시 39분 58초] 내용" 꼴.</summary>
        public string NormalizedText => _normalizedText ??= RawChatLogReader.NormalizeText(Html);
    }

    /// <summary>
    /// 원본 로그 한 번 읽기(패스)에 올라타는 소비자. 아카이브·사냥 기록·던전 타이머·시드·보물창고가 구현한다.
    /// 패스는 파일마다 <see cref="BeginFile"/>로 읽고 싶은지 묻고, 원하는 소비자가 하나라도 있으면 파일을 한 번 읽어 줄마다 <see cref="OnLine"/>을 부른다.
    /// 어디까지 처리했는지(체크포인트)는 소비자가 스스로 관리한다 — 패스가 두 번(전경 최근분 → 배경 나머지) 나뉘어 와도
    /// 소비자 상태가 이어지므로 결과는 한 번에 읽은 것과 같아야 한다.
    /// 모든 콜백은 스캔 스레드(백그라운드)에서 순서대로 불린다.
    /// </summary>
    public interface IRawLogScanConsumer
    {
        /// <summary>패스가 시작된다. <paramref name="files"/>는 이 패스가 볼 수 있는 파일(날짜순). 소비자는 이 중 원하는 것만 고른다.</summary>
        void BeginScan(IReadOnlyList<RawLogFile> files);

        /// <summary>이 파일을 읽고 싶으면 true. <paramref name="fileLength"/>는 열기 직전 파일 길이(어디까지 봤는지 기록용).</summary>
        bool BeginFile(RawLogFile file, long fileLength);

        /// <summary><see cref="BeginFile"/>에서 true를 돌려준 파일의 줄 하나.</summary>
        void OnLine(RawLogLine line);

        /// <summary>파일 읽기가 끝났다. <paramref name="outcome"/>이 Completed가 아니면 뒷부분을 못 읽었다.</summary>
        void EndFile(RawLogFile file, RawLogReadOutcome outcome);

        /// <summary>패스가 끝났다(취소로 끝나면 <paramref name="completed"/>=false).</summary>
        void EndScan(bool completed);
    }

    /// <summary>
    /// 오늘 파일을 "어디까지(바이트) 읽었는지" 기억하고, LogService가 실시간 읽기 시작 위치를 정한 뒤
    /// 그 사이 틈(패스가 읽은 끝 ~ 실시간 시작)을 이어서 받는 소비자. 던전 타이머·사냥 기록이 구현한다.
    /// 시드는 CoveredLength 자체가 이 역할이라 오늘을 CatchUpAsync에 맡긴다.
    /// </summary>
    public interface IRawLogTailConsumer : IRawLogScanConsumer
    {
        /// <summary>패스가 오늘 파일을 어디까지 읽었는지. 아직 안 읽었으면 null.</summary>
        (RawLogFile File, long Length)? ConsumedToday { get; }

        /// <summary>틈 구간 줄이 <see cref="IRawLogScanConsumer.OnLine"/>으로 이어서 들어온다.</summary>
        void BeginTail(RawLogFile file);

        /// <summary>틈 구간이 끝났다. <paramref name="newLength"/>까지 읽은 것으로 기억한다.</summary>
        void EndTail(RawLogFile file, long newLength);
    }

    public sealed record RawLogScanResult(IReadOnlyList<string> TimedOutFiles, int FilesRead)
    {
        public static RawLogScanResult Empty { get; } = new(Array.Empty<string>(), 0);
        public bool HasTimedOutFiles => TimedOutFiles.Count > 0;
    }

    /// <summary>
    /// 원본 채팅 로그 폴더를 한 번 훑으며 등록된 소비자 전부에 줄을 나눠 준다.
    /// 시작 시 서비스마다 같은 파일을 따로 열어 디코드하던 것을 파일당 한 번으로 줄인다.
    /// </summary>
    public static class RawLogScanService
    {
        /// <summary>
        /// <paramref name="dateFilter"/>에 맞는 파일을 날짜순으로 읽는다. 어떤 소비자도 원하지 않는 파일은 열지 않는다.
        /// <paramref name="onProgress"/>(날짜 문구, 현재, 전체)는 파일마다(건너뛴 파일 포함) 부른다.
        /// </summary>
        public static async Task<RawLogScanResult> RunAsync(
            string? chatLogFolderPath,
            IReadOnlyList<IRawLogScanConsumer> consumers,
            Func<DateTime, bool>? dateFilter = null,
            Action<string, int, int>? onProgress = null,
            CancellationToken cancellationToken = default,
            Func<RawLogFile, bool>? afterFile = null)
        {
            if (consumers == null || consumers.Count == 0)
                return RawLogScanResult.Empty;
            if (string.IsNullOrWhiteSpace(chatLogFolderPath) || !Directory.Exists(chatLogFolderPath))
                return RawLogScanResult.Empty;

            List<RawLogFile> files = RawChatLogReader.EnumerateFiles(chatLogFolderPath);
            if (dateFilter != null)
                files.RemoveAll(f => !dateFilter(f.Date));

            var timedOut = new List<string>();
            int filesRead = 0;
            bool completed = false;
            var wanting = new List<IRawLogScanConsumer>(consumers.Count);
            var failedConsumers = new List<IRawLogScanConsumer>();

            foreach (IRawLogScanConsumer consumer in consumers)
                consumer.BeginScan(files);

            try
            {
                for (int i = 0; i < files.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RawLogFile file = files[i];
                    onProgress?.Invoke($"{file.Date:yyyy-MM-dd} 처리 중...", i + 1, files.Count);

                    // 게임이 쓰는 중인 파일(오늘)은 마지막 줄 경계까지만 읽어 "어디까지 읽었나"를 정확히 남긴다 —
                    // 그 뒤 줄은 LogService가 실시간 위치를 정한 뒤 FeedTail이 이어 받는다
                    long length = 0;
                    try { length = new FileInfo(file.Path).Length; } catch { /* 없으면 0 */ }
                    length = RawChatLogReader.FindLineBoundary(file.Path, length);

                    wanting.Clear();
                    foreach (IRawLogScanConsumer consumer in consumers)
                    {
                        if (SafeBeginFile(consumer, file, length))
                            wanting.Add(consumer);
                    }

                    if (wanting.Count == 0)
                        continue;

                    failedConsumers.Clear();
                    RawLogReadOutcome outcome = await RawChatLogReader.StreamMergedLinesAsync(
                        file.Path,
                        html =>
                        {
                            var line = new RawLogLine(file, html);
                            // 소비자 하나의 줄 처리 실패가 다른 소비자·파일 전체를 잃게 하지 않는다 (처음 한 번만 남긴다)
                            for (int c = 0; c < wanting.Count; c++)
                            {
                                try { wanting[c].OnLine(line); }
                                catch (Exception ex)
                                {
                                    AppLogger.Warn($"Raw log scan consumer '{wanting[c].GetType().Name}' failed on a line of '{Path.GetFileName(file.Path)}'; skipping it for this file.", ex);
                                    failedConsumers.Add(wanting[c]);
                                    wanting.RemoveAt(c--);
                                }
                            }
                        },
                        cancellationToken,
                        readLimit: length).ConfigureAwait(false);

                    filesRead++;
                    if (outcome == RawLogReadOutcome.TimedOut)
                    {
                        timedOut.Add(file.Path);
                        onProgress?.Invoke($"{file.Date:yyyy-MM-dd} 처리 시간이 1분을 넘겨 다음 파일로 넘어갑니다...", i + 1, files.Count);
                    }

                    foreach (IRawLogScanConsumer consumer in wanting)
                        SafeEndFile(consumer, file, outcome);
                    foreach (IRawLogScanConsumer consumer in failedConsumers)
                        SafeEndFile(consumer, file, RawLogReadOutcome.Failed);

                    if (afterFile != null && !afterFile(file))
                        break;
                }

                completed = true;
            }
            finally
            {
                foreach (IRawLogScanConsumer consumer in consumers)
                {
                    try { consumer.EndScan(completed); }
                    catch (Exception ex) { AppLogger.Warn($"Raw log scan consumer '{consumer.GetType().Name}' failed to finish.", ex); }
                }
            }

            return new RawLogScanResult(timedOut, filesRead);
        }

        /// <summary>
        /// 시작 패스가 오늘 파일을 읽은 끝(소비자별 ConsumedToday)과 LogService의 실시간 읽기 시작 위치 사이 줄을
        /// 소비자에게 이어 넣는다. LogService.Start() 전에(실시간 줄이 들어오기 전에) 불러야 순서가 지켜진다.
        /// 실시간은 realtimeStart부터만 읽으므로 이 구간은 두 번 들어가지 않는다.
        /// </summary>
        public static void FeedTail(IEnumerable<IRawLogScanConsumer> consumers, string? todayPath, long realtimeStart)
        {
            if (string.IsNullOrWhiteSpace(todayPath) || realtimeStart <= 0)
                return;

            foreach (IRawLogScanConsumer candidate in consumers)
            {
                if (candidate is not IRawLogTailConsumer consumer || consumer.ConsumedToday is not (RawLogFile file, long consumed))
                    continue;
                if (!string.Equals(Path.GetFullPath(file.Path), Path.GetFullPath(todayPath), StringComparison.OrdinalIgnoreCase))
                    continue;
                if (realtimeStart <= consumed)
                    continue;

                try
                {
                    List<string> lines = RawChatLogReader.ReadMergedLinesInRange(file.Path, consumed, realtimeStart);
                    consumer.BeginTail(file);
                    foreach (string html in lines)
                        consumer.OnLine(new RawLogLine(file, html));
                    consumer.EndTail(file, realtimeStart);
                    if (lines.Count > 0)
                        AppLogger.Info($"Fed startup gap to '{consumer.GetType().DeclaringType?.Name ?? consumer.GetType().Name}': bytes {consumed}~{realtimeStart}, lines={lines.Count}.");
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to feed startup gap of '{Path.GetFileName(file.Path)}' to '{consumer.GetType().Name}'.", ex);
                }
            }
        }

        private static bool SafeBeginFile(IRawLogScanConsumer consumer, RawLogFile file, long length)
        {
            try { return consumer.BeginFile(file, length); }
            catch (Exception ex)
            {
                AppLogger.Warn($"Raw log scan consumer '{consumer.GetType().Name}' failed to begin '{Path.GetFileName(file.Path)}'.", ex);
                return false;
            }
        }

        private static void SafeEndFile(IRawLogScanConsumer consumer, RawLogFile file, RawLogReadOutcome outcome)
        {
            try { consumer.EndFile(file, outcome); }
            catch (Exception ex) { AppLogger.Warn($"Raw log scan consumer '{consumer.GetType().Name}' failed to finish '{Path.GetFileName(file.Path)}'.", ex); }
        }
    }
}
