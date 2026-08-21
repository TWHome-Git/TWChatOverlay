using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Controls;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    public partial class ShoutReplayWindow : Window
    {
        private static readonly Regex DivLogRegex = new("<div\\s+class=\"log\\s+shout\"[^>]*>(?<text>.*?)</div>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ShoutTrailingUserIdRegex = new(@"\[(?<id>[^\[\]]+)\]\s*$", RegexOptions.Compiled);
        private readonly ChatSettings _settings;
        private readonly List<DateTime> _dates = new();
        private List<string> _currentLines = new();
        private DateTime _currentDate;
        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private int _loadVersion;

        public ShoutReplayWindow(ChatSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            WindowFontService.Apply(this);
            LogRichText.FontSize = _settings.ShoutReplayFontSize;
            FontSizeSlider.Value = _settings.ShoutReplayFontSize;
            FontSizeText.Text = $"{_settings.ShoutReplayFontSize:F0}px";
            Loaded += ShoutReplayWindow_Loaded;
            Closing += ShoutReplayWindow_Closing;
            RefreshDates();
            if (_dates.Count > 0)
                _ = LoadDateAsync(_dates[^1]);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                try
                {
                    DragMove();
                }
                catch { }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;

            double size = Math.Round(e.NewValue);
            _settings.ShoutReplayFontSize = size;
            LogRichText.FontSize = size;
            FontSizeText.Text = $"{size:F0}px";
            ConfigService.SaveDeferred(_settings);
        }

        private void ShoutReplayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureMouseHitTestEnabled();
            if (_settings.ShoutReplayWindowLeft.HasValue && _settings.ShoutReplayWindowTop.HasValue)
            {
                Left = _settings.ShoutReplayWindowLeft.Value;
                Top = _settings.ShoutReplayWindowTop.Value;
            }

            if (_settings.ShoutReplayWindowWidth.HasValue && _settings.ShoutReplayWindowWidth.Value >= MinWidth)
                Width = _settings.ShoutReplayWindowWidth.Value;
            if (_settings.ShoutReplayWindowHeight.HasValue && _settings.ShoutReplayWindowHeight.Value >= MinHeight)
                Height = _settings.ShoutReplayWindowHeight.Value;

            Services.OsSnapGuard.Disable(this); // 상단 드래그 시 OS 스냅(최대화) 차단
        }

        private void ShoutReplayWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _settings.ShoutReplayWindowLeft = Left;
            _settings.ShoutReplayWindowTop = Top;
            _settings.ShoutReplayWindowWidth = Width;
            _settings.ShoutReplayWindowHeight = Height;
            ConfigService.SaveDeferred(_settings);
        }

        private void RefreshDates()
        {
            _dates.Clear();
            if (!Directory.Exists(LogStoragePaths.ShoutDirectory))
                return;
            foreach (string file in Directory.EnumerateFiles(LogStoragePaths.ShoutDirectory, "*.html"))
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
                    _dates.Add(d.Date);
            }
            _dates.Sort();
            DatePickerLog.DisplayDateStart = _dates.FirstOrDefault();
            DatePickerLog.DisplayDateEnd = _dates.LastOrDefault();
            DatePickerLog.BlackoutDates.Clear();
            if (_dates.Count == 0) return;
            var set = _dates.ToHashSet();
            DateTime start = _dates.First();
            DateTime end = _dates.Last();
            for (DateTime day = start; day <= end; day = day.AddDays(1))
            {
                if (!set.Contains(day))
                    DatePickerLog.BlackoutDates.Add(new System.Windows.Controls.CalendarDateRange(day));
            }
        }

        private async Task LoadDateAsync(DateTime date)
        {
            int myVersion = Interlocked.Increment(ref _loadVersion);
            await _loadGate.WaitAsync();
            LoadingOverlay.Visibility = Visibility.Visible;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            try
            {
                _currentDate = date.Date;
                BtnDate.Content = _currentDate.ToString("yyyy-MM-dd");
                string path = Path.Combine(LogStoragePaths.ShoutDirectory, _currentDate.ToString("yyyy-MM-dd") + ".html");
                List<string> parsedLines = new();
                if (File.Exists(path))
                {
                    parsedLines = await Task.Run(() =>
                    {
                        var list = new List<string>(512);
                        string html = File.ReadAllText(path, Encoding.UTF8);
                        foreach (Match m in DivLogRegex.Matches(html))
                            list.Add(WebUtility.HtmlDecode(m.Groups["text"].Value));
                        return list;
                    });
                }

                _currentLines = parsedLines.Select(DecorateShoutEta).ToList();
                if (myVersion == _loadVersion)
                    RenderLines(SearchBox.Text, scrollToBottom: true);

                ApplyVisualStyle();
                int idx = _dates.IndexOf(_currentDate);
                BtnPrev.IsEnabled = idx > 0;
                BtnNext.IsEnabled = idx >= 0 && idx < _dates.Count - 1;
                DatePickerLog.SelectedDate = _currentDate;
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                DatePopup.IsOpen = false;
                Mouse.Capture(null);
                LogRichText.IsHitTestVisible = true;
                Activate();
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    LogRichText.Focus();
                    Keyboard.Focus(LogRichText);
                }), DispatcherPriority.Input);
                _loadGate.Release();
            }
        }

        private void ApplyVisualStyle()
        {
            FontFamily = FontService.GetFont(_settings.FontFamily);
            FontSize = _settings.FontSize;
            try
            {
                object? converted = new BrushConverter().ConvertFromString(_settings.ShoutColor);
                LogRichText.Foreground = converted as Brush ?? Brushes.Orange;
            }
            catch { LogRichText.Foreground = Brushes.Orange; }
        }

        /// <summary>
        /// 외치기 라인 끝의 [보낸이] 대괄호에 에타 레벨/캐릭터를 덧붙인다. 메인 채팅과 동일한 방식.
        /// 프로필이 없거나 설정이 꺼져 있으면 원본을 그대로 반환한다.
        /// </summary>
        private string DecorateShoutEta(string line)
        {
            if (!_settings.ShowEtaLevel && !_settings.ShowEtaCharacter)
                return line;

            Match m = ShoutTrailingUserIdRegex.Match(line);
            if (!m.Success)
                return line;

            string rawId = m.Groups["id"].Value;
            string id = rawId.Trim();
            if (id.Length == 0)
                return line;

            if (!EtaProfileResolver.TryGetProfile(id, out var profile)
                && !EtaProfileResolver.TryGetProfile(rawId, out profile))
                return line;

            string suffix = string.Empty;
            if (_settings.ShowEtaLevel)
                suffix += $"[{profile.Level}]";
            if (_settings.ShowEtaCharacter && !string.IsNullOrWhiteSpace(profile.CharacterName))
                suffix += $"[{profile.CharacterName}]";
            if (suffix.Length == 0)
                return line;

            // 끝의 [보낸이] 대괄호 안 내용을 보존하며 접미사를 덧붙인다.
            return line.Substring(0, m.Index) + $"[{rawId}{suffix}]";
        }

        /// <summary>현재 날짜의 외치기 라인을 검색어로 필터링해 렌더한다. (파일 재로드 없이 즉시)</summary>
        /// <param name="scrollToBottom">날짜 로드처럼 최신 외치기를 보여줘야 할 때 최하단으로 스크롤.</param>
        private void RenderLines(string? filter, bool scrollToBottom = false)
        {
            // InitializeComponent 도중 TextChanged가 먼저 발생할 수 있으므로 요소 생성 여부를 확인.
            if (LogRichText is null || SearchInfo is null)
                return;

            string term = filter?.Trim() ?? string.Empty;
            bool hasFilter = term.Length > 0;

            var document = new FlowDocument();
            int shown = 0;
            foreach (string line in _currentLines)
            {
                if (hasFilter && line.IndexOf(term, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var para = new Paragraph { Margin = new Thickness(0, 0, 0, 4) };
                if (hasFilter)
                    AppendHighlighted(para, line, term);
                else
                    para.Inlines.Add(new Run(line));

                document.Blocks.Add(para);
                shown++;
            }

            LogRichText.Document = document;
            SearchInfo.Text = hasFilter ? $"{shown}건" : string.Empty;

            if (scrollToBottom)
            {
                // 레이아웃이 잡힌 뒤 최하단으로 스크롤해 가장 최근 외치기가 보이게 한다.
                _ = Dispatcher.BeginInvoke(new Action(() => LogRichText.ScrollToEnd()), DispatcherPriority.Loaded);
            }
        }

        /// <summary>검색어와 일치하는 부분을 강조 표시하며 라인을 추가한다.</summary>
        private static void AppendHighlighted(Paragraph para, string line, string term)
        {
            int start = 0;
            while (true)
            {
                int idx = line.IndexOf(term, start, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                {
                    para.Inlines.Add(new Run(line.Substring(start)));
                    break;
                }

                if (idx > start)
                    para.Inlines.Add(new Run(line.Substring(start, idx - start)));

                para.Inlines.Add(new Run(line.Substring(idx, term.Length))
                {
                    Background = new SolidColorBrush(Color.FromArgb(180, 255, 220, 0)),
                    Foreground = Brushes.Black,
                    FontWeight = FontWeights.Bold
                });

                start = idx + term.Length;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RenderLines(SearchBox.Text);
        }

        private void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SearchBox.Clear();
                e.Handled = true;
            }
        }

        private void Prev_Click(object sender, RoutedEventArgs e)
        {
            int idx = _dates.IndexOf(_currentDate);
            if (idx > 0) _ = LoadDateAsync(_dates[idx - 1]);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            int idx = _dates.IndexOf(_currentDate);
            if (idx >= 0 && idx < _dates.Count - 1) _ = LoadDateAsync(_dates[idx + 1]);
        }

        private void Date_Click(object sender, RoutedEventArgs e) => DatePopup.IsOpen = true;

        private void DatePickerLog_SelectedDateChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (DatePickerLog.SelectedDate is DateTime d && _dates.Contains(d.Date))
            {
                _ = LoadDateAsync(d.Date);
                DatePopup.IsOpen = false;
            }
        }

        private void LogArea_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!IsActive)
                Activate();
            LogRichText.Focus();
        }

        private void LogRichText_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DependencyObject root)
                return;

            ScrollViewer? sv = FindDescendant<ScrollViewer>(root);
            if (sv == null)
                return;

            if (e.Delta > 0)
                sv.LineUp();
            else
                sv.LineDown();

            e.Handled = true;
        }

        private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T found)
                    return found;

                T? nested = FindDescendant<T>(child);
                if (nested != null)
                    return nested;
            }
            return null;
        }

        private void EnsureMouseHitTestEnabled()
        {
            IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            int cleared = exStyle & ~NativeMethods.WS_EX_TRANSPARENT;
            if (cleared != exStyle)
            {
                NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, cleared);
            }
        }
    }
}
