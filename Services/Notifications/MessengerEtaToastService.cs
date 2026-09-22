using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    public sealed class MessengerEtaToastService
    {
        private readonly Dictionary<string, MessengerEtaToastWindow> FileWindows = new(StringComparer.OrdinalIgnoreCase);
        private MessengerEtaToastWindow? _previewToast;
        private const double ToastWidth = 420;
        private const double DefaultBaseTop = 42;
        private const double Gap = 8;

        public void ShowForFile(string filePath, IReadOnlyList<MessengerEtaEntry> entries, ChatSettings settings)
        {
            if (AppServices.Get<TrayAllWindowsService>().IsTrayed)
                return; // 트레이 최소화 중에는 알림 창을 띄우지 않는다

            if (string.IsNullOrWhiteSpace(filePath) || entries.Count == 0)
                return;

            if (Application.Current?.Dispatcher == null)
            {
                AppLogger.Warn("Messenger toast skipped because Application.Current.Dispatcher was unavailable.");
                return;
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    AppLogger.Info($"Messenger toast render requested. File={filePath}, EntryCount={entries.Count}");
                    if (!FileWindows.TryGetValue(filePath, out MessengerEtaToastWindow? window) || !window.IsLoaded)
                    {
                        window = new MessengerEtaToastWindow(ResolveToastFont(), settings);
                        string key = filePath;
                        window.Closed += (_, _) =>
                        {
                            if (FileWindows.TryGetValue(key, out var current) && ReferenceEquals(current, window))
                                FileWindows.Remove(key);
                            RearrangeWindows(settings);
                        };
                        FileWindows[filePath] = window;
                        AppLogger.Info($"Messenger toast window created. File={filePath}");
                    }

                    window.SetEntries(entries);
                    RearrangeWindows(settings);
                }
                catch (Exception ex)
                {
                    AppLogger.Error("Messenger toast render failed inside dispatcher.", ex);
                }
            });
        }

        public void ShowPositionPreview(ChatSettings settings, bool force = false)
        {
            if (settings == null || (!force && settings.MessengerToastWindowLeft == null && settings.MessengerToastWindowTop == null))
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_previewToast == null || !_previewToast.IsLoaded)
                {
                    _previewToast = new MessengerEtaToastWindow(ResolveToastFont(), settings);
                    _previewToast.Closed += (_, _) => _previewToast = null;
                }

                _previewToast.SetEntries(new[] { new MessengerEtaEntry("아이디1", 41, "캐릭터1"), new MessengerEtaEntry("아이디2", 10, "캐릭터2") });
                _previewToast.SetPreviewMode(true);
                var (left, topBase) = ResolveBasePositionFromSettings(settings);
                _previewToast.ShowAt(left, topBase);
                RearrangeWindows(settings);
            }));
        }

        /// <summary>위치 미리보기가 떠 있으면 설정에 저장된 위치로 다시 옮긴다. 설정 전체 교체(프로필 불러오기) 뒤에 쓴다.</summary>
        public void ReapplyPreviewPosition(ChatSettings settings)
        {
            if (settings == null || _previewToast?.IsVisible != true)
                return;

            ShowPositionPreview(settings, force: true);
        }

        public void ClosePositionPreview(ChatSettings settings)
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_previewToast == null)
                    return;

                _previewToast.SaveCurrentPosition();
                _previewToast.Close();
                _previewToast = null;
                RearrangeWindows(settings);
            }));
        }

        public void SaveCurrentPosition(ChatSettings settings)
        {
            if (_previewToast?.IsVisible == true)
                _previewToast.SaveCurrentPosition();
            ConfigService.SaveDeferred(settings);
        }


        private void RearrangeWindows(ChatSettings settings)
        {
            var alive = FileWindows.Values.ToList();
            var (left, baseTop) = ResolveBasePositionFromSettings(settings);

            // 위치 미리보기가 떠 있으면 실제 창은 그 아래부터 쌓아 겹치지 않게 한다
            if (_previewToast?.IsVisible == true)
                baseTop += _previewToast.Height + Gap;

            var area = SystemParameters.WorkArea;
            for (int i = 0; i < alive.Count; i++)
            {
                MessengerEtaToastWindow window = alive[i];
                double top = baseTop + (i * (window.Height + Gap));
                double clampedLeft = Math.Max(area.Left, Math.Min(left, area.Right - window.Width));
                double clampedTop = Math.Max(area.Top, Math.Min(top, area.Bottom - window.Height));
                window.SetPreviewMode(false);
                window.ShowAt(clampedLeft, clampedTop);
                AppLogger.Info($"Messenger toast window shown. Index={i}, Left={clampedLeft:0.##}, Top={clampedTop:0.##}, Width={window.Width:0.##}, Height={window.Height:0.##}");
            }
        }

        private (double Left, double Top) ResolveBasePositionFromSettings(ChatSettings settings)
            => ToastPresentationHelper.ResolveBasePosition(
                settings.MessengerToastWindowLeft, settings.MessengerToastWindowTop, ToastWidth, DefaultBaseTop);

        private FontFamily ResolveToastFont() => ToastPresentationHelper.ResolveToastFont();
    }
}
