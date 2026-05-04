using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TWChatOverlay.Models;
using TWChatOverlay.ViewModels;

namespace TWChatOverlay.Services
{
    public sealed class BuffTrackerService : ViewModelBase, IDisposable
    {
        private static readonly Regex TimeRegex = new(@"\[(?:\s*(?<h1>\d{1,2})시\s*(?<m1>\d{1,2})분\s*(?<s1>\d{1,2})초|(?<h2>\d{1,2}):(?<m2>\d{2}):(?<s2>\d{2}))\]", RegexOptions.Compiled);
        private static readonly Regex MagicEyeStartRegex = new(@"마법의 눈을 사용하였습니다\.\s*\[\s*(?:(?<hours>\d+)시간\s*)?(?<minutes>\d+)분\s*(?<seconds>\d+)초\s*\]\s*시간 동안", RegexOptions.Compiled);
        private static readonly Regex MagicEyeSavedRegex = new(@"마법의 눈 유지시간이 저장되었습니다\.\s*\(보유 시간\s*:\s*\[\s*(?:(?<hours>\d+)시간\s*)?(?<minutes>\d+)분\s*(?<seconds>\d+)초\s*\]\)", RegexOptions.Compiled);
        private const string MagicEyeKey = "rare-magic-eye";
        private static readonly string StateFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Cache", "buff_tracker_state.json");

        private readonly DispatcherTimer _timer;
        private readonly List<BuffDefinition> _definitions;
        private readonly Dictionary<string, DateTime> _activeUntil = new(StringComparer.Ordinal);
        private readonly Dictionary<string, TimeSpan> _pausedRemaining = new(StringComparer.Ordinal);
        private readonly ChatSettings _settings;
        private readonly bool _suppressEndSound;
        private DateTime _lastBuffEndSoundAt = DateTime.MinValue;

        private bool _hasAnyActiveBuffs;
        private bool _hasRareBuffs;
        private bool _hasExpBuffs;

        public ObservableCollection<BuffDisplayItem> ActiveRareBuffs { get; } = new();
        public ObservableCollection<BuffDisplayItem> ActiveExpBuffs { get; } = new();

        public bool HasAnyActiveBuffs
        {
            get => _hasAnyActiveBuffs;
            private set
            {
                if (SetProperty(ref _hasAnyActiveBuffs, value))
                {
                    OnPropertyChanged(nameof(HasNoActiveBuffs));
                }
            }
        }

        public bool HasNoActiveBuffs => !HasAnyActiveBuffs;

        public bool HasRareBuffs
        {
            get => _hasRareBuffs;
            private set => SetProperty(ref _hasRareBuffs, value);
        }

        public bool HasExpBuffs
        {
            get => _hasExpBuffs;
            private set => SetProperty(ref _hasExpBuffs, value);
        }

        public BuffTrackerService(ChatSettings settings, bool suppressEndSound = false)
        {
            _settings = settings;
            _suppressEndSound = suppressEndSound;
            _definitions = CreateDefinitions();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => RefreshActiveBuffs();
            LoadState();
            _timer.Start();
            RefreshActiveBuffs();
        }

        public void ProcessLog(string formattedText)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
            {
                AppLogger.Debug("Buff tracker skipped empty formatted text.");
                return;
            }

            DateTime? occurredAt = TryParseOccurredAt(formattedText);
            if (!occurredAt.HasValue)
            {
                AppLogger.Debug($"Buff tracker could not parse time from '{formattedText}'.");
                return;
            }

            bool matchedAny = false;
            if (TryProcessMagicEye(formattedText, occurredAt.Value))
            {
                matchedAny = true;
            }

            foreach (var definition in _definitions)
            {
                if (definition.Key == MagicEyeKey)
                    continue;

                if (!definition.IsMatch(formattedText))
                    continue;

                matchedAny = true;
                DateTime expiresAt = occurredAt.Value.Add(definition.Duration);
                if (_activeUntil.TryGetValue(definition.Key, out DateTime existing) && existing >= expiresAt)
                {
                    AppLogger.Debug($"Buff tracker ignored older duplicate buff '{definition.Key}'. ExistingUntil={existing:HH:mm:ss}, NewUntil={expiresAt:HH:mm:ss}");
                    continue;
                }

                _activeUntil[definition.Key] = expiresAt;
                _pausedRemaining.Remove(definition.Key);
                AppLogger.Info($"Buff tracker matched '{definition.Key}'. OccurredAt={occurredAt:HH:mm:ss}, ExpiresAt={expiresAt:HH:mm:ss}");
            }

            if (!matchedAny)
            {
                AppLogger.Debug($"Buff tracker found no match in '{formattedText}'.");
            }

            RefreshActiveBuffs();
        }

        public void ProcessLog(LogAnalysisResult analysis)
        {
            if (!analysis.ShouldRunBuffTracker)
                return;

            ProcessLog(analysis.Parsed.FormattedText);
        }

        public bool IsTrackableBuffLog(string? formattedText)
        {
            if (string.IsNullOrWhiteSpace(formattedText))
                return false;

            if (MagicEyeStartRegex.IsMatch(formattedText) || MagicEyeSavedRegex.IsMatch(formattedText))
                return true;

            foreach (var definition in _definitions)
            {
                if (definition.IsMatch(formattedText))
                    return true;
            }

            return false;
        }

        public void Dispose()
        {
            _timer.Stop();
        }

        private void RefreshActiveBuffs()
        {
            DateTime now = DateTime.Now;
            var rare = new List<BuffDisplayItem>();
            var exp = new List<BuffDisplayItem>();
            bool expiredBuffFound = false;

            foreach (var definition in _definitions)
            {
                if (!_activeUntil.TryGetValue(definition.Key, out DateTime expiresAt))
                {
                    if (!_pausedRemaining.TryGetValue(definition.Key, out TimeSpan pausedRemaining))
                        continue;

                    var pausedItem = new BuffDisplayItem(
                        definition.DisplayName,
                        FormatRemaining(pausedRemaining),
                        definition.IconSource,
                        definition.SortOrder);

                    if (definition.Category == BuffCategory.Rare)
                        rare.Add(pausedItem);
                    else
                        exp.Add(pausedItem);

                    continue;
                }

                TimeSpan remaining = expiresAt - now;
                if (remaining <= TimeSpan.Zero)
                    continue;

                var item = new BuffDisplayItem(
                    definition.DisplayName,
                    FormatRemaining(remaining),
                    definition.IconSource,
                    definition.SortOrder);

                if (definition.Category == BuffCategory.Rare)
                    rare.Add(item);
                else
                    exp.Add(item);
            }

            foreach (var expiredKey in _activeUntil.Where(x => x.Value <= now).Select(x => x.Key).ToList())
            {
                _activeUntil.Remove(expiredKey);
                expiredBuffFound = true;
                if (expiredKey == MagicEyeKey)
                {
                    DeleteState();
                }
            }

            if (expiredBuffFound)
            {
                TryPlayBuffEndSound(now);
            }

            rare = rare.OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayName, StringComparer.Ordinal).ToList();
            exp = exp.OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayName, StringComparer.Ordinal).ToList();

            ReplaceCollection(ActiveRareBuffs, rare);
            ReplaceCollection(ActiveExpBuffs, exp);

            HasRareBuffs = ActiveRareBuffs.Count > 0;
            HasExpBuffs = ActiveExpBuffs.Count > 0;
            HasAnyActiveBuffs = HasRareBuffs || HasExpBuffs;
        }

        private void TryPlayBuffEndSound(DateTime now)
        {
            if (_suppressEndSound)
                return;

            if (!_settings.EnableBuffTrackerAlert || !_settings.EnableBuffTrackerEndSound)
                return;

            if (_lastBuffEndSoundAt != DateTime.MinValue &&
                now - _lastBuffEndSoundAt < TimeSpan.FromSeconds(10))
            {
                return;
            }

            _lastBuffEndSoundAt = now;
            NotificationService.PlayAlert("BuffCheck.wav");
        }

        private static void ReplaceCollection(ObservableCollection<BuffDisplayItem> target, IReadOnlyList<BuffDisplayItem> source)
        {
            target.Clear();
            foreach (var item in source)
            {
                target.Add(item);
            }
        }

        private bool TryProcessMagicEye(string text, DateTime occurredAt)
        {
            var startMatch = MagicEyeStartRegex.Match(text);
            if (startMatch.Success && TryParseDuration(startMatch, out TimeSpan startDuration))
            {
                _pausedRemaining.Remove(MagicEyeKey);
                _activeUntil[MagicEyeKey] = occurredAt.Add(startDuration);
                DeleteState();
                AppLogger.Info($"Buff tracker matched magic eye start. Duration={startDuration}, OccurredAt={occurredAt:HH:mm:ss}");
                return true;
            }

            var savedMatch = MagicEyeSavedRegex.Match(text);
            if (savedMatch.Success && TryParseDuration(savedMatch, out TimeSpan savedDuration))
            {
                _activeUntil.Remove(MagicEyeKey);
                if (savedDuration > TimeSpan.Zero)
                {
                    _pausedRemaining[MagicEyeKey] = savedDuration;
                    SaveState(savedDuration);
                }
                else
                {
                    _pausedRemaining.Remove(MagicEyeKey);
                    DeleteState();
                }
                AppLogger.Info($"Buff tracker matched magic eye saved. Remaining={savedDuration}");
                return true;
            }

            return false;
        }

        private static bool TryParseDuration(Match match, out TimeSpan duration)
        {
            duration = TimeSpan.Zero;
            int hours = 0;

            if (match.Groups["hours"].Success &&
                !int.TryParse(match.Groups["hours"].Value, out hours))
            {
                return false;
            }

            if (!int.TryParse(match.Groups["minutes"].Value, out int minutes) ||
                !int.TryParse(match.Groups["seconds"].Value, out int seconds))
            {
                return false;
            }

            duration = new TimeSpan(hours, minutes, seconds);
            return duration >= TimeSpan.Zero;
        }

        private static string FormatRemaining(TimeSpan remaining)
            => remaining.TotalHours >= 1
                ? remaining.ToString(@"h\:mm\:ss")
                : remaining.ToString(@"mm\:ss");

        private void LoadState()
        {
            try
            {
                if (!File.Exists(StateFilePath))
                    return;

                string json = File.ReadAllText(StateFilePath);
                var state = JsonSerializer.Deserialize<BuffTrackerState>(json);
                if (state?.MagicEyeRemainingSeconds > 0)
                {
                    _pausedRemaining[MagicEyeKey] = TimeSpan.FromSeconds(state.MagicEyeRemainingSeconds);
                    AppLogger.Info($"Buff tracker restored magic eye remaining. Seconds={state.MagicEyeRemainingSeconds}");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load buff tracker state.", ex);
            }
        }

        private static void SaveState(TimeSpan magicEyeRemaining)
        {
            try
            {
                long seconds = Math.Max(0, (long)Math.Round(magicEyeRemaining.TotalSeconds));
                if (seconds <= 0)
                {
                    DeleteState();
                    return;
                }

                string? directory = Path.GetDirectoryName(StateFilePath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var state = new BuffTrackerState
                {
                    MagicEyeRemainingSeconds = seconds,
                    SavedAt = DateTimeOffset.Now
                };

                File.WriteAllText(StateFilePath, JsonSerializer.Serialize(state));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to save buff tracker state.", ex);
            }
        }

        private static void DeleteState()
        {
            try
            {
                if (File.Exists(StateFilePath))
                {
                    File.Delete(StateFilePath);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to delete buff tracker state.", ex);
            }
        }

        private static DateTime? TryParseOccurredAt(string formattedText)
        {
            var match = TimeRegex.Match(formattedText);
            if (!match.Success)
                return null;

            string hourText = match.Groups["h1"].Success ? match.Groups["h1"].Value : match.Groups["h2"].Value;
            string minuteText = match.Groups["m1"].Success ? match.Groups["m1"].Value : match.Groups["m2"].Value;
            string secondText = match.Groups["s1"].Success ? match.Groups["s1"].Value : match.Groups["s2"].Value;

            if (!int.TryParse(hourText, out int hour) ||
                !int.TryParse(minuteText, out int minute) ||
                !int.TryParse(secondText, out int second))
            {
                return null;
            }

            DateTime occurredAt = DateTime.Today
                .AddHours(hour)
                .AddMinutes(minute)
                .AddSeconds(second);

            if (occurredAt > DateTime.Now.AddMinutes(1))
            {
                occurredAt = occurredAt.AddDays(-1);
            }

            return occurredAt;
        }

        private static List<BuffDefinition> CreateDefinitions()
        {
            return new List<BuffDefinition>
            {
                new("rare-heart", BuffCategory.Rare, "레어의 심장", TimeSpan.FromMinutes(20), "레어의 심장를 사용하였습니다.", "pack://application:,,,/Data/images/Buff/RareHeart.png", 0),
                new("rare-loto", BuffCategory.Rare, "로토의 부적", TimeSpan.FromMinutes(30), "로토의 부적", "pack://application:,,,/Data/images/Buff/Roto.png", 1, "아이템을 사용하셨습니다"),
                new(MagicEyeKey, BuffCategory.Rare, "마법의 눈", TimeSpan.Zero, "마법의 눈", "pack://application:,,,/Data/images/Buff/마법의눈.png", -1),
                new("rare-r2", BuffCategory.Rare, "클럽 버프 R-2", TimeSpan.FromMinutes(30), "클럽 상점 버프 Type R-2", "pack://application:,,,/Data/images/Buff/Club.png", 99, "아이템을 사용하셨습니다"),
                new("rare-r1", BuffCategory.Rare, "클럽 버프 R-1", TimeSpan.FromMinutes(30), "클럽 상점 버프 Type R-1", "pack://application:,,,/Data/images/Buff/Club.png", 99, "아이템을 사용하셨습니다"),

                new("exp-heart", BuffCategory.Exp, "경험의 심장", TimeSpan.FromMinutes(20), "경험의 심장를 사용하였습니다.", "pack://application:,,,/Data/images/Buff/ExpHeart.png", 0),
                new("exp-eos", BuffCategory.Exp, "최상급 에오스", TimeSpan.FromMinutes(30), "최상급 에오스의 파편", "pack://application:,,,/Data/images/Buff/Eos.png", 1, "아이템을 사용하셨습니다"),
                new("exp-earlybird", BuffCategory.Exp, "얼리버드 부스터", TimeSpan.FromMinutes(30), "얼리버드 경험치 부스터", "pack://application:,,,/Data/images/Buff/Bird.png", 2, "아이템을 사용하셨습니다"),
                new("exp-sweetpotato", BuffCategory.Exp, "전설의 군고구마", TimeSpan.FromMinutes(30), "전설의 군고구마", "pack://application:,,,/Data/images/Buff/Sweet.png", 3, "아이템을 사용하셨습니다"),
                new("exp-e2", BuffCategory.Exp, "클럽 버프 E-2", TimeSpan.FromMinutes(30), "클럽 상점 버프 Type E-2", "pack://application:,,,/Data/images/Buff/Club.png", 99, "아이템을 사용하셨습니다"),
                new("exp-e1", BuffCategory.Exp, "클럽 버프 E-1", TimeSpan.FromMinutes(30), "클럽 상점 버프 Type E-1", "pack://application:,,,/Data/images/Buff/Club.png", 99, "아이템을 사용하셨습니다"),
            };
        }

        private enum BuffCategory
        {
            Rare,
            Exp
        }

        private sealed class BuffDefinition
        {
            public BuffDefinition(string key, BuffCategory category, string displayName, TimeSpan duration, string contains, string iconUri, int sortOrder, params string[] requiredTokens)
            {
                Key = key;
                Category = category;
                DisplayName = displayName;
                Duration = duration;
                Contains = contains;
                IconSource = CreateImage(iconUri);
                SortOrder = sortOrder;
                RequiredTokens = requiredTokens ?? Array.Empty<string>();
            }

            public string Key { get; }
            public BuffCategory Category { get; }
            public string DisplayName { get; }
            public TimeSpan Duration { get; }
            public string Contains { get; }
            public ImageSource IconSource { get; }
            public int SortOrder { get; }
            public IReadOnlyList<string> RequiredTokens { get; }

            public bool IsMatch(string text)
            {
                if (!text.Contains(Contains, StringComparison.Ordinal))
                {
                    return false;
                }

                for (int i = 0; i < RequiredTokens.Count; i++)
                {
                    if (!text.Contains(RequiredTokens[i], StringComparison.Ordinal))
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        private sealed class BuffTrackerState
        {
            public long MagicEyeRemainingSeconds { get; set; }
            public DateTimeOffset SavedAt { get; set; }
        }

        public sealed class BuffDisplayItem
        {
            public BuffDisplayItem(string displayName, string remainingText, ImageSource iconSource, int sortOrder)
            {
                DisplayName = displayName;
                RemainingText = remainingText;
                IconSource = iconSource;
                SortOrder = sortOrder;
            }

            public string DisplayName { get; }
            public string RemainingText { get; }
            public ImageSource IconSource { get; }
            public int SortOrder { get; }
        }

        private static BitmapImage CreateImage(string uri)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(uri, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            if (image.CanFreeze)
                image.Freeze();
            return image;
        }
    }
}
