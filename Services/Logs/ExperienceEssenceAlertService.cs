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

        /// <summary>누적치를 설정에 옮겨 쓰는 주기. 경험치 줄마다 쓰면 설정 저장(250ms 디바운스)과 PropertyChanged 전파가 사냥 중 초당 수 회 돌았다.</summary>
        private static readonly TimeSpan PersistInterval = TimeSpan.FromSeconds(10);

        private readonly ChatSettings _settings;
        private readonly DispatcherTimer _showDelayTimer;
        private readonly DispatcherTimer _persistTimer;
        private bool _isTracking;
        private long _trackedExp;
        // 메모리의 누적치가 설정에 아직 반영되지 않았는지
        private bool _stateDirty;

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
            _persistTimer = new DispatcherTimer { Interval = PersistInterval };
            _persistTimer.Tick += (_, _) =>
            {
                _persistTimer.Stop();
                if (_stateDirty)
                    SaveStateToSettings();
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
            // 누적치는 메모리에만 두고 임계치 도달·10초 주기·초기화·종료 때만 설정에 쓴다
            _stateDirty = true;
            if (!_persistTimer.IsEnabled)
                _persistTimer.Start();

            if (_trackedExp >= ThresholdExp)
            {
                SaveStateToSettings();
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

        /// <summary>아직 설정에 안 쓴 누적치를 지금 쓴다. 앱 종료(설정 저장 직전)에 부른다.</summary>
        public void FlushState()
        {
            _persistTimer.Stop();
            if (_stateDirty)
                SaveStateToSettings();
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

        /// <summary>사용자가 누적치를 직접 고쳤다 — 필드와 설정을 함께 갱신해 주기 저장이 수동 입력값을 덮지 않게 한다.</summary>
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
            _stateDirty = false;
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
