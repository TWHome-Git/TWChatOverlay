using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Unlock
{
    /// <summary>
    /// 선택된 창에 부착되는 편집 UI. 1px 이동 버튼이 창의 각 변 가운데(밖)에 붙고,
    /// 정보 패널(이름/좌표/가로·세로 입력)은 창 상단에 — 창이 화면 위쪽에 붙어 있으면
    /// 하단에 — 표시된다. 투명 영역 클릭은 아래(대상 창)로 통과한다.
    /// </summary>
    internal sealed class InspectorWindow : Window
    {
        private Window? _target;
        private readonly Canvas _canvas;
        private readonly Border _panel;
        private readonly RepeatButton _btnUp;
        private readonly RepeatButton _btnDown;
        private readonly RepeatButton _btnLeft;
        private readonly RepeatButton _btnRight;
        private readonly TextBox _widthBox;
        private readonly TextBox _heightBox;
        private readonly TextBox _xBox;
        private readonly TextBox _yBox;
        private readonly Slider _opacitySlider;
        private readonly TextBlock _opacityText;
        private bool _suppressOpacityEvent;
        private readonly StackPanel _fontRow;
        private readonly Slider _fontSlider;
        private readonly TextBlock _fontText;
        private bool _suppressFontEvent;

        private const double SideZone = 34;   // 좌우 버튼이 차지하는 폭
        private const double VertZone = 180;  // 상하 버튼 + 정보 패널이 차지하는 높이
        private const double PanelWidth = 248;
        private const double ButtonW = 26;
        private const double ButtonH = 24;
        private const double Gap = 6;

        private static readonly Color PanelBg = Color.FromRgb(0x10, 0x16, 0x14);
        private static readonly Color FieldBg = Color.FromRgb(0x0D, 0x12, 0x10);
        private static readonly Color BorderCol = Color.FromRgb(0x2A, 0x33, 0x2E);
        private static readonly Color TextCol = Color.FromRgb(0xE8, 0xEA, 0xE9);
        private static readonly Color SubTextCol = Color.FromRgb(0x8C, 0x91, 0x97);
        private static readonly Color Mint = Color.FromRgb(0x0C, 0xD2, 0x9D);

        // 선택할 때마다 창이 새로 만들어지므로 브러시는 Freeze해 공유한다
        private static readonly SolidColorBrush PanelBgBrush = UnlockWindowStyle.Frozen(Color.FromArgb(0xF2, PanelBg.R, PanelBg.G, PanelBg.B));
        private static readonly SolidColorBrush FieldBgBrush = UnlockWindowStyle.Frozen(FieldBg);
        private static readonly SolidColorBrush BorderBrushShared = UnlockWindowStyle.Frozen(BorderCol);
        private static readonly SolidColorBrush TextBrush = UnlockWindowStyle.Frozen(TextCol);
        private static readonly SolidColorBrush SubTextBrush = UnlockWindowStyle.Frozen(SubTextCol);
        private static readonly SolidColorBrush MintBrush = UnlockWindowStyle.Frozen(Mint);

        public InspectorWindow()
        {
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;

            // 가로/세로 크기 + X/Y 좌표 입력 (2행 x 4열)
            _widthBox = CreateNumberBox(ApplySizeFromBoxes);
            _heightBox = CreateNumberBox(ApplySizeFromBoxes);
            _xBox = CreateNumberBox(ApplyPositionFromBoxes);
            _yBox = CreateNumberBox(ApplyPositionFromBoxes);

            var fieldPanel = new Grid { HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < 4; i++)
                fieldPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            fieldPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            fieldPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            fieldPanel.Children.Add(CreateFieldLabel("가로", 0, 0, 0));
            fieldPanel.Children.Add(CreateFieldLabel("세로", 1, 0, 0));
            fieldPanel.Children.Add(CreateFieldLabel("X", 0, 2, 14));
            fieldPanel.Children.Add(CreateFieldLabel("Y", 1, 2, 14));
            fieldPanel.Children.Add(PlaceField(_widthBox, 0, 1));
            fieldPanel.Children.Add(PlaceField(_heightBox, 1, 1));
            fieldPanel.Children.Add(PlaceField(_xBox, 0, 3));
            fieldPanel.Children.Add(PlaceField(_yBox, 1, 3));

            // 투명도 슬라이더 (10~100%) + 우측 % 표시
            _opacitySlider = new Slider
            {
                Minimum = 10,
                Maximum = 100,
                Value = 100,
                Width = 138,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            _opacityText = new TextBlock
            {
                Text = "100%",
                FontSize = 11,
                Width = 36,
                TextAlignment = TextAlignment.Right,
                Foreground = TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _opacitySlider.ValueChanged += (_, e) =>
            {
                _opacityText.Text = $"{(int)Math.Round(e.NewValue)}%";
                if (_suppressOpacityEvent) return;
                AppServices.Get<UiLockService>().SetSelectedOpacity(e.NewValue);
            };

            var opacityRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 7, 0, 0),
            };
            opacityRow.Children.Add(new TextBlock
            {
                Text = "투명도",
                FontSize = 11,
                Foreground = SubTextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            });
            opacityRow.Children.Add(_opacitySlider);
            opacityRow.Children.Add(_opacityText);

            // 폰트 크기 슬라이더 (지원 창에서만 표시) — 투명도 아래
            _fontSlider = new Slider
            {
                Minimum = 10,
                Maximum = 40,
                Value = 16,
                Width = 138,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Focusable = false,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0),
            };
            _fontText = new TextBlock
            {
                Text = "16",
                FontSize = 11,
                Width = 36,
                TextAlignment = TextAlignment.Right,
                Foreground = TextBrush,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _fontSlider.ValueChanged += (_, e) =>
            {
                _fontText.Text = $"{(int)Math.Round(e.NewValue)}";
                if (_suppressFontEvent) return;
                AppServices.Get<UiLockService>().SetSelectedFontSize(e.NewValue);
            };

            _fontRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 7, 0, 0),
            };
            _fontRow.Children.Add(new TextBlock
            {
                Text = "폰트",
                FontSize = 11,
                Foreground = SubTextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(9, 0, 0, 0),
            });
            _fontRow.Children.Add(_fontSlider);
            _fontRow.Children.Add(_fontText);

            var root = new StackPanel();
            root.Children.Add(fieldPanel);
            root.Children.Add(opacityRow);
            root.Children.Add(_fontRow);

            _panel = new Border
            {
                Width = PanelWidth,
                Padding = new Thickness(12, 8, 12, 9),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                Background = PanelBgBrush,
                BorderBrush = BorderBrushShared,
                Child = root,
            };

            _btnUp = CreateNudgeButton("▲", 0, -1);
            _btnDown = CreateNudgeButton("▼", 0, 1);
            _btnLeft = CreateNudgeButton("◀", -1, 0);
            _btnRight = CreateNudgeButton("▶", 1, 0);

            _canvas = new Canvas();
            _canvas.Children.Add(_btnUp);
            _canvas.Children.Add(_btnDown);
            _canvas.Children.Add(_btnLeft);
            _canvas.Children.Add(_btnRight);
            _canvas.Children.Add(_panel);
            Content = _canvas;

            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) AppServices.Get<UiLockService>().Set(false);
            };
        }

        private RepeatButton CreateNudgeButton(string glyph, int dx, int dy)
        {
            var button = new RepeatButton
            {
                Content = glyph,
                Width = ButtonW,
                Height = ButtonH,
                FontSize = 10,
                Cursor = Cursors.Hand,
                Focusable = false,
                Delay = 400,
                Interval = 45,
                Foreground = TextBrush,
                Background = FieldBgBrush,
                BorderBrush = BorderBrushShared,
                ToolTip = "1px 이동 (길게 누르면 반복)",
            };
            button.Click += (_, _) => AppServices.Get<UiLockService>().NudgeSelected(dx, dy);
            return button;
        }

        private TextBlock CreateFieldLabel(string text, int row, int column, double leftMargin)
        {
            var label = new TextBlock
            {
                Text = text,
                FontSize = 11,
                Foreground = SubTextBrush,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(leftMargin, 0, 8, 0),
            };
            return PlaceField(label, row, column);
        }

        private static T PlaceField<T>(T element, int row, int column) where T : UIElement
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            return element;
        }

        private TextBox CreateNumberBox(Action commit)
        {
            var box = new TextBox
            {
                Width = 64,
                Height = 22,
                Margin = new Thickness(0, 2, 0, 2),
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = TextBrush,
                Background = FieldBgBrush,
                BorderBrush = BorderBrushShared,
                CaretBrush = MintBrush,
            };
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    commit();
                    e.Handled = true;
                }
            };
            box.LostFocus += (_, _) => commit();
            return box;
        }

        public void SetTarget(Window target)
        {
            _target = target;
            // SizeToContent 창은 내용이 크기를 결정하므로 수치 입력을 막는다
            bool canResize = target.SizeToContent == SizeToContent.Manual;
            _widthBox.IsEnabled = canResize;
            _heightBox.IsEnabled = canResize;
            double fieldOpacity = canResize ? 1.0 : 0.45;
            _widthBox.Opacity = fieldOpacity;
            _heightBox.Opacity = fieldOpacity;

            SyncOpacitySlider(target);
            SyncFontSlider();
            RefreshValues();
        }

        private void SyncFontSlider()
        {
            double? size = AppServices.Get<UiLockService>().GetSelectedFontSize();
            _fontRow.Visibility = size.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (!size.HasValue) return;

            _suppressFontEvent = true;
            try
            {
                _fontSlider.Value = Math.Max(10, Math.Min(40, Math.Round(size.Value)));
                _fontText.Text = $"{(int)Math.Round(size.Value)}";
            }
            finally
            {
                _suppressFontEvent = false;
            }
        }

        private void SyncOpacitySlider(Window target)
        {
            _suppressOpacityEvent = true;
            try
            {
                double percent = Math.Max(10, Math.Min(100, Math.Round(target.Opacity * 100)));
                _opacitySlider.Value = percent;
                _opacityText.Text = $"{(int)percent}%";
            }
            finally
            {
                _suppressOpacityEvent = false;
            }
        }

        public void RefreshValues()
        {
            var target = _target;
            if (target == null) return;

            try
            {
                if (!_xBox.IsKeyboardFocused)
                    _xBox.Text = ((int)Math.Round(target.Left)).ToString();
                if (!_yBox.IsKeyboardFocused)
                    _yBox.Text = ((int)Math.Round(target.Top)).ToString();
                if (!_widthBox.IsKeyboardFocused)
                    _widthBox.Text = ((int)Math.Round(target.ActualWidth)).ToString();
                if (!_heightBox.IsKeyboardFocused)
                    _heightBox.Text = ((int)Math.Round(target.ActualHeight)).ToString();

                RefreshLayout(target);
            }
            catch { }
        }

        /// <summary>대상 창의 위치·크기에 맞춰 이 창을 감싸도록 재배치한다.</summary>
        private void RefreshLayout(Window target)
        {
            double tw = Math.Max(16, target.ActualWidth);
            double th = Math.Max(16, target.ActualHeight);

            // 대상 창이 정보 패널보다 좁으면 인스펙터 창을 좌우로 더 넓혀 패널이 잘리지 않게 한다
            double extra = Math.Max(0, (PanelWidth + 8 - (tw + SideZone * 2)) / 2);
            double originX = SideZone + extra;

            Left = target.Left - originX;
            Top = target.Top - VertZone;
            Width = tw + originX * 2;
            Height = th + VertZone * 2;

            // 상하좌우 버튼: 각 변의 가운데, 창 바깥쪽에 붙인다
            Canvas.SetLeft(_btnLeft, originX - ButtonW - 4);
            Canvas.SetTop(_btnLeft, VertZone + th / 2 - ButtonH / 2);
            Canvas.SetLeft(_btnRight, originX + tw + 4);
            Canvas.SetTop(_btnRight, VertZone + th / 2 - ButtonH / 2);
            Canvas.SetLeft(_btnUp, originX + tw / 2 - ButtonW / 2);
            Canvas.SetTop(_btnUp, VertZone - ButtonH - 4);
            Canvas.SetLeft(_btnDown, originX + tw / 2 - ButtonW / 2);
            Canvas.SetTop(_btnDown, VertZone + th + 4);

            // 정보 패널: 기본은 창 상단(▲ 버튼 위), 화면 위쪽에 붙어 있으면 하단(▼ 버튼 아래)
            double panelHeight = _panel.ActualHeight > 0 ? _panel.ActualHeight : 140;
            bool showBelow = target.Top - (ButtonH + Gap * 2 + panelHeight) < SystemParameters.VirtualScreenTop + 4;

            double panelLeft = originX + tw / 2 - PanelWidth / 2;
            panelLeft = Math.Max(2, Math.Min(Width - PanelWidth - 2, panelLeft));
            Canvas.SetLeft(_panel, panelLeft);
            Canvas.SetTop(_panel, showBelow
                ? VertZone + th + 4 + ButtonH + Gap
                : VertZone - ButtonH - 4 - Gap - panelHeight);
        }

        /// <summary>다른 창이 덮었을 때 밖에서 다시 맨 위로 올린다 (UiLockService.ReassertZOrder).</summary>
        public void BringToFront()
        {
            if (IsVisible)
                RaiseToTop();
        }

        /// <summary>
        /// 다른 오버레이 창들이 Topmost 밴드 최상단으로 끼어들어도
        /// 인스펙터(좌표·크기 패널)는 항상 그 위에 보이도록 재삽입한다.
        /// </summary>
        private void RaiseToTop()
        {
            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                if (handle == IntPtr.Zero)
                    return;

                NativeMethods.SetWindowPos(handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
            catch { }
        }

        private void ApplyPositionFromBoxes()
        {
            var target = _target;
            if (target == null) return;

            double? left = double.TryParse(_xBox.Text, out double x) ? x : null;
            double? top = double.TryParse(_yBox.Text, out double y) ? y : null;
            if (left == null && top == null)
            {
                RefreshValues();
                return;
            }

            AppServices.Get<UiLockService>().MoveSelected(left, top);

            // 화면 밖으로 나가지 않게 보정될 수 있으므로 실제 값으로 되돌려 보여준다
            _xBox.Text = ((int)Math.Round(target.Left)).ToString();
            _yBox.Text = ((int)Math.Round(target.Top)).ToString();
            RefreshValues();
        }

        private void ApplySizeFromBoxes()
        {
            if (_target == null || !_widthBox.IsEnabled) return;

            double? width = double.TryParse(_widthBox.Text, out double w) ? w : null;
            double? height = double.TryParse(_heightBox.Text, out double h) ? h : null;
            if (width == null && height == null)
            {
                RefreshValues();
                return;
            }

            AppServices.Get<UiLockService>().ResizeSelected(width, height);
            RefreshValues();
        }

    }
}
