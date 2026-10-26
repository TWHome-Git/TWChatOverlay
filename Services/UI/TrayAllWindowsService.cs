using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 앱의 모든 창을 한 번에 트레이로 숨기고 다시 복원합니다.
    /// 숨길 당시 보이던 창만 기억해 두었다가 복원하므로, 원래 닫혀 있던 창은 다시 뜨지 않습니다.
    /// </summary>
    public sealed class TrayAllWindowsService
    {
        private readonly List<WeakReference<Window>> _hiddenWindows = new();
        private readonly object _lock = new();
        private TWChatOverlay.Views.TrayRestoreProxyWindow? _taskbarProxy;

        public bool IsTrayed { get; private set; }

        public event Action<bool>? TrayStateChanged;

        public void Toggle()
        {
            if (IsTrayed) RestoreAll();
            else HideAll();
        }

        public void HideAll()
        {
            var app = Application.Current;
            if (app == null) return;

            app.Dispatcher.Invoke(() =>
            {
                lock (_lock)
                {
                    // 창을 숨기기 전에 먼저 트레이 상태로 둔다 — 메인 창이 숨겨지는 순간 IsVisibleChanged로 깨어나는
                    // 창 고정 서비스가 "트레이 아님"으로 보고 메인 창을 도로 띄우고 서브 채팅창까지 따라 나오게 한다
                    IsTrayed = true;
                    _hiddenWindows.Clear();
                    foreach (Window w in app.Windows.Cast<Window>().ToList())
                    {
                        try
                        {
                            if (!w.IsVisible) continue;

                            // 작업 표시줄 복원용 창은 숨기지 않는다
                            if (w is TWChatOverlay.Views.TrayRestoreProxyWindow)
                                continue;

                            _hiddenWindows.Add(new WeakReference<Window>(w));
                            w.Hide();
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Failed to hide window '{w.GetType().Name}' for tray.", ex);
                        }
                    }
                }

                // 메뉴 바까지 모두 숨는 대신, 작업 표시줄에 복원용 버튼을 남긴다
                ShowTaskbarProxy();

                AppLogger.Info($"All windows hidden to tray ({_hiddenWindows.Count}).");
                TrayStateChanged?.Invoke(true);
            });
        }

        private void ShowTaskbarProxy()
        {
            try
            {
                if (_taskbarProxy != null)
                    return;

                _taskbarProxy = new TWChatOverlay.Views.TrayRestoreProxyWindow();
                _taskbarProxy.Closed += (_, _) =>
                {
                    _taskbarProxy = null;
                    // 숨긴 채로 닫혔다면 작업 표시줄의 '창 닫기'다 → 프로그램을 끝낸다.
                    // (복원하면서 닫을 때는 RestoreAll이 IsTrayed를 먼저 풀어서 여기로 오지 않는다)
                    if (IsTrayed)
                        ExitFromTaskbar();
                };
                _taskbarProxy.Show();
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Failed to show taskbar restore proxy.", ex);
            }
        }

        /// <summary>메뉴의 종료 버튼과 같은 순서로 끝낸다.</summary>
        private static void ExitFromTaskbar()
        {
            AppLogger.Warn("Exit requested from taskbar (closed while all windows were hidden).");
            try { AppServices.Get<ChatWindowHub>().BeginShutdown(); } catch { }
            Application.Current?.Shutdown();
        }

        private void CloseTaskbarProxy()
        {
            try
            {
                var proxy = _taskbarProxy;
                _taskbarProxy = null;
                proxy?.Close();
            }
            catch { }
        }

        public void RestoreAll()
        {
            var app = Application.Current;
            if (app == null) return;

            app.Dispatcher.Invoke(() =>
            {
                lock (_lock)
                {
                    foreach (var reference in _hiddenWindows)
                    {
                        if (!reference.TryGetTarget(out var w)) continue;
                        try
                        {
                            // 숨어 있는 동안 스스로 닫힌 창(알림 토스트 등)은 다시 열 수 없다 — 조용히 건너뛴다
                            if (new System.Windows.Interop.WindowInteropHelper(w).Handle == IntPtr.Zero)
                                continue;

                            w.Show();
                            if (w.WindowState == WindowState.Minimized)
                                w.WindowState = WindowState.Normal;
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Warn($"Failed to restore window '{w.GetType().Name}' from tray.", ex);
                        }
                    }
                    _hiddenWindows.Clear();
                    IsTrayed = false;
                }

                // IsTrayed 해제 후에 닫아야 Closed 처리에서 복원이 재귀되지 않는다
                CloseTaskbarProxy();

                AppLogger.Info("All windows restored from tray.");
                TrayStateChanged?.Invoke(false);
            });
        }
    }
}
