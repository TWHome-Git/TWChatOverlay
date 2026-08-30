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
    /// 통합 알림 스택: 외치기·던전 카운터·누적 경험치·아이템 획득·필드 보스 알림이
    /// 하나의 기준 위치(앵커)에서 시작해 위→아래로 차례로 쌓인다.
    /// 앵커는 잠금 해제/설정 미리보기 창("알림 표시 위치")을 끌어서 옮기고 설정에 저장한다.
    /// </summary>
    public static class ToastStackService
    {
        private const double Gap = 6;
        private const double DefaultWidth = 420;
        private const double DefaultTop = 124;

        private static readonly List<Window> Stack = new();
        private static ToastStackPreviewWindow? _preview;

        /// <summary>저장된 앵커 위치(없으면 화면 상단 중앙).</summary>
        public static (double Left, double Top) GetAnchor()
        {
            ChatSettings? settings = ToastPresentationHelper.FindSharedSettings();
            return ToastPresentationHelper.ResolveBasePosition(
                settings?.ToastStackLeft, settings?.ToastStackTop, DefaultWidth, DefaultTop);
        }

        /// <summary>
        /// 알림 창을 스택에 등록하고 배치될 (Left, Top)을 돌려준다.
        /// 이미 떠 있는 알림이 있으면 그 아래 슬롯이 배정된다. 닫히면 자동으로 재정렬.
        /// </summary>
        public static (double Left, double Top) Attach(Window toast)
        {
            if (!Stack.Contains(toast))
            {
                Stack.Add(toast);
                toast.Closed += (_, _) =>
                {
                    Stack.Remove(toast);
                    Reflow();
                };
                toast.SizeChanged += (_, _) => Reflow();
            }

            var (left, top) = GetAnchor();
            double y = top + PreviewSlotHeight();
            foreach (Window window in Stack)
            {
                if (ReferenceEquals(window, toast))
                    break;
                if (!window.IsVisible)
                    continue;
                y += EffectiveHeight(window) + Gap;
            }

            return (left, y);
        }

        /// <summary>앵커 기준으로 스택 전체를 다시 배치한다.</summary>
        public static void Reflow()
        {
            try
            {
                var (left, top) = GetAnchor();
                double y = top;

                if (_preview?.IsVisible == true)
                {
                    _preview.Left = left;
                    _preview.Top = y;
                    y += EffectiveHeight(_preview) + Gap;
                }

                foreach (Window window in Stack.ToList())
                {
                    if (!window.IsVisible)
                        continue;

                    window.Left = left;
                    MoveWindowTop(window, y);
                    y += EffectiveHeight(window) + Gap;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Toast stack reflow failed.", ex);
            }
        }

        private static double PreviewSlotHeight()
            => _preview?.IsVisible == true ? EffectiveHeight(_preview) + Gap : 0;

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

        /// <summary>알림 표시 위치 미리보기를 앵커에 띄운다. 끌어서 옮기면 즉시 저장·재정렬된다.</summary>
        public static void ShowPositionPreview(ChatSettings settings)
        {
            if (settings == null)
                return;

            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_preview == null || !_preview.IsLoaded)
                {
                    _preview = new ToastStackPreviewWindow(settings);
                    _preview.Closed += (_, _) =>
                    {
                        _preview = null;
                        Reflow();
                    };
                }

                var (left, top) = GetAnchor();
                if (!_preview.IsVisible)
                    _preview.Show();
                _preview.Left = left;
                _preview.Top = top;
                TopmostWindowHelper.BringToTopmost(_preview);
                Reflow();
            }));
        }

        /// <summary>미리보기를 닫는다 (앵커는 드래그 시점에 이미 저장됨).</summary>
        public static void ClosePositionPreview()
        {
            Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (_preview?.IsVisible == true)
                    {
                        _preview.SaveAnchor();
                        _preview.Close();
                    }
                }
                catch { }
            }));
        }

        /// <summary>미리보기가 떠 있으면 현재 위치를 앵커로 저장한다.</summary>
        public static void SaveCurrentPosition(ChatSettings settings)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                try { _preview?.SaveAnchor(); } catch { }
            });
        }

        /// <summary>"알림 표시 위치" 앵커 미리보기 창.</summary>
        private sealed class ToastStackPreviewWindow : Window
        {
            private readonly ChatSettings _settings;

            public ToastStackPreviewWindow(ChatSettings settings)
            {
                _settings = settings;

                WindowStyle = WindowStyle.None;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                ResizeMode = ResizeMode.NoResize;
                Width = DefaultWidth;
                Height = 72;
                WindowFontService.Apply(this);

                var title = new TextBlock
                {
                    Text = "알림 표시 위치",
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                };
                title.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");

                var subtitle = new TextBlock
                {
                    Text = "외치기 · 던전 · 경험치 · 아이템 · 필드 보스 알림이 여기서부터 아래로 쌓입니다",
                    FontSize = 11,
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0),
                };
                subtitle.SetResourceReference(TextBlock.ForegroundProperty, "OverlayHintTextBrush");

                var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(title);
                stack.Children.Add(subtitle);

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
                root.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ButtonState != MouseButtonState.Pressed)
                        return;
                    try { DragMove(); } catch { }
                    SaveAnchor();
                };
                LocationChanged += (_, _) =>
                {
                    if (!IsVisible)
                        return;
                    _settings.ToastStackLeft = Left;
                    _settings.ToastStackTop = Top;
                    Reflow();
                };
            }

            public void SaveAnchor()
            {
                if (!IsVisible)
                    return;
                _settings.ToastStackLeft = Left;
                _settings.ToastStackTop = Top;
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
