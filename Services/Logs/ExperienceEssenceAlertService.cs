using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 경험의 정수 획득 후 누적 경험치를 추적해 임계치 초과 시 알림을 발생시킵니다.
    /// </summary>
    public sealed class ExperienceEssenceAlertService
    {
        public const long ThresholdExp = 10_000_000_000L;
        private static readonly Regex LeadingTimestampRegex = new(
            @"^\[[^\]]+\]\s*",
            RegexOptions.Compiled);
        private static readonly Regex ResetMessageRegex = new(
            @"^\uACBD\uD5D8\uCE58\uAC00\s*10,?000,?000,?000\s*\uAC10\uC18C\uD588\uC2B5\uB2C8\uB2E4\.?$",
            RegexOptions.Compiled);
        private static readonly Regex LineSplitRegex = new(
            @"</?br\s*>|\r?\n",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private readonly ChatSettings _settings;
        private readonly DispatcherTimer _showDelayTimer;
        private bool _isTracking;
        private long _trackedExp;

        public ExperienceEssenceAlertService(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _trackedExp = Math.Max(0, _settings.ExperienceLimitTotalExp);
            _isTracking = true;
            _showDelayTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _showDelayTimer.Tick += (_, _) =>
            {
                _showDelayTimer.Stop();
                if (ShouldShowAlertNow())
                {
                    ShowAlertWindow();
                }
            };
        }

        public void Process(LogAnalysisResult analysis)
        {
            if (!analysis.IsSuccess)
                return;

            string text = analysis.Parsed.FormattedText ?? string.Empty;
            if (IsRuneExperienceLog(text))
            {
                AppLogger.Debug($"Experience essence tracker ignored rune EXP log. Message='{text}'");
                return;
            }

            if (IsExperienceResetLog(text))
            {
                // Keep this as a normal experience delta source; the actual delta is handled by the parser.
                AppLogger.Debug($"Experience essence tracker observed reset-formatted log. Message='{text}'");
            }

            if (analysis.Parsed.GainedExp == 0)
                return;

            if (!_isTracking)
            {
                AppLogger.Debug($"Experience essence tracker ignored EXP gain before reset point. Gain={analysis.Parsed.GainedExp:N0}, Message='{text}'");
                return;
            }

            long gainedExp = analysis.Parsed.GainedExp;
            _trackedExp += gainedExp;
            if (_trackedExp < 0)
                _trackedExp = 0;

            AppLogger.Info($"Experience essence tracker accumulated EXP. Gain={gainedExp:N0}, Total={_trackedExp:N0}, Threshold={ThresholdExp:N0}");
            SaveStateToSettings();

            if (_trackedExp >= ThresholdExp)
            {
                if (_settings.EnableExperienceLimitAlert)
                {
                    _showDelayTimer.Stop();
                    _showDelayTimer.Start();
                }
            }
            else
            {
                _showDelayTimer.Stop();
                ExperienceAlertWindowService.Close();
            }

            ExperienceAlertWindowService.RefreshState(_settings);
        }

        public void Reset()
        {
            _isTracking = false;
            _trackedExp = 0;
            _showDelayTimer.Stop();
            SaveStateToSettings();
        }

        public ExperienceAlertStateSnapshot GetStateSnapshot()
        {
            return new ExperienceAlertStateSnapshot
            {
                TotalExp = _trackedExp,
                IsVisible = _trackedExp >= ThresholdExp
            };
        }

        public void ApplyStateSnapshot(ExperienceAlertStateSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            _trackedExp = Math.Max(0, snapshot.TotalExp);

            _isTracking = true;
            SaveStateToSettings();
            ExperienceAlertWindowService.RefreshState(_settings);
            AppLogger.Info($"Experience essence tracker state overridden manually. Total={_trackedExp:N0}");
        }

        public void RestoreFromRecentLogs(IEnumerable<string> logPaths, LogAnalysisService logAnalysisService)
        {
            if (logPaths == null)
                throw new ArgumentNullException(nameof(logPaths));
            if (logAnalysisService == null)
                throw new ArgumentNullException(nameof(logAnalysisService));

            var existingPaths = logPaths.Where(File.Exists).ToList();
            var cachedLines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            string? resetPath = null;
            int resetLineIndex = -1;
            int reverseScannedLines = 0;

            for (int pathIndex = existingPaths.Count - 1; pathIndex >= 0 && resetPath == null; pathIndex--)
            {
                string path = existingPaths[pathIndex];
                var lines = ReadLogLines(path).ToList();
                cachedLines[path] = lines;

                for (int lineIndex = lines.Count - 1; lineIndex >= 0; lineIndex--)
                {
                    reverseScannedLines++;
                    var analysis = logAnalysisService.Analyze(lines[lineIndex], isRealTime: false);
                    if (!analysis.IsSuccess)
                        continue;

                    string text = analysis.Parsed.FormattedText ?? string.Empty;
                    if (IsExperienceResetLog(text))
                    {
                        resetPath = path;
                        resetLineIndex = lineIndex;
                        break;
                    }
                }
            }

            if (resetPath == null)
            {
                AppLogger.Info($"Experience essence tracker restore skipped. Files={existingPaths.Count}, ReverseScannedLines={reverseScannedLines}, ResetLogFound=False");
                return;
            }

            _isTracking = true;

            int replayedLines = 0;
            bool shouldReplay = false;

            foreach (string path in existingPaths)
            {
                if (!shouldReplay && !string.Equals(path, resetPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var lines = cachedLines.TryGetValue(path, out var cached)
                    ? cached
                    : ReadLogLines(path).ToList();

                int startIndex = 0;
                if (!shouldReplay)
                {
                    shouldReplay = true;
                    startIndex = resetLineIndex + 1;
                }

                for (int lineIndex = startIndex; lineIndex < lines.Count; lineIndex++)
                {
                    string replayLine = lines[lineIndex];
                    replayedLines++;
                    var analysis = logAnalysisService.Analyze(replayLine, isRealTime: false);
                    if (analysis.IsSuccess && analysis.Parsed.GainedExp > 0)
                    {
                        _trackedExp += analysis.Parsed.GainedExp;
                    }
                    else if (analysis.IsSuccess && analysis.Parsed.GainedExp < 0)
                    {
                        _trackedExp += analysis.Parsed.GainedExp;
                        if (_trackedExp < 0)
                            _trackedExp = 0;
                    }
                }
            }

            SaveStateToSettings();

            AppLogger.Info($"Experience essence tracker restored from latest reset log. Files={existingPaths.Count}, ReverseScannedLines={reverseScannedLines}, ReplayedLines={replayedLines}, Total={_trackedExp:N0}, ResetPath='{resetPath}', ResetLineIndex={resetLineIndex}");
        }

        private void SaveStateToSettings()
        {
            _settings.ExperienceLimitTotalExp = _trackedExp;
            _settings.ExperienceLimitStateInitialized = true;
        }

        private bool ShouldShowAlertNow()
            => _trackedExp >= ThresholdExp;

        private void ShowAlertWindow()
        {
            ExperienceAlertWindowService.Show(
                $"\uACBD\uD5D8\uCE58 {FormatExpEok(_trackedExp)} \uB204\uC801 \uB2EC\uC131",
                _settings);
        }

        private static bool IsExperienceResetLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string body = LeadingTimestampRegex.Replace(text.Trim(), string.Empty);
            return ResetMessageRegex.IsMatch(body);
        }

        private static bool IsRuneExperienceLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string body = LeadingTimestampRegex.Replace(text.Trim(), string.Empty);
            string compact = body.Replace(" ", string.Empty);
            return (body.Contains("룬 경험치", StringComparison.Ordinal) || compact.Contains("룬경험치", StringComparison.Ordinal)) &&
                   (body.Contains("올랐습니다", StringComparison.Ordinal) || body.Contains("상승했습니다", StringComparison.Ordinal));
        }

        private static IEnumerable<string> ReadLogLines(string path)
        {
            string content;
            try
            {
                content = File.ReadAllText(path, System.Text.Encoding.GetEncoding(949));
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to read experience essence restore log. Path='{path}'", ex);
                yield break;
            }

            foreach (string line in LineSplitRegex.Split(content))
            {
                if (!string.IsNullOrWhiteSpace(line))
                    yield return line.Trim();
            }
        }

        private static string FormatExpEok(long exp)
        {
            decimal eok = exp / 100_000_000m;
            return $"{eok.ToString("N0", CultureInfo.InvariantCulture)}\uC5B5";
        }
    }
}
