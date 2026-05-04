using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
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
using TWChatOverlay.Services.LogAnalysis;
using TWChatOverlay.ViewModels;

namespace TWChatOverlay.Views
{
    public partial class MainWindow
    {
        #region UI Methods

        private void ApplyInitialSettings()
        {
            if (_settings == null || MainBorder == null) return;

            this.Width = _settings.WindowWidth;
            this.Height = _settings.WindowHeight;

            FontFamily nextFont = FontService.GetFont(_settings.FontFamily);
            this.CurrentFont = nextFont;
            this.CurrentFontSize = _settings.FontSize;

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
            => InjectDebugLogText(rawText, DebugLogCategory.System);

        public void InjectDebugLogText(string rawText, DebugLogCategory category)
            => InjectDebugLogText(rawText, category, forceRealTime: true);

        public void InjectDebugLogText(string rawText, DebugLogCategory category, bool forceRealTime)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return;

            if (_logService == null)
            {
                AppLogger.Warn("Debug log injection skipped because LogService is not initialized yet.");
                return;
            }

            string payload = BuildDebugLogPayload(rawText, category);

            if (forceRealTime)
            {
                _uiLogBatchDispatcher.Enqueue(payload, true, ProcessUiLogBatch);
                return;
            }

            _logService.InjectTestContent(payload);
        }

        private static string BuildDebugLogPayload(string rawText, DebugLogCategory category)
        {
            if (HtmlFontTagRegex.IsMatch(rawText))
                return rawText;

            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            string contentColor = category switch
            {
                DebugLogCategory.Normal => "ffffff",
                DebugLogCategory.Team => "f7b73c",
                DebugLogCategory.Club => "94ddfa",
                DebugLogCategory.System => "ff64ff",
                DebugLogCategory.Shout => "c896c8",
                _ => "ffffff"
            };

            var lines = rawText
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => $@"<font color=""ffffff"">[{timestamp}]</font><font color=""{contentColor}"">{System.Net.WebUtility.HtmlEncode(x)}</font>");

            return string.Join("<br>", lines);
        }

        private static string? GetShoutNicknameForClipboard(LogParser.ParseResult parseResult)
        {
            if (parseResult == null)
                return null;

            if (!string.IsNullOrWhiteSpace(parseResult.SenderId))
                return parseResult.SenderId.Trim();

            string formattedText = parseResult.FormattedText ?? string.Empty;
            var bracketMatches = Regex.Matches(formattedText, @"\[(?<value>[^\[\]]+)\]");
            for (int i = bracketMatches.Count - 1; i >= 0; i--)
            {
                string candidate = bracketMatches[i].Groups["value"].Value.Trim();
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                if (IsTimestampText(candidate) || IsEtaLevelText(candidate))
                    continue;

                return candidate;
            }

            var colonMatch = Regex.Match(formattedText, @"(?:^|\]\s*)(?<nickname>[^:\[\]]{1,40})\s*:");
            if (colonMatch.Success)
            {
                string candidate = colonMatch.Groups["nickname"].Value.Trim();
                if (!string.IsNullOrWhiteSpace(candidate))
                    return candidate;
            }

            return null;
        }

        private static bool IsTimestampText(string value)
            => Regex.IsMatch(value, @"^\d{1,2}:\d{2}(?::\d{2})?$");

        private static bool IsEtaLevelText(string value)
            => int.TryParse(value, out int level) && level is >= 1 and <= 9999;

        private static void TrySetClipboardText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            string normalized = text.Trim();
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var dataObject = new DataObject();
                    dataObject.SetText(normalized, TextDataFormat.UnicodeText);
                    Clipboard.SetDataObject(dataObject, true);
                    AppLogger.Info($"Copied shout nickname to clipboard. Nickname='{normalized}'");
                    return;
                }
                catch (Exception ex)
                {
                    if (attempt == 2)
                        AppLogger.Warn($"Failed to copy shout nickname to clipboard. Nickname='{normalized}'", ex);

                    System.Threading.Thread.Sleep(20);
                }
            }
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

        private void WarmUpDefaultDropItemFilterSnapshot()
        {
            try
            {
                _ = GetDefaultDropItemFilterSnapshot();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to warm up default drop item filter snapshot.", ex);
            }
        }

        private DropItemResolver.DropItemFilterSnapshot GetDefaultDropItemFilterSnapshot()
        {
            if (_defaultDropItemFilterSnapshot != null)
                return _defaultDropItemFilterSnapshot;

            var snapshot = DropItemResolver.LoadDefaultFilterSnapshotAsync().GetAwaiter().GetResult();
            lock (_defaultDropItemFilterLock)
            {
                _defaultDropItemFilterSnapshot ??= snapshot;
                return _defaultDropItemFilterSnapshot;
            }
        }

        private void TryLoadTestDropItemJsonForSession()
        {
            try
            {
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                string[] candidateNames = ["Droptime.json", "DropItem.json", "DropItem.Json"];

                foreach (string candidateName in candidateNames)
                {
                    string path = Path.Combine(downloads, candidateName);
                    if (!File.Exists(path))
                        continue;

                    string json = File.ReadAllText(path, Encoding.UTF8);
                    if (DropItemResolver.TryCreateFilterSnapshot(json, out _))
                    {
                        _settings.UseCustomDropItemFilter = true;
                        _settings.CustomDropItemJson = json;
                        AppLogger.Info($"Loaded session DropItem JSON from '{path}'.");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to load session DropItem JSON.", ex);
            }
        }

        private void ReleaseMouseForce()
        {
            try
            {
                if (Mouse.Captured != null)
                {
                    Mouse.Capture(null);
                }
            }
            catch { }
        }

        private void ConfirmExit()
        {
            Application.Current.Shutdown();
        }

        #endregion
    }
}
