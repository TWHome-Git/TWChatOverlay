using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Documents;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views
{
    /// <summary>메신저 에타 알림 토스트. 위치는 알림 스택이 정하므로, 사용자가 미리보기에서 끌었을 때만 저장한다.</summary>
    public partial class MessengerEtaToastWindow : OverlayWindowBase
    {
        private readonly ChatSettings _settings;
        private bool _isPreviewMode;

        public MessengerEtaToastWindow(FontFamily fontFamily, ChatSettings settings)
        {
            InitializeComponent();
            _settings = settings;
            FontFamily = fontFamily;
            TitleText.FontFamily = fontFamily;
            CloseButton.FontFamily = fontFamily;
            CloseButton.Click += (_, _) => Close();
            EntryRichText.FontSize = _settings.MessengerEtaFontSize;
        }

        protected override bool ApplyAppFont => false;          // 생성자 인자의 폰트를 쓴다
        protected override bool PersistBoundsOnChange => false; // 스택이 옮긴 위치를 설정에 되쓰지 않는다
        protected override ChatSettings? ResolveSettings() => _settings;

        /// <summary>잠금 해제 인스펙터에서 폰트 크기 변경 시 즉시 반영.</summary>
        public void SetFontSize(double size)
        {
            EntryRichText.FontSize = size;
        }

        public void SetEntries(IReadOnlyList<string> entries)
        {
            var doc = new FlowDocument();
            if (entries != null)
            {
                foreach (string line in entries)
                {
                    doc.Blocks.Add(new Paragraph(new Run(line))
                    {
                        Margin = new Thickness(0, 2, 0, 2)
                    });
                }
            }

            EntryRichText.Document = doc;
        }

        public void SetPreviewMode(bool isPreview)
        {
            _isPreviewMode = isPreview;
            ApplyInteractiveStyle();
        }

        public void SaveCurrentPosition() => PersistBoundsNow();

        protected override bool PersistBounds(ChatSettings settings)
        {
            settings.MessengerToastWindowLeft = Left;
            settings.MessengerToastWindowTop = Top;
            return true;
        }

        public void ShowAt(double left, double top)
        {
            Left = left;
            Top = top;
            if (!IsVisible)
                Show();
            Visibility = Visibility.Visible;
            Opacity = 1.0;
            Activate();
            Topmost = true;
            BringToFront();
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).EnsureHandle();
                NativeMethods.SetWindowPos(
                    hwnd,
                    NativeMethods.HWND_TOPMOST,
                    0,
                    0,
                    0,
                    0,
                    NativeMethods.SWP_NOMOVE |
                    NativeMethods.SWP_NOSIZE |
                    NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_NOOWNERZORDER);
            }
            catch { }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ApplyInteractiveStyle();
        }

        /// <summary>항상 클릭을 받는 툴윈도우 (닫기 버튼이 있어 마우스 통과를 쓰지 않는다).</summary>
        private void ApplyInteractiveStyle()
            => SetExStyleFlags(add: NativeMethods.WS_EX_TOOLWINDOW, remove: NativeMethods.WS_EX_TRANSPARENT);

        private void RootBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragInPreviewOnly(e);

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
            => DragInPreviewOnly(e);

        /// <summary>실제 알림은 스택이 배치하므로 미리보기일 때만 끌 수 있다. 선택 하이라이트는 항상 허용.</summary>
        private void DragInPreviewOnly(MouseButtonEventArgs e)
        {
            if (!_isPreviewMode)
            {
                UiLockService.Select(this);
                return;
            }

            TryBeginDrag(e);
        }
    }
}
