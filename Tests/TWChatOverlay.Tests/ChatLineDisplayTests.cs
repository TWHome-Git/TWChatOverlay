using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TWChatOverlay.Views;
using Xunit;

namespace TWChatOverlay.Tests
{
    /// <summary>
    /// 직접 그리는 채팅 표시의 동작 테스트: 스크롤, 남길 줄 수, 글 선택, 글자 위치 찾기, 좌표 당기기, 글꼴·너비 변경.
    /// 그림을 픽셀로 비교하지 않는다 — 글꼴이나 윈도우 버전에 따라 달라지기 때문이다.
    /// </summary>
    public class ChatLineDisplayTests
    {
        private static readonly FontFamily Font = new("Malgun Gothic");
        private const double FontSize = 13;
        private const string LongTail = " 가나다라 마바사아 자차카타 파하 abcd efgh ijkl mnop 가나다라 마바사아 자차카타 파하 abcd efgh";

        private static ChatLineDisplay Create(double width = 400, double height = 200, int maxLines = 200)
        {
            var display = new ChatLineDisplay { FontFamily = Font, FontSize = FontSize, MaxLines = maxLines };
            Wpf.Layout(display, width, height);
            return display;
        }

        private static ChatLineContent Line(string text)
            => new(new (string Text, Brush? Brush)[] { (text, null) }, Brushes.White);

        private static string Text(int index) => $"줄 {index:000} 가나다 abc";

        private static void AddLines(ChatLineDisplay display, int from, int count, string tail = "")
        {
            display.BeginUpdate();
            for (int i = from; i < from + count; i++)
                display.AddLine(Line(Text(i) + tail));
            display.EndUpdate();
        }

        /// <summary>화면 맨 위에 보이는 줄의 글.</summary>
        private static string TopLine(ChatLineDisplay display)
        {
            display.SelectLineAt(new Point(5, 2));
            string text = display.GetSelectedText();
            display.ClearSelection();
            return text;
        }

        private static double LineHeight(ChatLineDisplay display) => display.ExtentHeight / display.LineCount;

        /// <summary>글자 자리를 표시 영역과 따로 계산한다 (WPF FormattedText). 표시 영역의 글자 위치 찾기가 이것과 맞아야 한다.</summary>
        private static FormattedText Measure(ChatLineDisplay display, string text, double wrapWidth)
            => new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
                FontSize, Brushes.White, VisualTreeHelper.GetDpi(display).PixelsPerDip)
            {
                MaxTextWidth = wrapWidth,
            };

        private static Rect CharRect(FormattedText formatted, int index)
            => formatted.BuildHighlightGeometry(new Point(0, 0), index, 1)!.Bounds;

        [Fact(DisplayName = "스크롤 범위: 내용이 넘치면 넘친 만큼, 범위 밖 값은 끝에 맞춘다")]
        public void ScrollRange_IsExtentMinusViewport_AndOffsetsAreClamped() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create(height: 200);
            AddLines(display, 0, 3);
            Assert.Equal(0, display.ScrollableHeight);
            Assert.True(display.IsAtEnd);

            AddLines(display, 3, 47);
            Assert.Equal(display.ExtentHeight - 200, display.ScrollableHeight, 6);

            display.SetVerticalOffset(-50);
            Assert.Equal(0, display.VerticalOffset, 6);
            Assert.False(display.IsAtEnd);

            display.SetVerticalOffset(1e9);
            Assert.Equal(display.ScrollableHeight, display.VerticalOffset, 6);
            Assert.True(display.IsAtEnd);

            display.SetVerticalOffset(10);
            display.ScrollToEnd();
            Assert.True(display.IsAtEnd);

            display.SetVerticalOffset(double.NaN);
            Assert.True(display.IsAtEnd);
        });

        [Fact(DisplayName = "넘기기: 한 줄·한 쪽·휠 단위로 움직이고 되돌아온다")]
        public void LinePageAndWheelSteps() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create(height: 200);
            AddLines(display, 0, 100);
            display.SetVerticalOffset(600);

            display.LineDown();
            double lineStep = display.VerticalOffset - 600;
            Assert.InRange(lineStep, 8, LineHeight(display) * 2);
            display.LineUp();
            Assert.Equal(600, display.VerticalOffset, 6);

            display.PageDown();
            double pageStep = display.VerticalOffset - 600;
            Assert.True(pageStep > lineStep * 3);
            Assert.True(pageStep < 200);
            display.PageUp();
            Assert.Equal(600, display.VerticalOffset, 6);

            display.MouseWheelDown();
            int wheelLines = SystemParameters.WheelScrollLines > 0 ? SystemParameters.WheelScrollLines : 3;
            Assert.Equal(lineStep * wheelLines, display.VerticalOffset - 600, 6);
            display.MouseWheelUp();
            Assert.Equal(600, display.VerticalOffset, 6);
        });

        [Fact(DisplayName = "올려 보는 중: 새 줄이 오고 오래된 줄이 지워져도 보던 줄이 그대로다")]
        public void ScrolledUp_KeepsViewedLine_WhileLinesAreAddedAndTrimmed() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create(maxLines: 50);
            AddLines(display, 0, 50);
            display.SetVerticalOffset(LineHeight(display) * 20);
            Assert.StartsWith("줄 020", TopLine(display));

            AddLines(display, 50, 10);
            Assert.StartsWith("줄 020", TopLine(display));
            Assert.False(display.IsAtEnd);

            // 보던 줄까지 지워지면 남은 첫 줄을 본다
            AddLines(display, 60, 30);
            Assert.Equal(0, display.VerticalOffset, 6);
            Assert.StartsWith("줄 040", TopLine(display));
        });

        [Fact(DisplayName = "남길 줄 수: 넘으면 오래된 줄부터 지우고, 줄이면 바로 지운다")]
        public void MaxLines_TrimsOldestLines() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create(maxLines: 200);
            AddLines(display, 0, 320);
            Assert.Equal(200, display.LineCount);

            display.SelectAll();
            string[] lines = display.GetSelectedText().Split("\r\n");
            Assert.Equal(200, lines.Length);
            Assert.Equal(Text(120), lines[0]);
            Assert.Equal(Text(319), lines[^1]);

            display.MaxLines = 50;
            Assert.Equal(50, display.LineCount);
            display.SelectAll();
            Assert.Equal(Text(270), display.GetSelectedText().Split("\r\n")[0]);
        });

        [Fact(DisplayName = "여러 줄을 한꺼번에 넣으면 스크롤 알림은 한 번만 간다")]
        public void BeginEndUpdate_RaisesMetricsChangedOnce() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create();
            int raised = 0;
            display.ScrollMetricsChanged += (_, _) => raised++;

            display.BeginUpdate();
            for (int i = 0; i < 20; i++)
                display.AddLine(Line(Text(i)));
            Assert.Equal(0, raised);
            display.EndUpdate();
            Assert.Equal(1, raised);

            display.AddLine(Line(Text(20)));
            Assert.Equal(2, raised);
        });

        [Fact(DisplayName = "전체 선택: 조각을 이어 붙인 글을 줄바꿈으로 잇는다")]
        public void SelectAll_ReturnsAllLinesJoinedByLineBreaks() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create();
            display.AddLine(new ChatLineContent(
                new (string Text, Brush? Brush)[] { ("[ 12시 00분 ] ", Brushes.Gray), ("클럽원", null), ("[250]", Brushes.Gold), (" : 안녕하세요", null) },
                Brushes.White));
            display.AddLine(Line("둘째 줄"));
            display.AddLine(Line(string.Empty));
            Assert.False(display.HasSelection);

            display.SelectAll();
            Assert.True(display.HasSelection);
            Assert.Equal("[ 12시 00분 ] 클럽원[250] : 안녕하세요\r\n둘째 줄\r\n", display.GetSelectedText());

            display.ClearSelection();
            Assert.False(display.HasSelection);
            Assert.Equal(string.Empty, display.GetSelectedText());
        });

        [Fact(DisplayName = "글자 위치 찾기: 글자의 왼쪽 절반은 그 글자 앞, 오른쪽 절반은 뒤")]
        public void HitTest_LeftHalfIsBeforeCharacter_RightHalfIsAfter() => Wpf.Run(() =>
        {
            const string text = "가나다라마바사 ABC defg 12345 !@#";
            ChatLineDisplay display = Create();
            display.AddLine(Line(text));
            FormattedText formatted = Measure(display, text, 400);

            for (int i = 0; i < text.Length; i += 2)
            {
                if (text[i] == ' ')
                    continue;
                Rect rect = CharRect(formatted, i);
                double y = rect.Top + rect.Height / 2;

                display.BeginSelection(new Point(0, y), extend: false);
                display.ExtendSelectionTo(new Point(rect.Left + rect.Width * 0.25, y));
                Assert.Equal(text[..i], display.GetSelectedText());

                display.ExtendSelectionTo(new Point(rect.Left + rect.Width * 0.75, y));
                Assert.Equal(text[..(i + 1)], display.GetSelectedText());
            }
        });

        [Fact(DisplayName = "글자 위치 찾기: 줄바꿈으로 넘어간 둘째 글줄의 글자도 맞게 찾는다")]
        public void HitTest_FindsCharacterOnWrappedRow() => Wpf.Run(() =>
        {
            string text = Text(0) + LongTail;
            ChatLineDisplay display = Create(width: 220);
            display.AddLine(Line(text));
            FormattedText formatted = Measure(display, text, 220);

            // 둘째 글줄에 있는 글자 하나
            int index = Enumerable.Range(0, text.Length).First(i => text[i] != ' ' && CharRect(formatted, i).Top > 1) + 2;
            while (text[index] == ' ')
                index++;
            Rect rect = CharRect(formatted, index);
            Assert.True(rect.Top > 1);

            display.BeginSelection(new Point(0, 1), extend: false);
            display.ExtendSelectionTo(new Point(rect.Left + rect.Width * 0.25, rect.Top + rect.Height / 2));
            Assert.Equal(text[..index], display.GetSelectedText());
        });

        [Fact(DisplayName = "끌어서 선택: 두 줄에 걸쳐도, 거꾸로 끌어도, Shift로 이어도 같은 글")]
        public void DragSelection_AcrossTwoLines() => Wpf.Run(() =>
        {
            const string first = "첫째 줄 가나다라마바사";
            const string second = "둘째 줄 ABCDEFG";
            ChatLineDisplay display = Create();
            display.AddLine(Line(first));
            double firstHeight = display.ExtentHeight;
            display.AddLine(Line(second));

            Rect a = CharRect(Measure(display, first, 400), 3);
            Rect b = CharRect(Measure(display, second, 400), 5);
            var from = new Point(a.Left + a.Width * 0.25, a.Top + a.Height / 2);
            var to = new Point(b.Left + b.Width * 0.25, firstHeight + b.Top + b.Height / 2);
            string expected = first[3..] + "\r\n" + second[..5];

            display.BeginSelection(from, extend: false);
            display.ExtendSelectionTo(to);
            Assert.Equal(expected, display.GetSelectedText());

            display.BeginSelection(to, extend: false);
            display.ExtendSelectionTo(from);
            Assert.Equal(expected, display.GetSelectedText());

            display.BeginSelection(from, extend: false);
            display.BeginSelection(to, extend: true);
            Assert.Equal(expected, display.GetSelectedText());
        });

        [Fact(DisplayName = "두 번 누르기: 그 줄 전체를 선택한다")]
        public void SelectLineAt_SelectsWholeLine() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create();
            AddLines(display, 0, 5);
            double lineHeight = LineHeight(display);

            Assert.True(display.SelectLineAt(new Point(30, lineHeight * 2.5)));
            Assert.Equal(Text(2), display.GetSelectedText());
        });

        [Fact(DisplayName = "선택한 줄이 지워질 때: 남은 줄까지만 남고, 다 지워지면 선택이 풀린다")]
        public void Selection_SurvivesTrimming() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create(maxLines: 10);
            AddLines(display, 0, 10);
            display.SelectAll();

            AddLines(display, 10, 5);
            string[] lines = display.GetSelectedText().Split("\r\n");
            Assert.Equal(Text(5), lines[0]);
            Assert.Equal(Text(9), lines[^1]);

            AddLines(display, 15, 20);
            Assert.False(display.HasSelection);
            Assert.Equal(string.Empty, display.GetSelectedText());
        });

        [Fact(DisplayName = "끌어서 선택하는 중: 영역 밖에 멈춰 있으면 그쪽으로 넘어가며 선택이 늘어난다")]
        public void AutoScrollSelection_ScrollsTowardPointerOutsideViewport() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create(height: 200);
            AddLines(display, 0, 60);
            display.ScrollToEnd();
            double end = display.VerticalOffset;
            display.BeginSelection(new Point(5, 195), extend: false);

            // 영역 안에서는 넘기지 않는다
            display.AutoScrollSelection(new Point(5, 100));
            Assert.Equal(end, display.VerticalOffset, 6);

            display.AutoScrollSelection(new Point(5, -60));
            double afterOne = display.VerticalOffset;
            Assert.True(afterOne < end);
            int selectedAfterOne = display.GetSelectedText().Split("\r\n").Length;

            display.AutoScrollSelection(new Point(5, -60));
            Assert.True(display.VerticalOffset < afterOne);
            Assert.True(display.GetSelectedText().Split("\r\n").Length > selectedAfterOne);

            // 멀리 끌어도 한 번에 넘기는 양에는 상한이 있다
            double before = display.VerticalOffset;
            display.AutoScrollSelection(new Point(5, 200 + 1000));
            Assert.InRange(display.VerticalOffset - before, 1, 120);
        });

        [Fact(DisplayName = "오래 켜 둔 상태: 좌표가 계속 커지지 않고, 새로 그린 것과 같은 결과가 나온다")]
        public void LongRun_CoordinatesStayBounded_AndMatchFreshDisplay() => Wpf.Run(() =>
        {
            const int total = 6000; // 약 10만 px — 좌표를 두 번쯤 당기게 된다
            ChatLineDisplay longRun = Create(maxLines: 200);
            double maxOrigin = 0;
            for (int i = 0; i < total; i++)
            {
                longRun.AddLine(Line(Text(i)));
                maxOrigin = Math.Max(maxOrigin, longRun.DocumentOrigin);
            }
            longRun.ScrollToEnd();

            // 당기지 않으면 약 10만까지 커진다
            Assert.True(maxOrigin < 51_000, $"좌표가 {maxOrigin:0}까지 커졌습니다.");
            Assert.True(maxOrigin > 40_000, "테스트가 좌표를 당길 만큼 줄을 넣지 못했습니다.");

            ChatLineDisplay fresh = Create(maxLines: 200);
            AddLines(fresh, total - 200, 200);
            fresh.ScrollToEnd();

            Assert.Equal(fresh.LineCount, longRun.LineCount);
            Assert.Equal(fresh.ExtentHeight, longRun.ExtentHeight, 6);
            Assert.Equal(fresh.VerticalOffset, longRun.VerticalOffset, 6);
            Assert.Equal(TopLine(fresh), TopLine(longRun));

            foreach (double y in new[] { 20.0, 100.0, 180.0 })
            {
                fresh.BeginSelection(new Point(0, y), extend: false);
                fresh.ExtendSelectionTo(new Point(90, y));
                longRun.BeginSelection(new Point(0, y), extend: false);
                longRun.ExtendSelectionTo(new Point(90, y));
                Assert.Equal(fresh.GetSelectedText(), longRun.GetSelectedText());
                Assert.NotEqual(string.Empty, longRun.GetSelectedText());
            }
        });

        [Fact(DisplayName = "올려 보는 중 좌표를 당겨도 보던 줄과 선택이 그대로다")]
        public void Rebase_WhileScrolledUp_KeepsViewAndSelection() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create(maxLines: 200);
            int next = 0;
            // 좌표를 당기기 직전까지 채운다
            while (display.DocumentOrigin < 49_000)
                display.AddLine(Line(Text(next++ % 1000)));

            display.SetVerticalOffset(display.ScrollableHeight * 0.6);
            string top = TopLine(display);
            display.BeginSelection(new Point(0, 50), extend: false);
            display.ExtendSelectionTo(new Point(200, 120));
            string selected = display.GetSelectedText();
            Assert.NotEqual(string.Empty, selected);
            double originBefore = display.DocumentOrigin;

            for (int i = 0; i < 80; i++)
                display.AddLine(Line(Text(next++ % 1000)));

            Assert.True(display.DocumentOrigin < originBefore - 40_000, "좌표를 당기지 않았습니다.");
            Assert.Equal(selected, display.GetSelectedText());
            display.ClearSelection();
            Assert.Equal(top, TopLine(display));
        });

        [Fact(DisplayName = "글꼴 변경: 줄을 다시 넣지 않고 배치만 다시 하고, 보던 자리를 지킨다")]
        public void FontChange_ReflowsWithoutRebuilding() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create();
            AddLines(display, 0, 60);
            display.ScrollToEnd();
            double height13 = display.ExtentHeight;

            display.FontSize = 18;
            Assert.Equal(60, display.LineCount);
            Assert.True(display.ExtentHeight > height13 * 1.2);
            Assert.True(display.IsAtEnd);

            display.FontSize = FontSize;
            Assert.Equal(height13, display.ExtentHeight, 6);
            Assert.True(display.IsAtEnd);

            // 줄 경계에 딱 걸치지 않게 줄 안쪽을 본다
            display.SetVerticalOffset(LineHeight(display) * 20 + 3);
            Assert.StartsWith("줄 020", TopLine(display));
            display.FontSize = 18;
            Assert.StartsWith("줄 020", TopLine(display));
            Assert.False(display.IsAtEnd);
        });

        [Fact(DisplayName = "너비 변경: 줄바꿈을 다시 잡고, 맨 아래였으면 맨 아래에, 올려 보던 중이면 그 줄에 머문다")]
        public void WidthChange_RewrapsAndKeepsPosition() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create(width: 400, height: 200);
            AddLines(display, 0, 40, LongTail);
            display.ScrollToEnd();
            double wide = display.ExtentHeight;

            Wpf.Layout(display, 220, 200);
            Assert.True(display.ExtentHeight > wide * 1.3);
            Assert.True(display.IsAtEnd);

            Wpf.Layout(display, 400, 200);
            Assert.Equal(wide, display.ExtentHeight, 6);

            display.SetVerticalOffset(LineHeight(display) * 10 + 3);
            string top = TopLine(display);
            Assert.StartsWith("줄 010", top);
            Wpf.Layout(display, 220, 200);
            Assert.Equal(top, TopLine(display));
            Assert.False(display.IsAtEnd);
        });

        [Fact(DisplayName = "높이 변경: 맨 아래를 보고 있었으면 바뀐 뒤에도 맨 아래다")]
        public void HeightChange_StaysAtEnd() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create(height: 250);
            AddLines(display, 0, 60);
            display.ScrollToEnd();

            Wpf.Layout(display, 400, 120);
            Assert.True(display.IsAtEnd);
            Assert.Equal(display.ExtentHeight - 120, display.VerticalOffset, 6);

            Wpf.Layout(display, 400, 250);
            Assert.True(display.IsAtEnd);
        });

        [Fact(DisplayName = "비우기: 줄·스크롤·선택이 모두 처음 상태로 돌아가고 다시 넣을 수 있다")]
        public void Clear_ResetsEverything() => Wpf.Run(() =>
        {
            ChatLineDisplay display = Create();
            AddLines(display, 0, 60);
            display.SetVerticalOffset(100);
            display.SelectAll();

            display.Clear();
            Assert.Equal(0, display.LineCount);
            Assert.Equal(0, display.ExtentHeight);
            Assert.Equal(0, display.ScrollableHeight);
            Assert.True(display.IsAtEnd);
            Assert.False(display.HasSelection);
            Assert.False(display.SelectLineAt(new Point(5, 5)));

            AddLines(display, 0, 3);
            Assert.Equal(3, display.LineCount);
            Assert.Equal(Text(0), TopLine(display));
        });
    }
}
