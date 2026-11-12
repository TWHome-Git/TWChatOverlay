using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Threading;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 화면(UI 스레드)이 오래 응답하지 않으면 그 순간의 프로세스 상태를 덤프 파일로 남긴다.
    /// 멈춘 원인은 멈춘 순간의 호출 스택에만 남는데, 앱을 꺼 버리면 사라진다 — 덤프가 있으면 나중에 어느 코드에 걸려 있었는지 볼 수 있다.
    ///
    ///  - 감시 스레드가 1초마다 UI 스레드에 "살아 있나" 신호를 보내고, 연속 <see cref="HangChecks"/>번 답이 없으면 멈춘 것으로 본다.
    ///  - 덤프는 .NET 런타임에 들어 있는 createdump로 쓴다 (Config\HangDumps\ui-hang-날짜-시각.dmp). 평소에는 아무 일도 하지 않는다.
    ///  - 한 번 멈출 때 한 번만, 한 실행에 <see cref="MaxDumpsPerRun"/>번까지만 쓰고, 파일은 최근 <see cref="KeepFiles"/>개만 남긴다.
    /// </summary>
    public sealed class UiHangWatchdog : IDisposable
    {
        /// <summary>신호를 보내는 간격.</summary>
        internal TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(1);
        /// <summary>연속으로 이만큼 답이 없으면 멈춘 것으로 본다 (기본 5번 = 약 5초).</summary>
        internal int HangChecks { get; set; } = 5;
        internal int MaxDumpsPerRun { get; set; } = 2;
        internal int KeepFiles { get; set; } = 3;
        internal string DumpDirectory { get; set; } = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "HangDumps");

        /// <summary>마지막으로 쓴 덤프 파일 (없으면 null).</summary>
        internal string? LastDumpPath { get; private set; }

        private Dispatcher? _dispatcher;
        private Thread? _thread;
        private volatile bool _stop;
        private long _sent;      // 보낸 신호 번호
        private long _answered;  // UI 스레드가 답한 마지막 신호 번호
        private int _dumpsWritten;

        public void Start(Dispatcher dispatcher)
        {
            if (_thread != null)
                return;

            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _thread = new Thread(Run) { IsBackground = true, Name = "UI hang watchdog", Priority = ThreadPriority.AboveNormal };
            _thread.Start();
        }

        public void Dispose() => _stop = true;

        private void Run()
        {
            int missed = 0;
            bool reported = false;
            while (!_stop)
            {
                Thread.Sleep(CheckInterval);
                if (_stop || _dispatcher == null || _dispatcher.HasShutdownStarted)
                    return;

                if (Interlocked.Read(ref _answered) == _sent)
                {
                    if (reported)
                        AppLogger.Warn($"UI thread is responding again after about {missed * CheckInterval.TotalSeconds:0}s.", "UiHangWatchdog");
                    missed = 0;
                    reported = false;
                    SendPing();
                    continue;
                }

                // 멈춘 시간을 시계가 아니라 "답 없는 확인 횟수"로 센다 — 디버거가 모든 스레드를 세웠거나
                // PC가 절전에서 깨어난 경우에는 감시 스레드도 같이 멈춰 있었으므로 멈춤으로 치지 않는다.
                missed++;
                if (missed == HangChecks && !reported)
                {
                    reported = true;
                    RecordHang(missed);
                }
            }
        }

        private void SendPing()
        {
            long number = ++_sent;
            try
            {
                // Loaded 우선순위: 창을 끄는 동안에도(입력이 밀려 있어도) 처리되고, 화면 배치·그리기(Render)보다는 낮다.
                // 그래서 한 작업이 오래 걸리는 멈춤과, 배치가 끝없이 반복되는 멈춤을 둘 다 잡는다.
                _dispatcher!.BeginInvoke(DispatcherPriority.Loaded, new Action(() => Interlocked.Exchange(ref _answered, number)));
            }
            catch (Exception)
            {
                // 디스패처가 내려가는 중이면 다음 확인에서 끝난다
            }
        }

        private void RecordHang(int missedChecks)
        {
            double seconds = missedChecks * CheckInterval.TotalSeconds;
            if (_dumpsWritten >= MaxDumpsPerRun)
            {
                AppLogger.Error($"UI thread has not responded for about {seconds:0}s. Dump limit for this run reached; no dump written.", "UiHangWatchdog");
                AppLogger.Flush();
                return;
            }

            // 덤프 도구는 경로 구분자가 섞여 있으면 실패한다 — 전체 경로로 정리해서 넘긴다
            string path = Path.GetFullPath(Path.Combine(DumpDirectory, $"ui-hang-{DateTime.Now:yyyyMMdd-HHmmss}.dmp"));
            AppLogger.Error($"UI thread has not responded for about {seconds:0}s. Writing dump: {path}", "UiHangWatchdog");
            AppLogger.Flush();

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string createDump = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location) ?? string.Empty, "createdump.exe");
                if (!File.Exists(createDump))
                {
                    AppLogger.Error($"Dump tool not found: {createDump}", "UiHangWatchdog");
                    AppLogger.Flush();
                    return;
                }

                // -n: 스레드의 호출 스택을 볼 수 있는 가장 작은 덤프.
                // .NET 8의 createdump는 자기를 실행한 프로세스(= 이 앱)를 덤프한다. 프로세스 번호를 받던 예전 판이면 번호를 붙여 다시 시도한다.
                (bool written, string said) = RunCreateDump(createDump, $"-n -f \"{path}\"", path);
                if (!written)
                    (written, said) = RunCreateDump(createDump, $"-n -f \"{path}\" {Environment.ProcessId}", path);
                _dumpsWritten++;

                if (written)
                {
                    LastDumpPath = path;
                    AppLogger.Error($"UI hang dump written: {path} ({new FileInfo(path).Length / (1024 * 1024)} MB).", "UiHangWatchdog");
                }
                else
                {
                    AppLogger.Error($"UI hang dump was not written. {said}", "UiHangWatchdog");
                }

                PruneOldDumps();
            }
            catch (Exception ex)
            {
                AppLogger.Error("Writing the UI hang dump failed.", ex, "UiHangWatchdog");
            }
            AppLogger.Flush();
        }

        /// <summary>덤프 도구를 한 번 돌린다. 파일이 생겼으면 true, 아니면 도구가 남긴 말을 함께 돌려준다.</summary>
        private static (bool Written, string Said) RunCreateDump(string createDump, string arguments, string path)
        {
            var startInfo = new ProcessStartInfo(createDump, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using Process? process = Process.Start(startInfo);
            if (process == null)
                return (false, "could not start createdump");

            // 도구가 남기는 말은 짧다 — 실패했을 때 이유를 로그에 옮기려고 받아 둔다
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                try { process.Kill(); } catch (Exception) { }
                return (false, "createdump did not finish within 60s");
            }

            string said = $"exit code {process.ExitCode}. {(output.Result + " " + error.Result).Trim()}";
            if (said.Length > 400)
                said = said.Substring(0, 400);
            return (process.ExitCode == 0 && File.Exists(path) && new FileInfo(path).Length > 0, said);
        }

        private void PruneOldDumps()
        {
            try
            {
                foreach (FileInfo old in new DirectoryInfo(DumpDirectory).GetFiles("ui-hang-*.dmp")
                             .OrderByDescending(file => file.LastWriteTimeUtc)
                             .Skip(Math.Max(1, KeepFiles)))
                {
                    old.Delete();
                }
            }
            catch (Exception)
            {
                // 지우지 못해도 다음에 다시 시도한다
            }
        }
    }
}
