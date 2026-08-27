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
                defaultItemDropAnalysis = TryAnalyzeWithDefaultFilter(html, isRealTime);
                toastAnalysis = defaultItemDropAnalysis;

                var customFilterSnapshot = GetCustomFilterSnapshot();
                if (_settings.UseCustomDropItemFilter && customFilterSnapshot != null)
                {
                    toastAnalysis = _logAnalysisService.Analyze(html, isRealTime, customFilterSnapshot);
                }
            }

            return new MainLogPipelineAnalysis(primaryAnalysis, defaultItemDropAnalysis, toastAnalysis);
        }

        /// <summary>아이템 드롭 판정이 나올 수 있는 줄인지 — 시스템 로그이면서 "획득" 문구가 있는 경우만.</summary>
        private static bool MayContainItemDrop(LogAnalysisResult primaryAnalysis)
        {
            if (!primaryAnalysis.IsSystemLog)
                return false;

            string text = primaryAnalysis.Parsed.FormattedText ?? string.Empty;
            return text.Contains("획득", StringComparison.Ordinal);
        }

        private LogAnalysisResult? TryAnalyzeWithDefaultFilter(string html, bool isRealTime)
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

            return _logAnalysisService.Analyze(html, isRealTime, snapshotTask.Result);
        }

        private void StartDefaultFilterSnapshotLoad()
        {
            var loadTask = DropItemResolver.LoadDefaultFilterSnapshotAsync();
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
            if (e.PropertyName == nameof(ChatSettings.UseCustomDropItemFilter) ||
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
                DropItemResolver.TryCreateFilterSnapshot(_settings.CustomDropItemJson, out var snapshot))
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
        LogAnalysisResult? Toast);
}
