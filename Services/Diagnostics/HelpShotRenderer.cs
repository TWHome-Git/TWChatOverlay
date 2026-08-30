using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 설정 [?] 도움말용 예시 이미지를 생성한다. 실행: TWChatOverlay.exe --render-help-shots [출력폴더]
    /// 실제 앱과 같은 팔레트로 "꺼짐/켜짐" 상태 프레임을 그려, 도움말 창이 이를 교차 표시해
    /// GIF처럼 전후 변화를 보여준다. 생성된 PNG는 Data/images/Help/ 에 넣고 리소스로 빌드한다.
    /// </summary>
    public static class HelpShotRenderer
    {
        // 도움말 창 폭(480) - 여백에 맞춘 프레임 폭. HelpWindow._frameImage.MaxWidth와 함께 조정한다.
        private const double PanelWidth = 448;

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
                Line(NormalCol, ("[일반] 아나이스 : 아페테리아 가실 분?", NormalCol)),
                Line(TeamCol, ("[팀] 벤야 : 입구로 와주세요", TeamCol)),
                Line(ClubCol, ("[클럽] 리체 : 클럽보스 하실분?", ClubCol)),
                Line(ShoutCol, ("[외치기] 티치엘 : 훈장 50개 삽니다", ShoutCol)),
                Line(SystemCol, ("[시스템] : [에오니스 라피스] 아이템을 획득하였습니다.",SystemCol))));

            // ── 색상 동기화·색 지정 (설정 UI 목업으로 사용법을 그림으로) ──
            Save(outDir, "chat_id_sender_sync_on.png", SyncGuideMock(syncOn: true));
            Save(outDir, "chat_id_sender_sync_off.png", SyncGuideMock(syncOn: false));

            // ── 에타 레벨 ──
            Save(outDir, "chat_id_eta_level_off.png", PanelToggle(false,
                Line(NormalCol, ("아나이스", NormalCol), (" : 어비스 가실 분?", NormalCol))));
            Save(outDir, "chat_id_eta_level_on.png", PanelToggle(true,
                Line(NormalCol, ("아나이스", NormalCol), ("[72]", GoldCol), (" : 어비스 가실 분?", NormalCol))));

            // ── 에타 레벨별 색상 ──
            Save(outDir, "chat_id_eta_range_off.png", PanelToggle(false,
                Line(NormalCol, ("보리스", NormalCol), ("[15]", GoldCol), (" : 반가워요", NormalCol)),
                Line(NormalCol, ("벤야", NormalCol), ("[55]", GoldCol), (" : 어비스 가실 분?", NormalCol)),
                Line(NormalCol, ("아나이스", NormalCol), ("[92]", GoldCol), (" : 클럽보스 5초후 잡아요", NormalCol))));
            Save(outDir, "chat_id_eta_range_on.png", PanelToggle(true,
                Line(NormalCol, ("보리스", NormalCol), ("[15]", Range1Col), (" : ㅎㅇ", NormalCol)),
                Line(NormalCol, ("루시안", NormalCol), ("[33]", Range2Col), (" : 에오니스 라피스 팝니다", NormalCol)),
                Line(NormalCol, ("벤야", NormalCol), ("[55]", Range3Col), (" : 어비스 가실 분?", NormalCol)),
                Line(NormalCol, ("로아미니", NormalCol), ("[71]", Range4Col), (" : 상실의 섬으로 와주세요", NormalCol)),
                Line(NormalCol, ("아나이스", NormalCol), ("[92]", Range5Col), (" : 클럽 보스 5초후 잡아요", NormalCol))));

            // ── 캐릭터 ──
            Save(outDir, "chat_id_character_off.png", PanelToggle(false,
                Line(NormalCol, ("비호", NormalCol), ("[72]", GoldCol), (" : 어비스 가실 분?", NormalCol))));
            Save(outDir, "chat_id_character_on.png", PanelToggle(true,
                Line(NormalCol, ("비호", NormalCol), ("[72]", GoldCol), ("[아나이스]", SkyCol), (" : 어비스 가실 분?", NormalCol))));

            // ── 아이디 태그 ──
            Save(outDir, "chat_id_tag_off.png", PanelToggle(false,
                Line(NormalCol, ("아나이스", NormalCol), (" : 에오니스 라피스 팝니다", NormalCol))));
            Save(outDir, "chat_id_tag_on.png", PanelToggle(true,
                Line(NormalCol, ("아나이스", NormalCol), ("[혐사]", SkyCol), (" : 에오니스 라피스 팝니다", NormalCol)),
                DimLine("(idtag.txt: 아나이스 = 혐사)")));

            // ── 클럽 보스 ──
            Save(outDir, "chat_id_club_boss_on.png", PanelToggle(true,
                Line(ClubCol, ("[클럽] 벤야 : 클럽 보스 갑니다", ClubCol)),
                Line(SystemCol, ("클럽 공지 : '[클럽 보스] 그람존' 에 '벤야' 님이 참가하셨습니다.", SystemCol))));
            Save(outDir, "chat_id_club_boss_off.png", PanelToggle(false,
                Line(ClubCol, ("[클럽] 벤야 : 클럽 보스 갑니다", ClubCol)),
                DimLine("(클럽 보스 공지는 표시되지 않음)")));

            // ── 타임 스탬프 ──
            Save(outDir, "chat_id_timestamp_off.png", PanelToggle(false,
                Line(NormalCol, ("아나이스 : 안녕하세요", NormalCol))));
            Save(outDir, "chat_id_timestamp_on.png", PanelToggle(true,
                Line(NormalCol, ("[7시 15분 18초] ", SubText), ("아나이스 : 안녕하세요", NormalCol))));

            // ── 폰트 크기 ──
            Save(outDir, "chat_font_13.png", Panel("크기 13",
                LineSized(13, ("아나이스 : 폰트 크기를 바꿀 수 있습니다", NormalCol))));
            Save(outDir, "chat_font_17.png", Panel("크기 17",
                LineSized(17, ("아나이스 : 폰트 크기를 바꿀 수 있습니다", NormalCol))));

            // ── 종류 말머리 ──

            Save(outDir, "chat_category_prefix_off.png", PanelToggle(false,
                Line(NormalCol, ("아나이스 : 어비스 가실 분?", NormalCol)),
                Line(TeamCol, ("로아미니 : 상실의 섬으로 와주세요", TeamCol)),
                Line(ClubCol, ("벤야 : 클럽 보스 5초후 잡아요", ClubCol)),
                Line(ShoutCol, ("외치기 : 훈장 50개 삽니다 [티치엘]", ShoutCol))));
            Save(outDir, "chat_category_prefix_on.png", PanelToggle(true,
                Line(NormalCol, ("[일반] ", NormalCol), ("아나이스 : 어비스 가실 분?", NormalCol)),
                Line(TeamCol, ("[팀] ", TeamCol), ("로아미니 : 상실의 섬으로 와주세요", TeamCol)),
                Line(ClubCol, ("[클럽] ", ClubCol), ("벤야 : 클럽 보스 5초후 잡아요", ClubCol)),
                Line(ShoutCol, ("[외치기] ", ShoutCol), ("티치엘", ShoutCol), (" : 훈장 50개 삽니다", ShoutCol))));

            // ── 서브 채팅창: 탭 전환 ──
            Save(outDir, "chat_clone_basic.png", CloneMock(activeShout: false));
            Save(outDir, "chat_clone_shout.png", CloneMock(activeShout: true));

            // ── 외치기: 토스트 팝업 (실제 창) ──
            Save(outDir, "shout_toast_off.png", PanelToggle(false,
                Line(ShoutCol, ("외치기 : 훈장 50개 삽니다 [티치엘]", ShoutCol)),
                DimLine("(채팅창에만 표시 — 팝업 없음)")));
            Save(outDir, "shout_toast_on.png", PanelToggle(true,
                Line(ShoutCol, ("외치기 : 훈장 50개 삽니다 [티치엘]", ShoutCol)),
                RealShoutToast("훈장 50개 삽니다 [티치엘]")));

            // ── 외치기: 닉네임 자동복사 (실제 창) ──
            Save(outDir, "shout_autocopy_off.png", PanelToggle(false,
                RealShoutToast("주화 일괄 삽니다 [김주화]"),
                DimLine("(클립보드 변화 없음)")));
            Save(outDir, "shout_autocopy_on.png", PanelToggle(true,
                RealShoutToast("주화 일괄 삽니다 [김주화]"),
                Chip("클립보드에 '김주화' 복사됨")));

            // ── 외치기: 토스트 글자 크기 (실제 창) ──
            Save(outDir, "shout_toast_font_13.png", Panel("크기 13",
                RealShoutToast("초보자 도와드려요 [벤야]", 13)));
            Save(outDir, "shout_toast_font_20.png", Panel("크기 20",
                RealShoutToast("초보자 도와드려요 [벤야]", 20)));

            // ── 화면: 투명도 ──
            Save(outDir, "display_opacity_100.png", OpacityMock(0xF2, "투명도 100%"));
            Save(outDir, "display_opacity_50.png", OpacityMock(0x66, "투명도 50%"));

            // ── 화면: 잠금 해제 모드 (실제 격자 배경 재현) ──
            Save(outDir, "display_unlock_off.png", UnlockMock(unlocked: false));
            Save(outDir, "display_unlock_on.png", UnlockMock(unlocked: true));

            // ── 화면: 메뉴 바 가로형 ──
            Save(outDir, "display_menu_h_off.png", PanelToggle(false, MenuBarMock(horizontal: false)));
            Save(outDir, "display_menu_h_on.png", PanelToggle(true, MenuBarMock(horizontal: true)));

            // ── 화면: 항상 위 ──
            Save(outDir, "display_ontop_off.png", PanelToggle(false, ZOrderMock(overlayOnTop: false)));
            Save(outDir, "display_ontop_on.png", PanelToggle(true, ZOrderMock(overlayOnTop: true)));

            // ── 키워드: 색상 강조 ──
            Save(outDir, "keyword_color_off.png", PanelToggle(false,
                Line(ShoutCol, ("외치기 : 세크리드 주화 주머니 팝니다 [김주화]", ShoutCol)),
                Line(ShoutCol, ("외치기 : 클럽원 모집 [김클럽]", ShoutCol))));
            Save(outDir, "keyword_color_on.png", PanelToggle(true,
                Line(ShoutCol, ("외치기 : ", ShoutCol), ("세크리드 주화 주머니", Color.FromRgb(0xFF, 0x5A, 0x5A)), (" 팝니다 [김주화]", ShoutCol)),
                Line(ShoutCol, ("외치기 : 클럽원 모집 [김클럽]", ShoutCol)),
                DimLine("(키워드: @세크리드 주화 주머니)")));

            // ── 경험치 추적 (실제 뷰) ──
            Save(outDir, "exp_tracker_off.png", PanelToggle(false,
                DimLine("(추적 창 표시 안 함)")));
            Save(outDir, "exp_tracker_on.png", PanelToggle(true, RealExpTracker()));

            // ── 경험치 누적 알림 (실제 창) ──
            Save(outDir, "exp_cum_off.png", PanelToggle(false,
                DimLine("(누적 표시 없음)")));
            Save(outDir, "exp_cum_on.png", PanelToggle(true,
                RealExpAlert("경험치 150억 누적 달성")));

            // ── 저효율 알림 (실제 창) ──
            Save(outDir, "exp_loweff_off.png", PanelToggle(false,
                DimLine("(효율이 떨어져도 알림 없음)")));
            Save(outDir, "exp_loweff_on.png", PanelToggle(true,
                RealExpAlert("저효율: 최근 획득이 기준치 미만입니다")));

            // ── 아이템 획득 알림 (실제 창) ──
            Save(outDir, "item_drop_off.png", PanelToggle(false,
                Line(SystemCol, ("[설계자의 반지] 을 획득하였습니다.", SystemCol)),
                DimLine("(알림 없음)")));
            Save(outDir, "item_drop_on.png", PanelToggle(true,
                Line(SystemCol, ("[설계자의 반지] 을 획득하였습니다.", SystemCol)),
                RealItemToast("설계자의 반지", ItemDropGrade.Rare)));

            // ── 버프 추적 (실제 창) ──
            Save(outDir, "buff_tracker_off.png", PanelToggle(false,
                DimLine("(버프 추적 창 표시 안 함)")));
            Save(outDir, "buff_tracker_on.png", PanelToggle(true, RealBuffTracker()));

            // ── 필드 보스 알림 ──
            Save(outDir, "boss_alert_3min.png", Panel("3분 전",
                ToastMock("필드 보스", "아칸 등장 3분 전", SystemCol)));
            Save(outDir, "boss_alert_spawn.png", Panel("5초 전",
                ToastMock("필드 보스", "아칸이 곧 등장합니다!", Color.FromRgb(0xFF, 0x5A, 0x5A))));

            // ── 던전 도우미 알림들 (실제 창) ──
            Save(outDir, "dungeon_wave_off.png", PanelToggle(false, DimLine("(웨이브가 끝나도 알림 없음)")));
            Save(outDir, "dungeon_wave_on.png", PanelToggle(true,
                RealDungeonAlert("웨이브 종료")));
            Save(outDir, "dungeon_reflect_off.png", PanelToggle(false, DimLine("(반사 패턴 알림 없음)")));
            Save(outDir, "dungeon_reflect_on.png", PanelToggle(true,
                RealDungeonAlert("2사도 반사 패턴!")));
            Save(outDir, "dungeon_abandon_count_off.png", PanelToggle(false, DimLine("(입장해도 알림 없음)")));
            Save(outDir, "dungeon_abandon_count_on.png", PanelToggle(true,
                RealDungeonAlert("어밴던로드 입장 3회")));
            Save(outDir, "dungeon_abandon_gold_off.png", PanelToggle(false, DimLine("(통계 창 표시 안 함)")));
            Save(outDir, "dungeon_abandon_gold_on.png", PanelToggle(true, RealAbandonSummary()));
            Save(outDir, "dungeon_craving_count_off.png", PanelToggle(false, DimLine("(입장해도 알림 없음)")));
            Save(outDir, "dungeon_craving_count_on.png", PanelToggle(true,
                RealDungeonAlert("갈망하는 즐거움 입장 2회")));
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
            var stack = new StackPanel { Margin = new Thickness(10, 2, 10, 8) };
            foreach (var line in lines)
                stack.Children.Add(line);

            // 배지(토글/텍스트)는 내용 위에 겹치지 않게 전용 줄로 분리 —
            // 실제 창 이미지에 토글이 붙어 있는 것처럼 보이지 않도록 한다
            var grid = new StackPanel();
            grid.Children.Add(badge);
            grid.Children.Add(stack);

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

        // ===== 실제 프로그램 창 렌더: 진짜 창/뷰를 인스턴스화해 그 모습 그대로 찍는다 =====

        private static readonly FontFamily RealFont = new("Malgun Gothic");

        /// <summary>창을 화면에 띄우지 않고 루트 비주얼만 분리해 렌더 대상으로 쓴다.</summary>
        private static FrameworkElement WindowRoot(Window w)
        {
            var root = (FrameworkElement)w.Content;
            // 분리하면 창에 걸린 DataContext 상속이 끊기므로 루트에 로컬 값으로 옮긴다
            // (분리 전 root.DataContext는 상속값을 돌려주므로 null 검사로는 거를 수 없다)
            if (w.DataContext != null)
                root.DataContext = w.DataContext;
            w.Content = null;
            try { w.Close(); } catch { }
            root.HorizontalAlignment = HorizontalAlignment.Left;
            return root;
        }

        /// <summary>실제 외치기 토스트 창.</summary>
        private static FrameworkElement RealShoutToast(string message, double? fontSize = null)
        {
            var settings = new ChatSettings();
            if (fontSize.HasValue)
                settings.ShoutToastFontSize = fontSize.Value;
            var root = WindowRoot(new Views.ShoutToastWindow(message, RealFont, settings));
            root.MaxWidth = 426;
            return root;
        }

        /// <summary>실제 경험치 알림 창.</summary>
        private static FrameworkElement RealExpAlert(string message)
        {
            var w = new Views.ExperienceAlertWindow(new ChatSettings());
            w.SetMessage(message);
            var root = WindowRoot(w);
            root.MaxWidth = 426;
            return root;
        }

        /// <summary>실제 던전 알림 창 (입장 횟수·웨이브 등 공용).</summary>
        private static FrameworkElement RealDungeonAlert(string message)
        {
            var root = WindowRoot(new Views.DungeonCountDisplayWindow(message, RealFont, durationSeconds: 0, new ChatSettings()));
            root.MaxWidth = 426;
            return root;
        }

        /// <summary>실제 아이템 획득 토스트 창.</summary>
        private static FrameworkElement RealItemToast(string itemName, ItemDropGrade grade)
        {
            var root = WindowRoot(new Views.ItemDropToastWindow(itemName, grade, RealFont));
            root.MaxWidth = 426;
            return root;
        }

        /// <summary>실제 경험치 추적 뷰 + 견본 값.</summary>
        private static FrameworkElement RealExpTracker()
        {
            return new Views.ExpTrackerView
            {
                DataContext = new ExpTrackerSample(),
                Width = 330,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
        }

        private sealed class ExpTrackerSample
        {
            public string TotalExpValueDisplay { get; } = "852.4억";
            public string ExpPerHourDisplay { get; } = "82.3억";
            public string LastGainedExpDisplay { get; } = "+152만";
            public string GainCountDisplay { get; } = "128";
            public bool ShowGainCountDisplay { get; } = true;
            public bool IsMeasurementStopped { get; } = false;
            public System.Windows.Input.ICommand? ResetExpCommand { get; } = null;
        }

        /// <summary>실제 버프 추적 창 + 견본 버프 (Has* 세터가 private이라 같은 모양의 견본 DataContext로 교체).</summary>
        private static FrameworkElement RealBuffTracker()
        {
            var settings = new ChatSettings();
            var tracker = new BuffTrackerService(settings, suppressEndSound: true);
            var root = WindowRoot(new Views.BuffTrackerWindow(tracker, settings));
            root.DataContext = new BuffTrackerSample();
            root.MaxWidth = 426;
            return root;
        }

        private sealed class BuffTrackerSample
        {
            public bool HasRareBuffs { get; } = true;
            public bool HasExpBuffs { get; } = true;
            public System.Collections.ObjectModel.ObservableCollection<BuffTrackerService.BuffDisplayItem> ActiveRareBuffs { get; } = new();
            public System.Collections.ObjectModel.ObservableCollection<BuffTrackerService.BuffDisplayItem> ActiveExpBuffs { get; } = new();

            public BuffTrackerSample()
            {
                ActiveRareBuffs.Add(new BuffTrackerService.BuffDisplayItem("레어 하트", "00:08", BuffIcon("RareHeart.png"), 0, isRare: true));
                ActiveExpBuffs.Add(new BuffTrackerService.BuffDisplayItem("경험의 하트", "14:32", BuffIcon("ExpHeart.png"), 0));
                ActiveExpBuffs.Add(new BuffTrackerService.BuffDisplayItem("EOS", "02:05", BuffIcon("EOS.png"), 1));
            }

            private static ImageSource? BuffIcon(string file)
            {
                try
                {
                    return new BitmapImage(new Uri($"pack://application:,,,/Data/images/Buff/{file}", UriKind.Absolute));
                }
                catch { return null; }
            }
        }

        /// <summary>실제 어밴던로드 상황판(통계) 창 + 견본 주간 데이터.</summary>
        private static FrameworkElement RealAbandonSummary()
        {
            var settings = new ChatSettings();
            var root = WindowRoot(new Views.AbandonRoadSummaryWindow(settings, new LogAnalysisService(settings)));
            root.DataContext = new AbandonSummarySample();
            root.Width = 400;
            return root;
        }

        private sealed class AbandonSummarySample
        {
            public string WeekText { get; } = "8/24 (일) ~ 8/30 (토)";
            public string SummaryText { get; } = "어밴던로드 주간 합계: 1억 2350만";
            public System.Collections.ObjectModel.ObservableCollection<AbandonMonthlyStoneSummaryEntryViewModel> StoneEntries { get; } = new()
            {
                new("하급 마정석", "pack://application:,,,/Data/images/Item/하급마정석.png", 42),
                new("중급 마정석", "pack://application:,,,/Data/images/Item/중급마정석.png", 17),
                new("상급 마정석", "pack://application:,,,/Data/images/Item/상급마정석.png", 5),
                new("최상급 마정석", "pack://application:,,,/Data/images/Item/최상급마정석.png", 1),
            };
        }

        /// <summary>알림 토스트 목업: 민트 테두리 작은 창 + 제목/본문.</summary>
        private static FrameworkElement ToastMock(string title, string body, Color bodyColor)
            => ToastMockSized(12, title, body, bodyColor);

        private static FrameworkElement ToastMockSized(double bodySize, string title, string body, Color bodyColor)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Mint),
                FontFamily = new FontFamily("Malgun Gothic"),
                Margin = new Thickness(0, 0, 0, 3),
            });
            stack.Children.Add(new TextBlock
            {
                Text = body,
                FontSize = bodySize,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(bodyColor),
                FontFamily = new FontFamily("Malgun Gothic"),
            });
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x16, 0x14)),
                BorderBrush = new SolidColorBrush(Mint),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 7),
                Margin = new Thickness(0, 4, 0, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = stack,
            };
        }

        /// <summary>작은 상태 칩 (예: 클립보드 복사됨).</summary>
        private static FrameworkElement Chip(string text)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x28, Mint.R, Mint.G, Mint.B)),
                BorderBrush = new SolidColorBrush(Mint),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(8, 2, 8, 3),
                Margin = new Thickness(0, 4, 0, 1),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Mint),
                    FontFamily = new FontFamily("Malgun Gothic"),
                },
            };
        }

        /// <summary>투명도 목업: 게임 화면 배경 위에 배경 투명도가 다른 채팅 판. 글자는 그대로 선명.</summary>
        private static FrameworkElement OpacityMock(byte panelAlpha, string badge)
        {
            var chat = new StackPanel { Margin = new Thickness(10, 8, 10, 8) };
            chat.Children.Add(Line(NormalCol, ("[일반] 아나이스 : 어비스 가실 분?", NormalCol)));
            chat.Children.Add(Line(TeamCol, ("[팀] 로아미니 : 상실의 섬으로 와주세요", TeamCol)));

            var panel = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(panelAlpha, PanelBg.R, PanelBg.G, PanelBg.B)),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(20, 16, 20, 16),
                Child = chat,
            };

            var backdrop = new Grid();
            // 게임 화면 느낌의 배경 (초원 톤 그라데이션)
            backdrop.Children.Add(new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x4A, 0x6B, 0x3A), Color.FromRgb(0x2C, 0x44, 0x28), 45),
            });
            backdrop.Children.Add(panel);
            backdrop.Children.Add(Badge(badge));

            return new Border
            {
                Width = PanelWidth,
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Child = backdrop,
            };
        }

        /// <summary>
        /// 잠금 해제 모드 목업: 실제 UiLockService.BackdropWindow와 동일한
        /// 민트 격자(32px, 50% 불투명)를 게임 화면 배경 위에 깔고 창과 좌표 칩을 얹는다.
        /// </summary>
        private static FrameworkElement UnlockMock(bool unlocked)
        {
            var chat = new StackPanel { Margin = new Thickness(10, 6, 10, 6) };
            chat.Children.Add(Line(NormalCol, ("아나이스 : 어비스 가실 분?", NormalCol)));

            var windowStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(24, 22, 0, 22),
            };
            windowStack.Children.Add(new Border
            {
                Width = 210,
                Background = new SolidColorBrush(Color.FromArgb(0xE8, PanelBg.R, PanelBg.G, PanelBg.B)),
                BorderBrush = unlocked ? new SolidColorBrush(Mint) : new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(unlocked ? 2 : 1),
                CornerRadius = new CornerRadius(4),
                Child = chat,
            });
            if (unlocked)
                windowStack.Children.Add(Chip("X: 120  Y: 340  가로: 320  세로: 180"));

            var backdrop = new Grid();
            backdrop.Children.Add(new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromRgb(0x4A, 0x6B, 0x3A), Color.FromRgb(0x2C, 0x44, 0x28), 45),
            });
            if (unlocked)
            {
                // 실제 배치 기준 격자와 동일: 민트 2px 선, 32px 칸, 전체 50% 불투명
                backdrop.Children.Add(new System.Windows.Shapes.Rectangle
                {
                    Opacity = 0.5,
                    Fill = CreateUnlockGridBrush(),
                });
            }
            backdrop.Children.Add(windowStack);
            backdrop.Children.Add(Badge(unlocked ? "잠금 해제 — 드래그로 이동" : "잠금 상태"));

            return new Border
            {
                Width = PanelWidth,
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Child = backdrop,
            };
        }

        /// <summary>UiLockService.BackdropWindow.CreateGridBrush와 동일한 격자 브러시.</summary>
        private static DrawingBrush CreateUnlockGridBrush()
        {
            var line = Color.FromArgb(0x70, 0x0C, 0xD2, 0x9D);
            var pen = new Pen(new SolidColorBrush(line), 2);
            var drawing = new GeometryDrawing
            {
                Pen = pen,
                Geometry = new GeometryGroup
                {
                    Children =
                    {
                        new LineGeometry(new Point(0, 0), new Point(32, 0)),
                        new LineGeometry(new Point(0, 0), new Point(0, 32)),
                    }
                }
            };
            var brush = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 32, 32),
                ViewportUnits = BrushMappingMode.Absolute,
            };
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 메뉴 바 목업: 실제 MenuWindow 구성 그대로 —
        /// 앱 아이콘 / 최소화 / 채팅 / TW DB / 일일·주간 / 달력 / 외치기 / M(메모) / 잠금 / 설정 / 종료.
        /// </summary>
        private static FrameworkElement MenuBarMock(bool horizontal)
        {
            const double cell = 28;
            var textCol = Color.FromRgb(0xE8, 0xEA, 0xE9);

            UIElement PackIcon(string uri)
            {
                var image = new System.Windows.Controls.Image
                {
                    Width = cell - 6,
                    Height = cell - 6,
                    Stretch = Stretch.Uniform,
                };
                try { image.Source = new BitmapImage(new Uri(uri, UriKind.Absolute)); } catch { }
                return Cell(image);
            }

            UIElement TextIcon(string text, double size, string family = "Malgun Gothic", double lineHeight = 0)
            {
                var tb = new TextBlock
                {
                    Text = text,
                    FontSize = size,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(textCol),
                    FontFamily = new FontFamily(family),
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                if (lineHeight > 0)
                {
                    tb.LineHeight = lineHeight;
                    tb.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
                }
                return Cell(tb);
            }

            UIElement Cell(UIElement content) => new Border
            {
                Width = cell,
                Height = cell,
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = content is FrameworkElement fe ? Center(fe) : content,
            };

            FrameworkElement Center(FrameworkElement fe)
            {
                fe.HorizontalAlignment = HorizontalAlignment.Center;
                fe.VerticalAlignment = VerticalAlignment.Center;
                return fe;
            }

            // 최소화: 세로 바는 가로 글자 낮은 버튼, 가로 바는 세로 글자 좁은 버튼
            var minText = new TextBlock
            {
                Text = horizontal ? "최\n소\n화" : "최소화",
                FontSize = 8.5,
                LineHeight = 9.5,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                Foreground = new SolidColorBrush(textCol),
                FontFamily = new FontFamily("Malgun Gothic"),
                TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var minButton = new Border
            {
                Width = horizontal ? 16 : cell + 2,
                Height = horizontal ? cell + 2 : 15,
                Margin = new Thickness(1, horizontal ? 1 : 2, 1, horizontal ? 1 : 2),
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromRgb(0x22, 0x2A, 0x26)),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = minText,
            };

            var items = new StackPanel
            {
                Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Left,
            };
            items.Children.Add(PackIcon("pack://application:,,,/favicon.ico"));
            items.Children.Add(minButton);
            items.Children.Add(PackIcon("pack://application:,,,/Data/images/MenuIcon/Chat.png"));
            items.Children.Add(TextIcon("TW\nDB", 8.5, lineHeight: 9));
            items.Children.Add(PackIcon("pack://application:,,,/Data/images/MenuIcon/DailyWeekly.png"));
            items.Children.Add(PackIcon("pack://application:,,,/Data/images/MenuIcon/Calendar.png"));
            items.Children.Add(PackIcon("pack://application:,,,/Data/images/MenuIcon/Shout.png"));
            items.Children.Add(TextIcon("M", 12));
            items.Children.Add(TextIcon("", 13, family: "Segoe MDL2 Assets"));
            items.Children.Add(PackIcon("pack://application:,,,/Data/images/MenuIcon/Setting.png"));
            items.Children.Add(PackIcon("pack://application:,,,/Data/images/MenuIcon/Exit.png"));

            // 실제 메뉴 바처럼 불투명한 어두운 판
            var bar = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x18, 0x1F, 0x1C)),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(2),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = items,
            };
            var stack = new StackPanel();
            stack.Children.Add(bar);
            stack.Children.Add(DimLine(horizontal ? "(가로형 메뉴 바)" : "(세로형 메뉴 바 — 기본)"));
            return stack;
        }

        /// <summary>z-순서 목업: '다른 앱' 창과 오버레이 창의 앞뒤 관계.</summary>
        private static FrameworkElement ZOrderMock(bool overlayOnTop)
        {
            var other = new Border
            {
                Width = 180,
                Height = 84,
                Background = new SolidColorBrush(Color.FromRgb(0x3C, 0x41, 0x47)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x67)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = overlayOnTop ? new Thickness(70, 0, 0, 0) : new Thickness(70, 14, 0, 0),
                Child = new TextBlock
                {
                    Text = "다른 앱 창",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontFamily = new FontFamily("Malgun Gothic"),
                    Margin = new Thickness(8, 5, 0, 0),
                },
            };
            var overlay = new Border
            {
                Width = 150,
                Height = 60,
                Background = new SolidColorBrush(Color.FromArgb(0xF2, PanelBg.R, PanelBg.G, PanelBg.B)),
                BorderBrush = new SolidColorBrush(Mint),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = overlayOnTop ? new Thickness(0, 32, 0, 0) : new Thickness(0, 40, 0, 0),
                Child = new TextBlock
                {
                    Text = "채팅 오버레이",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Mint),
                    FontFamily = new FontFamily("Malgun Gothic"),
                    Margin = new Thickness(8, 5, 0, 0),
                },
            };

            var grid = new Grid { Height = 104, HorizontalAlignment = HorizontalAlignment.Left, Width = 260 };
            if (overlayOnTop) { grid.Children.Add(other); grid.Children.Add(overlay); }
            else { grid.Children.Add(overlay); grid.Children.Add(other); }

            var stack = new StackPanel();
            stack.Children.Add(grid);
            stack.Children.Add(DimLine(overlayOnTop ? "(오버레이가 항상 앞에 유지됨)" : "(다른 앱이 앞으로 오면 그대로 둠)"));
            return stack;
        }

        /// <summary>경험치 추적 창 목업: 2x2 지표 + 중지/리셋.</summary>
        private static FrameworkElement TrackerMock()
        {
            var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition());

            void Cell(int r, int c, string label, string value, Color valueColor)
            {
                var cell = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
                cell.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 9,
                    Foreground = new SolidColorBrush(SubText),
                    FontFamily = new FontFamily("Malgun Gothic"),
                });
                cell.Children.Add(new TextBlock
                {
                    Text = value,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(valueColor),
                    FontFamily = new FontFamily("Malgun Gothic"),
                });
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }

            Cell(0, 0, "누적 경험치", "1,234.5억", Colors.White);
            Cell(0, 1, "1시간 예상", "82.3억", Colors.White);
            Cell(1, 0, "획득 경험치", "+1,520만", Color.FromRgb(0x7E, 0xE0, 0x81));
            Cell(1, 1, "처치 수", "128마리", Colors.White);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            buttons.Children.Add(Chip("중지"));
            buttons.Children.Add(new Border { Width = 6 });
            buttons.Children.Add(Chip("리셋"));

            var stack = new StackPanel();
            stack.Children.Add(grid);
            stack.Children.Add(buttons);
            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x16, 0x14)),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 6, 10, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = stack,
            };
        }

        /// <summary>버프 추적 창 목업: 버프 이름 + 남은 시간 (임박한 버프는 붉게).</summary>
        private static FrameworkElement BuffMock()
        {
            var stack = new StackPanel();
            void Row(string name, string time, Color timeColor)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Width = 190 };
                var timeText = new TextBlock
                {
                    Text = time,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(timeColor),
                    FontFamily = new FontFamily("Malgun Gothic"),
                };
                DockPanel.SetDock(timeText, Dock.Right);
                row.Children.Add(timeText);
                row.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(Colors.White),
                    FontFamily = new FontFamily("Malgun Gothic"),
                });
                stack.Children.Add(row);
            }
            Row("축복의 노래", "14:32", Mint);
            Row("가속", "02:05", Mint);
            Row("집중", "00:08", Color.FromRgb(0xFF, 0x5A, 0x5A));

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x10, 0x16, 0x14)),
                BorderBrush = new SolidColorBrush(BorderCol),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 6, 10, 8),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = stack,
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
                ? Line(NormalCol, ("아나이스", NormalCol), (" : 어비스 가실 분?", NormalCol))
                : Line(NormalCol, ("아나이스", SkyCol), (" : 어비스 가실 분?", NormalCol));

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
                lines.Children.Add(Line(ShoutCol, ("외치기 : 훈장 50개 삽니다 [티치엘]", ShoutCol)));
                lines.Children.Add(Line(ShoutCol, ("외치기 : 세크리드 주화 주머니 팝니다 [김주화]", ShoutCol)));
                lines.Children.Add(Line(ShoutCol, ("외치기 : 클럽원 모집합니다 [리체]", ShoutCol)));
            }
            else
            {
                lines.Children.Add(Line(NormalCol, ("아나이스 : 아페테리아 가실 분?", NormalCol)));
                lines.Children.Add(Line(TeamCol, ("[팀] 벤야 : 입구로 와주세요", TeamCol)));
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
