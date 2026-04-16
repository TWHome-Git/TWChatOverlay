using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
        private string _currentTabTag = "Basic";
        private readonly UiLogBatchDispatcher _uiLogBatchDispatcher;
        private readonly LogTabBufferStore _logTabBufferStore;
        private readonly TabDisplayStateResolver _tabDisplayStateResolver;
        private DateTime _loadedItemWeekMonday = DateTime.MinValue;
        private bool _isItemWeekLogsLoading;
        private bool _isRefreshLogDisplayScheduled;
        private AbaddonWeeklySummary _abaddonWeeklySummary = new();

        private static readonly Regex AbaddonEntryFeeRegex = new(
            @"입장료\s*(?<value>[\d,]+)\s*만\s*Seed",
            RegexOptions.Compiled);
        private static readonly Regex MagicStoneGainRegex = new(
            @"(?<grade>하급|중급|상급|최상급)\s*마정석\s*(?<count>[\d,]+)\s*개",
            RegexOptions.Compiled);
        private static readonly Regex MagicStoneLossRegex = new(
            @"하급\s*마정석\s*(?<count>[\d,]+)\s*개",
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
            DropItemResolver.InitializeAsync();
            _logService.OnNewLogRead += (html) =>
            {
                PerformanceDiagnosticsService.RecordIncomingLog();
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
            _logService.Initialize();
            _ = EnsureItemWeekLogsLoadedAsync();
            ApplyInitialSettings();
            PerformanceDiagnosticsService.SetEnabled(_settings.EnablePerformanceDiagnostics);
            ApplySubAddonWindowSettings();
            ApplyItemDropHelperWindowSettings();
            ApplyBuffTrackerWindowSettings();
            ApplyBuffTrackerHelperWindowSettings();

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
            try { PerformanceDiagnosticsService.Shutdown(); } catch { }
        }

        public SettingsViewModel SettingsViewModelInstance => _settingsViewModel;
        public bool IsDailyWeeklyVisible => _dailyWeeklyContentOverlay?.IsVisible == true;

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

            if (isRealTime && TryEstimateLogDelay(parseResult.FormattedText, DateTime.Now, out double delayMs))
            {
                _settingsViewModel.UpdateLogLatency(delayMs);
            }

            _buffTrackerService.ProcessLog(analysis);

            if (isRealTime && TryAccumulateAbaddonWeeklySummary(parseResult.FormattedText, ref _abaddonWeeklySummary))
            {
                if (_currentTabTag == "Item")
                    RequestRefreshLogDisplay();
            }

            if (analysis.HasExperienceGain) _expService.AddExp(parseResult.GainedExp);
            _experienceEssenceAlertService.Process(analysis);

            if (!handledDailyWeeklyCountLog &&
                analysis.ShouldRunDailyWeeklyContent &&
                _dailyWeeklyContentOverlay?.IsVisible == true)
                _dailyWeeklyContentOverlay.ProcessLog(analysis);

            foreach (string tabName in analysis.BufferTabs)
                AddToBuffer(tabName, parseResult);

            LogParser.ParseResult? itemTabLog = null;
            if (analysis.HasTrackedItemDrop)
            {
                if (analysis.IsRareTrackedItemDrop)
                {
                    itemTabLog = CreateItemTabLog(parseResult, DateTime.Today.ToString("yyyy-MM-dd"));
                    AddToBuffer("Item", itemTabLog);
                    if (isRealTime)
                        AppendWeeklyItemLog(itemTabLog, DateTime.Today);
                }
                if (analysis.ShouldShowItemDropToast)
                {
                    ItemDropToastService.Show(parseResult.TrackedItemName ?? "아이템", parseResult.TrackedItemGrade, withSound: true);
                }
            }

            if (isRealTime)
            {
                if (_currentTabTag == "Item")
                {
                    if (itemTabLog != null)
                    {
                        AddToUI(itemTabLog, isRealTime: isRealTime, deferScroll: deferUiScroll);
                    }
                    else if (parseResult.IsHighlight)
                    {
                        AddToUI(parseResult, isRealTime: isRealTime, deferScroll: deferUiScroll);
                    }
                }
                else if (_logAnalysisService.ShouldRenderToTab(parseResult, _currentTabTag))
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
            if (!isBlacklisted && _currentTabTag == "Item" && log.IsTrackedItemDrop)
            {
                foreground = GetItemDropForeground(log.TrackedItemGrade);
            }
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

                    if (_currentTabTag == "Item")
                    {
                        var summaryHeader = new Paragraph(new Run(GetAbaddonWeekHeaderText()))
                        {
                            Foreground = new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xFF)),
                            FontSize = _settings.FontSize,
                            FontFamily = this.CurrentFont,
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 0, 0, 6)
                        };
                        LogDisplay.Document.Blocks.Add(summaryHeader);

                        AddStoneIconLine(LogDisplay, this.CurrentFont, _settings.FontSize, _abaddonWeeklySummary);
                        AddMoneyLine(LogDisplay, "총 수익 - ", FormatManAmount(_abaddonWeeklySummary.NetProfitMan), this.CurrentFont, _settings.FontSize, Brushes.LightGreen);

                        LogDisplay.Document.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0, 0, 0, 2) });

                        var itemHeader = new Paragraph(new Run(GetItemWeekHeaderText()))
                        {
                            Foreground = new SolidColorBrush(Color.FromRgb(0xA5, 0xD6, 0xFF)),
                            FontSize = _settings.FontSize,
                            FontFamily = this.CurrentFont,
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 0, 0, 6)
                        };
                        LogDisplay.Document.Blocks.Add(itemHeader);
                    }

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
            DateTime pivot = _loadedItemWeekMonday != DateTime.MinValue
                ? _loadedItemWeekMonday
                : DateTime.Today;

            int weekOfMonth = GetWeekOfMonthByMonday(pivot);
            return $"< {pivot.Year}년 {pivot.Month}월 {weekOfMonth}주차 어밴던로드 수익 로그 >";
        }

        private string GetItemWeekHeaderText()
        {
            DateTime pivot = _loadedItemWeekMonday != DateTime.MinValue
                ? _loadedItemWeekMonday
                : DateTime.Today;

            int weekOfMonth = GetWeekOfMonthByMonday(pivot);
            return $"< {pivot.Year}년 {pivot.Month}월 {weekOfMonth}주차 아이템 획득 로그 >";
        }

        private static int GetWeekOfMonthByMonday(DateTime date)
        {
            DateTime firstDay = new DateTime(date.Year, date.Month, 1);
            int firstDayOffset = ((int)firstDay.DayOfWeek + 6) % 7;
            return ((date.Day + firstDayOffset - 1) / 7) + 1;
        }

        private async Task EnsureItemWeekLogsLoadedAsync()
        {
            if (_isItemWeekLogsLoading) return;

            DateTime monday = GetMonday(DateTime.Today);
            if (_loadedItemWeekMonday == monday && _logTabBufferStore.GetLogs("Item").Count > 0)
                return;

            _isItemWeekLogsLoading = true;
            try
            {
                var paths = GetWeekLogPaths(DateTime.Today).ToList();
                var (itemLogs, summary) = await Task.Run(() => ParseTrackedItemLogs(paths));
                _logTabBufferStore.Replace("Item", itemLogs);
                _abaddonWeeklySummary = summary;
                _loadedItemWeekMonday = monday;
                SaveWeeklyItemLogsSnapshot(monday, itemLogs);
                AppLogger.Info($"Loaded weekly item logs. Count={itemLogs.Count}");

                if (_currentTabTag == "Item")
                    RequestRefreshLogDisplay();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load weekly item logs.", ex);
            }
            finally
            {
                _isItemWeekLogsLoading = false;
            }
        }

        private IEnumerable<string> GetWeekLogPaths(DateTime today)
        {
            DateTime monday = GetMonday(today);
            for (int i = 0; i < 7; i++)
                yield return GetLogPath(monday.AddDays(i));
        }

        private string GetLogPath(DateTime date)
            => Path.Combine(_settings.ChatLogFolderPath, $"TWChatLog_{date:yyyy_MM_dd}.html");

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

            var gainMatch = MagicStoneGainRegex.Match(body);
            if (gainMatch.Success && TryParseLong(gainMatch.Groups["count"].Value, out long gainCount))
            {
                ApplyMagicStoneDelta(ref summary, gainMatch.Groups["grade"].Value, gainCount);
                return true;
            }

            var lossMatch = MagicStoneLossRegex.Match(body);
            if (lossMatch.Success && TryParseLong(lossMatch.Groups["count"].Value, out long lossCount))
            {
                summary.Low -= lossCount;
                return true;
            }

            return false;
        }

        private static bool TryParseLong(string raw, out long value)
            => long.TryParse(raw.Replace(",", string.Empty).Trim(), out value);

        private static DateTime GetMonday(DateTime date)
        {
            int diff = (7 + (int)date.DayOfWeek - (int)DayOfWeek.Monday) % 7;
            return date.AddDays(-diff).Date;
        }

        private static string GetWeeklyItemLogPath(DateTime monday)
            => Path.Combine(ItemLogDirectoryPath, $"ItemLog_{monday:yyyy_MM_dd}.txt");

        private void SaveWeeklyItemLogsSnapshot(DateTime monday, IReadOnlyList<LogParser.ParseResult> itemLogs)
        {
            try
            {
                Directory.CreateDirectory(ItemLogDirectoryPath);
                string path = GetWeeklyItemLogPath(monday);
                var lines = itemLogs
                    .Select(x => x.FormattedText)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .ToArray();
                File.WriteAllLines(path, lines, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save weekly item log snapshot.", ex);
            }
        }

        private void AppendWeeklyItemLog(LogParser.ParseResult itemLog, DateTime date)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(itemLog.FormattedText))
                    return;

                Directory.CreateDirectory(ItemLogDirectoryPath);
                string path = GetWeeklyItemLogPath(GetMonday(date));
                File.AppendAllText(path, itemLog.FormattedText + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to append weekly item log.", ex);
            }
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
            public long TotalEntryFeeMan;
            public long Low;
            public long Mid;
            public long High;
            public long Top;

            public long StoneRevenueMan => (Low + Mid + High + Top) * 50;
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

            if (_currentTabTag == "Item")
                _ = EnsureItemWeekLogsLoadedAsync();
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
                else if (e.PropertyName == nameof(_settings.EnablePerformanceDiagnostics))
                {
                    PerformanceDiagnosticsService.SetEnabled(_settings.EnablePerformanceDiagnostics);
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

        private static bool TryEstimateLogDelay(string formattedText, DateTime now, out double delayMs)
        {
            delayMs = 0;
            if (string.IsNullOrWhiteSpace(formattedText))
            {
                return false;
            }

            Match match = Regex.Match(formattedText, @"^\[(?<time>[^\]]+)\]");
            if (!match.Success)
            {
                return false;
            }

            string timeText = match.Groups["time"].Value.Trim();
            if (!TryParseLogTime(timeText, out TimeSpan logTime))
            {
                return false;
            }

            DateTime candidate = now.Date.Add(logTime);
            if (candidate > now.AddMinutes(1))
            {
                candidate = candidate.AddDays(-1);
            }

            TimeSpan delta = now - candidate;
            if (delta < TimeSpan.Zero)
            {
                return false;
            }

            delayMs = delta.TotalMilliseconds;
            return true;
        }

        private static bool TryParseLogTime(string timeText, out TimeSpan time)
        {
            time = default;

            if (TimeSpan.TryParseExact(timeText, @"hh\:mm\:ss", CultureInfo.InvariantCulture, out time))
            {
                return true;
            }

            Match korean = Regex.Match(timeText, @"^(?<hour>\d{1,2})시\s*(?<minute>\d{1,2})분\s*(?<second>\d{1,2})초$");
            if (!korean.Success)
            {
                return false;
            }

            if (!int.TryParse(korean.Groups["hour"].Value, out int hour) ||
                !int.TryParse(korean.Groups["minute"].Value, out int minute) ||
                !int.TryParse(korean.Groups["second"].Value, out int second))
            {
                return false;
            }

            if (hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
            {
                return false;
            }

            time = new TimeSpan(hour, minute, second);
            return true;
        }



        private void ShowDailyWeeklyWindow()
        {
            if (_dailyWeeklyContentOverlay == null || !_dailyWeeklyContentOverlay.IsLoaded)
            {
                _dailyWeeklyContentOverlay = new DailyWeeklyContentWindow(_settings);
                _dailyWeeklyContentOverlay.Closed += (s, e) =>
                {
                    if (s is Window window)
                    {
                        _settings.DailyWeeklyContentOverlayLeft = window.Left;
                        _settings.DailyWeeklyContentOverlayTop = window.Top;
                        PersistSettings();
                    }
                    _dailyWeeklyContentOverlay = null;
                    if (_settings.ShowDailyWeeklyContentOverlay)
                    {
                        _settings.ShowDailyWeeklyContentOverlay = false;
                        PersistSettings();
                    }

                    try { DailyWeeklyVisibilityChanged?.Invoke(this, false); } catch { }
                };
                AppLogger.Info("Created DailyWeekly window instance.");

                if (_settings.DailyWeeklyContentOverlayLeft.HasValue && _settings.DailyWeeklyContentOverlayTop.HasValue)
                {
                    _dailyWeeklyContentOverlay.WindowStartupLocation = WindowStartupLocation.Manual;
                    _dailyWeeklyContentOverlay.Left = _settings.DailyWeeklyContentOverlayLeft.Value;
                    _dailyWeeklyContentOverlay.Top = _settings.DailyWeeklyContentOverlayTop.Value;
                }
            }

            ApplyDailyWeeklyWindowVisibility();
            try { DailyWeeklyVisibilityChanged?.Invoke(this, _dailyWeeklyContentOverlay.IsVisible); } catch { }
            AppLogger.Info(_dailyWeeklyContentOverlay.IsVisible ? "Displayed DailyWeekly window." : "DailyWeekly window created but hidden due to game minimized/missing.");

            _ = _dailyWeeklyContentOverlay.ScanHistoricalLogsAsync();
        }

        private void CloseDailyWeeklyWindow()
        {
            _dailyWeeklyContentOverlay?.Close();
            _dailyWeeklyContentOverlay = null;
            try { DailyWeeklyVisibilityChanged?.Invoke(this, false); } catch { }
            AppLogger.Info("Closed DailyWeekly window.");
        }

        internal void ToggleDailyWeeklyContentWindow()
        {
            _settings.ShowDailyWeeklyContentOverlay = !_settings.ShowDailyWeeklyContentOverlay;
            PersistSettings();
        }

        internal void ToggleOverlayVisibility()
        {
            _isOverlayVisible = !_isOverlayVisible;

            try { OverlayVisibilityChanged?.Invoke(this, _isOverlayVisible); }
            catch (Exception ex) { AppLogger.Warn("Overlay visibility event dispatch failed.", ex); }

            AppLogger.Info($"Overlay visibility changed: {_isOverlayVisible}.");

            if (_isOverlayVisible)
            {
                this.Opacity = 0;
                this.IsHitTestVisible = true;
                if (!IsVisible)
                {
                    Show();
                }
                this.Visibility = Visibility.Visible;
                _stickyService?.SetForceHidden(false);
                _stickyService?.ResetGameWindow();
                _stickyService?.Start();
                _stickyService?.UpdatePositionImmediately();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_isOverlayVisible && this.Visibility == Visibility.Visible)
                    {
                        this.Opacity = 1;
                    }
                }), DispatcherPriority.Render);
            }
            else
            {
                this.Opacity = 0;
                this.IsHitTestVisible = false;
                _stickyService?.SetForceHidden(true);
                this.Visibility = Visibility.Collapsed;
            }
        }

        private void ReleaseMouseForce()
        {
            NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }

        private void ConfirmExit()
        {
            AppLogger.Warn("Exit requested from main window.");
            Application.Current.Shutdown();
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
        #endregion
    }
}
