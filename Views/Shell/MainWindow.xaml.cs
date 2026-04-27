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
    /// <summary>
    /// 채팅 오버레이 메인 창의 UI/서비스 연동을 담당합니다.
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Fields
        private DailyWeeklyContentWindow? _dailyWeeklyContentOverlay;
        private ItemCalendarWindow? _itemCalendarWindow;
        private AbaddonRoadSummaryWindow? _abaddonRoadSummaryWindow;
        private ExperienceService _expService;
        private HotKeyService? _hotKeyService;
        private WindowStickyService? _stickyService;
        private BossAlarmSchedulerService? _bossAlarmSchedulerService;
        private BuffTrackerService _buffTrackerService;
        private ExperienceEssenceAlertService _experienceEssenceAlertService;
        private DungeonCountDisplayService _dungeonCountDisplayService;
        private ChatSettings _settings;
        private LogService? _logService;
        private LogAnalysisService _logAnalysisService;
        private SettingsViewModel _settingsViewModel;
        private bool _hasCompletedInitialPresentation;
        private bool _canShowAuxiliaryWindows = true;
        private bool _isSettingsPositionMode;

        private bool _isOverlayVisible = true;
        /// <summary>
        /// Public read-only view of overlay visibility for other windows to subscribe to.
        /// </summary>
        public bool IsOverlayVisible => _isOverlayVisible;

        /// <summary>
        /// Raised when overlay visibility changes. The event argument is the new visibility value.
        /// </summary>
        public event EventHandler<bool>? OverlayVisibilityChanged;
        public event EventHandler<bool>? DailyWeeklyVisibilityChanged;
        public event EventHandler<bool>? ItemCalendarVisibilityChanged;
        private string _currentTabTag = "Basic";
        private readonly UiLogBatchDispatcher _uiLogBatchDispatcher;
        private readonly LogTabBufferStore _logTabBufferStore;
        private readonly TabDisplayStateResolver _tabDisplayStateResolver;
        private DateTime _loadedItemMonthStart = DateTime.MinValue;
        private DateTime _loadedAbaddonMonthStart = DateTime.MinValue;
        private bool _isItemMonthLogsLoading;
        private bool _isHistoricalItemWarmupRunning;
        private bool _isHistoricalAbaddonWarmupRunning;
        private bool _isRefreshLogDisplayScheduled;
        private AbaddonWeeklySummary _abaddonWeeklySummary = new();

        private static readonly Regex AbaddonEntryFeeRegex = new(
            @"입장료\s*(?<value>[\d,]+)\s*만\s*Seed",
            RegexOptions.Compiled);
        private static readonly Regex MagicStoneGainRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개",
            RegexOptions.Compiled);
        private static readonly Regex MagicStoneLossRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개를\s*빼앗겼습니다",
            RegexOptions.Compiled);
        private static readonly Regex HtmlFontTagRegex = new(
            @"<font\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private const string SeedIconUri = "pack://application:,,,/Data/images/Item/시드.png";
        private const string LowMagicStoneIconUri = "pack://application:,,,/Data/images/Item/하급마정석.png";
        private const string MiddleMagicStoneIconUri = "pack://application:,,,/Data/images/Item/중급마정석.png";
        private const string HighMagicStoneIconUri = "pack://application:,,,/Data/images/Item/상급마정석.png";
        private const string TopMagicStoneIconUri = "pack://application:,,,/Data/images/Item/최상급마정석.png";
        private static readonly string ItemLogDirectoryPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Itemlog");

        public static readonly DependencyProperty CurrentFontProperty =
            DependencyProperty.Register("CurrentFont", typeof(FontFamily), typeof(MainWindow));

        public FontFamily CurrentFont
        {
            get => (FontFamily)GetValue(CurrentFontProperty);
            set => SetValue(CurrentFontProperty, value);
        }

        private RichTextBox? LogDisplay => ChatDisplay?.LogDisplayControl;
        #endregion

        public MainWindow()
        {
            InitializeComponent();
            Opacity = 0;
            IsHitTestVisible = false;
            Topmost = false;
            _uiLogBatchDispatcher = new UiLogBatchDispatcher(Dispatcher, 60);
            _logTabBufferStore = new LogTabBufferStore(200);
            _tabDisplayStateResolver = new TabDisplayStateResolver();

            _settings = ConfigService.Load();
            ApplyStartupPreset();
            this.DataContext = _settings;
            _logAnalysisService = new LogAnalysisService(_settings);
            _settingsViewModel = new SettingsViewModel(_settings, OnColorsUpdatedFromSettings, ConfirmExit, OnSettingsResetFromSettings, ApplyHotKeys);
            SettingsDisplay.DataContext = _settingsViewModel;
            SettingsDisplay.OnlyChatMode = true;
            SettingsDisplay.SetCompactMode(true);

            _expService = new ExperienceService(_settings);
            _experienceEssenceAlertService = new ExperienceEssenceAlertService(_settings);
            _dungeonCountDisplayService = new DungeonCountDisplayService(_settings);
            _buffTrackerService = new BuffTrackerService(_settings);
            _buffTrackerService.PropertyChanged += BuffTrackerService_PropertyChanged;
            ExpTrackerPanel.DataContext = _expService.SessionState;
            _logService = new LogService(_expService, _settings);
            TryLoadTestDropItemJsonForSession();
            DropItemResolver.InitializeAsync(_settings);
            _logService.OnNewLogRead += (html) =>
            {
                _uiLogBatchDispatcher.Enqueue(html, _expService.IsReady, ProcessUiLogBatch);
            };
            _logService.InitialLogsLoaded += () =>
            {
                Dispatcher.BeginInvoke(new Action(RequestRefreshLogDisplay), DispatcherPriority.ApplicationIdle);
            };
            BlacklistService.BlacklistChanged += () =>
            {
                Dispatcher.BeginInvoke(new Action(RequestRefreshLogDisplay), DispatcherPriority.Background);
            };
            RestoreExperienceEssenceAlertState();
            _logService.Initialize();
            MonthlyReadableLogExportService.CleanupLegacyArtifacts();
            ApplyInitialSettings();
            ApplySubAddonWindowSettings();
            ApplyItemDropHelperWindowSettings();
            ApplyBuffTrackerWindowSettings();
            ApplyBuffTrackerHelperWindowSettings();
            StartHistoricalItemWarmup();
            StartHistoricalAbaddonWarmup();

            Dispatcher.BeginInvoke(new Action(() => InitializeNativeServices()), DispatcherPriority.Loaded);

            this.Deactivated += (s, e) => ReleaseMouseForce();
            this.Activated += (s, e) => ReleaseMouseForce();
            this.Closed += MainWindow_Closed;
            AppLogger.Info("Main window initialized.");
        }

        private void MainWindow_Closed(object? sender, EventArgs e)
        {
            try { ExperienceAlertWindowService.SaveCurrentPosition(_settings); } catch { }
            try { DungeonCountDisplayWindowService.SaveCurrentPosition(_settings); } catch { }
            try { _buffTrackerService.PropertyChanged -= BuffTrackerService_PropertyChanged; } catch { }
            try { BuffTrackerWindow.Instance?.Close(); } catch { }
            try { BuffTrackerHelperWindow.Instance?.Close(); } catch { }
            try { _abaddonRoadSummaryWindow?.Close(); } catch { }
            try { _logService?.Dispose(); } catch { }
            try { _expService?.Stop(); } catch { }
            try { _buffTrackerService?.Dispose(); } catch { }
            try
            {
                if (_stickyService != null)
                {
                    _stickyService.AuxiliaryWindowVisibilityChanged -= StickyService_AuxiliaryWindowVisibilityChanged;
                }
            }
            catch { }
            try { _stickyService?.Stop(); } catch { }
            try { _bossAlarmSchedulerService?.Stop(); } catch { }
            try { _hotKeyService?.Dispose(); } catch { }
        }

        public SettingsViewModel SettingsViewModelInstance => _settingsViewModel;
        public bool IsDailyWeeklyVisible => _dailyWeeklyContentOverlay?.IsVisible == true;
        public bool IsItemCalendarVisible => _itemCalendarWindow?.IsVisible == true;

        private void RestoreExperienceEssenceAlertState()
        {
            try
            {
                var logPaths = GetAllChatLogPaths();
                _experienceEssenceAlertService.RestoreFromRecentLogs(logPaths, _logAnalysisService);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to restore experience essence alert state from recent logs.", ex);
            }
        }

        private IEnumerable<string> GetAllChatLogPaths()
        {
            if (string.IsNullOrWhiteSpace(_settings.ChatLogFolderPath) ||
                !Directory.Exists(_settings.ChatLogFolderPath))
                yield break;

            foreach (string path in Directory.EnumerateFiles(_settings.ChatLogFolderPath, "TWChatLog_*.html")
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }

        private void BuffTrackerService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BuffTrackerService.HasAnyActiveBuffs))
                return;

            Dispatcher.BeginInvoke(new Action(ApplyBuffTrackerWindowSettings), DispatcherPriority.Background);
        }

        private void OnColorsUpdatedFromSettings(string _)
        {
            _logTabBufferStore.UpdateAllBrushes(category => ChatBrushResolver.Resolve(_settings, category));

            RequestRefreshLogDisplay();
        }

        private void OnSettingsResetFromSettings()
        {
            ApplyInitialSettings();
            RequestRefreshLogDisplay();
            try { ApplyHotKeys(); }
            catch (Exception ex) { AppLogger.Warn("Failed to reapply hotkeys after settings reset.", ex); }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var helper = new WindowInteropHelper(this);
            NativeMethods.SetWindowLong(helper.Handle, NativeMethods.GWL_EXSTYLE,
                NativeMethods.GetWindowLong(helper.Handle, NativeMethods.GWL_EXSTYLE) | 0x00000080);
        }

        private void InitializeNativeServices()
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(this).EnsureHandle();

                _hotKeyService = new HotKeyService(handle);
                ApplyHotKeys();

                _hotKeyService.HotKeyPressed += (id) =>
                {
                    if (ShouldSuppressGlobalHotKeys())
                    {
                        AppLogger.Debug($"Suppressed hotkey action id={id} while editing hotkey settings.");
                        return;
                    }

                    AppLogger.Info($"Handling hotkey action id={id}.");
                    switch (id)
                    {
                        case HotKeyService.EXIT_HOTKEY_ID:
                            ConfirmExit();
                            break;
                        case HotKeyService.TOGGLE_OVERLAY_ID:
                            ToggleOverlayVisibility();
                            break;
                        case HotKeyService.TOGGLE_ADDON_ID:
                            TriggerMenuButton("BtnAddon");
                            break;
                        case HotKeyService.TOGGLE_ETA_RANKING_ID:
                            TriggerMenuButton("BtnEtaRanking");
                            break;
                        case HotKeyService.TOGGLE_COEFFICIENT_ID:
                            TriggerMenuButton("BtnCoefficient");
                            break;
                        case HotKeyService.TOGGLE_EQUIPMENTDB_ID:
                            TriggerMenuButton("BtnEquipmentDb");
                            break;
                        case HotKeyService.TOGGLE_ENCRYPT_ID:
                            TriggerMenuButton("BtnEncrypt");
                            break;
                        case HotKeyService.TOGGLE_SETTINGS_ID:
                            TriggerMenuButton("BtnSettings");
                            break;
                        case HotKeyService.TOGGLE_ALWAYS_VISIBLE_ID:
                            _settings.AlwaysVisible = !_settings.AlwaysVisible;
                            AppLogger.Info($"AlwaysVisible toggled to {_settings.AlwaysVisible}.");
                            break;
                        case HotKeyService.TOGGLE_DAILY_WEEKLY_CONTENT_ID:
                            TriggerMenuButton("BtnDailyWeekly");
                            break;
                    }
                };

                _stickyService = new WindowStickyService(this, _settings);
                _stickyService.AuxiliaryWindowVisibilityChanged += StickyService_AuxiliaryWindowVisibilityChanged;
                _stickyService.Start();
                _stickyService.UpdatePositionImmediately();
                _bossAlarmSchedulerService = new BossAlarmSchedulerService(_settings);
                _bossAlarmSchedulerService.Start();
                _expService.Start();
                _logService?.Start();

                _settings.PropertyChanged += OnSettingsPropertyChanged;

                foreach (Window w in Application.Current.Windows)
                {
                    if (w is SubMenuWindow sub)
                    {
                        var settingsView = new SettingsView();
                        settingsView.DataContext = _settingsViewModel;
                        sub.ShowHostContent(settingsView, "설정");
                        sub.Hide();
                        break;
                    }
                }

                if (_settings.ShowDailyWeeklyContentOverlay)
                    ShowDailyWeeklyWindow();

                Dispatcher.BeginInvoke(new Action(CompleteInitialPresentation), DispatcherPriority.ApplicationIdle);

                AppLogger.Info("Native services initialized successfully.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"서비스 시작 중 오류: {ex.Message}");
            }
        }

        private void CompleteInitialPresentation()
        {
            if (_hasCompletedInitialPresentation)
            {
                return;
            }

            _hasCompletedInitialPresentation = true;

            if (_isOverlayVisible)
            {
                _stickyService?.UpdatePositionNow();

                if (!IsVisible)
                {
                    Show();
                }

                Opacity = 1;
                IsHitTestVisible = true;
                Visibility = Visibility.Visible;
                _stickyService?.UpdatePositionImmediately();
            }
        }

        private void StickyService_AuxiliaryWindowVisibilityChanged(bool canShow)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _canShowAuxiliaryWindows = canShow;
                ApplyBuffTrackerWindowSettings();
                ApplyDailyWeeklyWindowVisibility();
            }), DispatcherPriority.Background);
        }

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

            var analysis = _logAnalysisService.Analyze(html, isRealTime);
            if (!analysis.IsSuccess) return;
            var parseResult = analysis.Parsed;

            _buffTrackerService.ProcessLog(analysis);

            if (analysis.HasExperienceGain) _expService.AddExp(parseResult.GainedExp);
            if (isRealTime)
                _experienceEssenceAlertService.Process(analysis);

            if (!handledDailyWeeklyCountLog &&
                analysis.ShouldRunDailyWeeklyContent &&
                _dailyWeeklyContentOverlay?.IsVisible == true)
                _dailyWeeklyContentOverlay.ProcessLog(analysis);

            foreach (string tabName in analysis.BufferTabs)
                AddToBuffer(tabName, parseResult);

            if (analysis.HasTrackedItemDrop)
            {
                ItemMonthlySnapshotService.AppendMonthlySnapshot(DateTime.Today, parseResult);
                if (analysis.ShouldShowItemDropToast)
                {
                    ItemDropToastService.Show(parseResult.TrackedItemName ?? "아이템", parseResult.TrackedItemGrade, withSound: true);
                }
            }

            if (isRealTime)
            {
                RecaptureSupplyAlertService.Observe(parseResult.FormattedText);
            }

            if (isRealTime &&
                analysis.ShouldRunDailyWeeklyContent &&
                DailyWeeklyLogAnalyzer.TryMatchAbaddonRoadCount(parseResult.FormattedText, out _))
            {
                ShowAbaddonRoadSummaryWindow();
            }

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

        private static void AddStoneIconLine(RichTextBox box, FontFamily family, double size, AbaddonWeeklySummary summary)
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

        private (List<LogParser.ParseResult> Logs, AbaddonWeeklySummary Summary) ParseTrackedItemLogs(IEnumerable<string> filePaths)
        {
            var results = new List<LogParser.ParseResult>();
            var summary = new AbaddonWeeklySummary();

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
        {
            if (string.IsNullOrWhiteSpace(original))
                return $"[{dateLabel}]";

            string body = Regex.Replace(original, @"^\[[^\]]+\]\s*", string.Empty);
            return $"[{dateLabel}] {body}";
        }

        private static bool TryAccumulateAbaddonWeeklySummary(string formattedText, ref AbaddonWeeklySummary summary)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
                return false;

            string body = Regex.Replace(formattedText, @"^\[[^\]]+\]\s*", string.Empty);
            if (body.Contains("주문을 통해", StringComparison.Ordinal))
                return false;

            var feeMatch = AbaddonEntryFeeRegex.Match(body);
            if (feeMatch.Success && TryParseLong(feeMatch.Groups["value"].Value, out long feeMan))
            {
                summary.TotalEntryFeeMan += feeMan;
                return true;
            }

            var lossMatch = MagicStoneLossRegex.Match(body);
            if (lossMatch.Success && TryParseLong(lossMatch.Groups["count"].Value, out long lossCount))
            {
                ApplyMagicStoneDelta(ref summary, lossMatch.Groups["grade"].Value, -lossCount);
                return true;
            }

            var gainMatch = MagicStoneGainRegex.Match(body);
            if (gainMatch.Success &&
                body.Contains("획득", StringComparison.Ordinal) &&
                TryParseLong(gainMatch.Groups["count"].Value, out long gainCount))
            {
                ApplyMagicStoneDelta(ref summary, gainMatch.Groups["grade"].Value, gainCount);
                return true;
            }

            return false;
        }

        private static bool TryAccumulateAbaddonMonthlySummary(string formattedText, AbaddonMonthlySummarySnapshotEntry summary)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
                return false;

            string body = Regex.Replace(formattedText, @"^\[[^\]]+\]\s*", string.Empty);
            if (body.Contains("주문을 통해", StringComparison.Ordinal))
                return false;

            var feeMatch = AbaddonEntryFeeRegex.Match(body);
            if (feeMatch.Success && TryParseLong(feeMatch.Groups["value"].Value, out long feeMan))
            {
                summary.TotalEntryFeeMan += feeMan;
                return true;
            }

            var lossMatch = MagicStoneLossRegex.Match(body);
            if (lossMatch.Success && TryParseLong(lossMatch.Groups["count"].Value, out long lossCount))
            {
                ApplyMagicStoneDelta(summary, lossMatch.Groups["grade"].Value, -lossCount);
                return true;
            }

            var gainMatch = MagicStoneGainRegex.Match(body);
            if (gainMatch.Success &&
                body.Contains("획득", StringComparison.Ordinal) &&
                TryParseLong(gainMatch.Groups["count"].Value, out long gainCount))
            {
                ApplyMagicStoneDelta(summary, gainMatch.Groups["grade"].Value, gainCount);
                return true;
            }

            return false;
        }

        private static bool TryParseLong(string raw, out long value)
            => long.TryParse(raw.Replace(",", string.Empty).Trim(), out value);

        private static void ApplyMagicStoneDelta(AbaddonMonthlySummarySnapshotEntry summary, string grade, long delta)
        {
            switch (grade)
            {
                case "하급":
                    summary.Low += delta;
                    break;
                case "중급":
                    summary.Mid += delta;
                    break;
                case "상급":
                    summary.High += delta;
                    break;
                case "최상급":
                    summary.Top += delta;
                    break;
            }
        }

        private static DateTime GetMonday(DateTime date)
        {
            int diff = (7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return date.AddDays(-diff).Date;
        }

        private static string GetMonthlyItemLogPath(DateTime date)
            => Path.Combine(ItemLogDirectoryPath, $"ItemLog_{date:yyyy_MM}.jsonl");

        private static string GetMonthlyAbaddonSummaryPath(DateTime date)
            => Path.Combine(ItemLogDirectoryPath, $"AbaddonSummary_{date:yyyy_MM}.json");

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

        private void EnsureCurrentAbaddonSummaryLoaded(DateTime date)
        {
            DateTime monthStart = new(date.Year, date.Month, 1);
            _loadedAbaddonMonthStart = monthStart;
            _abaddonWeeklySummary = LoadOrBuildMonthlyAbaddonSummary(monthStart);
        }

        private AbaddonWeeklySummary LoadOrBuildMonthlyAbaddonSummary(DateTime monthStart)
        {
            MonthlyReadableLogData data = MonthlyReadableLogExportService.LoadOrBuildMonth(
                monthStart,
                _settings.ChatLogFolderPath,
                _logAnalysisService);

            return new AbaddonWeeklySummary
            {
                TotalEntryFeeMan = data.AbaddonSummary.TotalEntryFeeMan,
                Low = data.AbaddonSummary.Low,
                Mid = data.AbaddonSummary.Mid,
                High = data.AbaddonSummary.High,
                Top = data.AbaddonSummary.Top
            };
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
                            EnsureCurrentAbaddonSummaryLoaded(DateTime.Today);
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
                    LoadOrBuildMonthlyAbaddonSummary(monthStart);
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

            foreach (DateTime monthStart in monthStarts)
            {
                try
                {
                    ItemMonthlySnapshotService.LoadOrBuildMonthlySnapshots(monthStart, _settings.ChatLogFolderPath, _logAnalysisService);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Failed to warm up item snapshot for '{monthStart:yyyy-MM}'.", ex);
                }
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
        {
            string normalizedText = formattedText ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(dateLabel))
            {
                normalizedText = ReplaceLeadingTimeWithDate(normalizedText, dateLabel);
            }

            if (string.IsNullOrWhiteSpace(itemName))
                return normalizedText;

            var prefixMatch = Regex.Match(normalizedText, @"^\[[^\]]+\]");
            if (!prefixMatch.Success)
                return itemCount > 1 ? $"[{itemName}] x{itemCount}" : $"[{itemName}]";

            string suffix = itemCount > 1 ? $" [{itemName}] x{itemCount}" : $" [{itemName}]";
            return $"{prefixMatch.Value}{suffix}";
        }

        private static Brush GetItemDropForeground(ItemDropGrade grade)
        {
            return grade switch
            {
                ItemDropGrade.Rare => new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A)),
                ItemDropGrade.Special => new SolidColorBrush(Color.FromRgb(0xFF, 0x7E, 0xDB)),
                _ => Brushes.White
            };
        }

        private static void ApplyMagicStoneDelta(ref AbaddonWeeklySummary summary, string grade, long delta)
        {
            switch (grade)
            {
                case "하급":
                    summary.Low += delta;
                    break;
                case "중급":
                    summary.Mid += delta;
                    break;
                case "상급":
                    summary.High += delta;
                    break;
                case "최상급":
                    summary.Top += delta;
                    break;
            }
        }

        private static string FormatSignedCount(long count)
            => (count >= 0 ? "+" : string.Empty) + $"{count:N0}";

        private static string FormatManAmount(long totalMan)
        {
            string sign = totalMan < 0 ? "-" : string.Empty;
            long abs = Math.Abs(totalMan);
            long eok = abs / 10000;
            long man = abs % 10000;
            return $"{sign}{eok:N0}억 {man:N0}만";
        }

        private struct AbaddonWeeklySummary
        {
            private const long LowMagicStoneValueMan = 50;
            private const long MidMagicStoneValueMan = 500;
            private const long HighMagicStoneValueMan = 5000;
            private const long TopMagicStoneValueMan = 50000;

            public long TotalEntryFeeMan;
            public long Low;
            public long Mid;
            public long High;
            public long Top;

            public long StoneRevenueMan =>
                (Low * LowMagicStoneValueMan) +
                (Mid * MidMagicStoneValueMan) +
                (High * HighMagicStoneValueMan) +
                (Top * TopMagicStoneValueMan);
            public long NetProfitMan => StoneRevenueMan - TotalEntryFeeMan;
        }

        #endregion

        #region Event Handlers

        private void Tab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton btn || btn.Tag == null) return;

            _currentTabTag = btn.Tag.ToString() ?? string.Empty;
            AppLogger.Debug($"Switched log tab to '{_currentTabTag}'.");

            var displayState = _tabDisplayStateResolver.Resolve(_currentTabTag);
            var logDisplay = LogDisplay;

            if (logDisplay != null)
            {
                logDisplay.Visibility = displayState.IsLogVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            SettingsDisplay.Visibility = displayState.IsSettingsVisible ? Visibility.Visible : Visibility.Collapsed;

            bool isSettingsTab = displayState.IsSettingsTab;
            DragBar.Visibility = isSettingsTab ? Visibility.Visible : Visibility.Collapsed;
            DragBarRow.Height = isSettingsTab ? new GridLength(25) : new GridLength(0);
            SetSettingsPositionMode(isSettingsTab);

            if (_stickyService != null)
            {
                if (isSettingsTab)
                {
                    _stickyService.SetPositionTrackingEnabled(false);
                }
                else
                {
                    _stickyService.SetPositionTrackingEnabled(true);
                    _stickyService.UpdatePositionImmediately();
                }
            }

            if (logDisplay?.Visibility == Visibility.Visible) RequestRefreshLogDisplay();
        }

        private void DragBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                SyncMarginsFromWindowPosition(this.Left, this.Top);
                _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);
                PersistSettings();
            }
        }

        private void ResetExp_Click(object sender, RoutedEventArgs e)
        {
            _expService.Reset();
        }

        private void OnSettingsPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.PropertyName == nameof(_settings.FontFamily) || e.PropertyName == nameof(_settings.FontSize))
                {
                    ApplyInitialSettings();
                    RequestRefreshLogDisplay();
                }
                else if (e.PropertyName == nameof(_settings.LineMargin) || e.PropertyName == nameof(_settings.LineMarginLeft))
                {
                    _stickyService?.UpdatePositionImmediately();
                }
                else if (e.PropertyName == nameof(_settings.AlwaysVisible))
                {
                    _stickyService?.UpdatePositionImmediately();
                }
                else if (e.PropertyName == nameof(_settings.ShowDailyWeeklyContentOverlay))
                {
                    if (_settings.ShowDailyWeeklyContentOverlay)
                        ShowDailyWeeklyWindow();
                    else
                        CloseDailyWeeklyWindow();
                }
                else if (e.PropertyName == nameof(_settings.ShowEtosDirectionAlert) && !_settings.ShowEtosDirectionAlert)
                {
                    SubAddonWindow.Instance?.HideAlert();
                }
                else if (e.PropertyName == nameof(_settings.ShowEtosHelperWindow) && !_settings.ShowEtosHelperWindow)
                {
                    var helper = SubAddonWindow.Instance;
                    if (helper != null)
                    {
                        _settings.SubAddonWindowLeft = helper.Left;
                        _settings.SubAddonWindowTop = helper.Top;
                    }

                    ApplySubAddonWindowSettings();
                    PersistSettings();
                }
                else if (e.PropertyName == nameof(_settings.ShowEtosDirectionAlert) ||
                         e.PropertyName == nameof(_settings.ShowEtosHelperWindow))
                {
                    ApplySubAddonWindowSettings();
                }
                else if (e.PropertyName == nameof(_settings.ShowItemDropHelperWindow))
                {
                    ApplyItemDropHelperWindowSettings();
                }
                else if (e.PropertyName == nameof(_settings.ShowDailyWeeklyContentOverlay))
                {
                    ApplyDailyWeeklyWindowVisibility();
                }
                else if (e.PropertyName == nameof(_settings.ShowBuffTrackerWindow))
                {
                    ApplyBuffTrackerHelperWindowSettings();
                }
                else if (e.PropertyName == nameof(_settings.EnableBuffTrackerAlert))
                {
                    ApplyBuffTrackerWindowSettings();
                }
                else if (e.PropertyName == nameof(_settings.EnableExperienceLimitAlert))
                {
                    if (_settings.EnableExperienceLimitAlert && _settings.ShowExperienceLimitAlertWindow)
                        ExperienceAlertWindowService.ShowPositionPreview(_settings);
                    else if (!_settings.EnableExperienceLimitAlert && !_settings.ShowExperienceLimitAlertWindow)
                        ExperienceAlertWindowService.Close();
                }
                else if (e.PropertyName == nameof(_settings.ShowExperienceLimitAlertWindow))
                {
                    if (_settings.ShowExperienceLimitAlertWindow)
                        ExperienceAlertWindowService.ShowPositionPreview(_settings);
                    else
                        ExperienceAlertWindowService.Close();
                }
                else if (e.PropertyName == nameof(_settings.ShowDungeonCountDisplayWindow))
                {
                    if (_settings.ShowDungeonCountDisplayWindow)
                        DungeonCountDisplayWindowService.ShowPositionPreview(_settings);
                    else
                        DungeonCountDisplayWindowService.ClosePositionPreview(_settings);
                }
                else if (e.PropertyName == nameof(_settings.BuffTrackerWindowLeft) ||
                         e.PropertyName == nameof(_settings.BuffTrackerWindowTop))
                {
                    ApplyBuffTrackerWindowSettings();
                    ApplyBuffTrackerHelperWindowSettings();
                }
                else if (e.PropertyName == nameof(_settings.ExitHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleOverlayHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleAddonHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleAlwaysVisibleHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleDailyWeeklyContentHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleEtaRankingHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleCoefficientHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleEquipmentDbHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleEncryptHotKey) ||
                         e.PropertyName == nameof(_settings.ToggleSettingsHotKey))
                {
                    ApplyHotKeys();
                }
                else if (e.PropertyName != null && e.PropertyName.StartsWith("Show"))
                {
                    RequestRefreshLogDisplay();
                }

                PersistSettings();
            });
        }

        #endregion

        #region UI Methods

        private void ApplyInitialSettings()
        {
            if (_settings == null || MainBorder == null) return;

            this.Width = _settings.WindowWidth;
            this.Height = _settings.WindowHeight;

            FontFamily nextFont = FontService.GetFont(_settings.FontFamily);
            this.CurrentFont = nextFont;

            if (LogDisplay != null)
            {
                LogDisplay.FontFamily = nextFont;
                LogDisplay.FontSize = _settings.FontSize;
            }
            if (SettingsDisplay != null) SettingsDisplay.FontFamily = nextFont;

            foreach (Window window in Application.Current.Windows)
            {
                if (ReferenceEquals(window, this))
                    continue;

                window.FontFamily = nextFont;
            }

            MainBorder.Background = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30));
        }

        public void InjectDebugLogText(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return;

            if (_logService == null)
            {
                AppLogger.Warn("Debug log injection skipped because LogService is not initialized yet.");
                return;
            }

            string payload = BuildDebugLogPayload(rawText);
            _logService.InjectTestContent(payload);
        }

        private static string BuildDebugLogPayload(string rawText)
        {
            if (HtmlFontTagRegex.IsMatch(rawText))
                return rawText;

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            var lines = rawText
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => $@"<font color=""ffffff"">[{timestamp}]</font><font color=""ff64ff"">{System.Net.WebUtility.HtmlEncode(x)}</font>");

            return string.Join("<br>", lines);
        }

        private void ApplyStartupPreset()
        {
            var preset = _settings.GetLastSelectedPreset();
            if (preset == null) return;

            if (preset.HasMarginData)
            {
                _settings.LineMarginLeft = preset.LineMarginLeft;
                _settings.LineMargin = preset.LineMargin;
            }

            _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);
        }

        private void TryLoadTestDropItemJsonForSession()
        {
            try
            {
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string[] candidateNames = ["Droptime.json", "DropItem.json", "DropItem.Json"];

                foreach (string candidateName in candidateNames)
                {
                    string path = Path.Combine(downloads, candidateName);
                    if (!File.Exists(path))
                        continue;

                    string json = File.ReadAllText(path, Encoding.UTF8);
                    if (DropItemResolver.TryApplyJsonForSession(json))
                    {
                        AppLogger.Info($"Loaded session DropItem JSON from '{path}'.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load session DropItem JSON.", ex);
            }
        }

        private void ReleaseMouseForce()
        {
            try
            {
                if (Mouse.Captured != null)
                {
                    Mouse.Capture(null);
                }
            }
            catch { }
        }

        private void ConfirmExit()
        {
            Application.Current.Shutdown();
        }

        public void ToggleOverlayVisibility()
        {
            _isOverlayVisible = !_isOverlayVisible;
            OverlayVisibilityChanged?.Invoke(this, _isOverlayVisible);

            if (_stickyService != null)
            {
                _stickyService.SetForceHidden(!_isOverlayVisible);
                if (_isOverlayVisible)
                {
                    _stickyService.UpdatePositionImmediately();
                }
            }

            if (_isOverlayVisible)
            {
                if (!IsVisible)
                {
                    Show();
                }

                Opacity = 1;
                IsHitTestVisible = true;
                Visibility = Visibility.Visible;
                CompleteInitialPresentation();
            }
            else
            {
                IsHitTestVisible = false;
                Visibility = Visibility.Collapsed;
                Opacity = 0;
            }

            PersistSettings();
        }

        private void ShowDailyWeeklyWindow()
        {
            if (_dailyWeeklyContentOverlay == null || !_dailyWeeklyContentOverlay.IsLoaded)
            {
                _dailyWeeklyContentOverlay = new DailyWeeklyContentWindow(_settings);
                _dailyWeeklyContentOverlay.Closed += (_, _) =>
                {
                    _dailyWeeklyContentOverlay = null;
                    try { DailyWeeklyVisibilityChanged?.Invoke(this, false); } catch { }
                };
            }

            if (!_dailyWeeklyContentOverlay.IsVisible)
            {
                _dailyWeeklyContentOverlay.Owner = this;
                ApplyStoredPosition(_dailyWeeklyContentOverlay, _settings.DailyWeeklyContentOverlayLeft, _settings.DailyWeeklyContentOverlayTop);
                _dailyWeeklyContentOverlay.WindowStartupLocation = WindowStartupLocation.Manual;
                _dailyWeeklyContentOverlay.Show();
            }

            _dailyWeeklyContentOverlay.Activate();
            try { DailyWeeklyVisibilityChanged?.Invoke(this, true); } catch { }
        }

        private void CloseDailyWeeklyWindow()
        {
            try
            {
                _dailyWeeklyContentOverlay?.Close();
            }
            catch { }
            finally
            {
                _dailyWeeklyContentOverlay = null;
                try { DailyWeeklyVisibilityChanged?.Invoke(this, false); } catch { }
            }
        }

        public void ToggleDailyWeeklyContentWindow()
        {
            _settings.ShowDailyWeeklyContentOverlay = !_settings.ShowDailyWeeklyContentOverlay;
            PersistSettings();
        }

        public void ShowItemCalendarWindow()
        {
            if (_itemCalendarWindow == null || !_itemCalendarWindow.IsLoaded)
            {
                _itemCalendarWindow = new ItemCalendarWindow(_settings, _logAnalysisService);
                _itemCalendarWindow.Closed += (_, _) =>
                {
                    _settings.ItemCalendarWindowLeft = _itemCalendarWindow?.Left;
                    _settings.ItemCalendarWindowTop = _itemCalendarWindow?.Top;
                    PersistSettings();
                    _itemCalendarWindow = null;
                    try { ItemCalendarVisibilityChanged?.Invoke(this, false); } catch { }
                };
                AppLogger.Info("Created item calendar window instance.");
            }

            if (!_itemCalendarWindow.IsVisible)
            {
                _itemCalendarWindow.Owner = this;
                ApplyStoredPosition(_itemCalendarWindow, _settings.ItemCalendarWindowLeft, _settings.ItemCalendarWindowTop);
                _itemCalendarWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                _itemCalendarWindow.Show();
            }

            _itemCalendarWindow.Activate();
            _ = _itemCalendarWindow.LoadCurrentMonthAsync();
            try { ItemCalendarVisibilityChanged?.Invoke(this, true); } catch { }
        }

        public void ToggleItemCalendarWindow()
        {
            if (_itemCalendarWindow?.IsVisible == true)
            {
                try
                {
                    _itemCalendarWindow.Close();
                }
                catch { }
                return;
            }

            ShowItemCalendarWindow();
        }

        public void ShowAbaddonRoadSummaryWindow()
        {
            if (_abaddonRoadSummaryWindow == null || !_abaddonRoadSummaryWindow.IsLoaded)
            {
                _abaddonRoadSummaryWindow = new AbaddonRoadSummaryWindow(_settings, _logAnalysisService);
                _abaddonRoadSummaryWindow.Closed += (_, _) =>
                {
                    _settings.AbaddonRoadSummaryWindowLeft = _abaddonRoadSummaryWindow?.Left;
                    _settings.AbaddonRoadSummaryWindowTop = _abaddonRoadSummaryWindow?.Top;
                    PersistSettings();
                    _abaddonRoadSummaryWindow = null;
                };
                AppLogger.Info("Created AbaddonRoad summary window instance.");
            }

            if (!_abaddonRoadSummaryWindow.IsVisible)
            {
                _abaddonRoadSummaryWindow.Owner = this;
                ApplyStoredPosition(_abaddonRoadSummaryWindow, _settings.AbaddonRoadSummaryWindowLeft, _settings.AbaddonRoadSummaryWindowTop);
                _abaddonRoadSummaryWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                _abaddonRoadSummaryWindow.Show();
            }

            _abaddonRoadSummaryWindow.Activate();
            _ = _abaddonRoadSummaryWindow.LoadCurrentWeekAsync();
        }

        public void RefreshAbaddonRoadSummaryWindow()
        {
            if (_abaddonRoadSummaryWindow?.IsVisible != true)
                return;

            _ = _abaddonRoadSummaryWindow.LoadCurrentWeekAsync();
        }
        #endregion

        #region Resizing

        private void TopResize_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newHeight = this.Height - e.VerticalChange;
            if (newHeight > this.MinHeight)
            {
                this.Top += e.VerticalChange;
                this.Height = newHeight;
                _settings.WindowHeight = newHeight;
            }
            SyncMarginsFromWindowPosition(this.Left, this.Top);
            _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);
            PersistSettings();
        }

        private void LeftResize_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = this.Width - e.HorizontalChange;
            if (newWidth > this.MinWidth)
            {
                this.Left += e.HorizontalChange;
                this.Width = newWidth;
                _settings.WindowWidth = newWidth;
            }
            SyncMarginsFromWindowPosition(this.Left, this.Top);
            _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);
            PersistSettings();
        }

        private void RightResize_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = this.Width + e.HorizontalChange;
            if (newWidth > this.MinWidth)
            {
                this.Width = newWidth;
                _settings.WindowWidth = newWidth;
            }
            SyncMarginsFromWindowPosition(this.Left, this.Top);
            _settings.UpdatePositionDisplay(_settings.LineMarginLeft, _settings.LineMargin);
            PersistSettings();
        }

        #endregion

        #region Position Helpers

        private sealed class AbaddonMonthlySourceFileInfo
        {
            public string FullPath { get; set; } = string.Empty;

            public string FileName { get; set; } = string.Empty;

            public long Length { get; set; }

            public DateTime LastWriteTimeUtc { get; set; }
        }

        private sealed class AbaddonMonthlySourceSnapshot
        {
            public string FileName { get; set; } = string.Empty;

            public long Length { get; set; }

            public DateTime LastWriteTimeUtc { get; set; }
        }

        private sealed class AbaddonMonthlySyncState
        {
            public DateTime MonthStart { get; set; }

            public string SourceSignature { get; set; } = string.Empty;

            public long TotalEntryFeeMan { get; set; }

            public long Low { get; set; }

            public long Mid { get; set; }

            public long High { get; set; }

            public long Top { get; set; }

            public DateTime SyncedUtc { get; set; }

            public List<AbaddonMonthlySourceSnapshot> SourceFiles { get; set; } = new();
        }
        #endregion
    }
}

