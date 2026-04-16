using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace TWChatOverlay.Views.Addons
{
    public partial class CoreEnhanceSimulatorView : UserControl
    {
        private const int PieceCount = 1;
        private readonly List<CoreStage> _stages = BuildStages();

        public CoreEnhanceSimulatorView()
        {
            InitializeComponent();
            InitializeUi();
        }

        private void InitializeUi()
        {
            if (MainStatToggle != null)
                MainStatToggle.IsChecked = true;

            if (UseTicketToggle != null)
            {
                UseTicketToggle.IsChecked = false;
            }
            if (Ticket1TextBox != null) Ticket1TextBox.Text = "0.03"; // 300만원
            if (Ticket2TextBox != null) Ticket2TextBox.Text = "0.18"; // 1800만원
            if (Ticket3TextBox != null) Ticket3TextBox.Text = "0.65"; // 6500만원
            if (Ticket4TextBox != null) Ticket4TextBox.Text = "0.90"; // 9000만원

            UpdateStatModeText();
            ApplyModeUi();
        }

        private void MainStatToggle_Changed(object sender, RoutedEventArgs e)
        {
            UpdateStatModeText();
        }

        private void UpdateStatModeText()
        {
            if (StatModeTextBlock == null || MainStatToggle == null)
                return;

            bool isMainStat = MainStatToggle.IsChecked == true;
            StatModeTextBlock.Text = isMainStat ? "현재: 주스탯" : "현재: 부스탯";
        }

        private static List<CoreStage> BuildStages()
        {
            var rows = new (int Tier, int Enhance, int Stat, int Dust, int Crystal, long Seed, int RatePct)[]
            {
                (0,0,1,0,0,0,0),
                (0,1,2,10,0,4_000_000,100),
                (0,2,3,20,0,4_400_000,70),
                (0,3,4,30,0,4_800_000,50),
                (0,4,5,40,0,5_200_000,20),
                (1,0,6,50,0,5_600_000,10),
                (1,1,7,60,0,6_000_000,7),
                (1,2,8,70,0,6_400_000,7),
                (1,3,9,80,0,6_800_000,7),
                (1,4,10,90,0,7_200_000,7),
                (2,0,12,100,0,7_600_000,5),
                (2,1,14,110,0,8_000_000,5),
                (2,2,16,120,0,8_400_000,5),
                (2,3,18,130,0,8_800_000,5),
                (2,4,20,140,0,9_200_000,5),
                (3,0,23,200,5,12_000_000,2),
                (3,1,26,210,5,12_400_000,2),
                (3,2,29,220,5,12_800_000,2),
                (3,3,32,230,5,13_200_000,2),
                (3,4,35,240,5,13_600_000,2),
                (4,0,40,250,5,14_000_000,1),
                (4,1,50,260,5,14_400_000,1),
                (4,2,60,270,5,14_800_000,1),
                (4,3,70,280,5,15_200_000,1),
                (4,4,80,290,5,15_600_000,1)
            };

            return rows.Select((x, i) => new CoreStage
            {
                Index = i,
                Tier = x.Tier,
                Enhance = x.Enhance,
                StatGain = x.Stat,
                Dust = x.Dust,
                Crystal = x.Crystal,
                Seed = x.Seed,
                SuccessRate = x.RatePct / 100d,
                RatePctInt = x.RatePct
            }).ToList();
        }

        private void UseTicketToggle_Changed(object sender, RoutedEventArgs e)
        {
            ApplyModeUi();
        }

        private void HasDustToggle_Changed(object sender, RoutedEventArgs e)
        {
            UpdateBoxPriceVisibility();
        }

        private void ApplyModeUi()
        {
            bool useTicket = UseTicketToggle?.IsChecked == true;

            if (TicketOnPanel != null)
                TicketOnPanel.Visibility = useTicket ? Visibility.Visible : Visibility.Collapsed;
            if (TicketOffPanel != null)
                TicketOffPanel.Visibility = useTicket ? Visibility.Collapsed : Visibility.Visible;

            if (StartStageComboBox == null || TargetStageComboBox == null)
                return;

            var startCandidates = useTicket
                ? _stages.Where(s => s.Tier > 0 || (s.Tier == 1 && s.Enhance == 0)).ToList()
                : _stages.ToList();

            if (useTicket)
                startCandidates = _stages.Where(s => s.Index >= 5).ToList(); // 1진0강부터

            StartStageComboBox.ItemsSource = startCandidates;
            StartStageComboBox.DisplayMemberPath = nameof(CoreStage.Display);
            StartStageComboBox.SelectedIndex = 0;

            TargetStageComboBox.ItemsSource = _stages;
            TargetStageComboBox.DisplayMemberPath = nameof(CoreStage.Display);
            TargetStageComboBox.SelectedIndex = _stages.Count - 1;

            UpdateResultColumns(useTicket);
            UpdateBoxPriceVisibility();
        }

        private void UpdateBoxPriceVisibility()
        {
            if (BoxPricePanel == null)
                return;

            bool useTicket = UseTicketToggle?.IsChecked == true;
            bool hasDust = HasDustToggle?.IsChecked == true;
            BoxPricePanel.Visibility = (!useTicket && !hasDust) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateResultColumns(bool useTicket)
        {
            if (DustColumn == null || CrystalColumn == null || SeedColumn == null || TicketColumn == null)
                return;

            if (useTicket)
            {
                DustColumn.Visibility = Visibility.Collapsed;
                CrystalColumn.Visibility = Visibility.Collapsed;
                SeedColumn.Visibility = Visibility.Collapsed;
                TicketColumn.Visibility = Visibility.Visible;
            }
            else
            {
                DustColumn.Visibility = Visibility.Visible;
                CrystalColumn.Visibility = Visibility.Visible;
                SeedColumn.Visibility = Visibility.Visible;
                TicketColumn.Visibility = Visibility.Collapsed;
            }
        }


        private void CalculateButton_Click(object sender, RoutedEventArgs e)
        {
            if (StartStageComboBox?.SelectedItem is not CoreStage start ||
                TargetStageComboBox?.SelectedItem is not CoreStage target)
            {
                MessageBox.Show("시작/목표 단계를 선택해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (start.Index >= target.Index)
            {
                MessageBox.Show("목표 단계는 시작 단계보다 높아야 합니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool useTicket = UseTicketToggle?.IsChecked == true;
            bool isMainStat = MainStatToggle?.IsChecked == true;
            bool isSubStat = !isMainStat;
            bool hasDust = HasDustToggle?.IsChecked == true;

            long boxPrice = 0;
            long dustUnitPrice = 0;
            if (!useTicket && !hasDust)
            {
                if (!TryParseLong(BoxPriceTextBox?.Text, out boxPrice) || boxPrice < 0)
                {
                    MessageBox.Show("상자 가격을 숫자로 입력해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                dustUnitPrice = boxPrice + 20_000;
            }

            var ticketPrices = new Dictionary<int, long>();
            if (useTicket && !TryParseTicketPrices(ticketPrices))
            {
                MessageBox.Show("진화권 가격을 억 단위 숫자로 입력해주세요. (예: 1.25)", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var rows = new List<ResultRow>();
            long totalDust = 0;
            long totalCrystal = 0;
            long totalEnhanceSeed = 0;
            long totalDustCost = 0;
            double totalTicketCount = 0;
            long totalTicketCost = 0;
            long totalExpectedCost = 0;
            var ticketCountsByTier = new Dictionary<int, double>
            {
                [1] = 0,
                [2] = 0,
                [3] = 0,
                [4] = 0
            };

            for (int i = start.Index + 1; i <= target.Index; i++)
            {
                CoreStage step = _stages[i];

                if (step.SuccessRate <= 0)
                {
                    MessageBox.Show($"{step.Display} 단계 확률이 0%라 계산할 수 없습니다.", "계산 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                double expectedAttemptsRaw = 1d / step.SuccessRate;
                int ticketTier = 0;
                bool applyTicket = false;
                if (useTicket)
                {
                    ticketTier = GetTicketTierForStage(step);
                    applyTicket = ticketTier > 0;
                }

                int dustPerAttempt = step.Dust;
                int crystalPerAttempt = step.Crystal;
                long seedPerAttempt = step.Seed;

                if (isSubStat)
                {
                    dustPerAttempt /= 2;
                    crystalPerAttempt /= 2;
                    seedPerAttempt /= 2;
                }

                long dustExpected = applyTicket
                    ? 0
                    : (long)Math.Round(dustPerAttempt * expectedAttemptsRaw * PieceCount, MidpointRounding.AwayFromZero);
                long crystalExpected = applyTicket
                    ? 0
                    : (long)Math.Round(crystalPerAttempt * expectedAttemptsRaw * PieceCount, MidpointRounding.AwayFromZero);
                long seedExpected = applyTicket
                    ? 0
                    : (long)Math.Round(seedPerAttempt * expectedAttemptsRaw * PieceCount, MidpointRounding.AwayFromZero);

                // 진화권도 확률 기대값(1/p) 기준 장수로 계산
                double ticketCount = applyTicket ? expectedAttemptsRaw : 0d;
                long ticketCost = applyTicket
                    ? (long)Math.Round(ticketPrices.GetValueOrDefault(ticketTier, 0) * ticketCount, MidpointRounding.AwayFromZero)
                    : 0;
                if (applyTicket)
                {
                    ticketCountsByTier[ticketTier] += ticketCount;
                }

                long dustCost = 0;
                if (!useTicket && !hasDust)
                {
                    dustCost = dustExpected * dustUnitPrice;
                }

                long stepTotalCost = useTicket
                    ? ticketCost
                    : dustCost + seedExpected;

                totalDust += dustExpected;
                totalCrystal += crystalExpected;
                totalEnhanceSeed += seedExpected;
                totalDustCost += dustCost;
                totalTicketCount += ticketCount;
                totalTicketCost += ticketCost;
                totalExpectedCost += stepTotalCost;

                rows.Add(new ResultRow
                {
                    StageLabel = step.Display,
                    MethodText = $"{step.RatePctInt}%",
                    ExpectedAttemptsText = FormatExpectedCount(expectedAttemptsRaw),
                    DustText = dustExpected.ToString("N0", CultureInfo.CurrentCulture),
                    CrystalText = crystalExpected.ToString("N0", CultureInfo.CurrentCulture),
                    SeedText = FormatEokCompact(seedExpected),
                    TicketCountText = FormatExpectedCount(ticketCount),
                    TotalCostText = FormatEokCompact(stepTotalCost)
                });
            }

            if (ResultGrid != null)
            {
                ResultGrid.ItemsSource = rows;
            }

            string statLabel = isMainStat ? "주스탯" : "부스탯";
            if (useTicket)
            {
                SummaryTextBlock.Text =
                    $"{statLabel} / {start.Display} -> {target.Display} {PieceCount}개 기대값\n" +
                    $"총 진화권 사용 갯수: {FormatExpectedCount(totalTicketCount)}개\n" +
                    $"1진화 강화권 - {FormatExpectedCount(ticketCountsByTier[1])}개\n" +
                    $"2진화 강화권 - {FormatExpectedCount(ticketCountsByTier[2])}개\n" +
                    $"3진화 강화권 - {FormatExpectedCount(ticketCountsByTier[3])}개\n" +
                    $"4진화 강화권 - {FormatExpectedCount(ticketCountsByTier[4])}개\n" +
                    $"총 기대 비용 - {FormatEok(totalExpectedCost)}억";
            }
            else
            {
                SummaryTextBlock.Text =
                    $"{statLabel} / {start.Display} -> {target.Display} {PieceCount}개 기대값\n" +
                    $"총 가루 갯수 - {totalDust:N0}개 (총 가루 분해 비용 - {FormatEok(totalDustCost)}억)\n" +
                    $"총 결정 갯수 - {totalCrystal:N0}개\n" +
                    $"총 강화 비용 - {FormatEok(totalEnhanceSeed)}억\n" +
                    $"총 기대 비용 - {FormatEok(totalExpectedCost)}억";
            }
        }

        private bool TryParseTicketPrices(Dictionary<int, long> ticketPrices)
        {
            bool ok1 = TryParseEokToSeed(Ticket1TextBox?.Text, out long t1) && t1 >= 0;
            bool ok2 = TryParseEokToSeed(Ticket2TextBox?.Text, out long t2) && t2 >= 0;
            bool ok3 = TryParseEokToSeed(Ticket3TextBox?.Text, out long t3) && t3 >= 0;
            bool ok4 = TryParseEokToSeed(Ticket4TextBox?.Text, out long t4) && t4 >= 0;
            if (!ok1 || !ok2 || !ok3 || !ok4)
                return false;

            ticketPrices[1] = t1;
            ticketPrices[2] = t2;
            ticketPrices[3] = t3;
            ticketPrices[4] = t4;
            return true;
        }

        private static bool TryParseEokToSeed(string? input, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string normalized = input.Trim()
                .Replace(",", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("억", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("seed", string.Empty, StringComparison.OrdinalIgnoreCase);

            if (!decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out decimal eok) &&
                !decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out eok))
            {
                return false;
            }

            if (eok < 0)
                return false;

            decimal seedValue = eok * 100_000_000m;
            if (seedValue > long.MaxValue)
                return false;

            value = (long)Math.Round(seedValue, MidpointRounding.AwayFromZero);
            return true;
        }

        private static bool TryParseLong(string? input, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            string normalized = input.Trim()
                .Replace(",", string.Empty, StringComparison.Ordinal)
                .Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("seed", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("만", "0000", StringComparison.OrdinalIgnoreCase)
                .Replace("억", "00000000", StringComparison.OrdinalIgnoreCase);

            return long.TryParse(normalized, NumberStyles.Integer, CultureInfo.CurrentCulture, out value) ||
                   long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }

        private static string FormatSeed(long amount)
        {
            long eok = amount / 100_000_000;
            long man = (amount % 100_000_000) / 10_000;

            if (eok == 0 && man == 0)
                return "0 Seed";

            if (eok == 0)
                return $"{man:N0}만 Seed";

            return man == 0 ? $"{eok:N0}억 Seed" : $"{eok:N0}억 {man:N0}만 Seed";
        }

        private static int GetTicketTierForStage(CoreStage stage)
        {
            if (stage.Tier == 1 && stage.Enhance >= 1 && stage.Enhance <= 4) return 1;
            if (stage.Tier == 2 && stage.Enhance == 0) return 1;

            if (stage.Tier == 2 && stage.Enhance >= 1 && stage.Enhance <= 4) return 2;
            if (stage.Tier == 3 && stage.Enhance == 0) return 2;

            if (stage.Tier == 3 && stage.Enhance >= 1 && stage.Enhance <= 4) return 3;
            if (stage.Tier == 4 && stage.Enhance == 0) return 3;

            if (stage.Tier == 4 && stage.Enhance >= 1 && stage.Enhance <= 4) return 4;

            return 0;
        }

        private static string FormatEok(long amount)
            => (amount / 100_000_000d).ToString("0.00", CultureInfo.CurrentCulture);

        private static string FormatEokSeed(long amount)
            => $"{FormatEok(amount)} 억 Seed";

        private static string FormatEokCompact(long amount)
            => $"{(amount / 100_000_000d).ToString("0.00", CultureInfo.CurrentCulture)}억";

        private static string FormatExpectedCount(double value)
        {
            double rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
            if (Math.Abs(rounded - Math.Round(rounded)) < 0.0001)
                return Math.Round(rounded).ToString("N0", CultureInfo.CurrentCulture);

            return rounded.ToString("N2", CultureInfo.CurrentCulture);
        }

        private sealed class CoreStage
        {
            public int Index { get; init; }
            public int Tier { get; init; }
            public int Enhance { get; init; }
            public int StatGain { get; init; }
            public int Dust { get; init; }
            public int Crystal { get; init; }
            public long Seed { get; init; }
            public double SuccessRate { get; init; }
            public int RatePctInt { get; init; }
            public string Display => $"{Tier}진 {Enhance}강";

            public override string ToString() => Display;
        }

        private sealed class ResultRow
        {
            public string StageLabel { get; init; } = string.Empty;
            public string MethodText { get; init; } = string.Empty;
            public string ExpectedAttemptsText { get; init; } = string.Empty;
            public string DustText { get; init; } = string.Empty;
            public string CrystalText { get; init; } = string.Empty;
            public string SeedText { get; init; } = string.Empty;
            public string TicketCountText { get; init; } = string.Empty;
            public string TotalCostText { get; init; } = string.Empty;
        }
    }
}
