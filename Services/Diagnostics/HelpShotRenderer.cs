using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 설정 [?] 도움말용 예시 이미지를 생성한다. 실행: TWChatOverlay.exe --render-help-shots [출력폴더]
    /// 실제 앱과 같은 팔레트로 "꺼짐/켜짐" 상태 프레임을 그려, 도움말 창이 이를 교차 표시해
    /// GIF처럼 전후 변화를 보여준다. 생성된 PNG는 Data/images/Help/ 에 넣고 리소스로 빌드한다.
    /// </summary>
    public static class HelpShotRenderer
    {
        private const double PanelWidth = 328;

        private static readonly Color PanelBg = Color.FromRgb(0x12, 0x18, 0x16);
        private static readonly Color BorderCol = Color.FromRgb(0x2A, 0x33, 0x2E);
        private static readonly Color SubText = Color.FromRgb(0x8C, 0x91, 0x97);
        private static readonly Color Mint = Color.FromRgb(0x0C, 0xD2, 0x9D);

        // 기본 채팅 색 (ChatSettings 기본값과 동일)
        private static readonly Color NormalCol = Colors.White;
        private static readonly Color TeamCol = Color.FromRgb(0x00, 0xBF, 0xFF);
        private static readonly Color ClubCol = Color.FromRgb(0x00, 0xFF, 0x00);
        private static readonly Color ShoutCol = Color.FromRgb(0xFF, 0x80, 0x00);
        private static readonly Color SystemCol = Color.FromRgb(0xFF, 0xFF, 0x00);
        private static readonly Color GoldCol = Color.FromRgb(0xFF, 0xD8, 0x4A);
        private static readonly Color SkyCol = Color.FromRgb(0x5A, 0xC8, 0xE8);
        // 에타 레벨 구간별 기본 색 (IdDisplaySettings 기본값과 동일)
        private static readonly Color Range1Col = Color.FromRgb(0xC8, 0xCD, 0xD2);
        private static readonly Color Range2Col = Color.FromRgb(0x7E, 0xE0, 0x81);
        private static readonly Color Range3Col = Color.FromRgb(0x5A, 0xC8, 0xE8);
        private static readonly Color Range4Col = Color.FromRgb(0xC0, 0x8B, 0xFF);
        private static readonly Color Range5Col = Color.FromRgb(0xFF, 0xD8, 0x4A);

        public static void RenderAll(string outDir)
        {
            Directory.CreateDirectory(outDir);

            // ── 기본 채팅 필터: 종류별 색 한눈에 ──
            Save(outDir, "chat_filter.png", Panel("채팅 종류별 색",
                Line(NormalCol, ("모비딕 : 사냥 가실 분?", NormalCol)),
                Line(TeamCol, ("[팀] 나비 : 집결지로 와주세요", TeamCol)),
                Line(ClubCol, ("[클럽] 딩고 : 보스 곧 엽니다", ClubCol)),
                Line(ShoutCol, ("외치기 : 잡템 삽니다 [상점왕]", ShoutCol)),
                Line(SystemCol, ("[아이템] 을 획득하였습니다.", SystemCol))));

            // ── 색상 동기화·색 지정 (설정 UI 목업으로 사용법을 그림으로) ──
            Save(outDir, "chat_id_sender_sync_on.png", SyncGuideMock(syncOn: true));
            Save(outDir, "chat_id_sender_sync_off.png", SyncGuideMock(syncOn: false));

            // ── 에타 레벨 ──
            Save(outDir, "chat_id_eta_level_off.png", PanelToggle(false,
                Line(NormalCol, ("모비딕", NormalCol), (" : 사냥 가실 분?", NormalCol))));
            Save(outDir, "chat_id_eta_level_on.png", PanelToggle(true,
                Line(NormalCol, ("모비딕", NormalCol), ("[72]", GoldCol), (" : 사냥 가실 분?", NormalCol))));

            // ── 에타 레벨별 색상 ──
            Save(outDir, "chat_id_eta_range_off.png", PanelToggle(false,
                Line(NormalCol, ("나비", NormalCol), ("[15]", GoldCol), (" : 반가워요", NormalCol)),
                Line(NormalCol, ("딩고", NormalCol), ("[55]", GoldCol), (" : 사냥 가실 분?", NormalCol)),
                Line(NormalCol, ("모비딕", NormalCol), ("[92]", GoldCol), (" : 보스 곧 엽니다", NormalCol))));
            Save(outDir, "chat_id_eta_range_on.png", PanelToggle(true,
                Line(NormalCol, ("나비", NormalCol), ("[15]", Range1Col), (" : 반가워요", NormalCol)),
                Line(NormalCol, ("호밀", NormalCol), ("[33]", Range2Col), (" : 물약 팝니다", NormalCol)),
                Line(NormalCol, ("딩고", NormalCol), ("[55]", Range3Col), (" : 사냥 가실 분?", NormalCol)),
                Line(NormalCol, ("루카", NormalCol), ("[71]", Range4Col), (" : 집결지로 와주세요", NormalCol)),
                Line(NormalCol, ("모비딕", NormalCol), ("[92]", Range5Col), (" : 보스 곧 엽니다", NormalCol))));

            // ── 캐릭터 ──
            Save(outDir, "chat_id_character_off.png", PanelToggle(false,
                Line(NormalCol, ("모비딕", NormalCol), ("[72]", GoldCol), (" : 사냥 가실 분?", NormalCol))));
            Save(outDir, "chat_id_character_on.png", PanelToggle(true,
                Line(NormalCol, ("모비딕", NormalCol), ("[72]", GoldCol), ("[루시안]", SkyCol), (" : 사냥 가실 분?", NormalCol))));

            // ── 아이디 태그 ──
            Save(outDir, "chat_id_tag_off.png", PanelToggle(false,
                Line(NormalCol, ("모비딕", NormalCol), (" : 물약 팝니다", NormalCol))));
            Save(outDir, "chat_id_tag_on.png", PanelToggle(true,
                Line(NormalCol, ("모비딕", NormalCol), ("[상인]", SkyCol), (" : 물약 팝니다", NormalCol)),
                DimLine("(idtag.txt: 모비딕=상인)")));

            // ── 클럽 보스 ──
            Save(outDir, "chat_id_club_boss_on.png", PanelToggle(true,
                Line(ClubCol, ("[클럽] 딩고 : 보스 갑니다", ClubCol)),
                Line(SystemCol, ("클럽 공지 : '[클럽 보스] 그람존' 에 '딩고' 님이 참가하셨습니다.", SystemCol))));
            Save(outDir, "chat_id_club_boss_off.png", PanelToggle(false,
                Line(ClubCol, ("[클럽] 딩고 : 보스 갑니다", ClubCol)),
                DimLine("(클럽 보스 공지는 표시되지 않음)")));

            // ── 타임 스탬프 ──
            Save(outDir, "chat_id_timestamp_off.png", PanelToggle(false,
                Line(NormalCol, ("모비딕 : 안녕하세요", NormalCol))));
            Save(outDir, "chat_id_timestamp_on.png", PanelToggle(true,
                Line(NormalCol, ("[7시 15분 18초] ", SubText), ("모비딕 : 안녕하세요", NormalCol))));

            // ── 폰트 크기 ──
            Save(outDir, "chat_font_13.png", Panel("크기 13",
                LineSized(13, ("모비딕 : 폰트 크기를 바꿀 수 있습니다", NormalCol))));
            Save(outDir, "chat_font_17.png", Panel("크기 17",
                LineSized(17, ("모비딕 : 폰트 크기를 바꿀 수 있습니다", NormalCol))));

            // ── 종류 말머리 ──
            Save(outDir, "chat_category_prefix_off.png", PanelToggle(false,
                Line(NormalCol, ("모비딕 : 사냥 가실 분?", NormalCol)),
                Line(TeamCol, ("나비 : 집결지로 와주세요", TeamCol)),
                Line(ClubCol, ("딩고 : 보스 곧 엽니다", ClubCol))));
            Save(outDir, "chat_category_prefix_on.png", PanelToggle(true,
                Line(NormalCol, ("[일반] ", NormalCol), ("모비딕 : 사냥 가실 분?", NormalCol)),
                Line(TeamCol, ("[팀] ", TeamCol), ("나비 : 집결지로 와주세요", TeamCol)),
                Line(ClubCol, ("[클럽] ", ClubCol), ("딩고 : 보스 곧 엽니다", ClubCol))));

            // ── 서브 채팅창: 탭 전환 ──
            Save(outDir, "chat_clone_basic.png", CloneMock(activeShout: false));
            Save(outDir, "chat_clone_shout.png", CloneMock(activeShout: true));
        }

        // ===== 조립 헬퍼 =====

        /// <summary>어두운 채팅 판 + 우상단 상태 배지.</summary>
        private static FrameworkElement Panel(string badge, params UIElement[] lines)
            => PanelCore(Badge(badge), lines);

        /// <summary>어두운 채팅 판 + 우상단에 실제 설정과 같은 모양의 토글 스위치 (켜짐/꺼짐 상태 대응용).</summary>
        private static FrameworkElement PanelToggle(bool on, params UIElement[] lines)
            => PanelCore(ToggleBadge(on), lines);

        private static FrameworkElement PanelCore(UIElement badge, params UIElement[] lines)
        {
            var stack = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
            foreach (var line in lines)
                stack.Children.Add(line);

            var grid = new Grid();
            grid.Children.Add(stack);
            grid.Children.Add(badge);

            return new Border
            {
                Width = PanelWidth,
                Background = new SolidColorBrush(PanelBg),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = grid,
            };
        }

        /// <summary>설정 화면의 토글 스위치와 같은 모양: 켜짐=민트 트랙+오른쪽 흰 손잡이, 꺼짐=회색 트랙+왼쪽 손잡이.</summary>
        private static UIElement ToggleBadge(bool on)
        {
            var knob = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Colors.White),
                HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 2, 0),
            };
            var track = new Border
            {
                Width = 32,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(on ? Mint : Color.FromRgb(0x3A, 0x42, 0x3E)),
                Child = knob,
            };
            return new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0),
                Child = track,
            };
        }

        private static UIElement Badge(string text)
        {
            return new Border
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 6, 6, 0),
                Background = new SolidColorBrush(Color.FromArgb(0x28, Mint.R, Mint.G, Mint.B)),
                BorderBrush = new SolidColorBrush(Mint),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(6, 1, 6, 2),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Mint),
                    FontFamily = new FontFamily("Malgun Gothic"),
                },
            };
        }

        private static TextBlock Line(Color baseColor, params (string Text, Color Color)[] parts)
            => LineSized(13, parts);

        private static TextBlock LineSized(double size, params (string Text, Color Color)[] parts)
        {
            var block = new TextBlock
            {
                FontSize = size,
                FontFamily = new FontFamily("Malgun Gothic"),
                Margin = new Thickness(0, 1, 0, 1),
                TextWrapping = TextWrapping.Wrap,
                // 배지와 겹치지 않게 첫 줄 오른쪽 여유
                Padding = new Thickness(0, 0, 0, 0),
            };
            foreach (var (text, color) in parts)
                block.Inlines.Add(new Run(text) { Foreground = new SolidColorBrush(color) });
            return block;
        }

        private static TextBlock DimLine(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(SubText),
                FontFamily = new FontFamily("Malgun Gothic"),
                Margin = new Thickness(0, 1, 0, 1),
            };
        }

        /// <summary>
        /// 색상 동기화 사용법 목업: 실제 설정 행(동기화 링크 + 색 버튼)을 그려서
        /// "동기화를 끄면 색 버튼이 켜지고, 눌러 고른 색이 채팅에 적용된다"를 그림으로 보여준다.
        /// </summary>
        private static FrameworkElement SyncGuideMock(bool syncOn)
        {
            var textCol = Color.FromRgb(0xE8, 0xEA, 0xE9);

            // 설정 행 목업: [아이디 ......... 동기화  ▓색버튼]
            var row = new DockPanel { Margin = new Thickness(2, 2, 2, 6) };
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(new TextBlock
            {
                Text = "동기화",
                FontSize = 12,
                FontFamily = new FontFamily("Malgun Gothic"),
                Foreground = new SolidColorBrush(syncOn ? Mint : SubText),
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            });
            right.Children.Add(new Border
            {
                Width = 28,
                Height = 15,
                CornerRadius = new CornerRadius(2),
                Background = new SolidColorBrush(syncOn ? Color.FromRgb(0x3A, 0x42, 0x3E) : SkyCol),
                BorderBrush = new SolidColorBrush(syncOn ? BorderCol : Mint),
                BorderThickness = new Thickness(1),
                Opacity = syncOn ? 0.5 : 1.0,
                VerticalAlignment = VerticalAlignment.Center,
            });
            DockPanel.SetDock(right, Dock.Right);
            row.Children.Add(right);
            row.Children.Add(new TextBlock
            {
                Text = "아이디",
                FontSize = 12,
                FontFamily = new FontFamily("Malgun Gothic"),
                Foreground = new SolidColorBrush(textCol),
                VerticalAlignment = VerticalAlignment.Center,
            });

            var rowBox = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1F, 0x1C)),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 5, 10, 5),
                Child = row,
            };

            var hint = new TextBlock
            {
                Text = syncOn
                    ? "동기화 켜짐(민트색) — 색 버튼 비활성, 줄 색을 그대로 따름"
                    : "동기화를 눌러 끄면 색 버튼 활성 — 눌러서 색을 고르면 바로 적용",
                FontSize = 10,
                FontFamily = new FontFamily("Malgun Gothic"),
                Foreground = new SolidColorBrush(SubText),
                Margin = new Thickness(2, 5, 2, 5),
            };

            // 적용 결과 채팅 줄
            var result = syncOn
                ? Line(NormalCol, ("모비딕", NormalCol), (" : 사냥 가실 분?", NormalCol))
                : Line(NormalCol, ("모비딕", SkyCol), (" : 사냥 가실 분?", NormalCol));

            var stack = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
            stack.Children.Add(rowBox);
            stack.Children.Add(hint);
            stack.Children.Add(result);

            return new Border
            {
                Width = PanelWidth,
                Background = new SolidColorBrush(PanelBg),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = stack,
            };
        }

        /// <summary>서브 채팅창 축소 목업: 탭 스트립 + 내용이 탭에 따라 달라진다.</summary>
        private static FrameworkElement CloneMock(bool activeShout)
        {
            var tabs = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4, 6, 4) };
            foreach (string name in new[] { "기본", "외치기", "시스템" })
            {
                bool active = activeShout ? name == "외치기" : name == "기본";
                tabs.Children.Add(new Border
                {
                    Margin = new Thickness(2, 0, 2, 0),
                    Padding = new Thickness(8, 2, 8, 3),
                    CornerRadius = new CornerRadius(3),
                    Background = active
                        ? new SolidColorBrush(Color.FromArgb(0x30, Mint.R, Mint.G, Mint.B))
                        : Brushes.Transparent,
                    BorderBrush = active ? new SolidColorBrush(Mint) : new SolidColorBrush(BorderCol),
                    BorderThickness = new Thickness(0, 0, 0, active ? 2 : 1),
                    Child = new TextBlock
                    {
                        Text = name,
                        FontSize = 11,
                        Foreground = active ? new SolidColorBrush(Mint) : new SolidColorBrush(SubText),
                        FontFamily = new FontFamily("Malgun Gothic"),
                    },
                });
            }

            var lines = new StackPanel { Margin = new Thickness(10, 2, 10, 8) };
            if (activeShout)
            {
                lines.Children.Add(Line(ShoutCol, ("외치기 : 잡템 일괄 삽니다 [상점왕]", ShoutCol)));
                lines.Children.Add(Line(ShoutCol, ("외치기 : 각인 도와드려요 [세공사]", ShoutCol)));
                lines.Children.Add(Line(ShoutCol, ("외치기 : 클럽원 모집합니다 [달빛클럽]", ShoutCol)));
            }
            else
            {
                lines.Children.Add(Line(NormalCol, ("모비딕 : 사냥 가실 분?", NormalCol)));
                lines.Children.Add(Line(TeamCol, ("[팀] 나비 : 집결지로 와주세요", TeamCol)));
                lines.Children.Add(Line(SystemCol, ("[경험의 심장] 아이템을 사용하였습니다.", SystemCol)));
            }

            var stack = new StackPanel();
            stack.Children.Add(tabs);
            stack.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(BorderCol), Margin = new Thickness(6, 0, 6, 4) });
            stack.Children.Add(lines);

            var grid = new Grid();
            grid.Children.Add(stack);
            grid.Children.Add(Badge(activeShout ? "외치기 탭" : "기본 탭"));

            return new Border
            {
                Width = PanelWidth,
                Background = new SolidColorBrush(PanelBg),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = grid,
            };
        }

        private static void Save(string outDir, string fileName, FrameworkElement visual)
        {
            visual.Measure(new Size(PanelWidth, double.PositiveInfinity));
            visual.Arrange(new Rect(visual.DesiredSize));
            visual.UpdateLayout();

            // 2배 DPI로 렌더 → 도움말 창에서 선명하게 축소 표시
            const double scale = 2.0;
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(visual.ActualWidth * scale),
                (int)Math.Ceiling(visual.ActualHeight * scale),
                96 * scale,
                96 * scale,
                PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(outDir, fileName));
            encoder.Save(stream);
        }
    }
}
