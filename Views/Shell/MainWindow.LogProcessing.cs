using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.Services.LogAnalysis;

namespace TWChatOverlay.Views
{
    /// <summary>분석된 로그 배치를 화면에 반영 (탭 버퍼, 숨김 필터, 어밴던 주간 합계)</summary>
    public partial class MainWindow
    {
        // ── 이 부분 클래스가 주로 쓰는 상태 ──
        private bool _isRefreshLogDisplayScheduled;
        private AbandonSummaryValue _AbandonWeeklySummary = new();
        private string _AbandonWeeklySummaryWeekKey = string.Empty;

        private static readonly Regex ShoutToastSourceRegex = new(
            @"^\s*\[\s*(?:\d{1,2}:\d{2}(?::\d{2})?|\d{1,2}\s*시\s*\d{1,2}\s*분(?:\s*\d{1,2}\s*초)?)\s*\]\s*외치기\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex OrlyRemainingAttackOneRegex = new(
            @"남은\s*공격\s*횟수\s*:\s*1(?!\d)",
            RegexOptions.Compiled);
        private enum HiddenChatContinuationFamily
        {
            None,
            Normal,
            Club
        }

        private HiddenChatContinuationFamily _pendingHiddenChatContinuationFamily = HiddenChatContinuationFamily.None;
        private string? _pendingHiddenChatContinuationTimestamp;
        /// <summary>
        /// 반사 패턴이 걸려 있는 시간. 이 시간이 지나면 종료 알림음이 울린다.
        /// 알림 창 글자도 같은 값을 써야 패턴이 아직 도는데 글자만 먼저 사라지는 일이 없다.
        /// </summary>
        private static readonly TimeSpan ReflectionPatternDuration = TimeSpan.FromSeconds(7);

        /// <summary>
        /// 알림 창을 종료 알림음보다 조금 더 두는 시간.
        /// 둘을 같은 시각에 맞추면 창이 먼저 닫히고 소리가 뒤따라 나는 것처럼 보인다
        /// (타이머가 거의 동시에 돌고, 소리는 재생까지 약간 늦다).
        /// </summary>
        private static readonly TimeSpan ReflectionAlertLinger = TimeSpan.FromMilliseconds(150);

        // 줄마다 쓰는 싱글턴 서비스 — DI 조회를 줄마다 하지 않도록 처음 쓸 때 한 번 꺼내 둔다
        private ChatWindowHub? _chatWindowHubCache;
        private NotificationService? _notificationServiceCache;
        private RecaptureSupplyAlertService? _recaptureSupplyAlertServiceCache;
        private RecaptureSupplyPadOrderService? _recaptureSupplyPadOrderServiceCache;
        private ContentTimerService? _contentTimerServiceCache;
        private ItemDropToastService? _itemDropToastServiceCache;
        private ShoutToastService? _shoutToastServiceCache;

        private ChatWindowHub ChatWindowHubService => _chatWindowHubCache ??= AppServices.Get<ChatWindowHub>();
        private NotificationService NotificationServiceInstance => _notificationServiceCache ??= AppServices.Get<NotificationService>();
        private RecaptureSupplyAlertService RecaptureSupplyAlertServiceInstance => _recaptureSupplyAlertServiceCache ??= AppServices.Get<RecaptureSupplyAlertService>();
        private RecaptureSupplyPadOrderService RecaptureSupplyPadOrderServiceInstance => _recaptureSupplyPadOrderServiceCache ??= AppServices.Get<RecaptureSupplyPadOrderService>();
        private ContentTimerService ContentTimerServiceInstance => _contentTimerServiceCache ??= AppServices.Get<ContentTimerService>();
        private ItemDropToastService ItemDropToastServiceInstance => _itemDropToastServiceCache ??= AppServices.Get<ItemDropToastService>();
        private ShoutToastService ShoutToastServiceInstance => _shoutToastServiceCache ??= AppServices.Get<ShoutToastService>();

        // 이번 배치에서 탭 버퍼에 줄이 들어갔는지 — 서브 창 알림은 배치 끝에 한 번만 보낸다
        private bool _buffersChangedInBatch;

        private readonly LogDocumentRenderer _logDocumentRenderer = new(200);
        private readonly object _reflectionEndAlertTimerLock = new();
        private readonly HashSet<DispatcherTimer> _reflectionEndAlertTimers = new();

        #region Log Processing

        private void ProcessUiLogBatch(IReadOnlyList<AnalyzedLogEvent> batch)
        {
            if (batch.Count == 0) return;

            bool shouldAutoScroll = ChatDisplay?.IsAutoScrollEnabled == true;

            if (LogDisplay != null)
            {
                LogDisplay.BeginChange();
            }

            try
            {
                foreach (var item in batch)
                {
                    // 한 줄의 처리 실패가 배치의 나머지 줄을 잃게 하지 않는다 (로그 씹힘 방지)
                    try
                    {
                        // 백그라운드에서 이미 분석됨 — UI 스레드는 결과를 소비만 한다
                        var analysis = item.Analysis.Primary;

                        // 던전 입장 횟수 표시·일일/주간 어밴던/갈망 카운트는 분석기가 만든 FormattedText로 본다
                        // (원본 HTML을 UI 스레드에서 다시 디코드·정규화하지 않는다). 파싱이 안 된 줄(font 태그 2개 미만)은 건너뛴다.
                        bool handledDailyWeeklyCountLog = false;
                        if (analysis.IsSuccess)
                        {
                            _dungeonCountDisplayService.Process(analysis);
                            handledDailyWeeklyCountLog =
                                item.IsRealTime &&
                                _dailyWeeklyContentOverlay?.IsVisible == true &&
                                _dailyWeeklyContentOverlay.TryProcessAbandonOrCravingLog(analysis.Parsed.FormattedText);
                        }

                        ProcessLogPipelineContext(item, handledDailyWeeklyCountLog);
#if DEBUG
                        // 디버그 빌드: 로그 시각 대비 표시 지연 HUD
                        if (item.IsRealTime)
                            ChatLatencyHud.Report(analysis.Parsed.FormattedText, item.Source.ReadAtUtc, item.AnalyzedAtUtc);
#endif
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Log line processing failed; line skipped. Html='{item.Html}'", ex);
                    }
                }
            }
            finally
            {
                if (LogDisplay != null)
                {
                    // EndChange 이후 레이아웃은 WPF가 다음 렌더 틱에 알아서 수행한다 — 동기 강제 패스 불필요
                    LogDisplay.EndChange();
                    if (shouldAutoScroll)
                        ScrollLogDisplayToEndAfterLayout();
                }

                if (_buffersChangedInBatch)
                {
                    _buffersChangedInBatch = false;
                    GuardSideEffect("buffers-changed", () => ChatWindowHubService.NotifyBuffersChanged());
                }
            }
        }

        /// <summary>방전에 걸린 시각 — 풀린 줄이 올 때 얼마나 걸려 있었는지 적으려고 둔다.</summary>
        private DateTime? _dischargeStartedAt;

        /// <summary>"[ 9시 03분 32초] …" 앞머리에서 시각을 읽는다. 날짜는 오늘로 보되, 미래면 어제로 본다(자정 넘김).</summary>
        private static readonly System.Text.RegularExpressions.Regex LogLineTimeRegex =
            new(@"^\[\s*(?<h>\d{1,2})시\s*(?<m>\d{1,2})분\s*(?<s>\d{1,2})초\s*\]", System.Text.RegularExpressions.RegexOptions.Compiled);

        private static DateTime? TryReadLogTime(string? text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            var match = LogLineTimeRegex.Match(text);
            if (!match.Success)
                return null;

            int hour = int.Parse(match.Groups["h"].Value);
            int minute = int.Parse(match.Groups["m"].Value);
            int second = int.Parse(match.Groups["s"].Value);
            if (hour > 23 || minute > 59 || second > 59)
                return null;

            DateTime at = DateTime.Today.AddHours(hour).AddMinutes(minute).AddSeconds(second);
            if (at > DateTime.Now.AddMinutes(1))
                at = at.AddDays(-1);
            return at;
        }

        /// <summary>부수효과 한 기능의 실패가 같은 줄의 다른 기능(채팅 표시 등)까지 막지 않게 격리한다.</summary>
        private static void GuardSideEffect(string what, Action action)
        {
            try { action(); }
            catch (Exception ex) { AppLogger.Warn($"Log side effect '{what}' failed.", ex); }
        }

        /// <param name="item">백그라운드 파이프라인이 분석을 끝낸 줄. 여기서 다시 분석하지 않는다.</param>
        /// <param name="handledDailyWeeklyCountLog">일일/주간 창이 어밴던/갈망 카운트 줄로 이미 처리했는지 (중복 반영 방지).</param>
        private void ProcessLogPipelineContext(AnalyzedLogEvent item, bool handledDailyWeeklyCountLog)
        {
            var pipelineAnalysis = item.Analysis;
            var analysis = pipelineAnalysis.Primary;
            if (!analysis.IsSuccess) return;
            var parseResult = analysis.Parsed;
            bool isActualShout = parseResult.Category == ChatCategory.Shout && IsActualShoutSource(parseResult.FormattedText);
            bool isContentCompletionRelevant = item.IsContentCompletionRelevant; // 분석 스레드에서 한 번 계산한 값
            bool shouldRunLiveUiEffects = item.IsRealTime;
            // 시작 시 과거 로그(tail) 표시나 테스트 주입은 화면 표시만 하고 집계/아카이브 등 부수효과는 하지 않는다.
            // (그렇지 않으면 재실행 때마다 최근 1000줄이 재집계되어 카운트가 틀어지고, 외치기/컨텐츠 로그가 두 번씩 쌓인다.)
            bool isHistoricalDisplayOnly = !item.IsRealTime;

            if (!isHistoricalDisplayOnly)
            {
                GuardSideEffect("buff-tracker", () => _buffTrackerService.ProcessLog(analysis));

                // 추적창·사냥 기록은 사냥 경험치만 센다 (탐험·별동대·훈련소 같은 보상은 빼고)
                if (analysis.HasExperienceGain && parseResult.IsHuntingExp)
                {
                    // 공백 판정이 처리 지연에 휘둘리지 않게 로그에 찍힌 시각을 함께 넘긴다
                    DateTime? logTime = TryReadLogTime(parseResult.FormattedText);
                    GuardSideEffect("exp-gain", () => _expService.AddExp(parseResult.GainedExp, logTime));
                    // 사냥 기록(판 단위 요약)은 실시간 줄만 센다. 과거 로그는 시작할 때 따로 훑는다
                    if (item.IsRealTime)
                        GuardSideEffect("hunt-session", () => _huntSessionService.AddGain(logTime ?? DateTime.Now, parseResult.GainedExp));
                }
            }

            if (shouldRunLiveUiEffects)
                GuardSideEffect("essence-alert", () => _experienceEssenceAlertService.Process(analysis));

            if (!handledDailyWeeklyCountLog &&
                shouldRunLiveUiEffects &&
                _settings.ShowDailyWeeklyContentOverlay &&
                (analysis.ShouldRunDailyWeeklyContent || isContentCompletionRelevant))
            {
                GuardSideEffect("daily-weekly", () =>
                {
                    EnsureDailyWeeklyWindowForRealtimeProcessing();
                    _dailyWeeklyContentOverlay?.ProcessLog(analysis);
                });
            }

            bool suppressChatLine = ShouldHideChatLine(parseResult);

            // 설정과 무관하게 어느 창에도 그리지 않는 줄(반복 안내·에토스)은 버퍼에 넣지 않는다 — 서브 채팅창도 같은 규칙을 따른다
            if (!suppressChatLine && !parseResult.IsDisplaySuppressed)
            {
                foreach (string tabName in analysis.BufferTabs)
                    AddToBuffer(tabName, parseResult);
            }

            // 과거 로그 표시는 여기까지(버퍼 추가로 화면만 갱신). 이후 집계/토스트/아카이브는 건너뛴다.
            if (isHistoricalDisplayOnly)
                return;

            if (shouldRunLiveUiEffects)
            {
                // 감전 패턴: 방전에 걸린 동안 알림 창을 띄우고 풀리면 닫는다 (설정 - 던전 도우미 - 어비스)
                if (parseResult.IsDischargeAlert)
                {
                    GuardSideEffect("discharge-alert", () =>
                    {
                        _dischargeStartedAt = DateTime.Now;
                        AppLogger.Info($"Discharge status detected. Line='{parseResult.FormattedText}'");
                        if (_settings.EnableDischargeAlert)
                            DungeonAlertWindow.Show(DungeonAlertSource.AbyssDischarge, _settings, "감전", "방전 상태");
                    });
                }
                else if (parseResult.IsDischargeCleared)
                {
                    GuardSideEffect("discharge-cleared", () =>
                    {
                        string held = _dischargeStartedAt is DateTime at
                            ? $"{(DateTime.Now - at).TotalSeconds:F1}s"
                            : "unknown";
                        _dischargeStartedAt = null;
                        AppLogger.Info($"Discharge status cleared after {held}. Line='{parseResult.FormattedText}'");
                        DungeonAlertWindow.HideAlert(DungeonAlertSource.AbyssDischarge);   // 설정을 끈 뒤 풀린 경우에도 남지 않게 항상 닫는다
                    });
                }

                if (parseResult.IsReflectionPatternAlert)
                {
                    GuardSideEffect("reflection-alert", () =>
                    {
                        NotificationServiceInstance.PlayAlert("Reflection.wav");
                        // 패턴이 도는 동안 떠 있다가 종료 알림음이 난 직후 사라진다
                        DungeonAlertWindow.Flash(DungeonAlertSource.AbyssReflection, _settings, "반사", "반사 패턴",
                            ReflectionPatternDuration + ReflectionAlertLinger);
                        ScheduleReflectionEndAlert();
                    });
                }

                if (pipelineAnalysis.Toast is { HasTrackedItemDrop: true, ShouldShowItemDropToast: true } toastAnalysis)
                {
                    GuardSideEffect("item-toast", () => ItemDropToastServiceInstance.Show(
                        toastAnalysis.Parsed.TrackedItemName ?? "아이템",
                        toastAnalysis.Parsed.TrackedItemGrade,
                        withSound: true));
                }

                if (parseResult.Category == ChatCategory.Shout)
                GuardSideEffect("shout-actions", () =>
                {
                    if (!isActualShout)
                    {
                        AppLogger.Debug($"Skipped shout toast for non-shout formatted line: '{parseResult.FormattedText}'");
                    }

                    if (_settings.AutoCopyShoutNickname && isActualShout)
                    {
                        string? shoutNickname = GetShoutNicknameForClipboard(parseResult);
                        if (!string.IsNullOrWhiteSpace(shoutNickname))
                        {
                            TrySetClipboardText(shoutNickname);
                        }
                        else
                        {
                            AppLogger.Warn($"Shout nickname auto-copy skipped because nickname could not be extracted. Text='{parseResult.FormattedText}'");
                        }
                    }

                    // 표시를 꺼 둔 종류(무료/유료/공지)의 외치기는 토스트도 띄우지 않는다
                    if (_settings.ShowShoutToastPopup && isActualShout &&
                        LogParser.IsVisible(parseResult, _settings))
                        ShoutToastServiceInstance.Show(parseResult, _settings);
                });
            }

            // 아카이브 기록은 LogAnalysisPipeline의 백그라운드 핸들러로 이동 (UI 스레드 파일 IO 제거)

            if (pipelineAnalysis.DefaultItemDrop?.HasTrackedItemDrop ?? analysis.HasTrackedItemDrop)
            {
                GuardSideEffect("calendar-item", () =>
                {
                    var realtimeItemParseResult = pipelineAnalysis.DefaultItemDrop?.Parsed ?? parseResult;
                    _itemCalendarWindow?.ApplyRealtimeItemLog(realtimeItemParseResult, DateTime.Today);
                });
            }

            if (analysis.IsSystemLog)
            {
                GuardSideEffect("recapture-essence", () =>
                {
                    RecaptureSupplyAlertServiceInstance.Observe(parseResult.FormattedText);
                    RecaptureSupplyPadOrderServiceInstance.Observe(parseResult.FormattedText);
                    if (IsExperienceEssenceExchangeLog(parseResult.FormattedText))
                        _itemCalendarWindow?.ApplyRealtimeExperienceEssenceLog(parseResult.FormattedText, DateTime.Today);
                });
            }

            // 던전 타이머: 시스템 줄과 보스 대사(일반 흰 글, 분류표에 없는 색은 Unknown)만 본다 — 팀·클럽·외치기에 같은 말이 섞여도 반응하지 않는다.
            // 실시간 줄만 본다 — 시작 시 과거 로그를 다시 읽으며 타이머가 도는 것을 막는다.
            if (shouldRunLiveUiEffects &&
                (analysis.IsSystemLog || parseResult.Category is ChatCategory.Normal or ChatCategory.NormalSelf or ChatCategory.Unknown))
                GuardSideEffect("dungeon-timer", () => ContentTimerServiceInstance.Observe(parseResult.FormattedText));

            if (analysis.ShouldRunDailyWeeklyContent || analysis.IsSystemLog)
            GuardSideEffect("abandon-summary", () =>
            {
                EnsureAbandonWeeklySummaryCurrent(DateTime.Today);
                bool isAbandonCountEntry = DailyWeeklyLogAnalyzer.TryMatchAbandonRoadCount(parseResult.FormattedText, out _);

                if (AbandonSummaryCalculator.TryAccumulate(parseResult.FormattedText, ref _AbandonWeeklySummary))
                {
                    _itemCalendarWindow?.ApplyRealtimeAbandonLog(parseResult.FormattedText, DateTime.Today);

                    if (_settings.ShowAbandonRoadSummaryWindow &&
                        WindowState != WindowState.Minimized)
                    {
                        ShowAbandonRoadSummaryWindow(previewMode: false, restartLifetime: true, activateWindow: false);
                        _AbandonRoadSummaryWindow?.UpdateSummary(_AbandonWeeklySummary);
                    }
                }
                else if (isAbandonCountEntry &&
                         _settings.ShowAbandonRoadSummaryWindow &&
                         WindowState != WindowState.Minimized)
                {
                    ShowAbandonRoadSummaryWindow(previewMode: false, restartLifetime: true, activateWindow: false);
                    _AbandonRoadSummaryWindow?.UpdateSummary(_AbandonWeeklySummary);
                }
            });

            if (shouldRunLiveUiEffects)
            {
                // 실시간 추가와 탭 재표시(RequestRefreshLogDisplay)가 같은 억제 판정을 쓴다
                if (_logAnalysisService.ShouldRenderToTab(parseResult, _currentTabTag) &&
                    !ShouldSuppressOverlayText(parseResult, suppressChatLine))
                {
                    AddToUI(parseResult, isRealTime: item.IsRealTime);
                }
            }

            if (analysis.ShouldShowEtosDirection && shouldRunLiveUiEffects)
            {
                GuardSideEffect("etos-direction", () =>
                {
                    var helperWindow = SubAddonWindow.Instance ?? CreateSubAddonWindow();
                    helperWindow?.ShowEtosDirection(parseResult.EtosImagePath);
                });
            }
        }

        private static bool IsActualShoutSource(string? formattedText)
            => !string.IsNullOrWhiteSpace(formattedText) &&
               ShoutToastSourceRegex.IsMatch(formattedText);

        private void ResetHiddenChatContinuationState()
        {
            _pendingHiddenChatContinuationFamily = HiddenChatContinuationFamily.None;
            _pendingHiddenChatContinuationTimestamp = null;
        }

        private static HiddenChatContinuationFamily GetHiddenChatContinuationFamily(ChatCategory category)
            => category switch
            {
                ChatCategory.Normal or ChatCategory.NormalSelf => HiddenChatContinuationFamily.Normal,
                ChatCategory.Club => HiddenChatContinuationFamily.Club,
                _ => HiddenChatContinuationFamily.None
            };

        private static bool TrySplitTimestampAndBody(string formattedText, out string timestamp, out string body)
        {
            timestamp = string.Empty;
            body = string.Empty;

            if (string.IsNullOrWhiteSpace(formattedText))
                return false;

            int closingBracketIndex = formattedText.IndexOf(']');
            if (closingBracketIndex < 0)
            {
                body = formattedText.Trim();
                return false;
            }

            int openingBracketIndex = formattedText.LastIndexOf('[', closingBracketIndex);
            if (openingBracketIndex < 0)
                openingBracketIndex = 0;

            timestamp = formattedText.Substring(openingBracketIndex, closingBracketIndex - openingBracketIndex + 1).Trim();
            body = closingBracketIndex + 1 < formattedText.Length
                ? formattedText.Substring(closingBracketIndex + 1)
                : string.Empty;
            return true;
        }

        private bool ShouldHideChatLine(LogParser.ParseResult parseResult)
        {
            if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.FormattedText))
                return false;

            if (IsContinuationOfHiddenChat(parseResult))
                return true;

            // 무시 판정(클럽 보스 공지·보스 대사·안내문)은 분석 시점에 캐시한 매치 결과 + 현재 설정으로 한다 — 여기서 정규식을 다시 돌리지 않는다.
            // 이어지는 줄(continuation) 추적만 UI 상태라 이 창에 남긴다.
            if (!_logAnalysisService.IsHiddenByChatFilter(parseResult))
                return false;

            TrackHiddenChatContinuation(parseResult);
            return true;
        }

        private bool IsContinuationOfHiddenChat(LogParser.ParseResult parseResult)
        {
            if (_pendingHiddenChatContinuationFamily == HiddenChatContinuationFamily.None)
                return false;

            if (_pendingHiddenChatContinuationFamily == HiddenChatContinuationFamily.Club)
            {
                if (!TrySplitTimestampAndBody(parseResult.FormattedText, out string clubTimestamp, out _))
                {
                    ResetHiddenChatContinuationState();
                    return false;
                }

                if (parseResult.Category != ChatCategory.Club ||
                    !string.Equals(clubTimestamp, _pendingHiddenChatContinuationTimestamp, StringComparison.Ordinal))
                {
                    ResetHiddenChatContinuationState();
                    return false;
                }

                return true;
            }

            if (!parseResult.HasLeadingBodyWhitespace)
            {
                ResetHiddenChatContinuationState();
                return false;
            }

            if (!TrySplitTimestampAndBody(parseResult.FormattedText, out string timestamp, out _))
            {
                ResetHiddenChatContinuationState();
                return false;
            }

            HiddenChatContinuationFamily family = GetHiddenChatContinuationFamily(parseResult.Category);
            if (family != _pendingHiddenChatContinuationFamily ||
                !string.Equals(timestamp, _pendingHiddenChatContinuationTimestamp, StringComparison.Ordinal))
            {
                ResetHiddenChatContinuationState();
                return false;
            }

            return true;
        }

        private void TrackHiddenChatContinuation(LogParser.ParseResult parseResult)
        {
            HiddenChatContinuationFamily family = GetHiddenChatContinuationFamily(parseResult.Category);
            if (family == HiddenChatContinuationFamily.None)
            {
                ResetHiddenChatContinuationState();
                return;
            }

            if (!TrySplitTimestampAndBody(parseResult.FormattedText, out string timestamp, out _))
            {
                ResetHiddenChatContinuationState();
                return;
            }

            _pendingHiddenChatContinuationFamily = family;
            _pendingHiddenChatContinuationTimestamp = timestamp;
        }

        /// <summary>
        /// 메인 채팅 오버레이에 이 줄을 그리지 않을지. 실시간 추가와 탭 재표시가 같은 판정을 쓴다.
        /// 설정과 무관한 억제(반복 안내·에토스 줄)는 분석 시점에 계산된 <see cref="LogParser.ParseResult.IsDisplaySuppressed"/>를 그대로 쓰고,
        /// 숨김 판정(<see cref="ShouldHideChatLine"/>)은 이어지는 줄 추적 상태를 바꾸므로 호출자가 줄마다 한 번만 계산해 넘긴다.
        /// 반사 패턴 알림 줄(심연의 제2사도 대사)은 보스 대사 판정(화자·문구 모두 일치)에 포함되므로 따로 보지 않는다.
        /// </summary>
        private static bool ShouldSuppressOverlayText(LogParser.ParseResult parseResult, bool isHiddenChatLine)
        {
            if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.FormattedText))
                return false;

            return isHiddenChatLine || parseResult.IsDisplaySuppressed;
        }

        private void ScheduleReflectionEndAlert()
        {
            var timer = new DispatcherTimer
            {
                Interval = ReflectionPatternDuration
            };

            EventHandler? tickHandler = null;
            tickHandler = (_, _) =>
            {
                timer.Tick -= tickHandler;
                timer.Stop();

                lock (_reflectionEndAlertTimerLock)
                {
                    _reflectionEndAlertTimers.Remove(timer);
                }

                if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
                {
                    NotificationServiceInstance.PlayAlert("ReflectionEnd.wav");
                }
            };

            timer.Tick += tickHandler;

            lock (_reflectionEndAlertTimerLock)
            {
                _reflectionEndAlertTimers.Add(timer);
            }

            timer.Start();
        }

        private void CancelPendingReflectionEndAlerts()
        {
            lock (_reflectionEndAlertTimerLock)
            {
                foreach (DispatcherTimer timer in _reflectionEndAlertTimers)
                {
                    try { timer.Stop(); } catch { }
                }

                _reflectionEndAlertTimers.Clear();
            }
        }

        private static bool IsContentCompletionRelevantLog(string? formattedText)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
                return false;

            string text = formattedText;

            if (IsConfusedOrColorlessElsoRewardLine(text))
                return false;

            if (text.Contains("[1+1] 이벤트", StringComparison.Ordinal))
                return false;

            if (DailyWeeklyLogAnalyzer.TryMatchAbyssEntry(text, out _) ||
                DailyWeeklyLogAnalyzer.TryMatchAbyssFloor(text, out _) ||
                DailyWeeklyLogAnalyzer.TryMatchAbyssReward(text, out _) ||
                DailyWeeklyLogAnalyzer.TryMatchSinjoReward(text, out _) ||
                DailyWeeklyLogAnalyzer.TryMatchCatacombsReward(text, out _) ||
                DailyWeeklyLogAnalyzer.TryMatchAbandonRoadCount(text, out _))
            {
                return true;
            }

            if (DailyWeeklyContentArchiveKeywords.ContainsAny(text))
                return true;

            if (OrlyRemainingAttackOneRegex.IsMatch(text))
                return true;

            if (text.Contains("[경험의 정수] 아이템을 획득하였습니다.", StringComparison.Ordinal))
                return true;

            return false;
        }

        /// <summary>
        /// 일일/주간 컨텐츠 추적 항목의 현재 횟수/최대 횟수. 던전 타이머의 진행도 표시(아페티리아 3/7)에 쓴다.
        /// 추적 창이 아직 없으면 만들어서 읽는다 (실시간 처리용으로 어차피 만들어지는 창이다). UI 스레드에서 부른다.
        /// </summary>
        public bool TryGetDailyWeeklyItemProgress(string name, out int current, out int max)
        {
            current = 0;
            max = 0;
            try
            {
                EnsureDailyWeeklyWindowForRealtimeProcessing();
                return _dailyWeeklyContentOverlay?.TryGetItemProgress(name, out current, out max) == true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to read daily/weekly progress for '{name}'.", ex);
                return false;
            }
        }

        /// <summary>일일/주간 컨텐츠 창에서 그 항목을 켜 뒀는지. 항목이 없거나 창을 못 만들면 null.</summary>
        public bool? IsDailyWeeklyItemEnabled(string name)
        {
            try
            {
                EnsureDailyWeeklyWindowForRealtimeProcessing();
                return _dailyWeeklyContentOverlay?.IsItemEnabled(name);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to read daily/weekly item state for '{name}'.", ex);
                return null;
            }
        }

        /// <summary>
        /// 일일/주간 컨텐츠 창을 (없으면) 만든다. 표시 경로와 실시간 처리 경로가 같은 생성 코드를 쓴다 —
        /// 예전엔 실시간 경로가 IsVisibleChanged 구독을 빠뜨려, 그 경로로 먼저 만들어지면 가시성 이벤트가 오지 않았다.
        /// </summary>
        /// <returns>이번 호출에서 새로 만들었으면 true.</returns>
        private bool EnsureDailyWeeklyContentWindow()
        {
            if (_dailyWeeklyContentOverlay != null && _dailyWeeklyContentOverlay.IsLoaded)
                return false;

            _dailyWeeklyContentOverlay = new DailyWeeklyContentWindow(_settings);
            _dailyWeeklyContentOverlay.IsVisibleChanged += DailyWeeklyContentOverlay_IsVisibleChanged;
            _dailyWeeklyContentOverlay.Closed += (_, _) =>
            {
                _dailyWeeklyContentOverlay = null;
                try { DailyWeeklyVisibilityChanged?.Invoke(this, false); } catch { }
            };
            return true;
        }

        private void EnsureDailyWeeklyWindowForRealtimeProcessing()
            => EnsureDailyWeeklyContentWindow();

        private static bool IsConfusedOrColorlessElsoRewardLine(string text)
            => LogTextClassifier.IsConfusedOrColorlessElsoReward(text);

        private static bool IsExperienceEssenceExchangeLog(string text)
            => LogTextClassifier.IsExperienceEssenceExchange(text);

        private void AddToBuffer(string tabName, LogParser.ParseResult log)
        {
            _logTabBufferStore.Add(tabName, log);
            // 서브 창 알림은 ProcessUiLogBatch 끝에서 배치당 한 번 (허브도 50ms 타이머로 몰아 보내므로 보는 쪽 결과는 같다)
            _buffersChangedInBatch = true;
        }

        /// <summary>문단 하나를 채팅 문서에 더한다. 스크롤은 하지 않는다 — 호출자가 배치 끝에서 <see cref="ScrollLogDisplayToEndAfterLayout"/>를 부른다.</summary>
        private void AddToUI(LogParser.ParseResult log, bool isRealTime = false)
        {
            if (LogDisplay == null) return;

            FontFamily safeFont = this.CurrentFont ?? FontService.GetFont(_settings.FontFamily);
            if (safeFont == null)
                safeFont = new FontFamily("Malgun Gothic");

            // 서브 채팅창과 같은 렌더러 — 메인 창만 마법진 배경·Wave.wav를 쓰고, 소리는 경험치 서비스 준비 뒤에만
            _logDocumentRenderer.AddLog(
                LogDisplay.Document,
                log,
                _settings,
                safeFont,
                _settings.FontSize,
                isRealTime,
                canPlayAlertSound: _expService.IsReady,
                useMagicCircleAlert: true);
        }

        private void ScrollLogDisplayToEndAfterLayout()
        {
            var logDisplay = LogDisplay;
            if (logDisplay == null) return;

            logDisplay.ScrollToEnd();

            // 새 문단의 레이아웃이 끝난 뒤(ContextIdle) 진짜 끝으로 한 번 더 — UpdateLayout 강제는 불필요
            Dispatcher.BeginInvoke(new Action(() =>
            {
                LogDisplay?.ScrollToEnd();
            }), DispatcherPriority.ContextIdle);
        }

        private void RequestRefreshLogDisplay(bool force = false)
        {
            if (LogDisplay == null || !_isLogServiceInitialized)
                return;

            if (!force && _isRefreshLogDisplayScheduled)
                return;

            _isRefreshLogDisplayScheduled = true;

            // Background priority keeps the refresh draining even after the overlay was hidden for a while.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isRefreshLogDisplayScheduled = false;

                if (LogDisplay == null)
                    return;

                bool shouldAutoScroll = ChatDisplay?.IsAutoScrollEnabled == true;

                ResetHiddenChatContinuationState();
                LogDisplay.BeginChange();
                try
                {
                    LogDisplay.Document.Blocks.Clear();

                    var logs = _logTabBufferStore.GetLogs(_currentTabTag);
                    foreach (var log in logs)
                    {
                        // 실시간 경로와 같게 숨김 판정(이어지는 줄 추적 포함)을 줄마다 먼저 한 번 하고 억제 판정에 넘긴다
                        bool isHiddenChatLine = ShouldHideChatLine(log);
                        if (ShouldSuppressOverlayText(log, isHiddenChatLine))
                            continue;
                        AddToUI(log, isRealTime: false);
                    }
                }
                finally
                {
                    LogDisplay.EndChange();
                    LogDisplay.InvalidateMeasure();
                    LogDisplay.InvalidateVisual();
                    LogDisplay.UpdateLayout();
                    if (shouldAutoScroll)
                        ScrollLogDisplayToEndAfterLayout();
                }
            }), DispatcherPriority.Background);
        }

        private void EnsureAbandonWeeklySummaryCurrent(DateTime date)
        {
            string weekKey = LogTextClassifier.GetIsoWeekKey(date);
            if (string.Equals(_AbandonWeeklySummaryWeekKey, weekKey, StringComparison.Ordinal))
                return;

            _AbandonWeeklySummary = _readableLogArchiveService.LoadAbandonWeeklySummary(date);
            _AbandonWeeklySummaryWeekKey = weekKey;
        }

        #endregion
    }
}
