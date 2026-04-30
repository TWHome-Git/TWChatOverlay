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
                    AppendNewLogs(item.Html, item.IsRealTime, deferUiScroll: true);
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

        private void AppendNewLogs(string html, bool isRealTime, bool deferUiScroll = false)
        {
            if (string.IsNullOrWhiteSpace(html)) return;

            _dungeonCountDisplayService.ProcessRaw(html, isRealTime);
            bool handledDailyWeeklyCountLog = isRealTime &&
                                             _dailyWeeklyContentOverlay?.IsVisible == true &&
                                             _dailyWeeklyContentOverlay.TryProcessAbaddonOrCravingLog(html);

            var pipelineAnalysis = _logPipelineCoordinator.Analyze(html, isRealTime);
            var analysis = pipelineAnalysis.Primary;
            if (!analysis.IsSuccess) return;
            var parseResult = analysis.Parsed;

            _buffTrackerService.ProcessLog(analysis);

            if (analysis.HasExperienceGain) _expService.AddExp(parseResult.GainedExp);
            if (isRealTime)
                _experienceEssenceAlertService.Process(analysis);

            if (!handledDailyWeeklyCountLog &&
                analysis.ShouldRunDailyWeeklyContent &&
                _dailyWeeklyContentOverlay != null)
                _dailyWeeklyContentOverlay.ProcessLog(analysis);

            foreach (string tabName in analysis.BufferTabs)
                AddToBuffer(tabName, parseResult);

            if (isRealTime)
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
                    if (_settings.AutoCopyShoutNickname)
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

                    if (_settings.ShowShoutToastPopup)
                        ShoutToastService.Show(parseResult.FormattedText, _settings);
                }
            }

            if ((pipelineAnalysis.DefaultItemDrop?.HasTrackedItemDrop ?? analysis.HasTrackedItemDrop) &&
                (pipelineAnalysis.DefaultItemDrop?.Parsed is { } itemDropParseResult))
            {
                ItemMonthlySnapshotService.AppendMonthlySnapshot(DateTime.Today, itemDropParseResult);
            }
            else if (analysis.HasTrackedItemDrop)
            {
                ItemMonthlySnapshotService.AppendMonthlySnapshot(DateTime.Today, parseResult);
            }

            if (isRealTime)
                _itemCalendarWindow?.ApplyRealtimeItemLog(parseResult, DateTime.Today);

            if (isRealTime)
            {
                RecaptureSupplyAlertService.Observe(parseResult.FormattedText);
            }

            if (isRealTime &&
                analysis.ShouldRunDailyWeeklyContent &&
                DailyWeeklyLogAnalyzer.TryMatchAbaddonRoadCount(parseResult.FormattedText, out _))
            {
                if (_settings.ShowAbaddonRoadSummaryWindow)
                    ShowAbaddonRoadSummaryWindow(previewMode: _isSettingsPositionMode);
            }

            if (isRealTime)
                RefreshAbaddonRoadSummaryWindow();

            if (isRealTime)
            {
                if (_logAnalysisService.ShouldRenderToTab(parseResult, _currentTabTag))
                {
                    AddToUI(parseResult, isRealTime: isRealTime, deferScroll: deferUiScroll);
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

            Paragraph p = new Paragraph(new Run(displayText))
            {
                Foreground = foreground,
                FontSize = _settings.FontSize,
                FontFamily = this.CurrentFont,
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

        private static void AddStoneIconLine(RichTextBox box, FontFamily family, double size, AbaddonSummaryValue summary)
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

        private string GetAbaddonWeekHeaderText()
        {
            DateTime pivot = _loadedItemMonthStart != DateTime.MinValue
                ? _loadedItemMonthStart
                : DateTime.Today;

            return $"< {pivot.Year}년 {pivot.Month}월 어밴던로드 수익 로그 >";
        }

        private string GetItemWeekHeaderText()
        {
            DateTime pivot = _loadedItemMonthStart != DateTime.MinValue
                ? _loadedItemMonthStart
                : DateTime.Today;

            return $"< {pivot.Year}년 {pivot.Month}월 아이템 획득 로그 >";
        }

        private async Task EnsureItemMonthLogsLoadedAsync()
        {
            if (_isItemMonthLogsLoading) return;

            DateTime monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            if (_loadedItemMonthStart == monthStart && _logTabBufferStore.GetLogs("Item").Count > 0)
                return;

            _isItemMonthLogsLoading = true;
            try
            {
                var monthlySnapshotLogs = await Task.Run(() => LoadItemTabLogsFromMonthlySnapshot(monthStart)).ConfigureAwait(true);
                if (monthlySnapshotLogs.Count > 0)
                {
                    _logTabBufferStore.Replace("Item", monthlySnapshotLogs);
                    _loadedItemMonthStart = monthStart;
                    AppLogger.Info($"Loaded monthly item logs from snapshot. Count={monthlySnapshotLogs.Count}");
                }
                else
                {
                    AppLogger.Info("No monthly item snapshot found yet.");
                }

                if (_currentTabTag == "Item")
                    RequestRefreshLogDisplay();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load monthly item logs.", ex);
            }
            finally
            {
                _isItemMonthLogsLoading = false;
            }
        }

        private List<LogParser.ParseResult> LoadItemTabLogsFromMonthlySnapshot(DateTime monthStart)
        {
            var results = new List<LogParser.ParseResult>();
            foreach (var snapshot in ItemMonthlySnapshotService.LoadOrBuildMonthlySnapshots(monthStart, _settings.ChatLogFolderPath, _logAnalysisService))
            {
                string displayName = string.IsNullOrWhiteSpace(snapshot.DisplayName)
                    ? snapshot.ItemName ?? "아이템"
                    : snapshot.DisplayName;

                string dateLabel = snapshot.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var parsed = new LogParser.ParseResult
                {
                    FormattedText = BuildItemTabText(snapshot.FormattedText ?? string.Empty, displayName, snapshot.Count, dateLabel),
                    Brush = GetItemDropForeground(snapshot.Grade) as SolidColorBrush ?? Brushes.White,
                    Category = ChatCategory.System,
                    IsSuccess = true,
                    IsTrackedItemDrop = true,
                    TrackedItemName = displayName,
                    TrackedItemGrade = snapshot.Grade,
                    TrackedItemCount = snapshot.Count,
                    SenderId = null
                };

                results.Add(parsed);
            }

            return results;
        }

        private (List<LogParser.ParseResult> Logs, AbaddonSummaryValue Summary) ParseTrackedItemLogs(IEnumerable<string> filePaths)
        {
            var results = new List<LogParser.ParseResult>();
            var summary = new AbaddonSummaryValue();

            foreach (var filePath in filePaths)
            {
                if (!File.Exists(filePath)) continue;
                string dateLabel = ExtractDateLabelFromLogPath(filePath);

                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.GetEncoding(949));
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var analysis = _logAnalysisService.Analyze(line, isRealTime: false);
                    var parsed = analysis.Parsed;
                    if (analysis.IsSuccess)
                        TryAccumulateAbaddonWeeklySummary(parsed.FormattedText, ref summary);

                    if (analysis.IsSuccess && analysis.IsRareTrackedItemDrop)
                    {
                        parsed.FormattedText = ReplaceLeadingTimeWithDate(parsed.FormattedText, dateLabel);
                        if (parsed.Brush != null && !parsed.Brush.IsFrozen && parsed.Brush.CanFreeze)
                            parsed.Brush.Freeze();
                        results.Add(CreateItemTabLog(parsed, dateLabel));
                    }
                }
            }

            return (results, summary);
        }

        private static string ExtractDateLabelFromLogPath(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = Regex.Match(fileName, @"(?<y>\d{4})_(?<m>\d{2})_(?<d>\d{2})$");
            if (!match.Success) return DateTime.Today.ToString("yyyy-MM-dd");

            return $"{match.Groups["y"].Value}-{match.Groups["m"].Value}-{match.Groups["d"].Value}";
        }

        private static string ReplaceLeadingTimeWithDate(string original, string dateLabel)
            => ItemLogDisplayFormatter.ReplaceLeadingTimeWithDate(original, dateLabel);

        private static bool TryAccumulateAbaddonWeeklySummary(string formattedText, ref AbaddonSummaryValue summary)
            => AbaddonSummaryCalculator.TryAccumulate(formattedText, ref summary);

        private static bool TryAccumulateAbaddonMonthlySummary(string formattedText, AbaddonMonthlySummarySnapshotEntry summary)
            => AbaddonSummaryCalculator.TryAccumulate(formattedText, summary);

        private static bool TryParseLong(string raw, out long value)
            => long.TryParse(raw.Replace(",", string.Empty).Trim(), out value);

        private static DateTime GetMonday(DateTime date)
        {
            int diff = (7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return date.AddDays(-diff).Date;
        }

        private static string GetMonthlyItemLogPath(DateTime date)
            => Path.Combine(ItemLogDirectoryPath, $"ItemLog_{date:yyyy_MM}.jsonl");

        private void SaveMonthlyItemLogsSnapshot(DateTime monthStart, IReadOnlyList<LogParser.ParseResult> itemLogs)
        {
            try
            {
                Directory.CreateDirectory(ItemLogDirectoryPath);
                string path = GetMonthlyItemLogPath(monthStart);
                using var writer = new StreamWriter(path, append: false, Encoding.UTF8);
                foreach (var itemLog in itemLogs)
                {
                    var snapshot = CreateSnapshotFromItemLog(itemLog, ExtractSnapshotDate(itemLog.FormattedText) ?? monthStart.Date);
                    writer.WriteLine(JsonSerializer.Serialize(snapshot));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save monthly item log snapshot.", ex);
            }
        }

        private void AppendMonthlyItemSnapshot(LogParser.ParseResult itemLog, DateTime date)
            => ItemMonthlySnapshotService.AppendMonthlySnapshot(date, itemLog);

        private IReadOnlyList<ItemLogSnapshotEntry> LoadMonthlyItemSnapshots(DateTime monthStart)
        {
            string path = GetMonthlyItemLogPath(monthStart);
            if (!File.Exists(path))
                return Array.Empty<ItemLogSnapshotEntry>();

            var snapshots = new List<ItemLogSnapshotEntry>();
            try
            {
                foreach (string line in File.ReadLines(path, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var snapshot = JsonSerializer.Deserialize<ItemLogSnapshotEntry>(line);
                    if (snapshot == null)
                        continue;

                    if (snapshot.Date.Year != monthStart.Year || snapshot.Date.Month != monthStart.Month)
                        continue;

                    snapshots.Add(snapshot);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to load monthly item snapshots from '{path}'.", ex);
            }

            return snapshots;
        }

        private static ItemLogSnapshotEntry CreateSnapshotFromItemLog(LogParser.ParseResult itemLog, DateTime date)
        {
            return new ItemLogSnapshotEntry
            {
                Date = date.Date,
                ItemName = itemLog.TrackedItemName,
                DisplayName = string.IsNullOrWhiteSpace(itemLog.TrackedItemName)
                    ? "아이템"
                    : DropItemResolver.GetTrackedItemDisplayName(itemLog.TrackedItemName),
                Grade = itemLog.TrackedItemGrade,
                Count = Math.Max(1, itemLog.TrackedItemCount),
                FormattedText = itemLog.FormattedText
            };
        }

        private static DateTime? ExtractSnapshotDate(string? formattedText)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
                return null;

            var match = Regex.Match(formattedText, @"^\[(?<date>\d{4}-\d{2}-\d{2})\]");
            if (!match.Success)
                return null;

            if (DateTime.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out DateTime parsed))
            {
                return parsed.Date;
            }

            return null;
        }

        private void EnsureCurrentAbaddonWeeklySummaryLoaded(DateTime date)
        {
            DateTime monthStart = new(date.Year, date.Month, 1);
            _loadedAbaddonMonthStart = monthStart;
            _abaddonWeeklySummary = LoadCurrentAbaddonWeeklySummary(monthStart);
        }

        private AbaddonSummaryValue LoadCurrentAbaddonWeeklySummary(DateTime monthStart)
        {
            MonthlyReadableLogData data;
            DateTime today = DateTime.Today;
            if (monthStart.Year == today.Year && monthStart.Month == today.Month)
            {
                data = MonthlyReadableLogExportService.RefreshCurrentMonthFromTodayOnly(
                    monthStart,
                    _settings.ChatLogFolderPath,
                    _logAnalysisService);
            }
            else
            {
                data = MonthlyReadableLogExportService.LoadOrBuildMonth(
                    monthStart,
                    _settings.ChatLogFolderPath,
                    _logAnalysisService);
            }

            return AbaddonSummaryCalculator.FromMonthly(data.AbaddonSummary);
        }

        private IEnumerable<DateTime> EnumerateChatLogMonthStarts()
        {
            if (string.IsNullOrWhiteSpace(_settings.ChatLogFolderPath) ||
                !Directory.Exists(_settings.ChatLogFolderPath))
            {
                yield break;
            }

            foreach (string path in Directory.EnumerateFiles(_settings.ChatLogFolderPath, "TWChatLog_*.html")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                DateTime logDate = ExtractDateFromChatLogPath(path);
                if (logDate == DateTime.MinValue)
                    continue;

                yield return new DateTime(logDate.Year, logDate.Month, 1);
            }
        }

        private void StartHistoricalItemWarmup()
        {
            if (_isHistoricalItemWarmupRunning)
                return;

            _isHistoricalItemWarmupRunning = true;
            Task.Run(() =>
            {
                try
                {
                    BuildHistoricalMonthlyItemSnapshots();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to build historical monthly item snapshots.", ex);
                }
                finally
                {
                    _isHistoricalItemWarmupRunning = false;
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            if (_currentTabTag == "Item")
                                _ = EnsureItemMonthLogsLoadedAsync();
                        }), DispatcherPriority.Background);
                    }
                    catch { }
                }
            });
        }

        private void StartHistoricalAbaddonWarmup()
        {
            if (_isHistoricalAbaddonWarmupRunning)
                return;

            _isHistoricalAbaddonWarmupRunning = true;
            Task.Run(() =>
            {
                try
                {
                    BuildHistoricalMonthlyAbaddonSummaries();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Failed to build historical monthly Abaddon summaries.", ex);
                }
                finally
                {
                    _isHistoricalAbaddonWarmupRunning = false;
                    try
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            EnsureCurrentAbaddonWeeklySummaryLoaded(DateTime.Today);
                            RefreshAbaddonRoadSummaryWindow();
                        }), DispatcherPriority.Background);
                    }
                    catch { }
                }
            });
        }

        private void BuildHistoricalMonthlyAbaddonSummaries()
        {
            if (string.IsNullOrWhiteSpace(_settings.ChatLogFolderPath) ||
                !Directory.Exists(_settings.ChatLogFolderPath))
                return;

            Directory.CreateDirectory(ItemLogDirectoryPath);
            foreach (DateTime monthStart in EnumerateChatLogMonthStarts())
            {
                try
                {
                    LoadCurrentAbaddonWeeklySummary(monthStart);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to warm up Abaddon summary for '{monthStart:yyyy-MM}'.", ex);
                }
            }
        }

        private void BuildHistoricalMonthlyItemSnapshots()
        {
            if (string.IsNullOrWhiteSpace(_settings.ChatLogFolderPath) ||
                !Directory.Exists(_settings.ChatLogFolderPath))
                return;

            Directory.CreateDirectory(ItemLogDirectoryPath);
            var monthStarts = Directory.EnumerateFiles(_settings.ChatLogFolderPath, "TWChatLog_*.html")
                .Select(ExtractDateFromChatLogPath)
                .Where(date => date != DateTime.MinValue)
                .Select(date => new DateTime(date.Year, date.Month, 1))
                .Distinct()
                .OrderBy(date => date)
                .ToList();

            DateTime currentMonthStart = new(DateTime.Today.Year, DateTime.Today.Month, 1);
            foreach (DateTime monthStart in monthStarts)
            {
                try
                {
                    if (monthStart == currentMonthStart)
                    {
                        ItemMonthlySnapshotService.RefreshCurrentMonthIncremental(monthStart, _settings.ChatLogFolderPath, _logAnalysisService);
                    }
                    else
                    {
                        ItemMonthlySnapshotService.LoadOrBuildMonthlySnapshots(monthStart, _settings.ChatLogFolderPath, _logAnalysisService);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to warm up item snapshot for '{monthStart:yyyy-MM}'.", ex);
                }
            }

        }

        private void QueuePendingMonthlyItemSnapshot(LogParser.ParseResult itemLog, DateTime date)
        {
            if (itemLog == null || string.IsNullOrWhiteSpace(itemLog.FormattedText))
                return;

            var snapshot = new ItemLogSnapshotEntry
            {
                Date = date.Date,
                ItemName = itemLog.TrackedItemName,
                DisplayName = string.IsNullOrWhiteSpace(itemLog.TrackedItemName)
                    ? "Item"
                    : DropItemResolver.GetTrackedItemDisplayName(itemLog.TrackedItemName),
                Grade = itemLog.TrackedItemGrade,
                Count = Math.Max(1, itemLog.TrackedItemCount),
                FormattedText = itemLog.FormattedText
            };

            lock (_pendingMonthlyItemSnapshotsLock)
            {
                _pendingMonthlyItemSnapshots.Add(snapshot);
            }
        }

        private void FlushPendingMonthlyItemSnapshots()
        {
            List<ItemLogSnapshotEntry> pendingSnapshots;
            lock (_pendingMonthlyItemSnapshotsLock)
            {
                if (_pendingMonthlyItemSnapshots.Count == 0)
                    return;

                pendingSnapshots = new List<ItemLogSnapshotEntry>(_pendingMonthlyItemSnapshots);
                _pendingMonthlyItemSnapshots.Clear();
            }

            var snapshotsByMonth = pendingSnapshots
                .GroupBy(snapshot => new DateTime(snapshot.Date.Year, snapshot.Date.Month, 1))
                .ToList();

            foreach (var monthGroup in snapshotsByMonth)
            {
                ItemMonthlySnapshotService.AppendMonthlySnapshots(monthGroup.Key, monthGroup.ToList());
            }
        }

        private static DateTime ExtractDateFromChatLogPath(string filePath)
        {
            string fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = Regex.Match(fileName, @"(?<y>\d{4})_(?<m>\d{2})_(?<d>\d{2})$");
            if (!match.Success)
                return DateTime.MinValue;

            if (!int.TryParse(match.Groups["y"].Value, out int year) ||
                !int.TryParse(match.Groups["m"].Value, out int month) ||
                !int.TryParse(match.Groups["d"].Value, out int day))
                return DateTime.MinValue;

            return new DateTime(year, month, day);
        }

        private static LogParser.ParseResult CreateItemTabLog(LogParser.ParseResult source, string? dateLabel = null)
        {
            return new LogParser.ParseResult
            {
                FormattedText = BuildItemTabText(source.FormattedText, source.TrackedItemName, source.TrackedItemCount, dateLabel),
                Brush = source.Brush,
                Category = source.Category,
                IsSuccess = source.IsSuccess,
                IsTrackedItemDrop = source.IsTrackedItemDrop,
                TrackedItemName = source.TrackedItemName,
                TrackedItemGrade = source.TrackedItemGrade,
                TrackedItemCount = source.TrackedItemCount,
                SenderId = source.SenderId
            };
        }

        private static string BuildItemTabText(string formattedText, string? itemName, int itemCount, string? dateLabel)
            => ItemLogDisplayFormatter.BuildItemTabText(formattedText, itemName, itemCount, dateLabel);

        private static Brush GetItemDropForeground(ItemDropGrade grade)
            => ItemLogDisplayFormatter.GetItemDropForeground(grade);

        private static string FormatSignedCount(long count)
            => AbaddonSummaryCalculator.FormatSignedCount(count);

        private static string FormatManAmount(long totalMan)
            => AbaddonSummaryCalculator.FormatManAmount(totalMan);

        #endregion
    }
}
