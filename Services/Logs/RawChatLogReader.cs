using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace TWChatOverlay.Services
{
    /// <summary>원본 채팅 로그 파일 하나 (TWChatLog_yyyy_MM_dd.html)와 파일 이름에서 읽은 날짜.</summary>
    public sealed record RawLogFile(string Path, DateTime Date);

    /// <summary>
    /// 게임 원본 채팅 로그(TWChatLog_*.html, CP949)를 읽는 유일한 통로.
    /// 열기(공유 모드)·줄 분리(&lt;br&gt;/줄바꿈)·외치기/시스템 줄바꿈 병합(<see cref="LineMerger"/>)·HTML 태그 제거·
    /// "[h시 m분 s초]" 시각 파싱·파일명→날짜를 한곳에 두어 스캐너마다 규칙이 어긋나지 않게 한다.
    /// 파싱 깊이는 호출자가 고른다 — 줄 열거는 HTML 그대로 주고, 태그 제거·정규화는 필요한 쪽만 부른다.
    /// </summary>
    public static class RawChatLogReader
    {
        public const string FileSearchPattern = "TWChatLog_*.html";

        private static readonly Regex FileDateRegex = new(
            @"TWChatLog_(?<y>\d{4})_(?<m>\d{2})_(?<d>\d{2})\.html$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex HtmlTagRegex = new(@"<[^>]+>", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRunRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex LogTimeRegex = new(
            @"^\[\s*(?<h>\d{1,2})시\s*(?<m>\d{1,2})분\s*(?<s>\d{1,2})초\s*\]",
            RegexOptions.Compiled);
        // 물리적 줄 안에 <br>로 이어진 줄이 있을 수 있어(LogService와 같은 규칙) 둘 다로 나눈다
        private static readonly Regex LineSplitRegex = new(@"</?br\s*>|\r?\n", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Lazy<Encoding> Cp949Lazy = new(() =>
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(949);
        });

        /// <summary>채팅 로그 인코딩(CP949).</summary>
        public static Encoding Cp949 => Cp949Lazy.Value;

        // ── 파일 ──

        /// <summary>"TWChatLog_2026_09_16.html"에서 날짜를 읽는다. 형식이 다르거나 날짜가 아니면 false.</summary>
        public static bool TryParseFileDate(string path, out DateTime date)
        {
            date = default;
            if (string.IsNullOrEmpty(path))
                return false;

            Match m = FileDateRegex.Match(System.IO.Path.GetFileName(path));
            if (!m.Success)
                return false;

            return DateTime.TryParseExact(
                $"{m.Groups["y"].Value}-{m.Groups["m"].Value}-{m.Groups["d"].Value}",
                "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        /// <summary>그 날짜의 로그 파일 경로.</summary>
        public static string GetFilePath(string folder, DateTime day)
            => System.IO.Path.Combine(folder, $"TWChatLog_{day:yyyy_MM_dd}.html");

        /// <summary>
        /// 폴더의 로그 파일을 날짜순으로 나열한다. <paramref name="from"/>/<paramref name="to"/>는 날짜(포함) 범위, null이면 제한 없음.
        /// 폴더가 없으면 빈 목록.
        /// </summary>
        public static List<RawLogFile> EnumerateFiles(string? folder, DateTime? from = null, DateTime? to = null)
        {
            var result = new List<RawLogFile>();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return result;

            foreach (string path in Directory.EnumerateFiles(folder, FileSearchPattern))
            {
                if (!TryParseFileDate(path, out DateTime date))
                    continue;
                DateTime day = date.Date;
                if (from.HasValue && day < from.Value.Date)
                    continue;
                if (to.HasValue && day > to.Value.Date)
                    continue;
                result.Add(new RawLogFile(path, day));
            }

            result.Sort((a, b) =>
            {
                int byDate = a.Date.CompareTo(b.Date);
                return byDate != 0 ? byDate : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
            });
            return result;
        }

        /// <summary>게임이 쓰는 중인 파일도 읽을 수 있게 공유 모드로 연다.</summary>
        public static FileStream OpenShared(string path, bool async = false)
            => new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1 << 20,
                async ? FileOptions.SequentialScan | FileOptions.Asynchronous : FileOptions.SequentialScan);

        // ── 줄 열거 ──

        /// <summary>
        /// 파일의 줄을 병합(외치기·시스템 줄바꿈)까지 마친 HTML로 차례로 돌려준다. 빈 줄은 뺀다.
        /// 실시간 경로(LogService)와 같은 줄 분리·병합 규칙을 쓴다.
        /// </summary>
        public static IEnumerable<string> ReadMergedLines(string path)
        {
            var buffer = new List<string>();
            var merger = new LineMerger(buffer.Add);
            using FileStream stream = OpenShared(path);
            using var reader = new StreamReader(stream, Cp949, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 20);
            string? physical;
            while ((physical = reader.ReadLine()) != null)
            {
                foreach (string piece in SplitLine(physical))
                    merger.Push(piece);
                if (buffer.Count > 0)
                {
                    foreach (string line in buffer)
                        yield return line;
                    buffer.Clear();
                }
            }
            merger.Flush();
            foreach (string line in buffer)
                yield return line;
        }

        /// <summary>물리적 줄 하나를 &lt;br&gt;/줄바꿈 기준으로 나눈다(빈 조각 제외). 대부분의 줄은 조각이 하나다.</summary>
        public static IEnumerable<string> SplitLine(string physicalLine)
        {
            if (string.IsNullOrWhiteSpace(physicalLine))
                yield break;

            if (physicalLine.IndexOf("br", StringComparison.OrdinalIgnoreCase) < 0 &&
                physicalLine.IndexOf('\n') < 0 && physicalLine.IndexOf('\r') < 0)
            {
                yield return physicalLine;
                yield break;
            }

            foreach (string piece in LineSplitRegex.Split(physicalLine))
            {
                if (!string.IsNullOrWhiteSpace(piece))
                    yield return piece;
            }
        }

        /// <summary>
        /// <paramref name="limit"/> 바이트 이하에서 마지막 줄바꿈('\n') 바로 뒤 위치. 게임이 쓰는 중인 파일을 줄 경계에서 끊어 읽기 위한 것이다.
        /// CP949의 둘째 바이트는 0x41~0xFE라 0x0A가 글자 안에 나올 수 없어 바이트만 보고 찾아도 안전하다. 못 찾으면 limit 그대로.
        /// </summary>
        public static long FindLineBoundary(string path, long limit)
        {
            if (limit <= 0)
                return 0;

            try
            {
                using FileStream stream = OpenShared(path);
                const int window = 64 * 1024;
                long start = Math.Max(0, limit - window);
                int count = (int)(limit - start);
                byte[] buffer = new byte[count];
                stream.Seek(start, SeekOrigin.Begin);
                int read = 0;
                while (read < count)
                {
                    int n = stream.Read(buffer, read, count - read);
                    if (n <= 0)
                        break;
                    read += n;
                }

                if (read <= 0)
                    return limit;

                int lastNewline = Array.LastIndexOf(buffer, (byte)'\n', read - 1);
                return lastNewline >= 0 ? start + lastNewline + 1 : limit;
            }
            catch
            {
                return limit;
            }
        }

        /// <summary>
        /// 파일의 [start, end) 바이트 구간을 줄로 읽어 병합한다. start는 줄 경계(<see cref="FindLineBoundary"/>)여야 한다.
        /// 시작 패스가 읽은 지점과 실시간 읽기 시작 위치 사이의 틈을 메우는 데 쓴다.
        /// </summary>
        public static List<string> ReadMergedLinesInRange(string path, long start, long end)
        {
            var result = new List<string>();
            if (end <= start || start < 0)
                return result;

            using FileStream stream = OpenShared(path);
            long available = Math.Min(end, stream.Length) - start;
            if (available <= 0)
                return result;

            byte[] buffer = new byte[available];
            stream.Seek(start, SeekOrigin.Begin);
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                    break;
                read += n;
            }

            string text = Cp949.GetString(buffer, 0, read);
            var merger = new LineMerger(result.Add);
            foreach (string piece in SplitLine(text))
                merger.Push(piece);
            merger.Flush();
            return result;
        }

        /// <summary>
        /// 파일을 스트리밍하며 병합된 줄마다 <paramref name="onLine"/>을 부른다. 실패·시간 초과 처리는 결과로 알린다.
        /// <paramref name="readLimit"/>(바이트, 0 이상)을 주면 그 지점까지만 읽는다 — 읽는 동안 파일이 더 자라도 "어디까지 읽었나"가 정확해진다.
        /// </summary>
        public static async Task<RawLogReadOutcome> StreamMergedLinesAsync(
            string path,
            Action<string> onLine,
            CancellationToken cancellationToken,
            TimeSpan? readTimeout = null,
            long readLimit = -1)
        {
            const int maxAttempts = 5;
            TimeSpan timeout = readTimeout ?? TimeSpan.FromMinutes(1);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    DateTime deadlineUtc = DateTime.UtcNow.Add(timeout);
                    using FileStream fileStream = OpenShared(path, async: true);
                    using Stream stream = readLimit >= 0 ? new LengthLimitedStream(fileStream, readLimit) : fileStream;
                    using var reader = new StreamReader(stream, Cp949, detectEncodingFromByteOrderMarks: true, bufferSize: 1 << 20);

                    bool timedOut = false;
                    var merger = new LineMerger(line =>
                    {
                        onLine(line);
                        if (DateTime.UtcNow >= deadlineUtc)
                            timedOut = true;
                    });

                    while (true)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        string? raw = await ReadLineWithTimeoutAsync(reader, cancellationToken, deadlineUtc).ConfigureAwait(false);
                        if (raw == null)
                            break;

                        foreach (string piece in SplitLine(raw))
                        {
                            merger.Push(piece);
                            if (timedOut)
                                throw new TimeoutException();
                        }
                    }

                    merger.Flush();
                    if (timedOut)
                        throw new TimeoutException();
                    return RawLogReadOutcome.Completed;
                }
                catch (TimeoutException)
                {
                    AppLogger.Warn($"Failed to stream source chat log '{path}' because reading it took more than {timeout.TotalMinutes:0} minute(s). Skipping the rest of the file.");
                    return RawLogReadOutcome.TimedOut;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (IOException ex)
                {
                    if (attempt == maxAttempts)
                    {
                        AppLogger.Warn($"Failed to stream source chat log '{path}' after {maxAttempts} attempts.", ex);
                        return RawLogReadOutcome.Failed;
                    }

                    await Task.Delay(120 * attempt, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to stream source chat log '{path}'.", ex);
                    return RawLogReadOutcome.Failed;
                }
            }

            return RawLogReadOutcome.Failed;
        }

        private static async Task<string?> ReadLineWithTimeoutAsync(StreamReader reader, CancellationToken cancellationToken, DateTime deadlineUtc)
        {
            TimeSpan remaining = deadlineUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException();

            Task<string?> readTask = reader.ReadLineAsync();
            _ = readTask.ContinueWith(
                t => _ = t.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return await readTask.WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
        }

        // ── 텍스트 정규화 ──

        /// <summary>HTML 태그만 떼고 양끝 공백을 지운다 (엔티티·안쪽 공백은 그대로). 문구 Contains 판정용.</summary>
        public static string StripTags(string html)
            => string.IsNullOrEmpty(html) ? string.Empty : HtmlTagRegex.Replace(html, string.Empty).Trim();

        /// <summary>태그를 공백으로 바꾸고 HTML 디코드·&amp;nbsp 치환·공백 축약·Trim까지 한 순수 텍스트. "[ 6시 39분 58초] 내용" 꼴.</summary>
        public static string NormalizeText(string html)
        {
            if (string.IsNullOrWhiteSpace(html))
                return string.Empty;

            string text = HtmlTagRegex.Replace(html, " ");
            text = WebUtility.HtmlDecode(text).Replace("&nbsp", " ");
            return WhitespaceRunRegex.Replace(text, " ").Trim();
        }

        /// <summary>줄 맨 앞의 "[h시 m분 s초]"를 읽는다. 범위를 벗어나는 값(24시 등)은 실패.</summary>
        public static bool TryParseLogTime(string? text, out TimeSpan time, out int matchLength)
        {
            time = default;
            matchLength = 0;
            if (string.IsNullOrEmpty(text))
                return false;

            Match m = LogTimeRegex.Match(text);
            if (!m.Success)
                return false;

            int h = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
            int mi = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
            int s = int.Parse(m.Groups["s"].Value, CultureInfo.InvariantCulture);
            if (h > 23 || mi > 59 || s > 59)
                return false;

            time = new TimeSpan(h, mi, s);
            matchLength = m.Length;
            return true;
        }

        /// <summary>줄 맨 앞의 시각을 <paramref name="date"/>의 그 시각으로 읽는다. 시각이 없으면 null.</summary>
        public static DateTime? TryParseLogTime(string? text, DateTime date)
            => TryParseLogTime(text, out TimeSpan time, out _) ? date.Date.Add(time) : null;
    }

    public enum RawLogReadOutcome
    {
        Completed,
        TimedOut,
        Failed
    }

    /// <summary>안쪽 스트림을 앞에서부터 정해진 바이트 수까지만 읽어 주는 읽기 전용 스트림 (StreamReader 입력용).</summary>
    internal sealed class LengthLimitedStream : Stream
    {
        private readonly Stream _inner;
        private long _remaining;

        public LengthLimitedStream(Stream inner, long limit)
        {
            _inner = inner;
            _remaining = Math.Max(0, limit);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0)
                return 0;
            int n = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= n;
            return n;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0)
                return 0;
            int n = await _inner.ReadAsync(buffer.Slice(0, (int)Math.Min(buffer.Length, _remaining)), cancellationToken).ConfigureAwait(false);
            _remaining -= n;
            return n;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        protected override void Dispose(bool disposing)
        {
            // 안쪽 스트림은 호출자가 닫는다
            base.Dispose(disposing);
        }
    }
}
