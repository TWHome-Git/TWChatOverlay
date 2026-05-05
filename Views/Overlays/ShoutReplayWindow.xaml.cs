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
        private readonly ChatSettings _settings;
        private readonly List<DateTime> _dates = new();
        private DateTime _currentDate;
        private readonly SemaphoreSlim _loadGate = new(1, 1);
        private int _loadVersion;

        public ShoutReplayWindow(ChatSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            WindowFontService.Apply(this);
            Loaded += ShoutReplayWindow_Loaded;
            Closing += ShoutReplayWindow_Closing;
            RefreshDates();
            if (_dates.Count > 0)
                _ = LoadDateAsync(_dates[^1]);
        }

        private void ShoutReplayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            EnsureMouseHitTestEnabled();
            if (_settings.ShoutReplayWindowLeft.HasValue && _settings.ShoutReplayWindowTop.HasValue)
            {
                Left = _settings.ShoutReplayWindowLeft.Value;
                Top = _settings.ShoutReplayWindowTop.Value;
            }
        }

        private void ShoutReplayWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _settings.ShoutReplayWindowLeft = Left;
            _settings.ShoutReplayWindowTop = Top;
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

                var document = new FlowDocument();
                foreach (string line in parsedLines)
                    document.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0, 0, 0, 4) });
                if (myVersion == _loadVersion)
                    LogRichText.Document = document;

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
