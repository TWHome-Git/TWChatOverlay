using System;
using System.Globalization;
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
                Interval = TimeSpan.FromSeconds(1)
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

            // 경험치 증감이 없는 줄(대부분)은 아래 어떤 처리도 하지 않으므로 룬 경험치 정규식보다 먼저 거른다
            if (analysis.Parsed.GainedExp == 0)
                return;

            string text = analysis.Parsed.FormattedText ?? string.Empty;
            if (IsRuneExperienceLog(text))
            {
                AppLogger.Debug($"Experience essence tracker ignored rune EXP log. Message='{text}'");
                return;
            }

            if (!_isTracking)
            {
                AppLogger.Debug($"Experience essence tracker ignored EXP gain before reset point. Gain={analysis.Parsed.GainedExp:N0}, Message='{text}'");
                return;
            }

            long gainedExp = analysis.Parsed.GainedExp;
            _trackedExp += gainedExp;
            if (_trackedExp < 0)
                _trackedExp = 0;

            AppLogger.Debug($"Experience essence tracker accumulated EXP. Gain={gainedExp:N0}, Total={_trackedExp:N0}, Threshold={ThresholdExp:N0}");
            SaveStateToSettings();

            if (_trackedExp >= ThresholdExp)
            {
                // 1초 뒤에 띄운다 (곧 이어질 차감 줄을 기다리는 여유).
                // 이미 기다리는 중이면 다시 걸지 않는다 — 사냥 중에는 경험치가 0.1초 간격으로 들어와
                // 매번 타이머를 다시 시작하면 사냥을 멈출 때까지 영영 뜨지 않았다.
                if (_settings.EnableExperienceLimitAlert && !_showDelayTimer.IsEnabled)
                {
                    _showDelayTimer.Start();
                }
            }
            else
            {
                _showDelayTimer.Stop();
                AppServices.Get<ExperienceAlertWindowService>().Close();
            }

            AppServices.Get<ExperienceAlertWindowService>().RefreshState(_settings);
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
                TotalExp = _trackedExp
            };
        }

        public void ApplyStateSnapshot(ExperienceAlertStateSnapshot snapshot)
        {
            if (snapshot == null)
                return;

            _trackedExp = Math.Max(0, snapshot.TotalExp);

            _isTracking = true;
            SaveStateToSettings();
            AppServices.Get<ExperienceAlertWindowService>().RefreshState(_settings);
            AppLogger.Info($"Experience essence tracker state overridden manually. Total={_trackedExp:N0}");
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
            AppServices.Get<ExperienceAlertWindowService>().Show(
                $"\uACBD\uD5D8\uCE58 {FormatExpEok(_trackedExp)} \uB204\uC801 \uB2EC\uC131",
                _settings);
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

        private static string FormatExpEok(long exp)
        {
            decimal eok = exp / 100_000_000m;
            return $"{eok.ToString("N0", CultureInfo.InvariantCulture)}\uC5B5";
        }
    }
}
