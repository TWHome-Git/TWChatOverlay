using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TWChatOverlay.Views
{
    public partial class DebugLogTestWindow : Window
    {
        public static DebugLogTestWindow? Instance { get; private set; }

        private readonly ObservableCollection<string> _recentLogs = new();
        private readonly ObservableCollection<DebugSampleLogItem> _sampleLogs = new();
        private DebugLogCategory _selectedCategory = DebugLogCategory.System;
        private bool _isCategorySelectionReady;

        public DebugLogTestWindow()
        {
            InitializeComponent();
            RecentLogsListBox.ItemsSource = _recentLogs;
            SampleLogsListBox.ItemsSource = _sampleLogs;
            LoadSampleLogs();
            _isCategorySelectionReady = true;
        }

        public static void ShowOrActivate()
        {
            if (Instance == null || !Instance.IsLoaded)
            {
                Instance = new DebugLogTestWindow();
                Instance.Closed += (_, _) => Instance = null;
            }

            if (!Instance.IsVisible)
                Instance.Show();

            Instance.Topmost = true;
            Instance.Activate();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SelectCategory(DebugLogCategory.System);
            Top = 24;
            Left = Math.Max(24, SystemParameters.WorkArea.Right - Width - 24);
        }

        private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            if (Application.Current?.Dispatcher?.HasShutdownStarted == true)
                return;

            e.Cancel = true;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void DebugCategoryRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isCategorySelectionReady)
                return;

            if (sender is not RadioButton radio || radio.Tag is not string tag)
                return;

            SelectCategory(tag switch
            {
                "Normal" => DebugLogCategory.Normal,
                "Team" => DebugLogCategory.Team,
                "Club" => DebugLogCategory.Club,
                "System" => DebugLogCategory.System,
                "Shout" => DebugLogCategory.Shout,
                _ => DebugLogCategory.System
            });
        }

        private void SelectCategory(DebugLogCategory category)
        {
            _selectedCategory = category;
            NormalRadio.IsChecked = category == DebugLogCategory.Normal;
            TeamRadio.IsChecked = category == DebugLogCategory.Team;
            ClubRadio.IsChecked = category == DebugLogCategory.Club;
            SystemRadio.IsChecked = category == DebugLogCategory.System;
            ShoutRadio.IsChecked = category == DebugLogCategory.Shout;
        }

        private void InjectDebugLogButton_Click(object sender, RoutedEventArgs e)
        {
            string rawText = DebugLogInputTextBox?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(rawText))
            {
                MessageBox.Show("테스트할 로그 텍스트를 입력해주세요.", "입력 필요", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (Window window in Application.Current.Windows)
            {
                if (window is MainWindow mainWindow)
                {
                    mainWindow.InjectDebugLogText(rawText, _selectedCategory);
                    AddRecentLog(_selectedCategory, rawText);
                    DebugLogInputTextBox?.Clear();
                    return;
                }
            }

            MessageBox.Show("메인 윈도우를 찾을 수 없어 테스트 로그를 주입하지 못했습니다.", "주입 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void LoadSampleLogs()
        {
            _sampleLogs.Clear();

            AddSampleText("[신조의 정수] 아이템을 획득하였습니다.", DebugLogCategory.System);
            AddSampleText("[고대 기사의 건틀렛 파편] 아이템을 획득하였습니다.", DebugLogCategory.System);
            AddSampleText("[에오니스 라피스] 아이템을 획득하였습니다.", DebugLogCategory.System);
            AddSampleText("[드드해]님이 [로토의 부적] 아이템을 사용하셨습니다", DebugLogCategory.System);
            AddSampleText("레어의 심장를 사용하였습니다.", DebugLogCategory.System);
            AddSampleText("[클럽 상점 버프 Type R-2] 아이템을 사용하셨습니다", DebugLogCategory.System);
            AddSampleText("[클럽 상점 버프 Type R-1] 아이템을 사용하셨습니다", DebugLogCategory.System);
            AddSampleText("경험의 심장를 사용하였습니다.", DebugLogCategory.System);
            AddSampleText("[드드해]님이 [클럽 상점 버프 Type E-2] 아이템을 사용하셨습니다", DebugLogCategory.System);
            AddSampleText("[드드해]님이 [클럽 상점 버프 Type E-1] 아이템을 사용하셨습니다", DebugLogCategory.System);
            AddSampleText("[뜨뜨해]님이 [최상급 에오스의 파편] 아이템을 사용하셨습니다", DebugLogCategory.System);
            AddSampleText("[뜨뜨해]님이 [얼리버드 경험치 부스터] 아이템을 사용하셨습니다", DebugLogCategory.System);
            AddSampleText("[뜨뜨해]님이 [전설의 군고구마] 아이템을 사용하셨습니다", DebugLogCategory.System);
            AddSampleText("별동대 토벌 보상으로 경험치 1억을 획득했습니다.", DebugLogCategory.System);
            AddSampleText("경험치가 [10000000] 상승했습니다.", DebugLogCategory.System);
            AddSampleText("달여왕 군대 훈련소 클리어 보너스 경험치를 5000000000 획득했습니다.", DebugLogCategory.System);
            AddSampleText("이번 주 어밴던로드 필멸의 땅 지역의 도전 횟수는 1번 입니다.", DebugLogCategory.System);
            AddSampleText("경험치가 10000000000 감소했습니다", DebugLogCategory.System);
            AddSampleText("경험치가 10000000000 올랐습니다", DebugLogCategory.System);
            AddSampleText("클리어 보너스 경험치를 5000000000 획득했습니다", DebugLogCategory.System);
            AddSampleText("경험치 500,000,000이 지급되었습니다.", DebugLogCategory.System);
            AddSampleText("수색대장, 에토스 : 암호는 번개", DebugLogCategory.Normal);
            AddSampleText("수색대장, 에토스 : 암호는 갈퀴 모양 번개", DebugLogCategory.Normal);
            AddSampleText("수색대장, 에토스 : 암호는 갈퀴", DebugLogCategory.Normal);
            AddSampleText("수색대장, 에토스 : 암호는 갈고리", DebugLogCategory.Normal);
            AddSampleText("수색대장, 에토스 : 암호는 파도 모양 갈고리", DebugLogCategory.Normal);
            AddSampleText("수색대장, 에토스 : 암호는 갈퀴 모양 갈고리", DebugLogCategory.Normal);
            AddSampleText("수색대장, 에토스 : 암호는 파도", DebugLogCategory.Normal);
            AddSampleText("수색대장, 에토스 : 암호는 파도 모양 번개", DebugLogCategory.Normal);
        }

        private void AddSampleHeader(string text, DebugLogCategory category)
        {
            _sampleLogs.Add(new DebugSampleLogItem(text, category, true));
        }

        private void AddSampleText(string text, DebugLogCategory category)
        {
            _sampleLogs.Add(new DebugSampleLogItem(text, category, false));
        }

        private void AddRecentLog(DebugLogCategory category, string rawText)
        {
            string preview = $"{category}: {rawText.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)}";
            if (preview.Length > 100)
                preview = preview[..100] + "...";

            _recentLogs.Remove(preview);
            _recentLogs.Insert(0, preview);

            while (_recentLogs.Count > 12)
                _recentLogs.RemoveAt(_recentLogs.Count - 1);
        }

        private void RecentLogsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RecentLogsListBox.SelectedItem is not string text || string.IsNullOrWhiteSpace(text))
                return;

            int separatorIndex = text.IndexOf(": ", StringComparison.Ordinal);
            string body = separatorIndex >= 0 ? text[(separatorIndex + 2)..] : text;
            DebugLogInputTextBox.Text = body;
            DebugLogInputTextBox.CaretIndex = DebugLogInputTextBox.Text.Length;
            DebugLogInputTextBox.Focus();
        }

        private void SampleLogsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SampleLogsListBox.SelectedItem is not DebugSampleLogItem item || item.IsHeader || string.IsNullOrWhiteSpace(item.Text))
                return;

            SelectCategory(item.Category);
            DebugLogInputTextBox.Text = item.Text;
            DebugLogInputTextBox.CaretIndex = DebugLogInputTextBox.Text.Length;
            DebugLogInputTextBox.Focus();
        }

        private sealed record DebugSampleLogItem(string Text, DebugLogCategory Category, bool IsHeader)
        {
            public string Foreground => IsHeader ? "#AAB3BD" : "#F2F6FA";
        }
    }
}
