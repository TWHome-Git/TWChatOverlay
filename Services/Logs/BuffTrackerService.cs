using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

        private readonly DispatcherTimer _timer;
        private readonly List<BuffDefinition> _definitions;
        private readonly Dictionary<string, DateTime> _activeUntil = new(StringComparer.Ordinal);
        private readonly ChatSettings _settings;

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

        public BuffTrackerService(ChatSettings settings)
        {
            _settings = settings;
            _definitions = CreateDefinitions();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (_, _) => RefreshActiveBuffs();
            _timer.Start();
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
            foreach (var definition in _definitions)
            {
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

        public void Dispose()
        {
            _timer.Stop();
        }

        private void RefreshActiveBuffs()
        {
            DateTime now = DateTime.Now;
            var rare = new List<BuffDisplayItem>();
            var exp = new List<BuffDisplayItem>();

            foreach (var definition in _definitions)
            {
                if (!_activeUntil.TryGetValue(definition.Key, out DateTime expiresAt))
                    continue;

                TimeSpan remaining = expiresAt - now;
                if (remaining <= TimeSpan.Zero)
                    continue;

                var item = new BuffDisplayItem(
                    definition.DisplayName,
                    remaining.ToString(@"mm\:ss"),
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
            }

            rare = rare.OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayName, StringComparer.Ordinal).ToList();
            exp = exp.OrderBy(x => x.SortOrder).ThenBy(x => x.DisplayName, StringComparer.Ordinal).ToList();

            ReplaceCollection(ActiveRareBuffs, rare);
            ReplaceCollection(ActiveExpBuffs, exp);

            HasRareBuffs = ActiveRareBuffs.Count > 0;
            HasExpBuffs = ActiveExpBuffs.Count > 0;
            HasAnyActiveBuffs = HasRareBuffs || HasExpBuffs;
        }

        private static void ReplaceCollection(ObservableCollection<BuffDisplayItem> target, IReadOnlyList<BuffDisplayItem> source)
        {
            target.Clear();
            foreach (var item in source)
            {
                target.Add(item);
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
