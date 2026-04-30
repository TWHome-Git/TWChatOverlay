using System;
using System.Text.RegularExpressions;
using System.Windows.Media;

namespace TWChatOverlay.Services
{
    public static class ItemLogDisplayFormatter
    {
        public static string ReplaceLeadingTimeWithDate(string original, string dateLabel)
        {
            if (string.IsNullOrWhiteSpace(original))
                return $"[{dateLabel}]";

            string body = Regex.Replace(original, @"^\[[^\]]+\]\s*", string.Empty);
            return $"[{dateLabel}] {body}";
        }

        public static string BuildItemTabText(string formattedText, string? itemName, int itemCount, string? dateLabel)
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

        public static Brush GetItemDropForeground(ItemDropGrade grade)
        {
            return grade switch
            {
                ItemDropGrade.Rare => new SolidColorBrush(Color.FromRgb(0xFF, 0xD8, 0x4A)),
                ItemDropGrade.Special => new SolidColorBrush(Color.FromRgb(0xFF, 0x7E, 0xDB)),
                _ => Brushes.White
            };
        }
    }
}
