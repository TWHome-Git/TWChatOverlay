using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TWChatOverlay.Views
{
    /// <summary>메모 텍스트만 띄우는 창. 잠금 모드와 무관하게 항상 끌 수 있고, 더블클릭으로 닫는다.</summary>
    public partial class MemoTextOnlyWindow : OverlayWindowBase
    {
        public MemoTextOnlyWindow()
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                UpdateTextBounds();
                SetMousePassthrough(true);
            };
            SizeChanged += (_, _) => UpdateTextBounds();
        }

        protected override bool KeepBelowSettingsHost => false; // 메모는 설정 창과 무관하게 위에 둔다
        protected override bool DragRequiresUnlock => false;
        protected override bool PersistBoundsOnChange => false;

        public void SetText(string text)
        {
            MemoText.Text = text;
            UpdateTextBounds();
        }

        public void SetStyle(Brush foreground, double fontSize, FontWeight weight, FontStyle style)
        {
            MemoText.Foreground = foreground;
            MemoText.FontSize = fontSize;
            MemoText.FontWeight = weight;
            MemoText.FontStyle = style;
            UpdateTextBounds();
        }

        public void SetBackgroundOpacity(double opacity)
        {
            byte alpha = (byte)Math.Clamp((int)Math.Round((opacity / 100.0) * 255.0), 0, 255);
            Color baseColor = Colors.Black;
            if (Application.Current?.Resources["OverlayShellBackgroundBrush"] is SolidColorBrush themedBrush)
                baseColor = themedBrush.Color;

            MemoText.Background = new SolidColorBrush(Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B));
            OverlayBackground.Background = Brushes.Transparent;
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                Close();
            else
                TryBeginDrag(e);
        }

        private void UpdateTextBounds()
        {
            double maxWidth = Math.Max(0, ActualWidth - MemoText.Margin.Left - MemoText.Margin.Right);
            MemoText.MaxWidth = maxWidth;
        }
    }
}
