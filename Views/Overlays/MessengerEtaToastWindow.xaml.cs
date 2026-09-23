using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
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
            _fontSize = _settings.MessengerEtaFontSize;
        }

        private double _fontSize;
        private IReadOnlyList<MessengerEtaEntry> _entries = Array.Empty<MessengerEtaEntry>();

        protected override bool ApplyAppFont => false;          // 생성자 인자의 폰트를 쓴다
        protected override bool PersistBoundsOnChange => false; // 스택이 옮긴 위치를 설정에 되쓰지 않는다
        protected override ChatSettings? ResolveSettings() => _settings;

        /// <summary>잠금 해제 인스펙터에서 폰트 크기 변경 시 즉시 반영. 아이디 글자가 기준이고 알약·캐릭터 이름은 그에 비례한다.</summary>
        public void SetFontSize(double size)
        {
            _fontSize = size;
            RebuildRows();
        }

        public void SetEntries(IReadOnlyList<MessengerEtaEntry> entries)
        {
            _entries = entries ?? Array.Empty<MessengerEtaEntry>();
            RebuildRows();
        }

        private static readonly SolidColorBrush CautionBrush = new(Color.FromRgb(255, 123, 123));

        /// <summary>한글·영문·숫자가 아닌 글자 — 비슷하게 보이는 아이디를 가려내기 위해 빨갛게 칠하는 기준.</summary>
        private static bool IsSpecial(char c)
            => !(c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9'
                 || c is >= '가' and <= '힣' || c is >= 'ㄱ' and <= 'ㅣ');

        /// <summary>
        /// 상대마다 한 줄: 왼쪽에 아이디, 오른쪽에 레벨 알약.
        /// 아이디에 한글·영문·숫자 아닌 글자가 있으면 그 글자를 빨갛게 칠하고 아래에 "주의" 줄을 붙인다 (닮은 아이디 사칭 대비).
        /// 랭킹에 닮은 아이디가 있으면(YulLin ↔ YuILin, 드드해 ↔ 뜨뜨해·드드해1) 그 아이디와 레벨을 아래에 빨갛게 적고,
        /// 길이가 같은 닮은꼴이면 어느 글자가 다른지 아이디 안에서 빨갛게 칠한다.
        /// 알약은 채팅창의 레벨 구간 색을 글자·테두리에, 같은 색의 옅은 물결을 배경에 쓴다. 랭킹에 없으면 흐린 "정보 없음".
        /// </summary>
        private void RebuildRows()
        {
            EntryList.Children.Clear();
            double scale = _fontSize / 20.0; // 기본 글자 크기 20 기준 비율
            for (int i = 0; i < _entries.Count; i++)
            {
                MessengerEtaEntry entry = _entries[i];

                var idText = new TextBlock { FontSize = _fontSize, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
                idText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                bool hasSpecial = false;
                // 길이가 같은 닮은꼴 중 헷갈리는 글자만 다른 것 — 그 아이디와 다른 자리를 칠한다
                string? twin = entry.Lookalikes.FirstOrDefault(l => l.Kind == EtaLookalikeKind.Confusable && l.UserId.Length == entry.UserId.Length).UserId;
                for (int k = 0; k < entry.UserId.Length; k++)
                {
                    char c = entry.UserId[k];
                    var run = new System.Windows.Documents.Run(c.ToString());
                    bool special = IsSpecial(c);
                    hasSpecial |= special;
                    if (special || (twin != null && twin[k] != c))
                        run.Foreground = CautionBrush; // 어느 글자가 문제인지 바로 보이게
                    idText.Inlines.Add(run);
                }
                // 주의 줄들은 아이디·알약 아래에 두 열을 가로질러 놓아 알약에 밀려 잘리지 않게 한다
                var left = new StackPanel();
                double cautionSize = Math.Max(10, Math.Round(_fontSize * 0.6));
                if (hasSpecial)
                    left.Children.Add(new TextBlock { Text = "주의 - 특수 문자 포함", FontSize = cautionSize, Foreground = CautionBrush, Margin = new Thickness(0, 1, 0, 0) });
                // 닮은 아이디는 하나에 한 줄 — 줄바꿈으로 아이디가 끊겨 읽히지 않게. 헷갈리는 글자만 다른 것은 "닮은꼴", 한 글자 차이는 "비슷한"
                foreach (EtaLookalike lookalike in entry.Lookalikes)
                {
                    string kind = lookalike.Kind == EtaLookalikeKind.Confusable ? "닮은꼴" : "비슷한";
                    left.Children.Add(new TextBlock
                    {
                        Text = $"주의 - {kind} 아이디 {lookalike.UserId} (Lv {lookalike.Level})",
                        FontSize = cautionSize,
                        Foreground = CautionBrush,
                        Margin = new Thickness(0, 1, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    });
                }

                var pillText = new TextBlock
                {
                    Text = entry.Level.HasValue ? $"Lv {entry.Level.Value}" : "정보 없음",
                    FontSize = Math.Max(10, Math.Round(_fontSize * 0.7)),
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var pill = new Border
                {
                    Child = pillText,
                    CornerRadius = new CornerRadius(20),
                    Padding = new Thickness(Math.Round(10 * scale) + 2, 1, Math.Round(10 * scale) + 2, 2),
                    BorderThickness = new Thickness(1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0),
                };
                if (entry.Level.HasValue)
                {
                    Color color = ChatBrushResolver.ToBrush(ChatLineComposer.EtaLevelRangeHex(entry.Level.Value, _settings)).Color;
                    pillText.Foreground = new SolidColorBrush(color);
                    pill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, color.R, color.G, color.B));
                    pill.Background = new SolidColorBrush(Color.FromArgb(0x22, color.R, color.G, color.B));
                }
                else
                {
                    pillText.SetResourceReference(TextBlock.ForegroundProperty, "OverlayMutedTextBrush");
                    pill.SetResourceReference(Border.BorderBrushProperty, "ControlBorderBrush");
                    pill.Background = Brushes.Transparent;
                }

                var row = new Grid { Margin = new Thickness(6, 4, 6, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                idText.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(pill, 1);
                Grid.SetRow(left, 1);
                Grid.SetColumnSpan(left, 2);
                row.Children.Add(idText);
                row.Children.Add(pill);
                row.Children.Add(left);
                EntryList.Children.Add(row);

                // 줄 사이 가는 구분선
                if (i < _entries.Count - 1)
                {
                    var rule = new Border { Height = 1, Margin = new Thickness(4, 0, 4, 0), Opacity = 0.7 };
                    rule.SetResourceReference(Border.BackgroundProperty, "OverlayCardBorderBrush");
                    EntryList.Children.Add(rule);
                }
            }
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
                AppServices.Get<UiLockService>().Select(this);
                return;
            }

            TryBeginDrag(e);
        }
    }
}
