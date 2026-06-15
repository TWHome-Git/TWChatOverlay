using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.Services;
using TWChatOverlay.Services.LogAnalysis;
using TWChatOverlay.ViewModels;

namespace TWChatOverlay.Views
{
    public partial class MainWindow
    {
        private static readonly Regex LeadingTimestampRegex = new(
            @"^\s*\[[^\]]+\]\s*",
            RegexOptions.Compiled);
        private static readonly Regex ShoutToastSourceRegex = new(
            @"^\s*\[\s*(?:\d{1,2}:\d{2}(?::\d{2})?|\d{1,2}\s*시\s*\d{1,2}\s*분(?:\s*\d{1,2}\s*초)?)\s*\]\s*외치기\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex OrlyRemainingAttackOneRegex = new(
            @"남은\s*공격\s*횟수\s*:\s*1(?!\d)",
            RegexOptions.Compiled);
        private static readonly string[] DailyWeeklyContentArchiveKeywords =
        {
            "이클립스 보스전(로카고스) 클리어 횟수:",
            "이클립스 보스전(에토스) 클리어 횟수:",
            "이클립스 보스전(체리아) 클리어 횟수:",
            "이클립스 보스전(마티아) 클리어 횟수:",
            "이클립스 보스전(티로로스) 클리어 횟수:",
            "이클립스 보스전(라이코스) 클리어 횟수:",
            "이클립스 보스 토벌전 클리어 횟수:",
            "보급품 탈환 클리어 횟수:",
            "달여왕 군대 훈련소 클리어 횟수:",
            "별동대 토벌 클리어 횟수:",
            "혼란한 대지 미션에 성공하여",
            "색을 잃은 땅 미션에 성공하여",
            "보스 몬스터를 퇴치하세요.",
            "던전을 클리어 하였습니다. 곧 마을로 돌아가게 됩니다.",
            "모든 일반지역을 토벌하여",
            "?고대 렐릭의 성소? - 주간 무료 클리어 횟수 :",
            "숨겨진 구역으로 이동할 수 있는 포탈이 맵 중앙",
            "지하요새의 망령 클리어 횟수:",
            "심연의 보물창고 입장 횟수:",
            "청소 아르바이트 보상 조건을 달성하였습니다.",
            "프라바 방어전 성공 보상으로 경험치 1000만을 획득",
            "이번 주 사명의 계승자 닉스 보상을",
            "이번 주 신조 보상을",
            "아페티리아 클리어 횟수:",
            "시오칸하임 - 보스 토벌전의 클리어 횟수 :",
            "시오칸하임 - 오딘 전면전의 클리어 횟수 :",
            "이번 주 어밴던로드 필멸의 땅 지역의 도전 횟수는",
            "이번 주 어밴던로드 카디프 지역의 도전 횟수는",
            "이번 주 어밴던로드 오를란느 지역의 도전 횟수는",
            "어비스 - 심층Ⅰ(보스전) 플레이를 이번 주에 7회 중",
            "어비스 - 심층Ⅱ(보스전) 플레이를 이번 주에 7회 중",
            "어비스 - 심층Ⅲ(보스전) 플레이를 이번 주에 7회 중",
            "어비스 보스전(EX) 클리어 횟수:",
            "남은 에너지는",
            "경험치가 100억이 차감되고, 경험의 정수 1개를 획득 하였습니다."
            ,"[경험의 정수] 아이템을 획득하였습니다."
            ,"[성난 빅테디의 별사탕] 아이템을 획득하였습니다."
            ,"레이티아 퇴치 보상으로 레이티아 보상 상자 (일반) 1개, 루비코나 코어 상자 20개를"
            ,"설계자 퇴치 보상으로 설계자 보상 상자 (일반) 1개, 루비코나 코어 상자 20개를 획득"
            ,"레이티아 퇴치 보상으로 레이티아 보상 상자 (어려움) 1개, 루비코나 코어 상자 30개"
            ,"설계자 퇴치 보상으로 설계자 보상 상자 (어려움) 1개, 루비코나 코어 상자 30개"
            ,"[환희의 레이티아 보상 상자] 아이템을 1개 획득하였습니다."
            ,"[에타의 의지 레벨업 상자]을(를) 1개 습득했습니다."
            ,"[루이나 및 제네로 일반 상자]을(를) 1개 습득했습니다."
            ,"샐리온 클리어 횟수:"
            ,"샐레아나 클리어 횟수:"
            ,"실라이론 클리어 횟수:"
            ,"실반 클리어 횟수:"
            ,"루미너스 클리어 횟수:"
            ,"루미너스(EX) 클리어 횟수:"
            ,"샐리온 코어 마스터 던전 클리어 횟수:"
            ,"샐레아나 코어 마스터 던전 클리어 횟수:"
            ,"실라이론 코어 마스터 던전 클리어 횟수:"
            ,"실반 코어 마스터 던전 클리어 횟수:"
            ,"루미너스 코어 마스터 던전 클리어 횟수:"
            ,"아페티리아(EX) 클리어 횟수:"
            ,"로카고스 코어 마스터 클리어 횟수:"
            ,"에토스 코어 마스터 클리어 횟수:"
            ,"체리아 코어 마스터 클리어 횟수:"
            ,"마티아 코어 마스터 클리어 횟수:"
            ,"라이코스 코어 마스터 클리어 횟수:"
            ,"티로로스 코어 마스터 클리어 횟수:"
            ,"코어 마스터 - 심층Ⅰ 클리어 횟수:"
            ,"코어 마스터 - 심층Ⅱ 클리어 횟수:"
            ,"코어 마스터 - 심층Ⅲ 클리어 횟수:"
            ,"티로로스의 계략을 막아내었습니다. 잠시 후 기억의 숲 전초기지로 이동됩니다."
        };

        private enum HiddenChatContinuationFamily
        {
            None,
            Normal,
            Club
        }

        private HiddenChatContinuationFamily _pendingHiddenChatContinuationFamily = HiddenChatContinuationFamily.None;
        private string? _pendingHiddenChatContinuationTimestamp;
        private readonly object _reflectionEndAlertTimerLock = new();
        private readonly HashSet<DispatcherTimer> _reflectionEndAlertTimers = new();

        #region Log Processing

        private void ProcessUiLogBatch(IReadOnlyList<(string Html, bool IsRealTime, bool IsStartupBackfill)> batch)
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
                    var context = CreateLogPipelineContext(item.Html, item.IsRealTime, item.IsStartupBackfill, deferUiScroll: true);
                    ProcessLogPipelineContext(context);
                }
            }
            finally
            {
                if (LogDisplay != null)
                {
                    LogDisplay.EndChange();
                    LogDisplay.InvalidateMeasure();
                    LogDisplay.InvalidateVisual();
                    LogDisplay.UpdateLayout();
                    if (shouldAutoScroll)
                        ScrollLogDisplayToEndAfterLayout();
                }
            }
        }

        private UnifiedLogPipelineContext CreateLogPipelineContext(string html, bool isRealTime, bool isStartupBackfill, bool deferUiScroll)
        {
            var context = new UnifiedLogPipelineContext(
                html,
                isRealTime,
                isStartupBackfill,
                deferUiScroll,
                1,
                false);

            _dungeonCountDisplayService.ProcessRaw(context.RawHtml, context.IsRealTime);
            context.HandledDailyWeeklyCountLog =
                context.IsRealTime &&
                _dailyWeeklyContentOverlay?.IsVisible == true &&
                _dailyWeeklyContentOverlay.TryProcessAbandonOrCravingLog(context.RawHtml);

            return context;
        }

        private void ProcessLogPipelineContext(UnifiedLogPipelineContext context)
        {
            if (string.IsNullOrWhiteSpace(context.RawHtml)) return;

            var pipelineAnalysis = _logPipelineCoordinator.Analyze(context.RawHtml, context.IsRealTime);
            context.PipelineAnalysis = pipelineAnalysis;
            var analysis = pipelineAnalysis.Primary;
            if (!analysis.IsSuccess) return;
            var parseResult = analysis.Parsed;
            bool isActualShout = parseResult.Category == ChatCategory.Shout && IsActualShoutSource(parseResult.FormattedText);
            bool isContentCompletionRelevant = IsContentCompletionRelevantLog(parseResult.FormattedText);
            bool shouldRunLiveUiEffects = context.IsRealTime && !context.IsStartupBackfill;

            _buffTrackerService.ProcessLog(analysis);

            if (analysis.HasExperienceGain) _expService.AddExp(parseResult.GainedExp);

            if (shouldRunLiveUiEffects)
                _experienceEssenceAlertService.Process(analysis);

            if (!context.HandledDailyWeeklyCountLog &&
                shouldRunLiveUiEffects &&
                _settings.ShowDailyWeeklyContentOverlay &&
                (analysis.ShouldRunDailyWeeklyContent || isContentCompletionRelevant))
            {
                if (parseResult.FormattedText.Contains("남은 공격 횟수 : 1", StringComparison.Ordinal) ||
                    parseResult.FormattedText.Contains("[경험의 정수] 아이템을 획득하였습니다.", StringComparison.Ordinal))
                {
                    AppLogger.Debug($"[DailyWeekly] Realtime dispatch. StartupBackfill={context.IsStartupBackfill}, OverlayExists={_dailyWeeklyContentOverlay != null}, OverlayVisible={_dailyWeeklyContentOverlay?.IsVisible == true}, Text='{parseResult.FormattedText}'");
                }

                EnsureDailyWeeklyWindowForRealtimeProcessing();

                _dailyWeeklyContentOverlay?.ProcessLog(analysis);
            }

            bool suppressChatLine = ShouldHideChatLine(parseResult);

            if (!suppressChatLine)
            {
                foreach (string tabName in analysis.BufferTabs)
                    AddToBuffer(tabName, parseResult);
            }

            if (shouldRunLiveUiEffects)
            {
                if (parseResult.IsReflectionPatternAlert)
                {
                    NotificationService.PlayAlert("Reflection.wav");
                    if (parseResult.IsReflectionPatternEndAlert)
                        ScheduleReflectionEndAlert();
                }

                if (pipelineAnalysis.Toast is { HasTrackedItemDrop: true, ShouldShowItemDropToast: true } toastAnalysis)
                {
                    ItemDropToastService.Show(
                        toastAnalysis.Parsed.TrackedItemName ?? "아이템",
                        toastAnalysis.Parsed.TrackedItemGrade,
                        withSound: true);
                }

                if (parseResult.Category == ChatCategory.Shout)
                {
                    bool allowLiveShoutActions = true;

                    if (!isActualShout)
                    {
                        AppLogger.Debug($"Skipped shout toast for non-shout formatted line: '{parseResult.FormattedText}'");
                    }

                    if (_settings.AutoCopyShoutNickname && isActualShout && allowLiveShoutActions)
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

                    if (_settings.ShowShoutToastPopup && isActualShout && allowLiveShoutActions)
                        ShoutToastService.Show(parseResult, _settings);
                }
            }

            _readableLogArchiveService.AppendFromAnalysis(DateTime.Today, analysis, isContentCompletionRelevant);

            if (pipelineAnalysis.DefaultItemDrop?.HasTrackedItemDrop ?? analysis.HasTrackedItemDrop)
            {
                var realtimeItemParseResult = pipelineAnalysis.DefaultItemDrop?.Parsed ?? parseResult;
                _itemCalendarWindow?.ApplyRealtimeItemLog(realtimeItemParseResult, DateTime.Today);
            }

            if (analysis.IsSystemLog)
            {
                RecaptureSupplyAlertService.Observe(parseResult.FormattedText);
                if (IsExperienceEssenceExchangeLog(parseResult.FormattedText))
                    _itemCalendarWindow?.ApplyRealtimeExperienceEssenceLog(parseResult.FormattedText, DateTime.Today);
            }

            if (analysis.ShouldRunDailyWeeklyContent || analysis.IsSystemLog)
            {
                EnsureAbandonWeeklySummaryCurrent(DateTime.Today);
                bool isAbandonCountEntry = DailyWeeklyLogAnalyzer.TryMatchAbandonRoadCount(parseResult.FormattedText, out _);

                if (AbandonSummaryCalculator.TryAccumulate(parseResult.FormattedText, ref _AbandonWeeklySummary))
                {
                    _itemCalendarWindow?.ApplyRealtimeAbandonLog(parseResult.FormattedText, DateTime.Today);

                    if (_settings.ShowAbandonRoadSummaryWindow &&
                        _canShowAuxiliaryWindows &&
                        WindowState != WindowState.Minimized)
                    {
                        ShowAbandonRoadSummaryWindow(previewMode: false, restartLifetime: true, activateWindow: false);
                        _AbandonRoadSummaryWindow?.UpdateSummary(_AbandonWeeklySummary);
                    }
                }
                else if (isAbandonCountEntry &&
                         _settings.ShowAbandonRoadSummaryWindow &&
                         _canShowAuxiliaryWindows &&
                         WindowState != WindowState.Minimized)
                {
                    ShowAbandonRoadSummaryWindow(previewMode: false, restartLifetime: true, activateWindow: false);
                    _AbandonRoadSummaryWindow?.UpdateSummary(_AbandonWeeklySummary);
                }
            }

            if (shouldRunLiveUiEffects)
            {
                if (_logAnalysisService.ShouldRenderToTab(parseResult, _currentTabTag))
                {
                    bool suppressOverlayText = suppressChatLine || !string.IsNullOrWhiteSpace(parseResult.EtosImagePath) || ShouldSuppressEtosChatLine(parseResult);

                    if (!suppressOverlayText)
                        AddToUI(parseResult, isRealTime: context.IsRealTime, deferScroll: context.DeferUiScroll);
                }
            }

            if (analysis.ShouldShowEtosDirection && shouldRunLiveUiEffects)
            {
                try
                {
                    var helperWindow = SubAddonWindow.Instance ?? CreateSubAddonWindow();
                    helperWindow?.ShowEtosDirection(parseResult.EtosImagePath);
                }
                catch { }
            }
        }

        private static bool IsActualShoutSource(string? formattedText)
            => !string.IsNullOrWhiteSpace(formattedText) &&
               ShoutToastSourceRegex.IsMatch(formattedText);

        private static bool ShouldSuppressEtosChatLine(LogParser.ParseResult parseResult)
        {
            if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.FormattedText))
                return false;

            if (parseResult.Category is not (ChatCategory.Normal or ChatCategory.NormalSelf or ChatCategory.Team))
                return false;

            return IsEtosEtosSender(parseResult);
        }

        private static bool IsEtosEtosSender(LogParser.ParseResult parseResult)
        {
            if (parseResult == null)
                return false;

            string sender = parseResult.SenderId ?? parseResult.RawSenderId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(sender))
                return false;

            string lower = sender.ToLowerInvariant();
            return lower.Contains("수색대장") &&
                   lower.Contains("에토스");
        }

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

            string text = parseResult.FormattedText;
            bool isHiddenClub = parseResult.Category == ChatCategory.Club &&
                                !_settings.ShowClubBoss &&
                                IgnoredChatMessageService.IsIgnoredClubMessage(text);

            bool isHiddenNormal = (parseResult.Category == ChatCategory.Normal ||
                                   parseResult.Category == ChatCategory.NormalSelf) &&
                                  IgnoredChatMessageService.IsIgnoredNormalMessage(text);

            if (!isHiddenClub && !isHiddenNormal)
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

        private bool ShouldSuppressOverlayText(LogParser.ParseResult parseResult)
        {
            if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.FormattedText))
                return false;

            // 반사 패턴 알림 트리거 문구는 알림 소리만 울리고 채팅에는 남기지 않는다.
            if (parseResult.IsReflectionPatternAlert)
                return true;

            // 에토스 방향 알림 트리거 문구는 방향 오버레이만 표시하고 일반 채팅 오버레이에는 노출하지 않음.
            if (!string.IsNullOrWhiteSpace(parseResult.EtosImagePath))
                return true;

            if ((parseResult.Category == ChatCategory.Normal ||
                 parseResult.Category == ChatCategory.NormalSelf ||
                 parseResult.Category == ChatCategory.Team) &&
                IsEtosEtosSender(parseResult))
                return true;

            return ShouldHideChatLine(parseResult);
        }

        private void ScheduleReflectionEndAlert()
        {
            var timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(7)
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
                    NotificationService.PlayAlert("ReflectionEnd.wav");
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

            if (ContainsAnyContentArchiveKeyword(text))
                return true;

            if (OrlyRemainingAttackOneRegex.IsMatch(text))
                return true;

            if (text.Contains("[경험의 정수] 아이템을 획득하였습니다.", StringComparison.Ordinal))
                return true;

            return false;
        }

        private void EnsureDailyWeeklyWindowForRealtimeProcessing()
        {
            if (_dailyWeeklyContentOverlay != null && _dailyWeeklyContentOverlay.IsLoaded)
                return;

            _dailyWeeklyContentOverlay = new DailyWeeklyContentWindow(_settings);
            _dailyWeeklyContentOverlay.Closed += (_, _) =>
            {
                _dailyWeeklyContentOverlay = null;
                try { DailyWeeklyVisibilityChanged?.Invoke(this, false); } catch { }
            };
        }

        private static bool IsMercurialSeedCompletionLog(string text)
        {
            return false;
        }

        private static bool IsStrictMercurialCompletionLog(string text)
        {
            return false;
        }

        private static bool IsConfusedOrColorlessElsoRewardLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            bool isTargetContent =
                text.Contains("혼란한 대지 미션에 성공하여", StringComparison.Ordinal) ||
                text.Contains("색을 잃은 땅 미션에 성공하여", StringComparison.Ordinal);

            if (!isTargetContent)
                return false;

            return text.Contains("ELSO를 획득했습니다", StringComparison.Ordinal);
        }

        private static bool IsExperienceEssenceExchangeLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains(
                "경험치 100억이 차감되고, 경험의 정수 1개를 획득 하였습니다.",
                StringComparison.Ordinal);
        }

        private static bool ContainsAnyContentArchiveKeyword(string text)
        {
            foreach (string keyword in DailyWeeklyContentArchiveKeywords)
            {
                if (text.Contains(keyword, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private void AddToBuffer(string tabName, LogParser.ParseResult log)
        {
            _logTabBufferStore.Add(tabName, log);
            ChatWindowHub.NotifyBuffersChanged();
        }

        private void AddToUI(LogParser.ParseResult log, bool isRealTime = false, bool deferScroll = false)
        {
            if (LogDisplay == null) return;

            bool shouldAutoScroll = ChatDisplay?.IsAutoScrollEnabled == true;

            bool isBlacklisted = BlacklistService.TryGetReason(log.SenderId, out string blacklistReason);
            Brush foreground = isBlacklisted ? BlacklistService.HighlightBrush : log.Brush;
            string displayText = isBlacklisted ? $"{log.FormattedText} [ {blacklistReason} ]" : log.FormattedText;
            displayText = ApplyEtaDecorations(displayText, log);
            if (!_settings.ShowTimestamp)
                displayText = LeadingTimestampRegex.Replace(displayText, string.Empty);

            FontFamily safeFont = this.CurrentFont ?? FontService.GetFont(_settings.FontFamily);
            if (safeFont == null)
                safeFont = new FontFamily("Malgun Gothic");

            Paragraph p = new Paragraph(new Run(displayText))
            {
                Foreground = foreground,
                FontSize = _settings.FontSize,
                FontFamily = safeFont,
                Margin = new Thickness(0, 0, 0, 1),
                LineHeight = 1
            };

            if (isBlacklisted)
            {
                p.Background = BlacklistService.HighlightBackgroundBrush;
                p.FontWeight = FontWeights.Bold;
            }

            if (log.IsHighlight)
            {
                if (log.IsMagicCircleAlert)
                {
                    p.Background = new SolidColorBrush(Color.FromArgb(140, 180, 60, 255));
                    p.FontWeight = FontWeights.Bold;
                }
                if (_settings.UseAlertColor && !isBlacklisted)
                {
                    p.Background = new SolidColorBrush(Color.FromArgb(120, 255, 140, 0));
                    p.FontWeight = FontWeights.Bold;
                }
                if (isRealTime && _expService.IsReady)
                {
                    if (log.IsMagicCircleAlert && _settings.UseMagicCircleAlert)
                        NotificationService.PlayAlert("Wave.wav");
                    else if (_settings.UseAlertSound)
                        NotificationService.PlayAlert("Highlight.wav");
                }
            }

            var blocks = LogDisplay.Document.Blocks;
            blocks.Add(p);

            if (blocks.Count > 200) blocks.Remove(blocks.FirstBlock);
            if (!deferScroll)
            {
                if (shouldAutoScroll)
                {
                    Dispatcher.BeginInvoke(new Action(ScrollLogDisplayToEndAfterLayout), DispatcherPriority.Background);
                }
            }
        }

        private string ApplyEtaDecorations(string text, LogParser.ParseResult log)
        {
            string lookupSenderId = log.RawSenderId ?? log.SenderId ?? string.Empty;
            string displaySenderId = log.SenderId ?? log.RawSenderId ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text) || lookupSenderId.Length == 0 || displaySenderId.Length == 0)
                return text;
            if (!_settings.ShowEtaLevel && !_settings.ShowEtaCharacter)
                return text;
            if (!EtaProfileResolver.TryGetProfile(lookupSenderId, out var profile))
                return text;

            string suffix = string.Empty;
            if (_settings.ShowEtaLevel)
                suffix += $"[{profile.Level}]";
            if (_settings.ShowEtaCharacter && !string.IsNullOrWhiteSpace(profile.CharacterName))
                suffix += $"[{profile.CharacterName}]";
            if (string.IsNullOrEmpty(suffix))
                return text;

            if (log.Category == ChatCategory.Shout)
            {
                return Regex.Replace(
                    text,
                    $@"\[{Regex.Escape(displaySenderId)}\]\s*$",
                    $"[{displaySenderId}{suffix}]");
            }

            if (!TrySplitTimestampAndBody(text, out string body))
                return text;

            int colon = body.IndexOf(':');
            if (colon <= 0) return text;
            string left = body.Substring(0, colon);
            int idx = left.LastIndexOf(displaySenderId, StringComparison.Ordinal);
            if (idx < 0) return text;
            int bodySenderIndex = text.IndexOf(left, StringComparison.Ordinal);
            if (bodySenderIndex < 0) return text;

            int insertIndex = bodySenderIndex + idx + displaySenderId.Length;
            return text.Substring(0, insertIndex) + suffix + text.Substring(insertIndex);
        }

        private static bool TrySplitTimestampAndBody(string text, out string body)
        {
            body = string.Empty;
            int closingBracketIndex = text.IndexOf(']');
            if (closingBracketIndex < 0 || closingBracketIndex + 1 >= text.Length)
                return false;

            body = text[(closingBracketIndex + 1)..].TrimStart();
            return body.Length > 0;
        }

        private void ScrollLogDisplayToEndAfterLayout()
        {
            var logDisplay = LogDisplay;
            if (logDisplay == null) return;

            logDisplay.ScrollToEnd();

            Dispatcher.BeginInvoke(new Action(() =>
            {
                var refreshedLogDisplay = LogDisplay;
                if (refreshedLogDisplay == null) return;

                refreshedLogDisplay.UpdateLayout();
                refreshedLogDisplay.ScrollToEnd();
            }), DispatcherPriority.ContextIdle);
        }

        private void RequestRefreshLogDisplay()
        {
            if (LogDisplay == null || _isRefreshLogDisplayScheduled || !_isLogServiceInitialized) return;

            _isRefreshLogDisplayScheduled = true;

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
                        if (ShouldSuppressOverlayText(log))
                            continue;
                        AddToUI(log, isRealTime: false, deferScroll: true);
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
            }), DispatcherPriority.Render);
        }

        private static void AddMoneyLine(RichTextBox box, string label, string amountText, FontFamily family, double size, Brush brush)
        {
            var p = new Paragraph
            {
                Foreground = brush,
                FontFamily = family,
                FontSize = size,
                Margin = new Thickness(0, 0, 0, 2)
            };
            p.Inlines.Add(new Run(label));
            p.Inlines.Add(BuildIconInline(SeedIconUri, 16));
            p.Inlines.Add(new Run($" {amountText}"));
            box.Document.Blocks.Add(p);
        }

        private static void AddStoneIconLine(RichTextBox box, FontFamily family, double size, AbandonSummaryValue summary)
        {
            var p = new Paragraph
            {
                Foreground = Brushes.LightGray,
                FontFamily = family,
                FontSize = size,
                Margin = new Thickness(0, 0, 0, 2)
            };

            p.Inlines.Add(BuildIconInline(LowMagicStoneIconUri, 16));
            p.Inlines.Add(new Run($" {FormatSignedCount(summary.Low)}  "));
            p.Inlines.Add(BuildIconInline(MiddleMagicStoneIconUri, 16));
            p.Inlines.Add(new Run($" {FormatSignedCount(summary.Mid)}  "));
            p.Inlines.Add(BuildIconInline(HighMagicStoneIconUri, 16));
            p.Inlines.Add(new Run($" {FormatSignedCount(summary.High)}  "));
            p.Inlines.Add(BuildIconInline(TopMagicStoneIconUri, 16));
            p.Inlines.Add(new Run($" {FormatSignedCount(summary.Top)}"));

            box.Document.Blocks.Add(p);
        }

        private static InlineUIContainer BuildIconInline(string uri, double size)
        {
            var img = new Image
            {
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                Source = new BitmapImage(new Uri(uri, UriKind.Absolute))
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            return new InlineUIContainer(img) { BaselineAlignment = BaselineAlignment.Center };
        }

        private static string FormatSignedCount(long count)
            => AbandonSummaryCalculator.FormatSignedCount(count);

        private void EnsureAbandonWeeklySummaryCurrent(DateTime date)
        {
            string weekKey = GetIsoWeekKey(date);
            if (string.Equals(_AbandonWeeklySummaryWeekKey, weekKey, StringComparison.Ordinal))
                return;

            _AbandonWeeklySummary = _readableLogArchiveService.LoadAbandonWeeklySummary(date);
            _AbandonWeeklySummaryWeekKey = weekKey;
        }

        private static string GetIsoWeekKey(DateTime date)
        {
            int isoYear = ISOWeek.GetYear(date);
            int isoWeek = ISOWeek.GetWeekOfYear(date);
            return $"{isoYear}-W{isoWeek:00}";
        }

        #endregion
    }
}
