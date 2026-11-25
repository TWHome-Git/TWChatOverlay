using System;
using System.ComponentModel;
using System.Threading.Tasks;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    public sealed class MainLogPipelineCoordinator
    {
        private static readonly TimeSpan DefaultFilterRetryDelay = TimeSpan.FromSeconds(30);

        private readonly ChatSettings _settings;
        private readonly LogAnalysisService _logAnalysisService;
        private readonly object _customFilterLock = new();
        private Task<DropItemResolver.DropItemFilterSnapshot>? _defaultDropItemFilterSnapshotTask;
        private DateTime _nextDefaultFilterRetryAt = DateTime.MinValue;
        private bool _defaultFilterLoadFailureLogged;
        private DropItemResolver.DropItemFilterSnapshot? _customFilterSnapshot;

        public MainLogPipelineCoordinator(ChatSettings settings, LogAnalysisService logAnalysisService)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logAnalysisService = logAnalysisService ?? throw new ArgumentNullException(nameof(logAnalysisService));
            _settings.PropertyChanged += Settings_PropertyChanged;
            RefreshCustomFilterSnapshot();
            StartDefaultFilterSnapshotLoad();
        }

        public MainLogPipelineAnalysis Analyze(string html, bool isRealTime)
        {
            var primaryAnalysis = _logAnalysisService.Analyze(html, isRealTime);
            if (!primaryAnalysis.IsSuccess)
                return new MainLogPipelineAnalysis(primaryAnalysis, null, null);

            LogAnalysisResult? defaultItemDropAnalysis = null;
            LogAnalysisResult? toastAnalysis = null;

            // 아이템 필터 재분석은 시스템 획득 로그일 때만 — 일반 채팅까지 줄당 3회 분석하던 낭비 제거
            if (isRealTime && MayContainItemDrop(primaryAnalysis))
            {
                // HTML 전체를 다시 분석하지 않고 아이템 판정만 필터 스냅샷으로 다시 한다
                defaultItemDropAnalysis = TryAnalyzeWithDefaultFilter(primaryAnalysis);
                toastAnalysis = defaultItemDropAnalysis;

                var customFilterSnapshot = GetCustomFilterSnapshot();
                if (_settings.UseCustomDropItemFilter && customFilterSnapshot != null)
                {
                    toastAnalysis = _logAnalysisService.ReanalyzeItemDrop(primaryAnalysis, customFilterSnapshot);
                }
            }

            ItemDropGrade? topMagicStoneAlertGrade = isRealTime
                ? ResolveTopMagicStoneAlertGrade(primaryAnalysis, toastAnalysis)
                : null;

            return new MainLogPipelineAnalysis(primaryAnalysis, defaultItemDropAnalysis, toastAnalysis, topMagicStoneAlertGrade);
        }

        private const string TopMagicStoneItemName = "최상급 마정석";

        /// <summary>
        /// 최상급 마정석을 얻은 실시간 줄이면 아이템 획득 알림과 같은 토스트를 띄울 등급을 준다 (2026-11-25 사용자: "최상급 마정석을 먹을 때, 알림이 있으면 좋겠음").
        /// 드롭 목록(DropItem.Json)에 "최상급 마정석"(희귀)이 있지만 게임 문구가 "최상급 마정석 1개를 획득 하였습니다."(대괄호 없음)라
        /// 아이템 판정(DropItemResolver, "[아이템]"으로 시작하는 줄)에 걸리지 않아 알림이 난 적이 없다 — 원본 로그 2025-12-19 ~ 2026-11-20에 42줄(30일).
        /// 아이템 판정을 넓히지 않고 여기서 따로 본다: 넓히면 아이템 아카이브 · 어밴던 요약 경로까지 바뀐다.
        /// 켜기는 아이템 획득 알림 설정, 대상 여부 · 등급은 그 알림이 쓰는 필터(사용자 정의를 켜면 그 목록, 아니면 기본 목록)를 그대로 따른다.
        /// 바닥에서 주운 "[최상급 마정석] 1개를 입수했습니다."도 같이 본다(판정은 어밴던 요약과 같은 AbandonSummaryCalculator).
        /// </summary>
        private ItemDropGrade? ResolveTopMagicStoneAlertGrade(LogAnalysisResult primaryAnalysis, LogAnalysisResult? toastAnalysis)
        {
            if (!_settings.ShowItemDropAlert || !primaryAnalysis.IsSystemLog)
                return null;

            // 이미 아이템 획득 알림으로 잡힌 줄이면 그쪽이 띄운다 (같은 줄에 토스트 두 개 방지)
            if (toastAnalysis is { HasTrackedItemDrop: true })
                return null;

            if (!AbandonSummaryCalculator.IsTopMagicStoneGain(primaryAnalysis.Parsed.FormattedText))
                return null;

            DropItemResolver.DropItemFilterSnapshot? filter = GetActiveToastFilterSnapshot();
            return filter != null && filter.Items.TryGetValue(TopMagicStoneItemName, out ItemDropGrade grade)
                ? grade
                : null;
        }

        /// <summary>아이템 획득 토스트가 쓰는 필터와 같은 것 — 사용자 정의를 켰고 목록이 있으면 그것, 아니면 받아 둔 기본 목록(아직 없으면 null).</summary>
        private DropItemResolver.DropItemFilterSnapshot? GetActiveToastFilterSnapshot()
        {
            var customFilterSnapshot = GetCustomFilterSnapshot();
            if (_settings.UseCustomDropItemFilter && customFilterSnapshot != null)
                return customFilterSnapshot;

            var snapshotTask = _defaultDropItemFilterSnapshotTask;
            return snapshotTask is { IsCompletedSuccessfully: true } ? snapshotTask.Result : null;
        }

        /// <summary>아이템 드롭 판정이 나올 수 있는 줄인지 — 시스템 로그이면서 "획득" 문구가 있는 경우만.</summary>
        private static bool MayContainItemDrop(LogAnalysisResult primaryAnalysis)
        {
            if (!primaryAnalysis.IsSystemLog)
                return false;

            string text = primaryAnalysis.Parsed.FormattedText ?? string.Empty;
            return text.Contains("획득", StringComparison.Ordinal);
        }

        private LogAnalysisResult? TryAnalyzeWithDefaultFilter(LogAnalysisResult primaryAnalysis)
        {
            var snapshotTask = _defaultDropItemFilterSnapshotTask;
            if (snapshotTask == null)
                return null;

            if (snapshotTask.IsFaulted || snapshotTask.IsCanceled)
            {
                ScheduleDefaultFilterRetryIfDue();
                return null;
            }

            if (!snapshotTask.IsCompletedSuccessfully)
                return null;

            return _logAnalysisService.ReanalyzeItemDrop(primaryAnalysis, snapshotTask.Result);
        }

        private void StartDefaultFilterSnapshotLoad()
        {
            var loadTask = AppServices.Get<DropItemResolver>().LoadDefaultFilterSnapshotAsync();
            _defaultDropItemFilterSnapshotTask = loadTask;

            _ = loadTask.ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    _defaultFilterLoadFailureLogged = false;
                    return;
                }

                _nextDefaultFilterRetryAt = DateTime.UtcNow.Add(DefaultFilterRetryDelay);
                if (_defaultFilterLoadFailureLogged)
                    return;

                _defaultFilterLoadFailureLogged = true;
                Exception failure = task.Exception != null
                    ? task.Exception
                    : new InvalidOperationException("Default drop item filter snapshot loading was canceled.");
                AppLogger.Warn("Default drop item filter snapshot loading failed. It will be retried later.", failure);
            }, TaskScheduler.Default);
        }

        private void ScheduleDefaultFilterRetryIfDue()
        {
            if (DateTime.UtcNow < _nextDefaultFilterRetryAt)
                return;

            StartDefaultFilterSnapshotLoad();
        }

        private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 빈 이름 = 설정 전체 교체(프로필/초기화/마법사) — 필터 스냅샷을 다시 만든다
            if (string.IsNullOrEmpty(e.PropertyName) ||
                e.PropertyName == nameof(ChatSettings.UseCustomDropItemFilter) ||
                e.PropertyName == nameof(ChatSettings.CustomDropItemJson))
            {
                RefreshCustomFilterSnapshot();
            }
        }

        private DropItemResolver.DropItemFilterSnapshot? GetCustomFilterSnapshot()
        {
            lock (_customFilterLock)
            {
                return _customFilterSnapshot;
            }
        }

        private void RefreshCustomFilterSnapshot()
        {
            DropItemResolver.DropItemFilterSnapshot? nextSnapshot = null;
            if (_settings.UseCustomDropItemFilter &&
                !string.IsNullOrWhiteSpace(_settings.CustomDropItemJson) &&
                AppServices.Get<DropItemResolver>().TryCreateFilterSnapshot(_settings.CustomDropItemJson, out var snapshot))
            {
                nextSnapshot = snapshot;
            }

            lock (_customFilterLock)
            {
                _customFilterSnapshot = nextSnapshot;
            }
        }
    }

    public sealed record MainLogPipelineAnalysis(
        LogAnalysisResult Primary,
        LogAnalysisResult? DefaultItemDrop,
        LogAnalysisResult? Toast,
        ItemDropGrade? TopMagicStoneAlertGrade = null);
}
