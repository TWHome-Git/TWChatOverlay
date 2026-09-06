using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.ViewModels
{
    /// <summary>
    /// 추가 기능 설정을 관리하는 ViewModel입니다.
    /// </summary>
    public class AddonViewModel : ViewModelBase
    {
        private readonly ChatSettings _settings;

        /// <summary>부수 효과가 없는 단순 설정은 화면이 이 원본에 바로 바인딩한다. (예: {Binding Settings.ShowRecaptureSupplyMap})</summary>
        public ChatSettings Settings => _settings;
        private readonly BossAlarmCardViewModelProvider _bossAlarmCardProvider;

        private bool _enableExperienceLimitAlert;
        private long _expAlarmThresholdMan;
        private bool _useCustomDropItemFilter;
        private string _customDropItemJson = string.Empty;
        private string _customDropItemStatus = string.Empty;
        private string _experienceLimitTotalExp = "0";

        public ObservableCollection<BossAlarmCardViewModel> BossAlarmCards { get; } = new();
        public ObservableCollection<DropItemFilterEntry> DefaultDropItems { get; } = new();
        public ObservableCollection<DropItemFilterEntry> CustomDropItems { get; } = new();
        public ICommand SelectDefaultDropFilterCommand { get; }
        public ICommand SelectCustomDropFilterCommand { get; }
        public ICommand ApplyCustomDropItemFilterCommand { get; }
        public ICommand LoadCustomDropItemFilterCommand { get; }
        public ICommand SaveCustomDropItemFilterCommand { get; }
        public ICommand ApplyExperienceLimitStateCommand { get; }

        public bool EnableExperienceLimitAlert
        {
            get => _enableExperienceLimitAlert;
            set
            {
                if (SetSetting(ref _enableExperienceLimitAlert, value, (settings, newValue) => settings.EnableExperienceLimitAlert = newValue))
                {
                    RefreshExperienceLimitState();
                }
            }
        }

        public string ExperienceLimitTotalExp
        {
            get => _experienceLimitTotalExp;
            set => SetProperty(ref _experienceLimitTotalExp, value ?? "0");
        }


        public int ExpAlarmThresholdMan
        {
            get => (int)(_expAlarmThresholdMan / 10000);
            set
            {
                long newThreshold = value * 10000L;
                SetSetting(ref _expAlarmThresholdMan, newThreshold, (settings, threshold) => settings.ExpAlarmThreshold = threshold);
            }
        }

        public bool UseCustomDropItemFilter
        {
            get => _useCustomDropItemFilter;
            private set => SetProperty(ref _useCustomDropItemFilter, value);
        }

        public string CustomDropItemJson
        {
            get => _customDropItemJson;
            set => SetProperty(ref _customDropItemJson, value ?? string.Empty);
        }

        public string CustomDropItemStatus
        {
            get => _customDropItemStatus;
            private set => SetProperty(ref _customDropItemStatus, value);
        }

        private double _dungeonCountDisplayFontSize;
        private double _experienceAlertFontSize;
        private double _itemDropToastFontSize;

        public double DungeonCountDisplayFontSize
        {
            get => _dungeonCountDisplayFontSize;
            set
            {
                if (SetSetting(ref _dungeonCountDisplayFontSize, value, (settings, newValue) => settings.DungeonCountDisplayFontSize = newValue))
                    DungeonCountDisplayWindowService.ApplyFontSize(value);
            }
        }

        private double _cravingPleasureCountFontSize;

        public double CravingPleasureCountFontSize
        {
            get => _cravingPleasureCountFontSize;
            set
            {
                if (SetSetting(ref _cravingPleasureCountFontSize, value, (settings, newValue) => settings.CravingPleasureCountFontSize = newValue))
                    DungeonCountDisplayWindowService.ApplyFontSize(value);
            }
        }

        public double ExperienceAlertFontSize
        {
            get => _experienceAlertFontSize;
            set
            {
                if (SetSetting(ref _experienceAlertFontSize, value, (settings, newValue) => settings.ExperienceAlertFontSize = newValue))
                    ExperienceAlertWindowService.ApplyFontSize(value);
            }
        }

        public double ItemDropToastFontSize
        {
            get => _itemDropToastFontSize;
            set
            {
                if (SetSetting(ref _itemDropToastFontSize, value, (settings, newValue) => settings.ItemDropToastFontSize = newValue))
                {
                    try { Views.ItemDropHelperWindow.Instance?.SetFontSize(value); } catch { }
                }
            }
        }

        private double _bossAlertToastFontSize;

        public double BossAlertToastFontSize
        {
            get => _bossAlertToastFontSize;
            set
            {
                if (SetSetting(ref _bossAlertToastFontSize, value, (settings, newValue) => settings.BossAlertToastFontSize = newValue))
                    Views.BossAlertToastWindow.ApplyFontSize(value);
            }
        }

        public AddonViewModel(ChatSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _bossAlarmCardProvider = new BossAlarmCardViewModelProvider(_settings);
            SelectDefaultDropFilterCommand = new RelayCommand(_ => SelectDefaultDropFilter());
            SelectCustomDropFilterCommand = new RelayCommand(_ => SelectCustomDropFilter());
            ApplyCustomDropItemFilterCommand = new RelayCommand(async _ => await ApplyCustomDropItemFilterAsync());
            LoadCustomDropItemFilterCommand = new RelayCommand(_ => LoadCustomDropItemFilter());
            SaveCustomDropItemFilterCommand = new RelayCommand(_ => SaveCustomDropItemFilter());
            ApplyExperienceLimitStateCommand = new RelayCommand(_ => ApplyExperienceLimitState());

            _enableExperienceLimitAlert = _settings.EnableExperienceLimitAlert;
            _expAlarmThresholdMan = _settings.ExpAlarmThreshold;
            _useCustomDropItemFilter = _settings.UseCustomDropItemFilter;
            _customDropItemJson = _settings.CustomDropItemJson;
            _customDropItemStatus = !_useCustomDropItemFilter
                ? "기본 GitHub 드롭 테이블을 사용 중입니다."
                : "사용자 정의 필터를 사용 중입니다.";
            _dungeonCountDisplayFontSize = _settings.DungeonCountDisplayFontSize;
            _cravingPleasureCountFontSize = _settings.CravingPleasureCountFontSize;
            _experienceAlertFontSize = _settings.ExperienceAlertFontSize;
            _itemDropToastFontSize = _settings.ItemDropToastFontSize;
            _bossAlertToastFontSize = _settings.BossAlertToastFontSize;
            ReplaceBossAlarmCards(_bossAlarmCardProvider.CreateCards());
            _ = InitializeBossAlarmCardsAsync();
            _ = InitializeDropItemFilterListsAsync();
            RefreshExperienceLimitState();

            // 잠금 해제 인스펙터 등 밖에서 설정이 바뀌면 화면 값도 실시간 동기화
            _settings.PropertyChanged += Settings_PropertyChanged;
        }

        /// <summary>
        /// 설정 구독을 해제한다. 설정 화면/마법사가 닫힐 때 반드시 호출 —
        /// 앱 수명 내내 사는 ChatSettings에 붙은 채 남으면 VM 전체(드롭 아이템 목록 포함)가 누수된다.
        /// </summary>
        public void Detach()
        {
            _settings.PropertyChanged -= Settings_PropertyChanged;
        }

        /// <summary>Detach 후 화면이 다시 로드될 때 재구독한다. (중복 구독 방지를 위해 해제 후 구독)</summary>
        public void Attach()
        {
            _settings.PropertyChanged -= Settings_PropertyChanged;
            _settings.PropertyChanged += Settings_PropertyChanged;
        }

        private void Settings_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // 빈 이름 = 설정 전체 교체(초기화·프로필 불러오기). 여기 남아 있는 사본 필드를 모두 원본으로 되돌린다.
            // 단순 통과 설정은 Settings 속성으로 원본에 직접 바인딩되어 있어 따로 할 일이 없다.
            if (string.IsNullOrEmpty(e.PropertyName))
            {
                SyncFromSettings(ref _enableExperienceLimitAlert, _settings.EnableExperienceLimitAlert, nameof(EnableExperienceLimitAlert));
                if (_expAlarmThresholdMan != _settings.ExpAlarmThreshold)
                {
                    _expAlarmThresholdMan = _settings.ExpAlarmThreshold;
                    OnPropertyChanged(nameof(ExpAlarmThresholdMan));
                }
                SyncFromSettings(ref _dungeonCountDisplayFontSize, _settings.DungeonCountDisplayFontSize, nameof(DungeonCountDisplayFontSize));
                SyncFromSettings(ref _cravingPleasureCountFontSize, _settings.CravingPleasureCountFontSize, nameof(CravingPleasureCountFontSize));
                SyncFromSettings(ref _experienceAlertFontSize, _settings.ExperienceAlertFontSize, nameof(ExperienceAlertFontSize));
                SyncFromSettings(ref _itemDropToastFontSize, _settings.ItemDropToastFontSize, nameof(ItemDropToastFontSize));
                SyncFromSettings(ref _bossAlertToastFontSize, _settings.BossAlertToastFontSize, nameof(BossAlertToastFontSize));
                OnPropertyChanged(nameof(Settings));
                RefreshExperienceLimitState();
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(ChatSettings.DungeonCountDisplayFontSize):
                    SyncFromSettings(ref _dungeonCountDisplayFontSize, _settings.DungeonCountDisplayFontSize, nameof(DungeonCountDisplayFontSize));
                    break;
                case nameof(ChatSettings.CravingPleasureCountFontSize):
                    SyncFromSettings(ref _cravingPleasureCountFontSize, _settings.CravingPleasureCountFontSize, nameof(CravingPleasureCountFontSize));
                    break;
                case nameof(ChatSettings.ExperienceAlertFontSize):
                    SyncFromSettings(ref _experienceAlertFontSize, _settings.ExperienceAlertFontSize, nameof(ExperienceAlertFontSize));
                    break;
                case nameof(ChatSettings.ItemDropToastFontSize):
                    SyncFromSettings(ref _itemDropToastFontSize, _settings.ItemDropToastFontSize, nameof(ItemDropToastFontSize));
                    break;
                case nameof(ChatSettings.BossAlertToastFontSize):
                    SyncFromSettings(ref _bossAlertToastFontSize, _settings.BossAlertToastFontSize, nameof(BossAlertToastFontSize));
                    break;
            }
        }

        private void SyncFromSettings<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;
            field = value;
            OnPropertyChanged(propertyName);
        }

        private void SaveSettings()
        {
            ConfigService.SaveDeferred(_settings);
        }

        private void RefreshExperienceLimitState()
        {
            if (!ExperienceAlertWindowService.TryGetStateSnapshot(_settings, out var snapshot))
                return;

            ExperienceLimitTotalExp = FormatExpEokValue(snapshot.TotalExp);
        }


        private void ApplyExperienceLimitState()
        {
            if (!TryParseExpEokValue(_experienceLimitTotalExp, out long totalExp))
                return;

            ApplyExperienceLimitState(totalExp, _experienceLimitTotalExp);
        }

        public void ApplyExperienceLimitStateFromSettings()
        {
            ApplyExperienceLimitState(_settings.ExperienceLimitTotalExp, FormatExpEokValue(_settings.ExperienceLimitTotalExp));
        }

        private void ApplyExperienceLimitState(long totalExp, string inputEokText)
        {
            _settings.ExperienceLimitTotalExp = totalExp;
            _settings.ExperienceLimitStateInitialized = true;
            AppLogger.Info($"Applied manual total exp from eok input. InputEok='{inputEokText}', TotalExp={totalExp:N0}");
            SaveSettings();
            _ = ExperienceAlertWindowService.ApplyStateSnapshot(new ExperienceAlertStateSnapshot
            {
                TotalExp = totalExp
            });

            ExperienceWeeklyRefreshService.MarkCurrentWeekRefreshed(_settings, DateTime.Now);
            RefreshExperienceLimitState();
        }


        private static bool TryParseExpEokValue(string? text, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return true;

            string normalized = text.Replace(",", string.Empty).Replace("억", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return true;

            if (!long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out long eok))
                return false;

            if (eok < 0)
                eok = 0;

            value = checked(eok * 100_000_000L);

            return true;
        }

        private static string FormatExpEokValue(long value)
            => (Math.Max(0, value) / 100_000_000L).ToString("N0", CultureInfo.InvariantCulture);


        private bool SetSetting<T>(ref T field, T value, Action<ChatSettings, T> apply, [CallerMemberName] string? propertyName = null)
        {
            if (!SetProperty(ref field, value, propertyName))
            {
                return false;
            }

            apply(_settings, value);
            SaveSettings();
            return true;
        }

        private void ReplaceBossAlarmCards(IEnumerable<BossAlarmCardViewModel> cards)
        {
            BossAlarmCards.Clear();
            foreach (var card in cards)
            {
                BossAlarmCards.Add(card);
            }
        }

        private async Task InitializeBossAlarmCardsAsync()
        {
            var cards = await _bossAlarmCardProvider.LoadCardsAsync();
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => ReplaceBossAlarmCards(cards)));
        }

        public async Task RefreshBossAlarmCardsAsync(bool forceRefresh = false)
        {
            var cards = await _bossAlarmCardProvider.LoadCardsAsync(forceRefresh);
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => ReplaceBossAlarmCards(cards)));
        }

        private async Task ApplyCustomDropItemFilterAsync()
        {
            string json = SerializeDropItems(CustomDropItems);
            if (!DropItemResolver.TryValidateJson(json, out string message))
            {
                CustomDropItemStatus = message;
                return;
            }

            _settings.UseCustomDropItemFilter = true;
            _settings.CustomDropItemJson = json;
            UseCustomDropItemFilter = true;
            CustomDropItemJson = json;
            SaveSettings();
            await DropItemResolver.ReloadAsync(_settings);
            CustomDropItemStatus = $"사용자 정의 필터 적용 완료: {message}";
        }

        private async void SelectDefaultDropFilter()
        {
            _settings.UseCustomDropItemFilter = false;
            UseCustomDropItemFilter = false;
            SaveSettings();
            await DropItemResolver.ReloadAsync(_settings);
            CustomDropItemStatus = "기본 GitHub 드롭 테이블을 사용 중입니다.";
        }

        private void SelectCustomDropFilter()
        {
            UseCustomDropItemFilter = true;
            CustomDropItemStatus = "사용자 정의 목록을 편집한 뒤 적용을 누르세요.";
        }

        public void MoveToCustom(IEnumerable<DropItemFilterEntry> entries)
        {
            MoveEntries(entries, DefaultDropItems, CustomDropItems);
            CustomDropItemStatus = $"{CustomDropItems.Count:N0}개 항목이 사용자 정의 목록에 있습니다.";
        }

        public void MoveToDefault(IEnumerable<DropItemFilterEntry> entries)
        {
            MoveEntries(entries, CustomDropItems, DefaultDropItems);
            CustomDropItemStatus = $"{CustomDropItems.Count:N0}개 항목이 사용자 정의 목록에 있습니다.";
        }

        private async Task InitializeDropItemFilterListsAsync()
        {
            var defaultItems = await DropItemResolver.LoadDefaultItemsAsync();
            var customItems = ParseDropItems(_settings.CustomDropItemJson);

            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                DefaultDropItems.Clear();
                CustomDropItems.Clear();

                var customNames = new HashSet<string>(customItems.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);
                foreach (var item in defaultItems)
                {
                    var entry = new DropItemFilterEntry(item.Name, item.Grade, item.Abbreviation);
                    if (customNames.Contains(item.Name))
                        continue;
                    DefaultDropItems.Add(entry);
                }

                foreach (var item in customItems)
                    CustomDropItems.Add(item);

                CustomDropItemStatus = _settings.UseCustomDropItemFilter
                    ? $"{CustomDropItems.Count:N0}개 사용자 정의 항목을 사용 중입니다."
                    : "기본 GitHub 드롭 테이블을 사용 중입니다.";
            }));
        }

        private void LoadCustomDropItemFilter()
        {
            var dialog = new OpenFileDialog
            {
                Title = "사용자 정의 드롭 필터 불러오기",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                string json = File.ReadAllText(dialog.FileName);
                var loaded = ParseDropItems(json);
                if (loaded.Count == 0)
                {
                    CustomDropItemStatus = "불러온 파일에 사용할 수 있는 항목이 없습니다.";
                    return;
                }

                var existingDefault = DefaultDropItems
                    .Concat(CustomDropItems)
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

                DefaultDropItems.Clear();
                CustomDropItems.Clear();
                var loadedNames = new HashSet<string>(loaded.Select(item => item.Name), StringComparer.OrdinalIgnoreCase);

                foreach (var item in SortDropItems(existingDefault.Values))
                {
                    if (!loadedNames.Contains(item.Name))
                        DefaultDropItems.Add(item);
                }

                foreach (var item in loaded)
                    CustomDropItems.Add(item);

                CustomDropItemStatus = $"{CustomDropItems.Count:N0}개 항목을 불러왔습니다. 적용을 누르면 저장됩니다.";
            }
            catch (Exception ex)
            {
                CustomDropItemStatus = $"불러오기 실패: {ex.Message}";
            }
        }

        private void SaveCustomDropItemFilter()
        {
            var dialog = new SaveFileDialog
            {
                Title = "사용자 정의 드롭 필터 저장",
                Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
                FileName = "CustomDropItem.json"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                File.WriteAllText(dialog.FileName, SerializeDropItems(CustomDropItems));
                CustomDropItemStatus = $"{CustomDropItems.Count:N0}개 항목을 저장했습니다.";
            }
            catch (Exception ex)
            {
                CustomDropItemStatus = $"저장 실패: {ex.Message}";
            }
        }

        private static void MoveEntries(
            IEnumerable<DropItemFilterEntry> entries,
            ObservableCollection<DropItemFilterEntry> source,
            ObservableCollection<DropItemFilterEntry> target)
        {
            foreach (var entry in entries.ToList())
            {
                if (source.Remove(entry) &&
                    !target.Any(item => item.Name.Equals(entry.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    target.Add(entry);
                }
            }

            SortEntries(source);
            SortEntries(target);
        }

        private static void SortEntries(ObservableCollection<DropItemFilterEntry> entries)
        {
            var sorted = SortDropItems(entries);
            entries.Clear();
            foreach (var item in sorted)
                entries.Add(item);
        }

        private static string SerializeDropItems(IEnumerable<DropItemFilterEntry> entries)
        {
            var payload = new DropItemEditorPayload
            {
                Items = entries
                    .OrderBy(item => GetGradeSortOrder(item.Grade))
                    .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(item => new DropItemEditorRow { Name = item.Name, Grade = item.Grade.ToString(), Abbreviation = item.Abbreviation })
                    .ToList()
            };

            return JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }

        private static List<DropItemFilterEntry> ParseDropItems(string? json)
        {
            var result = new List<DropItemFilterEntry>();
            if (string.IsNullOrWhiteSpace(json))
                return result;

            try
            {
                var payload = JsonSerializer.Deserialize<DropItemEditorPayload>(json);
                if (payload?.Items == null)
                    return result;

                foreach (var item in payload.Items)
                {
                    if (string.IsNullOrWhiteSpace(item.Name))
                        continue;

                    result.Add(new DropItemFilterEntry(item.Name.Trim(), ParseGrade(item.Grade), item.Abbreviation));
                }
            }
            catch
            {
            }

            return result
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(item => GetGradeSortOrder(item.Grade))
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private static List<DropItemFilterEntry> SortDropItems(IEnumerable<DropItemFilterEntry> entries)
            => entries
                .OrderBy(item => GetGradeSortOrder(item.Grade))
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        private static int GetGradeSortOrder(ItemDropGrade grade)
            => grade switch
            {
                ItemDropGrade.Normal => 0,
                ItemDropGrade.Rare => 1,
                ItemDropGrade.Special => 2,
                _ => 3
            };

        private static ItemDropGrade ParseGrade(string? grade)
        {
            if (grade?.Equals("Special", StringComparison.OrdinalIgnoreCase) == true)
                return ItemDropGrade.Special;
            if (grade?.Equals("Rare", StringComparison.OrdinalIgnoreCase) == true)
                return ItemDropGrade.Rare;
            return ItemDropGrade.Normal;
        }

        private sealed class DropItemEditorPayload
        {
            [JsonPropertyName("items")]
            public List<DropItemEditorRow> Items { get; set; } = new();
        }

        private sealed class DropItemEditorRow
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("grade")]
            public string Grade { get; set; } = "Normal";

            [JsonPropertyName("abbr")]
            public string? Abbreviation { get; set; }
        }
    }

    public sealed class DropItemFilterEntry
    {
        public string Name { get; }
        public ItemDropGrade Grade { get; }
        public string? Abbreviation { get; }
        public Brush Foreground { get; }

        public DropItemFilterEntry(string name, ItemDropGrade grade, string? abbreviation = null)
        {
            Name = name;
            Grade = grade;
            Abbreviation = string.IsNullOrWhiteSpace(abbreviation) ? null : abbreviation.Trim();
            Foreground = grade switch
            {
                ItemDropGrade.Rare => new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A)),
                ItemDropGrade.Special => new SolidColorBrush(Color.FromRgb(0xFF, 0x7E, 0xDB)),
                _ => Brushes.White
            };
        }
    }

}
