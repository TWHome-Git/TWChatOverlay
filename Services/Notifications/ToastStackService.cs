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

            if (!Stacks.TryGetValue(key, out var stack))
            {
                stack = new List<Window>();
                Stacks[key] = stack;
            }

            // 모드가 바뀌었을 수 있으므로 다른 그룹에서는 뺀다.
            // 같은 그룹에 이미 있으면 자리를 그대로 둔다. 경험치 알림처럼 창을 재사용하는 알림이
            // 메시지만 바꿔 다시 붙을 때 끝으로 옮기면, 옛 자리에 남은 다른 창들과 겹친다.
            foreach (var (otherKey, list) in Stacks)
            {
                if (otherKey != key)
                    list.Remove(toast);
            }
            if (!stack.Contains(toast))
                stack.Add(toast);

            if (Subscribed.Add(toast))
            {
                toast.Closed += (_, _) =>
                {
                    Subscribed.Remove(toast);
                    Detach(toast);
                };
                // Close가 아니라 Hide로 사라지는 창(트레이 최소화 등)도 자리를 비운다.
                // 서비스가 Attach 없이 다시 Show하면 스택 끝에 붙여 자리를 새로 준다.
                toast.IsVisibleChanged += (_, args) =>
                {
                    if (args.NewValue is false)
                    {
                        if (IsAttached(toast))
                            Detach(toast);
                    }
                    else if (!IsAttached(toast))
                    {
                        Attach(toast);
                        Reflow();
                    }
                };
                toast.SizeChanged += (_, _) => Reflow();
            }

            // 스택에 든 창은 아직 Show 전이라도 자리를 잡은 것으로 센다.
            // 예전엔 보이는 창만 셌는데, 두 알림이 붙어서 뜨면 서로를 빼고 같은 칸을 잡아 겹쳤다.
            var (left, top) = GetAnchorFor(key);
            double y = top + PreviewSlotHeight(key);
            foreach (Window window in stack)
            {
                if (ReferenceEquals(window, toast))
                    break;
                y += EffectiveHeight(window) + Gap;
            }

            // 붙인 창 아래에 있던 창들도 제자리로. 붙인 창 자체는 호출한 쪽이 받은 좌표로 띄운다.
            ReflowGroup(key, skip: toast);

            return (left, y);
        }

        private static bool IsAttached(Window toast)
        {
            foreach (var list in Stacks.Values)
            {
                if (list.Contains(toast))
                    return true;
            }
            return false;
        }

        /// <summary>스택에서 빼고 나머지를 다시 배치한다. Closed·Hide 양쪽에서 부른다.</summary>
        private static void Detach(Window toast)
        {
            bool removed = false;
            foreach (var list in Stacks.Values)
                removed |= list.Remove(toast);
            if (removed)
                Reflow();
        }

        /// <summary>모든 그룹을 앵커 기준으로 다시 배치한다.</summary>
        public static void Reflow()
        {
            foreach (string key in Stacks.Keys.Concat(Previews.Keys).Distinct().ToList())
            {
                try
                {
                    ReflowGroup(key);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Toast stack reflow failed for group '{key}'.", ex);
                }
            }
        }

        /// <param name="skip">자리는 세되 옮기지는 않을 창. Attach 직후 호출한 쪽이 직접 띄우는 창에 쓴다.</param>
        private static void ReflowGroup(string key, Window? skip = null)
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
                if (ReferenceEquals(window, skip))
                {
                    y += EffectiveHeight(window) + Gap;
                    continue;
                }

                // 창 하나가 옮겨지지 않아도(닫히는 중 등) 나머지는 계속 배치한다
                try
                {
                    window.Left = left;
                    MoveWindowTop(window, y);
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("Toast stack could not move a window; skipping it.", ex);
                }
                y += EffectiveHeight(window) + Gap;
            }
        }

        private static double PreviewSlotHeight(string key)
            => Previews.TryGetValue(key, out var preview) && preview.IsVisible
                ? EffectiveHeight(preview) + Gap
                : 0;

        // 아직 레이아웃 전이면 지정 높이를, 그것도 없으면(SizeToContent) 기본 높이를 쓴다. NaN이 섞이면 아래 창이 전부 제자리를 잃는다.
        private static double EffectiveHeight(Window window)
        {
            if (window.ActualHeight > 0) return window.ActualHeight;
            if (!double.IsNaN(window.Height) && window.Height > 0) return window.Height;
            if (!double.IsNaN(window.MinHeight) && window.MinHeight > 0) return window.MinHeight;
            return 72;
        }

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
