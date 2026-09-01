using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TWChatOverlay.Models;
using TWChatOverlay.Views;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 알림 표시 위치 관리: 외치기·던전 카운터·누적 경험치·아이템 획득·필드 보스 알림.
    /// - 통합 모드(기본): 하나의 기준 위치(앵커)에서 시작해 위→아래로 차례로 쌓인다.
    /// - 분리 모드: 알림 종류마다 각자의 저장 위치에 표시된다 (같은 종류끼리는 그 위치에서 쌓임).
    /// 앵커는 잠금 해제/설정 미리보기 창을 끌어서 옮기고 설정에 저장한다.
    /// </summary>
    public static class ToastStackService
    {
        private const double Gap = 6;
        private const double DefaultWidth = 420;
        private const double DefaultTop = 124;
        private const string UnifiedKey = "unified";

        // 그룹(통합 = 1그룹, 분리 = 종류별)별 알림 창 스택과 위치 미리보기 창
        private static readonly Dictionary<string, List<Window>> Stacks = new(StringComparer.Ordinal);
        private static readonly Dictionary<string, ToastStackPreviewWindow> Previews = new(StringComparer.Ordinal);
        private static readonly HashSet<Window> Subscribed = new();

        private static readonly (string Key, string Title)[] SeparateGroups =
        {
            ("shout", "외치기 알림 위치"),
            ("dungeon", "던전 알림 위치"),
            ("exp", "경험치 알림 위치"),
            ("item", "아이템 알림 위치"),
            ("boss", "필드 보스 알림 위치"),
        };

        private static bool IsUnified()
            => ToastPresentationHelper.FindSharedSettings()?.UnifiedToastStack != false;

        private static string GroupKeyFor(Window toast)
        {
            if (IsUnified())
                return UnifiedKey;

            return toast switch
            {
                ShoutToastWindow => "shout",
                ItemDropToastWindow => "item",
                DungeonCountDisplayWindow => "dungeon",
                BossAlertToastWindow => "boss",
                ExperienceAlertWindow => "exp",
                _ => UnifiedKey,
            };
        }

        /// <summary>그룹의 저장된 앵커 위치(없으면 화면 상단 중앙).</summary>
        private static (double Left, double Top) GetAnchorFor(string key)
        {
            ChatSettings? s = ToastPresentationHelper.FindSharedSettings();
            (double? left, double? top) = key switch
            {
                "shout" => (s?.ShoutToastWindowLeft, s?.ShoutToastWindowTop),
                "item" => (s?.ItemDropWindowLeft, s?.ItemDropWindowTop),
                "dungeon" => (s?.DungeonCountDisplayWindowLeft, s?.DungeonCountDisplayWindowTop),
                "exp" => (s?.ExperienceLimitAlertWindowLeft, s?.ExperienceLimitAlertWindowTop),
                "boss" => (s?.BossAlertToastWindowLeft, s?.BossAlertToastWindowTop),
                _ => (s?.ToastStackLeft, s?.ToastStackTop),
            };
            return ToastPresentationHelper.ResolveBasePosition(left, top, DefaultWidth, DefaultTop);
        }

        private static void SetAnchorFor(string key, ChatSettings s, double left, double top)
        {
            switch (key)
            {
                case "shout": s.ShoutToastWindowLeft = left; s.ShoutToastWindowTop = top; break;
                case "item": s.ItemDropWindowLeft = left; s.ItemDropWindowTop = top; break;
                case "dungeon": s.DungeonCountDisplayWindowLeft = left; s.DungeonCountDisplayWindowTop = top; break;
                case "exp": s.ExperienceLimitAlertWindowLeft = left; s.ExperienceLimitAlertWindowTop = top; break;
                case "boss": s.BossAlertToastWindowLeft = left; s.BossAlertToastWindowTop = top; break;
                default: s.ToastStackLeft = left; s.ToastStackTop = top; break;
            }
        }

        /// <summary>통합 모드용 앵커 (호환 유지).</summary>
        public static (double Left, double Top) GetAnchor() => GetAnchorFor(UnifiedKey);

        /// <summary>
        /// 알림 창을 스택에 등록하고 배치될 (Left, Top)을 돌려준다.
        /// 통합 모드는 하나의 스택, 분리 모드는 종류별 스택에 쌓인다. 닫히면 자동으로 재정렬.
        /// </summary>
        public static (double Left, double Top) Attach(Window toast)
        {
            string key = GroupKeyFor(toast);

            // 모드가 바뀌었을 수 있으므로 다른 그룹에서 제거 후 현재 그룹에 넣는다
            foreach (var list in Stacks.Values)
                list.Remove(toast);

            if (!Stacks.TryGetValue(key, out var stack))
            {
                stack = new List<Window>();
                Stacks[key] = stack;
            }
            stack.Add(toast);

            if (Subscribed.Add(toast))
            {
                toast.Closed += (_, _) =>
                {
                    Subscribed.Remove(toast);
                    foreach (var list in Stacks.Values)
                        list.Remove(toast);
                    Reflow();
                };
                toast.SizeChanged += (_, _) => Reflow();
            }

            var (left, top) = GetAnchorFor(key);
            double y = top + PreviewSlotHeight(key);
            foreach (Window window in stack)
            {
                if (ReferenceEquals(window, toast))
                    break;
                if (!window.IsVisible)
                    continue;
                y += EffectiveHeight(window) + Gap;
            }

            return (left, y);
        }

        /// <summary>모든 그룹을 앵커 기준으로 다시 배치한다.</summary>
        public static void Reflow()
        {
            try
            {
                foreach (string key in Stacks.Keys.Concat(Previews.Keys).Distinct().ToList())
                    ReflowGroup(key);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Toast stack reflow failed.", ex);
            }
        }

        private static void ReflowGroup(string key)
        {
            var (left, top) = GetAnchorFor(key);
            double y = top;

            if (Previews.TryGetValue(key, out var preview) && preview.IsVisible)
            {
                preview.Left = left;
                preview.Top = y;
                y += EffectiveHeight(preview) + Gap;
            }

            if (!Stacks.TryGetValue(key, out var stack))
                return;

            foreach (Window window in stack.ToList())
            {
                if (!window.IsVisible)
                    continue;

                window.Left = left;
                MoveWindowTop(window, y);
                y += EffectiveHeight(window) + Gap;
            }
        }

        private static double PreviewSlotHeight(string key)
            => Previews.TryGetValue(key, out var preview) && preview.IsVisible
                ? EffectiveHeight(preview) + Gap
                : 0;

        private static double EffectiveHeight(Window window)
            => window.ActualHeight > 0 ? window.ActualHeight : window.Height;

        private static void MoveWindowTop(Window window, double top)
        {
            // 이동 애니메이션이 있는 창은 그 경로로 (외치기/아이템)
            switch (window)
            {
                case ShoutToastWindow shout: shout.MoveTo(top); break;
                case ItemDropToastWindow item: item.MoveTo(top); break;
                default: window.Top = top; break;
            }
        }

        // ===== 위치 미리보기(앵커) =====

        /// <summary>
        /// 알림 표시 위치 미리보기를 띄운다. 통합 모드는 앵커 1개, 분리 모드는 종류별 5개.
        /// 끌어서 옮기면 즉시 저장·재정렬된다.
        /// </summary>
        public static void ShowPositionPreview(ChatSettings settings)
        {
            if (settings == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                bool unified = settings.UnifiedToastStack;

                // 현재 모드와 맞지 않는 미리보기는 정리한다 (모드 전환 대응)
                foreach (var pair in Previews.ToList())
                {
                    bool belongs = unified ? pair.Key == UnifiedKey : pair.Key != UnifiedKey;
                    if (!belongs)
                    {
                        try { pair.Value.SaveAnchor(); pair.Value.Close(); } catch { }
                    }
                }

                if (unified)
                {
                    EnsurePreview(settings, UnifiedKey, "알림 표시 위치",
                        "외치기 · 던전 · 경험치 · 아이템 · 필드 보스 알림이 여기서부터 아래로 쌓입니다");
                }
                else
                {
                    foreach (var (key, title) in SeparateGroups)
                        EnsurePreview(settings, key, title, "이 알림이 여기서부터 아래로 쌓입니다");
                }

                Reflow();
            }));
        }

        private static void EnsurePreview(ChatSettings settings, string key, string title, string subtitle)
        {
            if (!Previews.TryGetValue(key, out var preview) || !preview.IsLoaded)
            {
                preview = new ToastStackPreviewWindow(settings, key, title, subtitle);
                preview.Closed += (_, _) =>
                {
                    Previews.Remove(key);
                    Reflow();
                };
                Previews[key] = preview;
            }

            var (left, top) = GetAnchorFor(key);
            if (!preview.IsVisible)
                preview.Show();
            preview.Left = left;
            preview.Top = top;
            TopmostWindowHelper.BringToTopmost(preview);
        }

        /// <summary>미리보기를 모두 닫는다 (위치는 드래그 시점에 이미 저장됨).</summary>
        public static void ClosePositionPreview()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                foreach (var preview in Previews.Values.ToList())
                {
                    try
                    {
                        if (preview.IsVisible)
                            preview.SaveAnchor();
                        preview.Close();
                    }
                    catch { }
                }
            }));
        }

        /// <summary>미리보기가 떠 있으면 현재 위치를 앵커로 저장한다.</summary>
        public static void SaveCurrentPosition(ChatSettings settings)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                foreach (var preview in Previews.Values.ToList())
                {
                    try { preview.SaveAnchor(); } catch { }
                }
            });
        }

        /// <summary>통합/분리 모드 전환 시: 미리보기가 떠 있으면 새 모드로 다시 그린다.</summary>
        public static void RefreshPreviews(ChatSettings settings)
        {
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Previews.Values.Any(p => p.IsVisible))
                    ShowPositionPreview(settings);
                else
                    Reflow();
            }));
        }

        /// <summary>"알림 표시 위치" 앵커 미리보기 창.</summary>
        private sealed class ToastStackPreviewWindow : Window
        {
            private readonly ChatSettings _settings;
            private readonly string _groupKey;

            public ToastStackPreviewWindow(ChatSettings settings, string groupKey, string title, string subtitle)
            {
                _settings = settings;
                _groupKey = groupKey;

                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                Width = DefaultWidth;
                Height = 72;
                Title = title;
                WindowFontService.Apply(this);

                var titleText = new TextBlock
                {
                    Text = title,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                };
                titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

                var subtitleText = new TextBlock
                {
                    Text = subtitle,
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                };
                subtitleText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");

                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(titleText);
                stack.Children.Add(subtitleText);

                var root = new Border
                {
                    BorderThickness = new Thickness(1.2),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 8, 16, 9),
                    Child = stack,
                };
                root.SetResourceReference(Border.BackgroundProperty, "OverlayWindowBackgroundBrush");
                root.SetResourceReference(Border.BorderBrushProperty, "OverlayAccentBorderBrush");
                Content = root;

                // 드래그로 앵커 이동 — 이동 중에도 실제 알림들이 따라오도록 즉시 재정렬
                // 잠금 해제 모드에서는 다른 창처럼 선택 시 인스펙터(X/Y 입력·넛지)로도 편집 가능
                root.MouseLeftButtonDown += (_, e) =>
                {
                    UiLockService.Select(this);
                    if (e.ButtonState != MouseButtonState.Pressed)
                        return;
                    try { DragMove(); } catch { }
                    SaveAnchor();
                };
                LocationChanged += (_, _) =>
                {
                    if (!IsVisible)
                        return;
                    SetAnchorFor(_groupKey, _settings, Left, Top);
                    ConfigService.SaveDeferred(_settings); // 인스펙터(X/Y 입력·넛지) 이동도 저장되도록
                    ReflowGroup(_groupKey);
                };
            }

            public void SaveAnchor()
            {
                if (!IsVisible)
                    return;
                SetAnchorFor(_groupKey, _settings, Left, Top);
                ConfigService.SaveDeferred(_settings);
            }

            protected override void OnSourceInitialized(EventArgs e)
            {
                base.OnSourceInitialized(e);
                try
                {
                    IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                    int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
                    NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_TOOLWINDOW);
                }
                catch { }
            }
        }
    }
}
