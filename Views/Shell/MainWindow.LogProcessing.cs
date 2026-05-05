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
        private static readonly Regex ShoutToastSourceRegex = new(
            @"^\s*\[\s*(?:\d{1,2}:\d{2}(?::\d{2})?|\d{1,2}\s*시\s*\d{1,2}\s*분(?:\s*\d{1,2}\s*초)?)\s*\]\s*외치기\s*:",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly string[] DailyWeeklyContentArchiveKeywords =
        {
            "금일 [루미너스] 보스 토벌에 성공하였습니다.",
            "로카고스의 보관 주머니",
            "에토스의 보관 주머니",
            "체리아의 보관 주머니",
            "마티아의 보관 주머니",
            "티로로스의 보관 주머니",
            "라이코스의 보관 주머니",
            "이클립스 보스 토벌전 보상 상자",
            "보급품 탈환에 성공하였",
            "모든 훈련을 클리어했",
            "별동대 토벌 보상으로 경험치",
            "혼란한 대지 미션에 성공하여",
            "색을 잃은 땅 미션에 성공하여",
            "코어 던전 몬스터를 모두 퇴치하여",
            "모든 일반지역을 토벌하여",
            "오늘 무료 클리어 횟수 : 1/1 회",
            "숨겨진 구역으로 이동할 수 있는 포탈이 맵 중앙",
            "차원의 틈 봉인에 성공하였",
            "심연의 보물창고 밖으로 이동 됩니다",
            "청소 아르바이트 보상 조건을 달성하였습니다.",
            "프라바 방어전 성공 보상으로 경험치 1000만을 획득",
            "이번 주 사명의 계승자 닉스 보상을",
            "이번 주 신조 보상을",
            "[키시니크의 보관 주머니]",
            "[아페티리아 어려움 보상 상자]",
            "시오칸하임 - 보스 토벌전의 클리어 횟수 :",
            "시오칸하임 - 오딘 전면전의 클리어 횟수 :",
            "이번 주 어밴던로드 필멸의 땅 지역의 도전 횟수는",
            "이번 주 어밴던로드 카디프 지역의 도전 횟수는",
            "이번 주 어밴던로드 오를란느 지역의 도전 횟수는",
            "어비스 - 심층Ⅰ(보스전) 플레이를 이번 주에 7회 중",
            "어비스 - 심층Ⅱ(보스전) 플레이를 이번 주에 7회 중",
            "어비스 - 심층Ⅲ(보스전) 플레이를 이번 주에 7회 중",
            "남은 에너지는",
            "경험치가 100억이 차감되고, 경험의 정수 1개를 획득 하였습니다."
        };

        #region Log Processing

        private void ProcessUiLogBatch(IReadOnlyList<(string Html, bool IsRealTime)> batch)
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
                    var context = CreateLogPipelineContext(item.Html, item.IsRealTime, deferUiScroll: true);
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

        private UnifiedLogPipelineContext CreateLogPipelineContext(string html, bool isRealTime, bool deferUiScroll)
        {
            var context = new UnifiedLogPipelineContext(
                html,
                isRealTime,
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

            _buffTrackerService.ProcessLog(analysis);

            if (analysis.HasExperienceGain) _expService.AddExp(parseResult.GainedExp);

            if (context.IsRealTime)
                _experienceEssenceAlertService.Process(analysis);

            if (!context.HandledDailyWeeklyCountLog &&
                analysis.ShouldRunDailyWeeklyContent &&
                _dailyWeeklyContentOverlay != null)
                _dailyWeeklyContentOverlay.ProcessLog(analysis);

            foreach (string tabName in analysis.BufferTabs)
                AddToBuffer(tabName, parseResult);

            if (context.IsRealTime)
            {
                if (pipelineAnalysis.Toast is { HasTrackedItemDrop: true, ShouldShowItemDropToast: true } toastAnalysis)
                {
                    ItemDropToastService.Show(
                        toastAnalysis.Parsed.TrackedItemName ?? "아이템",
                        toastAnalysis.Parsed.TrackedItemGrade,
                        withSound: true);
                }

                if (parseResult.Category == ChatCategory.Shout)
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

                    if (_settings.ShowShoutToastPopup && isActualShout && IsTalesWeaverWindowActive())
                        ShoutToastService.Show(parseResult.FormattedText, _settings);
                }
            }

            if (context.IsRealTime)
            {
                _readableLogArchiveService.AppendFromAnalysis(DateTime.Today, analysis, isContentCompletionRelevant);
            }

            if (context.IsRealTime &&
                (pipelineAnalysis.DefaultItemDrop?.HasTrackedItemDrop ?? analysis.HasTrackedItemDrop))
            {
                var realtimeItemParseResult = pipelineAnalysis.DefaultItemDrop?.Parsed ?? parseResult;
                _itemCalendarWindow?.ApplyRealtimeItemLog(realtimeItemParseResult, DateTime.Today);
            }

            if (context.IsRealTime)
            {
                RecaptureSupplyAlertService.Observe(parseResult.FormattedText);
                if (IsExperienceEssenceExchangeLog(parseResult.FormattedText))
                    _itemCalendarWindow?.ApplyRealtimeExperienceEssenceLog(parseResult.FormattedText, DateTime.Today);
            }

            if (context.IsRealTime && analysis.ShouldRunDailyWeeklyContent)
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

            if (context.IsRealTime)
            {
                if (_logAnalysisService.ShouldRenderToTab(parseResult, _currentTabTag))
                {
                    if (!analysis.ShouldShowEtosDirection && !ShouldSuppressOverlayText(parseResult))
                        AddToUI(parseResult, isRealTime: context.IsRealTime, deferScroll: context.DeferUiScroll);
                }
            }

            if (analysis.ShouldShowEtosDirection)
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

        private static bool IsTalesWeaverWindowActive()
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
                return false;

            int len = NativeMethods.GetWindowTextLength(hwnd);
            if (len <= 0)
                return false;

            var sb = new StringBuilder(len + 1);
            _ = NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            string title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title))
                return false;

            return title.Contains("TalesWeaver", StringComparison.OrdinalIgnoreCase) ||
                   title.Contains("테일즈위버", StringComparison.Ordinal);
        }

        private bool ShouldSuppressOverlayText(LogParser.ParseResult parseResult)
        {
            if (parseResult == null || string.IsNullOrWhiteSpace(parseResult.FormattedText))
                return false;

            // 에토스 방향 알림 트리거 문구는 방향 오버레이만 표시하고 일반 채팅 오버레이에는 노출하지 않음.
            if (!string.IsNullOrWhiteSpace(parseResult.EtosImagePath))
                return true;

            string text = parseResult.FormattedText;
            if ((parseResult.Category == ChatCategory.Normal ||
                 parseResult.Category == ChatCategory.NormalSelf ||
                 parseResult.Category == ChatCategory.Team) &&
                text.Contains("에토스", StringComparison.Ordinal))
                return true;

            if (parseResult.Category == ChatCategory.Club &&
                !_settings.ShowClubBoss &&
                IgnoredChatMessageService.IsIgnoredClubMessage(text))
            {
                return true;
            }

            if (parseResult.Category == ChatCategory.Normal || parseResult.Category == ChatCategory.NormalSelf)
            {
                return IgnoredChatMessageService.IsIgnoredNormalMessage(text);
            }

            return false;
        }

        private static bool IsContentCompletionRelevantLog(string? formattedText)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
                return false;

            string text = formattedText;

            if (IsConfusedOrColorlessElsoRewardLine(text))
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

            if (IsExperienceEssenceExchangeLog(text))
                return true;

            if (IsMercurialSeedCompletionLog(text))
                return true;

            return DailyWeeklyLogAnalyzer.IsMercurialEntryMessage(text) ||
                   DailyWeeklyLogAnalyzer.IsMercurialRewardExpMessage(text) ||
                   DailyWeeklyLogAnalyzer.IsMercurialRewardSeedMessage(text) ||
                   IsStrictMercurialCompletionLog(text);
        }

        private static bool IsMercurialSeedCompletionLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.Contains("머큐리얼 케이브", StringComparison.Ordinal) &&
                   text.Contains("콘텐츠 클리어 보상으로", StringComparison.Ordinal) &&
                   text.Contains("SEED", StringComparison.Ordinal) &&
                   text.Contains("획득했습니다", StringComparison.Ordinal);
        }

        private static bool IsStrictMercurialCompletionLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return IsMercurialSeedCompletionLog(text) ||
                   text.Contains("머큐리얼 케이브의 [싱글 플레이] 모드로 입장 하였습니다.", StringComparison.Ordinal) ||
                   (text.Contains("머큐리얼 케이브", StringComparison.Ordinal) &&
                    text.Contains("싱글 플레이", StringComparison.Ordinal) &&
                    text.Contains("입장", StringComparison.Ordinal));
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
                        NotificationService.PlayAlert("MagicCircle.wav");
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
            if (LogDisplay == null || _isRefreshLogDisplayScheduled) return;

            _isRefreshLogDisplayScheduled = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isRefreshLogDisplayScheduled = false;

                if (LogDisplay == null)
                    return;

                bool shouldAutoScroll = ChatDisplay?.IsAutoScrollEnabled == true;

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
