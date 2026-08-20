using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>
    /// 오버레이 창 제목 표시줄에 넣는 투명도 조절 슬라이더.
    /// GroupKey에 해당하는 그룹 값을 읽고/저장하며, 값이 바뀌면 같은 그룹 창에 즉시 반영된다.
    /// </summary>
    public class OverlayOpacityChip : UserControl
    {
        public static readonly DependencyProperty GroupKeyProperty =
            DependencyProperty.Register(
                nameof(GroupKey),
                typeof(string),
                typeof(OverlayOpacityChip),
                new PropertyMetadata(string.Empty, OnGroupKeyChanged));

        public static readonly DependencyProperty SliderWidthProperty =
            DependencyProperty.Register(
                nameof(SliderWidth),
                typeof(double),
                typeof(OverlayOpacityChip),
                new PropertyMetadata(68.0, OnSliderWidthChanged));

        public static readonly DependencyProperty ShowValueTextProperty =
            DependencyProperty.Register(
                nameof(ShowValueText),
                typeof(bool),
                typeof(OverlayOpacityChip),
                new PropertyMetadata(true, OnShowValueTextChanged));

        /// <summary>OverlayOpacityService의 그룹 키.</summary>
        public string GroupKey
        {
            get => (string)GetValue(GroupKeyProperty);
            set => SetValue(GroupKeyProperty, value);
        }

        /// <summary>슬라이더 가로 폭. 폭이 좁은 창에서는 줄여서 쓴다.</summary>
        public double SliderWidth
        {
            get => (double)GetValue(SliderWidthProperty);
            set => SetValue(SliderWidthProperty, value);
        }

        /// <summary>오른쪽 % 숫자 표시 여부. 끄면 값은 툴팁으로만 보여준다.</summary>
        public bool ShowValueText
        {
            get => (bool)GetValue(ShowValueTextProperty);
            set => SetValue(ShowValueTextProperty, value);
        }

        private readonly Slider _slider;
        private readonly TextBlock _valueText;
        private bool _suppressCallback;

        public OverlayOpacityChip()
        {
            _slider = new Slider
            {
                Minimum = 20,
                Maximum = 100,
                Width = 68,
                Height = 18,
                TickFrequency = 5,
                IsSnapToTickEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
            };
            _slider.ValueChanged += Slider_ValueChanged;

            var glyph = new TextBlock
            {
                Text = "◐",
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
            };
            glyph.SetResourceReference(TextBlock.ForegroundProperty, "OverlaySubtleTextBrush");

            _valueText = new TextBlock
            {
                FontSize = 11,
                Width = 32,
                TextAlignment = TextAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0),
            };
            _valueText.SetResourceReference(TextBlock.ForegroundProperty, "OverlaySubtleTextBrush");

            var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panel.Children.Add(glyph);
            panel.Children.Add(_slider);
            panel.Children.Add(_valueText);

            Content = panel;
            VerticalAlignment = VerticalAlignment.Center;
            ToolTip = "오버레이 투명도 (100% = 뒤가 비치지 않음)";

            // 제목 표시줄의 창 이동(DragMove) 핸들러로 클릭이 새어나가지 않게 막는다.
            AddHandler(MouseLeftButtonDownEvent, new MouseButtonEventHandler(OnMouseLeftButtonDown), true);

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            OverlayOpacityService.GroupOpacityChanged -= OnGroupOpacityChanged;
            OverlayOpacityService.GroupOpacityChanged += OnGroupOpacityChanged;
            ReloadValue();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
            => OverlayOpacityService.GroupOpacityChanged -= OnGroupOpacityChanged;

        /// <summary>다른 경로(설정 초기화 등)로 값이 바뀌면 슬라이더 위치를 맞춘다.</summary>
        private void OnGroupOpacityChanged(string? groupKey)
        {
            if (groupKey != null && !string.Equals(groupKey, GroupKey, StringComparison.OrdinalIgnoreCase))
                return;

            Dispatcher.BeginInvoke(new Action(ReloadValue));
        }

        private static void OnGroupKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is OverlayOpacityChip chip)
                chip.ReloadValue();
        }

        private static void OnSliderWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is OverlayOpacityChip chip && e.NewValue is double width && width > 0)
                chip._slider.Width = width;
        }

        private static void OnShowValueTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is OverlayOpacityChip chip && e.NewValue is bool show)
                chip._valueText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>저장된 그룹 값을 슬라이더에 표시한다. (저장 콜백은 발생시키지 않음)</summary>
        public void ReloadValue()
        {
            if (string.IsNullOrEmpty(GroupKey)) return;

            _suppressCallback = true;
            try
            {
                _slider.Value = OverlayOpacityService.GetGroupOpacity(GroupKey);
                UpdateValueText();
            }
            finally
            {
                _suppressCallback = false;
            }
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateValueText();

            if (_suppressCallback || string.IsNullOrEmpty(GroupKey)) return;

            OverlayOpacityService.SetGroupOpacity(GroupKey, e.NewValue);
        }

        private void UpdateValueText()
        {
            string percent = Math.Round(_slider.Value).ToString("F0") + "%";
            _valueText.Text = percent;
            ToolTip = $"오버레이 투명도 {percent} (100% = 뒤가 비치지 않음)";
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => e.Handled = true;
    }
}
