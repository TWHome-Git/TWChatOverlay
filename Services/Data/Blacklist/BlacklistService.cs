using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    public static class BlacklistService
    {
        private static readonly object SyncRoot = new();
        private static readonly Dictionary<string, string> Rules = new(StringComparer.OrdinalIgnoreCase);

        public static event Action? BlacklistChanged;

        public static string FilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "blacklist.txt");
        public static SolidColorBrush HighlightBrush { get; } = CreateFrozenBrush(0xFF, 0x8A, 0x8A);
        public static SolidColorBrush HighlightBackgroundBrush { get; } = CreateFrozenBrush(0x45, 0x21, 0x21);

        public static void Initialize()
        {
            EnsureFileExists();
            Reload();
        }

        public static string GetRawText()
        {
            EnsureFileExists();
            return File.ReadAllText(FilePath, Encoding.UTF8);
        }

        public static void SaveRawText(string text)
        {
            EnsureFileExists();
            File.WriteAllText(FilePath, text ?? string.Empty, new UTF8Encoding(false));
            Reload();
        }

        public static void Reload()
        {
            EnsureFileExists();

            var nextRules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string[] lines = File.ReadAllLines(FilePath, Encoding.UTF8);

            foreach (string rawLine in lines)
            {
                if (IsIgnorableLine(rawLine))
                {
                    continue;
                }

                if (TryParseEntry(rawLine, out string userId, out string reason))
                {
                    nextRules[userId] = reason;
                }
            }

            lock (SyncRoot)
            {
                Rules.Clear();
                foreach (var pair in nextRules)
                {
                    Rules[pair.Key] = pair.Value;
                }
            }

            BlacklistChanged?.Invoke();
        }

        public static bool TryGetReason(string? userId, out string reason)
        {
            reason = string.Empty;
            if (string.IsNullOrWhiteSpace(userId))
            {
                return false;
            }

            lock (SyncRoot)
            {
                return Rules.TryGetValue(userId.Trim(), out reason!);
            }
        }

        public static bool TryParseEntry(string? line, out string userId, out string reason)
        {
            userId = string.Empty;
            reason = string.Empty;

            if (IsIgnorableLine(line))
            {
                return false;
            }

            string trimmed = line!.Trim();
            int separatorIndex = trimmed.IndexOf(" - ", StringComparison.Ordinal);
            int separatorLength = 3;
            if (separatorIndex < 0)
            {
                separatorIndex = trimmed.IndexOf('-', StringComparison.Ordinal);
                separatorLength = 1;
            }

            if (separatorIndex <= 0 || separatorIndex + separatorLength >= trimmed.Length)
            {
                return false;
            }

            userId = trimmed.Substring(0, separatorIndex).Trim();
            reason = trimmed.Substring(separatorIndex + separatorLength).Trim();

            return !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(reason);
        }

        private static bool IsIgnorableLine(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return true;
            }

            string trimmed = line.Trim();
            return trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal);
        }

        private static void EnsureFileExists()
        {
            string path = FilePath;
            string? directoryPath = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directoryPath) && !Directory.Exists(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            if (!File.Exists(path))
            {
                string template = "# 형식: 아이디 - 이유" + Environment.NewLine +
                                  "# 예시: 드드해 - 주의가 필요한 대상" + Environment.NewLine;
                File.WriteAllText(path, template, new UTF8Encoding(false));
            }
        }

        private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }
    }
}
