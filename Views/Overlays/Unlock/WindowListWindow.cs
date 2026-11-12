using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Unlock
{
    /// <summary>
    /// 잠금 해제 모드의 창 목록. 지금 떠 있는 창을 이름으로 보여 주고,
    ///  - 줄을 누르면 그 창을 보이게 하고 선택한다 (다른 창 밑에 깔렸거나 화면 밖으로 나간 창도 잡을 수 있다)
    ///  - 선택된 줄을 한 번 더 누르면 그 창을 배치하는 동안만 숨긴다 (설정은 바뀌지 않고, 잠그면 전부 다시 보인다)
    /// 메뉴바 옆에 뜬다 — 세로 메뉴바면 오른쪽, 가로 메뉴바면 아래쪽. 머리 부분을 끌어 옮길 수 있다 (잠금 해제 동안만).
    /// </summary>
    internal sealed class WindowListWindow : Window
    {
        private const double PanelWidth = 220;
        private const double DefaultMargin = 12;
        /// <summary>메뉴바와의 간격.</summary>
        private const double MenuBarGap = 6;

        private static readonly SolidColorBrush PanelBgBrush = UnlockWindowStyle.Frozen(Color.FromArgb(0xF2, 0x10, 0x16, 0x14));
        private static readonly SolidColorBrush HeaderBgBrush = UnlockWindowStyle.Frozen(Color.FromRgb(0x19, 0x21, 0x1D));
        private static readonly SolidColorBrush FieldBgBrush = UnlockWindowStyle.Frozen(Color.FromRgb(0x0D, 0x12, 0x10));
        private static readonly SolidColorBrush BorderBrushShared = UnlockWindowStyle.Frozen(Color.FromRgb(0x2A, 0x33, 0x2E));
        private static readonly SolidColorBrush CheckBorderBrush = UnlockWindowStyle.Frozen(Color.FromRgb(0x3A, 0x46, 0x40));
        private static readonly SolidColorBrush TextBrush = UnlockWindowStyle.Frozen(Color.FromRgb(0xE8, 0xEA, 0xE9));
        private static readonly SolidColorBrush SubTextBrush = UnlockWindowStyle.Frozen(Color.FromRgb(0x8C, 0x91, 0x97));
        private static readonly SolidColorBrush MintBrush = UnlockWindowStyle.Frozen(Color.FromRgb(0x0C, 0xD2, 0x9D));
        private static readonly SolidColorBrush RowHoverBrush = UnlockWindowStyle.Frozen(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF));
        private static readonly SolidColorBrush RowSelectedBrush = UnlockWindowStyle.Frozen(Color.FromArgb(0x1F, 0x0C, 0xD2, 0x9D));
        private static readonly Geometry CheckMark = CreateCheckMark();

        private readonly TextBlock _title;
        private readonly StackPanel _rows;
        private readonly DispatcherTimer _refreshTimer;
        private MenuWindow? _menuBar;
        // 끌어 옮긴 뒤에는 메뉴바를 따라가지 않는다 (다음에 잠금 해제할 때는 다시 메뉴바 옆에 뜬다)
        private bool _movedByUser;
        // 목록 내용이 그대로면 줄을 다시 만들지 않는다 (마우스를 올려 둔 줄이 깜빡이지 않게)
        private string _signature = "\0";

        public WindowListWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = PanelWidth;
            SizeToContent = SizeToContent.Height;

            _title = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var header = new Border
            {
                Background = HeaderBgBrush,
                Padding = new Thickness(10, 7, 10, 7),
                Cursor = Cursors.SizeAll,
                ToolTip = "끌어서 옮기기",
                Child = _title,
            };
            header.MouseLeftButtonDown += Header_MouseLeftButtonDown;

            Button showAll = CreateButton("전체 보이기");
            showAll.Click += (_, _) =>
            {
                Service.SetAllListedHidden(false);
                Refresh();
            };
            Button hideAll = CreateButton("전체 숨기기");
            hideAll.Margin = new Thickness(6, 0, 0, 0);
            hideAll.Click += (_, _) =>
            {
                Service.SetAllListedHidden(true);
                Refresh();
            };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 8, 8, 6),
                Children = { showAll, hideAll },
            };

            _rows = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
            var scroll = new ScrollViewer
            {
                Content = _rows,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                // 창이 많아도 화면을 넘지 않게
                MaxHeight = Math.Max(160, SystemParameters.PrimaryScreenHeight * 0.6),
                Focusable = false,
            };
            scroll.SetResourceReference(StyleProperty, "OverlayScrollViewerStyle");

            var hint = new TextBlock
            {
                Text = "누르면 그 창을 보이게 하고 선택합니다. 선택된 줄을 한 번 더 누르면 배치하는 동안만 숨깁니다.",
                FontSize = 10.5,
                Foreground = SubTextBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(10, 6, 10, 8),
            };

            Content = new Border
            {
                Background = PanelBgBrush,
                BorderBrush = MintBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                SnapsToDevicePixels = true,
                Child = new StackPanel { Children = { header, buttons, scroll, hint } },
            };

            PositionNextToMenuBar();

            // 창이 새로 뜨거나 닫히는 것을 따라가고, 숨겨 둔 종류의 창이 다시 만들어지면 그것도 가린다
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _refreshTimer.Tick += (_, _) =>
            {
                Service.ApplyHiddenState();
                Refresh();
            };

            Loaded += (_, _) =>
            {
                Service.SelectionChanged += Refresh;
                Refresh();
                _refreshTimer.Start();

                // 메뉴바를 옮기거나 가로·세로를 바꾸면 따라간다
                _menuBar = FindMenuBar();
                if (_menuBar != null)
                {
                    _menuBar.LocationChanged += MenuBar_BoundsChanged;
                    _menuBar.SizeChanged += MenuBar_SizeChanged;
                }
                PositionNextToMenuBar();
            };
            // 줄 수에 따라 높이가 바뀌면 자리를 다시 잡는다 (화면 아래로 넘치지 않게)
            SizeChanged += (_, _) => PositionNextToMenuBar();
            Closed += (_, _) =>
            {
                _refreshTimer.Stop();
                Service.SelectionChanged -= Refresh;
                if (_menuBar != null)
                {
                    _menuBar.LocationChanged -= MenuBar_BoundsChanged;
                    _menuBar.SizeChanged -= MenuBar_SizeChanged;
                    _menuBar = null;
                }
            };

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape)
                    Service.Set(false);
            };
        }

        private static UiLockService Service => AppServices.Get<UiLockService>();

        /// <summary>목록을 지금 상태로 맞춘다. 내용이 그대로면 아무것도 하지 않는다.</summary>
        internal void Refresh()
        {
            IReadOnlyList<UiLockService.ListedWindow> rows = Service.GetListedWindows();
            string signature = string.Join("\n", rows.Select(row => $"{row.Key}|{row.Name}|{row.IsHidden}|{row.IsSelected}"));
            if (signature == _signature)
                return;
            _signature = signature;

            int shown = rows.Count(row => !row.IsHidden);
            _title.Text = shown == rows.Count ? $"창 목록 ({rows.Count})" : $"창 목록 ({shown}/{rows.Count})";

            _rows.Children.Clear();
            foreach (UiLockService.ListedWindow row in rows)
                _rows.Children.Add(CreateRow(row));

            if (rows.Count == 0)
            {
                _rows.Children.Add(new TextBlock
                {
                    Text = "떠 있는 창이 없습니다",
                    FontSize = 12,
                    Foreground = SubTextBrush,
                    Margin = new Thickness(4, 4, 4, 4),
                });
            }
        }

        /// <summary>
        /// 목록의 한 줄. 줄 전체가 하나의 누름 대상이다: 누르면 그 창을 보이게 하고 선택하며,
        /// 이미 선택된 줄을 한 번 더 누르면 배치하는 동안 숨긴다. 체크 표시는 보이는지만 나타낸다.
        /// </summary>
        private FrameworkElement CreateRow(UiLockService.ListedWindow row)
        {
            var check = new Border
            {
                Width = 16,
                Height = 16,
                Margin = new Thickness(4, 0, 8, 0),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(1),
                BorderBrush = row.IsHidden ? CheckBorderBrush : MintBrush,
                Background = FieldBgBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Child = new Path
                {
                    Data = CheckMark,
                    Stroke = MintBrush,
                    StrokeThickness = 1.8,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    StrokeLineJoin = PenLineJoin.Round,
                    Visibility = row.IsHidden ? Visibility.Collapsed : Visibility.Visible,
                },
            };

            var name = new TextBlock
            {
                Text = row.Name,
                FontSize = 12,
                FontWeight = row.IsSelected ? FontWeights.Bold : FontWeights.Normal,
                Foreground = row.IsSelected ? MintBrush : row.IsHidden ? SubTextBrush : TextBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(check, 0);
            Grid.SetColumn(name, 1);
            grid.Children.Add(check);
            grid.Children.Add(name);

            var host = new Border
            {
                Background = row.IsSelected ? RowSelectedBrush : Brushes.Transparent,
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(0, 4, 6, 4),
                Margin = new Thickness(0, 1, 0, 1),
                Cursor = Cursors.Hand,
                Tag = row.Key,
                ToolTip = row.IsHidden ? $"{row.Name} — 눌러서 다시 보이고 선택"
                    : row.IsSelected ? $"{row.Name} — 한 번 더 누르면 배치하는 동안 숨김"
                    : $"{row.Name} — 눌러서 선택",
                Child = grid,
            };
            if (!row.IsSelected)
            {
                host.MouseEnter += (_, _) => host.Background = RowHoverBrush;
                host.MouseLeave += (_, _) => host.Background = Brushes.Transparent;
            }
            host.MouseLeftButtonDown += (_, e) =>
            {
                Service.ToggleListed(row.Key);
                Refresh();
                e.Handled = true;
            };
            return host;
        }

        private static Button CreateButton(string text)
        {
            var button = new Button
            {
                Content = text,
                Padding = new Thickness(8, 3, 8, 3),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand,
                Foreground = Brushes.White,
                Focusable = false,
            };
            button.SetResourceReference(StyleProperty, "SimButtonStyle");
            return button;
        }

        private static Geometry CreateCheckMark()
        {
            Geometry geometry = Geometry.Parse("M 3,7.5 L 6,10.5 L 11,4");
            geometry.Freeze();
            return geometry;
        }

        #region 자리 (메뉴바 옆, 끌어 옮기기)

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed)
                return;

            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // DragMove는 왼쪽 버튼을 누르고 있는 동안에만 된다
            }

            _movedByUser = true;
            var (left, top) = ScreenBoundsHelper.ClampToVirtualScreen(Left, Top, ActualWidth, ActualHeight);
            Left = left;
            Top = top;
            e.Handled = true;
        }

        private void MenuBar_BoundsChanged(object? sender, EventArgs e) => PositionNextToMenuBar();

        private void MenuBar_SizeChanged(object sender, SizeChangedEventArgs e) => PositionNextToMenuBar();

        private static MenuWindow? FindMenuBar()
            => Application.Current?.Windows.OfType<MenuWindow>().FirstOrDefault(window => window.IsVisible && window.ActualWidth > 0);

        /// <summary>메뉴바 옆에 놓는다. 메뉴바가 없으면 주 모니터 왼쪽 위.</summary>
        private void PositionNextToMenuBar()
        {
            if (_movedByUser)
                return;

            MenuWindow? menu = _menuBar ?? FindMenuBar();
            if (menu == null)
            {
                Left = DefaultMargin;
                Top = DefaultMargin;
                return;
            }

            var menuBounds = new Rect(menu.Left, menu.Top, menu.ActualWidth, menu.ActualHeight);
            bool horizontal = ToastPresentationHelper.FindSharedSettings()?.MenuWindowHorizontal ?? menuBounds.Width > menuBounds.Height;
            var size = new Size(ActualWidth > 0 ? ActualWidth : PanelWidth, ActualHeight > 0 ? ActualHeight : 200);
            Point place = PlaceNextToMenuBar(menuBounds, horizontal, size, FindScreen(menuBounds));
            Left = place.X;
            Top = place.Y;
        }

        /// <summary>메뉴바가 놓인 모니터의 작업 영역. 못 찾으면 화면 전체.</summary>
        private static Rect FindScreen(Rect menuBounds)
        {
            var center = new Point(menuBounds.Left + menuBounds.Width / 2, menuBounds.Top + menuBounds.Height / 2);
            foreach (Rect area in ScreenBoundsHelper.GetMonitorWorkAreas())
            {
                if (area.Contains(center))
                    return area;
            }
            return ScreenBoundsHelper.GetVirtualDesktop();
        }

        /// <summary>
        /// 메뉴바 옆의 목록 자리. 세로 메뉴바면 오른쪽에(자리가 없으면 왼쪽), 가로 메뉴바면 아래쪽에(자리가 없으면 위쪽),
        /// 메뉴바와 같은 줄에 맞춰 놓는다. 화면 밖으로 나가지 않게 마지막에 한 번 더 보정한다.
        /// </summary>
        internal static Point PlaceNextToMenuBar(Rect menu, bool horizontal, Size list, Rect screen)
        {
            double left;
            double top;
            if (horizontal)
            {
                left = menu.Left;
                top = menu.Bottom + MenuBarGap;
                if (top + list.Height > screen.Bottom && menu.Top - MenuBarGap - list.Height >= screen.Top)
                    top = menu.Top - MenuBarGap - list.Height;
            }
            else
            {
                left = menu.Right + MenuBarGap;
                top = menu.Top;
                if (left + list.Width > screen.Right && menu.Left - MenuBarGap - list.Width >= screen.Left)
                    left = menu.Left - MenuBarGap - list.Width;
            }

            left = Math.Max(screen.Left, Math.Min(left, screen.Right - list.Width));
            top = Math.Max(screen.Top, Math.Min(top, screen.Bottom - list.Height));
            return new Point(left, top);
        }

        #endregion
    }
}
