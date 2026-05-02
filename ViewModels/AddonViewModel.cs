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
        private readonly BossAlarmCardViewModelProvider _bossAlarmCardProvider;

        private bool _useAlertColor;
        private bool _useAlertSound;
        private bool _useMagicCircleAlert;
        private string _keywordInput;
        private bool _showExpTracker;
        private bool _isExpAlarmEnabled;
        private bool _enableExperienceLimitAlert;
        private bool _showExperienceLimitAlertWindow;
        private long _expAlarmThresholdMan;
        private bool _showDailyWeeklyContentOverlay;
        private bool _showEtosDirectionAlert;
        private bool _showEtosHelperWindow;
        private bool _enableAbaddonRoadCountAlert;
        private bool _showAbaddonRoadSummaryWindow;
        private bool _enableCravingPleasureCountAlert;
        private bool _showDungeonCountDisplayWindow;
        private int _abaddonRoadCountAlertDurationSeconds;
        private bool _showItemDropAlert;
        private bool _showItemDropHelperWindow;
        private bool _useCustomDropItemFilter;
        private string _customDropItemJson = string.Empty;
        private string _customDropItemStatus = string.Empty;
        private bool _enableBuffTrackerAlert;
        private bool _enableBuffTrackerEndSound;
        private bool _showBuffTrackerWindow;
        private bool _enableCharacterProfiles;
        private string _profile1DisplayName = "프로필1";
        private string _profile2DisplayName = "프로필2";
        private string _profile1SwitchLog = string.Empty;
        private string _profile2SwitchLog = string.Empty;
        private double _itemDropAlertVolumePercent;
        private double _highlightAlertVolumePercent;
        private double _magicCircleAlertVolumePercent;
        private double _expBuffAlertVolumePercent;
        private double _buffTrackerEndSoundVolumePercent;
        private double _bossAlertVolumePercent;
        private string _experienceLimitTotalExp = "0";
        private string _experienceLimitProfile1Exp = "0";
        private string _experienceLimitProfile2Exp = "0";

        public ObservableCollection<BossAlarmCardViewModel> BossAlarmCards { get; } = new();
        public ObservableCollection<DropItemFilterEntry> DefaultDropItems { get; } = new();
        public ObservableCollection<DropItemFilterEntry> CustomDropItems { get; } = new();
        public ICommand SelectDefaultDropFilterCommand { get; }
        public ICommand SelectCustomDropFilterCommand { get; }
        public ICommand ApplyCustomDropItemFilterCommand { get; }
        public ICommand LoadCustomDropItemFilterCommand { get; }
        public ICommand SaveCustomDropItemFilterCommand { get; }
        public ICommand RefreshExperienceLimitStateCommand { get; }
        public ICommand ApplyExperienceLimitStateCommand { get; }

        public bool UseAlertColor
        {
            get => _useAlertColor;
            set => SetSetting(ref _useAlertColor, value, (settings, newValue) => settings.UseAlertColor = newValue);
        }

        public bool UseAlertSound
        {
            get => _useAlertSound;
            set => SetSetting(ref _useAlertSound, value, (settings, newValue) => settings.UseAlertSound = newValue);
        }

        public bool UseMagicCircleAlert
        {
            get => _useMagicCircleAlert;
            set => SetSetting(ref _useMagicCircleAlert, value, (settings, newValue) => settings.UseMagicCircleAlert = newValue);
        }

        public string KeywordInput
        {
            get => _keywordInput;
            set => SetSetting(ref _keywordInput, value ?? string.Empty, (settings, newValue) => settings.KeywordInput = newValue);
        }

        public bool ShowExpTracker
        {
            get => _showExpTracker;
            set => SetSetting(ref _showExpTracker, value, (settings, newValue) => settings.ShowExpTracker = newValue);
        }

        public bool IsExpAlarmEnabled
        {
            get => _isExpAlarmEnabled;
            set => SetSetting(ref _isExpAlarmEnabled, value, (settings, newValue) => settings.IsExpAlarmEnabled = newValue);
        }

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

        public bool ShowExperienceLimitAlertWindow
        {
            get => _showExperienceLimitAlertWindow;
            set => SetSetting(ref _showExperienceLimitAlertWindow, value, (settings, newValue) => settings.ShowExperienceLimitAlertWindow = newValue);
        }

        public bool IsExperienceLimitProfileMode => _enableCharacterProfiles;

        public string ExperienceLimitProfile1Label => string.IsNullOrWhiteSpace(_profile1DisplayName) ? "프로필1" : _profile1DisplayName;

        public string ExperienceLimitProfile2Label => string.IsNullOrWhiteSpace(_profile2DisplayName) ? "프로필2" : _profile2DisplayName;

        public string ExperienceLimitTotalExp
        {
            get => _experienceLimitTotalExp;
            set => SetProperty(ref _experienceLimitTotalExp, value ?? "0");
        }

        public string ExperienceLimitProfile1Exp
        {
            get => _experienceLimitProfile1Exp;
            set => SetProperty(ref _experienceLimitProfile1Exp, value ?? "0");
        }

        public string ExperienceLimitProfile2Exp
        {
            get => _experienceLimitProfile2Exp;
            set => SetProperty(ref _experienceLimitProfile2Exp, value ?? "0");
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

        public bool ShowDailyWeeklyContentOverlay
        {
            get => _showDailyWeeklyContentOverlay;
            set => SetSetting(ref _showDailyWeeklyContentOverlay, value, (settings, newValue) => settings.ShowDailyWeeklyContentOverlay = newValue);
        }

        public bool ShowEtosDirectionAlert
        {
            get => _showEtosDirectionAlert;
            set => SetSetting(ref _showEtosDirectionAlert, value, (settings, newValue) => settings.ShowEtosDirectionAlert = newValue);
        }

        public bool ShowEtosHelperWindow
        {
            get => _showEtosHelperWindow;
            set => SetSetting(ref _showEtosHelperWindow, value, (settings, newValue) => settings.ShowEtosHelperWindow = newValue);
        }

        public bool EnableAbaddonRoadCountAlert
        {
            get => _enableAbaddonRoadCountAlert;
            set => SetSetting(ref _enableAbaddonRoadCountAlert, value, (settings, newValue) => settings.EnableAbaddonRoadCountAlert = newValue);
        }

        public bool ShowAbaddonRoadSummaryWindow
        {
            get => _showAbaddonRoadSummaryWindow;
            set => SetSetting(ref _showAbaddonRoadSummaryWindow, value, (settings, newValue) => settings.ShowAbaddonRoadSummaryWindow = newValue);
        }

        public bool EnableCravingPleasureCountAlert
        {
            get => _enableCravingPleasureCountAlert;
            set => SetSetting(ref _enableCravingPleasureCountAlert, value, (settings, newValue) => settings.EnableCravingPleasureCountAlert = newValue);
        }

        public bool ShowDungeonCountDisplayWindow
        {
            get => _showDungeonCountDisplayWindow;
            set => SetSetting(ref _showDungeonCountDisplayWindow, value, (settings, newValue) => settings.ShowDungeonCountDisplayWindow = newValue);
        }

        public int AbaddonRoadCountAlertDurationSeconds
        {
            get => _abaddonRoadCountAlertDurationSeconds;
            set => SetSetting(ref _abaddonRoadCountAlertDurationSeconds, value, (settings, newValue) => settings.AbaddonRoadCountAlertDurationSeconds = newValue);
        }

        public bool ShowItemDropAlert
        {
            get => _showItemDropAlert;
            set => SetSetting(ref _showItemDropAlert, value, (settings, newValue) => settings.ShowItemDropAlert = newValue);
        }

        public bool ShowItemDropHelperWindow
        {
            get => _showItemDropHelperWindow;
            set => SetSetting(ref _showItemDropHelperWindow, value, (settings, newValue) => settings.ShowItemDropHelperWindow = newValue);
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

        public bool EnableBuffTrackerAlert
        {
            get => _enableBuffTrackerAlert;
            set => SetSetting(ref _enableBuffTrackerAlert, value, (settings, newValue) => settings.EnableBuffTrackerAlert = newValue);
        }

        public bool EnableBuffTrackerEndSound
        {
            get => _enableBuffTrackerEndSound;
            set => SetSetting(ref _enableBuffTrackerEndSound, value, (settings, newValue) => settings.EnableBuffTrackerEndSound = newValue);
        }

        public bool ShowBuffTrackerWindow
        {
            get => _showBuffTrackerWindow;
            set => SetSetting(ref _showBuffTrackerWindow, value, (settings, newValue) => settings.ShowBuffTrackerWindow = newValue);
        }

        public bool EnableCharacterProfiles
        {
            get => _enableCharacterProfiles;
            set
            {
                if (SetSetting(ref _enableCharacterProfiles, value, (settings, newValue) => settings.EnableCharacterProfiles = newValue))
                {
                    OnPropertyChanged(nameof(IsExperienceLimitProfileMode));
                    RefreshExperienceLimitState();
                }
            }
        }

        public string Profile1DisplayName
        {
            get => _profile1DisplayName;
            set
            {
                if (SetSetting(ref _profile1DisplayName, value ?? "프로필1", (settings, newValue) => settings.Profile1DisplayName = newValue))
                {
                    OnPropertyChanged(nameof(ExperienceLimitProfile1Label));
                }
            }
        }

        public string Profile2DisplayName
        {
            get => _profile2DisplayName;
            set
            {
                if (SetSetting(ref _profile2DisplayName, value ?? "프로필2", (settings, newValue) => settings.Profile2DisplayName = newValue))
                {
                    OnPropertyChanged(nameof(ExperienceLimitProfile2Label));
                }
            }
        }

        public string Profile1SwitchLog
        {
            get => _profile1SwitchLog;
            set => SetSetting(ref _profile1SwitchLog, value ?? string.Empty, (settings, newValue) => settings.Profile1SwitchLog = newValue);
        }

        public string Profile2SwitchLog
        {
            get => _profile2SwitchLog;
            set => SetSetting(ref _profile2SwitchLog, value ?? string.Empty, (settings, newValue) => settings.Profile2SwitchLog = newValue);
        }

        public double ItemDropAlertVolumePercent
        {
            get => _itemDropAlertVolumePercent;
            set => SetSetting(ref _itemDropAlertVolumePercent, value, (settings, newValue) => settings.ItemDropAlertVolumePercent = newValue);
        }

        public double HighlightAlertVolumePercent
        {
            get => _highlightAlertVolumePercent;
            set => SetSetting(ref _highlightAlertVolumePercent, value, (settings, newValue) => settings.HighlightAlertVolumePercent = newValue);
        }

        public double MagicCircleAlertVolumePercent
        {
            get => _magicCircleAlertVolumePercent;
            set => SetSetting(ref _magicCircleAlertVolumePercent, value, (settings, newValue) => settings.MagicCircleAlertVolumePercent = newValue);
        }

        public double ExpBuffAlertVolumePercent
        {
            get => _expBuffAlertVolumePercent;
            set => SetSetting(ref _expBuffAlertVolumePercent, value, (settings, newValue) => settings.ExpBuffAlertVolumePercent = newValue);
        }

        public double BuffTrackerEndSoundVolumePercent
        {
            get => _buffTrackerEndSoundVolumePercent;
            set => SetSetting(ref _buffTrackerEndSoundVolumePercent, value, (settings, newValue) => settings.BuffTrackerEndSoundVolumePercent = newValue);
        }

        public double BossAlertVolumePercent
        {
            get => _bossAlertVolumePercent;
            set => SetSetting(ref _bossAlertVolumePercent, value, (settings, newValue) => settings.BossAlertVolumePercent = newValue);
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
            RefreshExperienceLimitStateCommand = new RelayCommand(_ => RefreshExperienceLimitState());
            ApplyExperienceLimitStateCommand = new RelayCommand(_ => ApplyExperienceLimitState());

            _useAlertColor = _settings.UseAlertColor;
            _useAlertSound = _settings.UseAlertSound;
            _useMagicCircleAlert = _settings.UseMagicCircleAlert;
            _keywordInput = _settings.KeywordInput;
            _showExpTracker = _settings.ShowExpTracker;
            _isExpAlarmEnabled = _settings.IsExpAlarmEnabled;
            _enableExperienceLimitAlert = _settings.EnableExperienceLimitAlert;
            _showExperienceLimitAlertWindow = _settings.ShowExperienceLimitAlertWindow;
            _expAlarmThresholdMan = _settings.ExpAlarmThreshold;
            _showDailyWeeklyContentOverlay = _settings.ShowDailyWeeklyContentOverlay;
            _showEtosDirectionAlert = _settings.ShowEtosDirectionAlert;
            _showEtosHelperWindow = _settings.ShowEtosHelperWindow;
            _enableAbaddonRoadCountAlert = _settings.EnableAbaddonRoadCountAlert;
            _showAbaddonRoadSummaryWindow = _settings.ShowAbaddonRoadSummaryWindow;
            _enableCravingPleasureCountAlert = _settings.EnableCravingPleasureCountAlert;
            _showDungeonCountDisplayWindow = _settings.ShowDungeonCountDisplayWindow;
            _abaddonRoadCountAlertDurationSeconds = _settings.AbaddonRoadCountAlertDurationSeconds;
            _showItemDropAlert = _settings.ShowItemDropAlert;
            _showItemDropHelperWindow = _settings.ShowItemDropHelperWindow;
            _useCustomDropItemFilter = _settings.UseCustomDropItemFilter;
            _customDropItemJson = _settings.CustomDropItemJson;
            _customDropItemStatus = !_useCustomDropItemFilter
                ? "기본 GitHub 드롭 테이블을 사용 중입니다."
                : "사용자 정의 필터를 사용 중입니다.";
            _enableBuffTrackerAlert = _settings.EnableBuffTrackerAlert;
            _enableBuffTrackerEndSound = _settings.EnableBuffTrackerEndSound;
            _showBuffTrackerWindow = _settings.ShowBuffTrackerWindow;
            _enableCharacterProfiles = _settings.EnableCharacterProfiles;
            _profile1DisplayName = _settings.Profile1DisplayName;
            _profile2DisplayName = _settings.Profile2DisplayName;
            _profile1SwitchLog = _settings.Profile1SwitchLog;
            _profile2SwitchLog = _settings.Profile2SwitchLog;
            _itemDropAlertVolumePercent = _settings.ItemDropAlertVolumePercent;
            _highlightAlertVolumePercent = _settings.HighlightAlertVolumePercent;
            _magicCircleAlertVolumePercent = _settings.MagicCircleAlertVolumePercent;
            _expBuffAlertVolumePercent = _settings.ExpBuffAlertVolumePercent;
            _buffTrackerEndSoundVolumePercent = _settings.BuffTrackerEndSoundVolumePercent;
            _bossAlertVolumePercent = _settings.BossAlertVolumePercent;

            ReplaceBossAlarmCards(_bossAlarmCardProvider.CreateCards());
            _ = InitializeBossAlarmCardsAsync();
            _ = InitializeDropItemFilterListsAsync();
            RefreshExperienceLimitState();
        }

        private void SaveSettings()
        {
            ConfigService.SaveDeferred(_settings);
        }

        private void RefreshExperienceLimitState()
        {
            if (!ExperienceAlertWindowService.TryGetStateSnapshot(_settings, out var snapshot))
                return;

            ExperienceLimitTotalExp = FormatExpValue(snapshot.TotalExp);
            ExperienceLimitProfile1Exp = FormatExpValue(snapshot.Profile1Exp);
            ExperienceLimitProfile2Exp = FormatExpValue(snapshot.Profile2Exp);
        }

        private void ApplyExperienceLimitState()
        {
            if (_enableCharacterProfiles)
            {
                if (!TryParseExpValue(_experienceLimitProfile1Exp, out long profile1Exp) ||
                    !TryParseExpValue(_experienceLimitProfile2Exp, out long profile2Exp))
                {
                    return;
                }

                _settings.ExperienceLimitProfile1Exp = profile1Exp;
                _settings.ExperienceLimitProfile2Exp = profile2Exp;
                if (!TryParseExpValue(_experienceLimitTotalExp, out long totalExp))
                {
                    return;
                }

                _settings.ExperienceLimitTotalExp = totalExp;
                _settings.ExperienceLimitStateInitialized = true;
                SaveSettings();
                _ = ExperienceAlertWindowService.ApplyStateSnapshot(new ExperienceAlertStateSnapshot
                {
                    IsProfileMode = true,
                    Profile1Exp = profile1Exp,
                    Profile2Exp = profile2Exp,
                    TotalExp = totalExp,
                    Profile1Label = ExperienceLimitProfile1Label,
                    Profile2Label = ExperienceLimitProfile2Label
                });
            }
            else
            {
                if (!TryParseExpValue(_experienceLimitTotalExp, out long totalExp))
                {
                    return;
                }

                _settings.ExperienceLimitTotalExp = totalExp;
                _settings.ExperienceLimitStateInitialized = true;
                SaveSettings();
                _ = ExperienceAlertWindowService.ApplyStateSnapshot(new ExperienceAlertStateSnapshot
                {
                    IsProfileMode = false,
                    TotalExp = totalExp
                });
            }

            RefreshExperienceLimitState();
        }

        private static bool TryParseExpValue(string? text, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text))
                return true;

            string normalized = text.Replace(",", string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return true;

            if (!long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                return false;

            if (value < 0)
                value = 0;

            return true;
        }

        private static string FormatExpValue(long value)
            => Math.Max(0, value).ToString("N0", CultureInfo.InvariantCulture);

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
