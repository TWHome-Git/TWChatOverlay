using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TWChatOverlay.Views.Addons
{
    public partial class RelicExpectationSimulatorView : UserControl
    {
        private const int MaxLevel = 20;
        private const string PowderIconUri = "pack://application:,,,/Data/images/Item/응축된신조의가루.png";
        private const string TemporaryMaterialIconUri = "pack://application:,,,/Data/images/Item/시드.png";
        private static readonly Brush SummaryValueBrush = new SolidColorBrush(Color.FromRgb(0xD6, 0xE8, 0xFF));
        private static readonly double[,] SuccessRates = CreateSuccessRates();
        private static readonly IReadOnlyList<RelicStepCost> PendantCosts = CreateCosts(isPendant: true);
        private static readonly IReadOnlyList<RelicStepCost> BraceletCosts = CreateCosts(isPendant: false);

        public RelicExpectationSimulatorView()
        {
            InitializeComponent();
        }

        private void CalculateButton_Click(object sender, RoutedEventArgs e)
        {
            if (!TryReadInputs(out RelicInput input))
                return;

            IReadOnlyList<RelicStepCost> costs = input.IsPendant ? PendantCosts : BraceletCosts;
            var rows = new List<ResultRow>();
            double totalAttempts = 0d;
            double totalPowder = 0d;
            double totalEssence = 0d;
            double totalMoonStone = 0d;
            double totalMoonPiece = 0d;
            int reachedLevel = input.CurrentLevel;
            string? stopReason = null;

            for (int level = input.CurrentLevel + 1; level <= input.TargetLevel; level++)
            {
                double chance = SuccessRates[level - 1, input.Difficulty - 1];
                if (chance <= 0d)
                {
                    stopReason = $"{FormatLevel(level)} 확률이 0%라 {FormatLevel(level - 1)}에서 정지";
                    break;
                }

                RelicStepCost cost = costs[level - 1];
                double expectedAttempts = cost.RequiredSuccesses / chance;
                double powder = expectedAttempts * cost.Powder;
                double essence = level < input.TargetLevel ? cost.Essence : 0;
                double moonStone = level < input.TargetLevel ? cost.MoonStone : 0;
                double moonPiece = expectedAttempts * cost.MoonPiece;

                rows.Add(new ResultRow(level, chance, cost, expectedAttempts, powder, essence, moonStone, moonPiece));
                totalAttempts += expectedAttempts;
                totalPowder += powder;
                totalEssence += essence;
                totalMoonStone += moonStone;
                totalMoonPiece += moonPiece;
                reachedLevel = level;
            }

            RenderResult(input, rows, reachedLevel, stopReason, totalAttempts, totalPowder, totalEssence, totalMoonStone, totalMoonPiece);
        }

        private bool TryReadInputs(out RelicInput input)
        {
            input = default;

            if (!TryReadInt(CurrentLevelTextBox, "현재 레벨", 0, 19, out int currentLevel) ||
                !TryReadInt(TargetLevelTextBox, "목표 레벨", 1, 20, out int targetLevel) ||
                !TryReadInt(DifficultyTextBox, "강화 가능 단수", 1, 20, out int difficulty))
            {
                return false;
            }

            if (targetLevel <= currentLevel)
            {
                MessageBox.Show("목표 레벨은 현재 레벨보다 높아야 합니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            input = new RelicInput(
                IsPendant: PendantRadio.IsChecked == true,
                CurrentLevel: currentLevel,
                TargetLevel: targetLevel,
                Difficulty: difficulty);
            return true;
        }

        private static bool TryReadInt(TextBox textBox, string label, int min, int max, out int value)
        {
            value = 0;
            string raw = (textBox.Text ?? string.Empty).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ||
                value < min ||
                value > max)
            {
                string range = max == int.MaxValue ? $"{min:N0} 이상" : $"{min:N0}~{max:N0}";
                MessageBox.Show($"{label}은 {range} 숫자로 입력해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void RenderResult(
            RelicInput input,
            IReadOnlyList<ResultRow> rows,
            int reachedLevel,
            string? stopReason,
            double totalAttempts,
            double totalPowder,
            double totalEssence,
            double totalMoonStone,
            double totalMoonPiece)
        {
            string equipmentName = input.IsPendant ? "펜던트" : "브레이슬릿";
            ResultDataGrid.ItemsSource = rows.Select(ResultGridRow.FromResult).ToList();

            SummaryTextBlock.Inlines.Clear();
            SummaryTextBlock.Inlines.Add(new Run($"| {equipmentName} | {FormatSummaryLevel(input.CurrentLevel)} -> {FormatSummaryLevel(input.TargetLevel)} | {FormatLevel(reachedLevel)} MAX |"));
            SummaryTextBlock.Inlines.Add(new LineBreak());
            SummaryTextBlock.Inlines.Add(new LineBreak());

            if (totalPowder > 0 || totalEssence > 0)
            {
                AddMaterialSummaryItem(PowderIconUri, FormatNumber(totalPowder));
                AddMaterialSummaryItem(TemporaryMaterialIconUri, FormatNumber(totalEssence));
            }

            if (totalMoonPiece > 0 || totalMoonStone > 0)
            {
                AddMaterialSummaryItem(TemporaryMaterialIconUri, FormatNumber(totalMoonPiece));
                AddMaterialSummaryItem(TemporaryMaterialIconUri, FormatNumber(totalMoonStone));
            }
        }

        private void AddMaterialSummaryItem(string imageUri, string countText)
        {
            SummaryTextBlock.Inlines.Add(new InlineUIContainer(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 22, 6),
                Children =
                {
                    new Image
                    {
                        Source = new BitmapImage(new Uri(imageUri, UriKind.Absolute)),
                        Width = 36,
                        Height = 36,
                        Margin = new Thickness(0, 0, 8, 0)
                    },
                    new TextBlock
                    {
                        Text = $"- {countText}개",
                        Foreground = SummaryValueBrush,
                        FontSize = 14,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            }));
        }

        private static string FormatLevel(int level)
        {
            if (level <= 0)
                return "0단계";

            return level <= 10
                ? $"신조 {level}단계"
                : $"루나 {level - 10}단계";
        }

        private static string FormatSummaryLevel(int level)
        {
            if (level <= 10)
                return $"신조 {Math.Max(0, level)}단계";

            return $"루나 {level - 10}단계";
        }

        private static string FormatNumber(double value)
            => Math.Ceiling(Math.Max(0d, value)).ToString("N0", CultureInfo.InvariantCulture);

        private static IReadOnlyList<RelicStepCost> CreateCosts(bool isPendant)
        {
            int[] shinjoCounts = isPendant
                ? new[] { 140, 100, 25, 35, 25, 25, 25, 25, 50, 50 }
                : new[] { 110, 80, 20, 30, 20, 20, 20, 20, 40, 40 };
            int lunaCount = isPendant ? 50 : 40;
            int[] powder = { 5, 5, 7, 10, 12, 14, 16, 17, 18, 19 };
            int[] essence = { 0, 3, 6, 10, 15, 21, 28, 36, 45, 54 };
            int[] moonPieces = { 9, 11, 12, 14, 15, 18, 21, 24, 27, 30 };
            int[] moonStones = { 1, 3, 6, 10, 15, 21, 28, 36, 45, 0 };

            var costs = new List<RelicStepCost>(MaxLevel);
            for (int i = 0; i < 10; i++)
                costs.Add(new RelicStepCost(Powder: powder[i], Essence: essence[i], MoonStone: 0, MoonPiece: 0, RequiredSuccesses: shinjoCounts[i]));
            for (int i = 0; i < 10; i++)
                costs.Add(new RelicStepCost(Powder: 0, Essence: 0, MoonStone: moonStones[i], MoonPiece: moonPieces[i], RequiredSuccesses: lunaCount));
            return costs;
        }

        private static double[,] CreateSuccessRates()
        {
            double[,] rates = new double[20, 20];
            double[][] rows =
            {
                new double[] {20,20,20,22,24,26,28,30,32,34,36,38,40,42,44,46,48,50,52,54},
                new double[] {10,20,20,20,22,24,26,28,30,32,34,36,38,40,42,44,46,48,50,52},
                new double[] {10,10,20,20,20,22,24,26,28,30,32,34,36,38,40,42,44,46,48,50},
                new double[] {0,0,10,20,20,20,22,24,26,28,30,32,34,36,38,40,42,44,46,48},
                new double[] {0,0,0,10,20,20,20,22,24,26,28,30,32,34,36,38,40,42,44,46},
                new double[] {0,0,0,0,10,20,20,20,22,24,26,28,30,32,34,36,38,40,42,44},
                new double[] {0,0,0,0,0,10,20,20,20,22,24,26,28,30,32,34,36,38,40,42},
                new double[] {0,0,0,0,0,0,10,20,20,20,22,24,26,28,30,32,34,36,38,40},
                new double[] {0,0,0,0,0,0,0,10,20,20,20,22,24,26,28,30,32,34,36,38},
                new double[] {0,0,0,0,0,0,0,0,10,20,20,20,22,24,26,28,30,32,34,36},
                new double[] {0,0,0,0,0,0,0,0,0,10,20,20,20,22,24,26,28,30,32,34},
                new double[] {0,0,0,0,0,0,0,0,0,0,10,20,20,20,22,24,26,28,30,32},
                new double[] {0,0,0,0,0,0,0,0,0,0,0,10,20,20,20,22,24,26,28,30},
                new double[] {0,0,0,0,0,0,0,0,0,0,0,0,10,20,20,20,22,24,26,28},
                new double[] {0,0,0,0,0,0,0,0,0,0,0,0,0,10,20,20,20,22,24,26},
                new double[] {0,0,0,0,0,0,0,0,0,0,0,0,0,0,10,20,20,20,22,24},
                new double[] {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,10,20,20,20,22},
                new double[] {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,10,20,20,20},
                new double[] {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,10,20,20},
                new double[] {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,10,20}
            };

            for (int row = 0; row < rows.Length; row++)
            {
                for (int column = 0; column < rows[row].Length; column++)
                    rates[row, column] = rows[row][column] / 100d;
            }

            return rates;
        }

        private readonly record struct RelicInput(
            bool IsPendant,
            int CurrentLevel,
            int TargetLevel,
            int Difficulty);

        private readonly record struct RelicStepCost(int Powder, int Essence, int MoonStone, int MoonPiece, int RequiredSuccesses);

        private readonly record struct ResultRow(
            int Level,
            double SuccessChance,
            RelicStepCost Cost,
            double ExpectedAttempts,
            double Powder,
            double Essence,
            double MoonStone,
            double MoonPiece);

        private sealed class ResultGridRow
        {
            public string StageLabel { get; init; } = string.Empty;
            public string ChanceText { get; init; } = string.Empty;
            public string ExpectedAttemptsText { get; init; } = string.Empty;
            public string PowderText { get; init; } = string.Empty;
            public string EssenceText { get; init; } = string.Empty;
            public string MoonPieceText { get; init; } = string.Empty;
            public string MoonStoneText { get; init; } = string.Empty;

            public static ResultGridRow FromResult(ResultRow row)
            {
                bool isShinjo = row.Level <= 10;
                return new ResultGridRow
                {
                    StageLabel = FormatLevel(row.Level),
                    ChanceText = row.SuccessChance.ToString("P2", CultureInfo.InvariantCulture),
                    ExpectedAttemptsText = FormatNumber(row.ExpectedAttempts),
                    PowderText = isShinjo ? FormatNumber(row.Powder) : string.Empty,
                    EssenceText = isShinjo ? FormatNumber(row.Essence) : string.Empty,
                    MoonPieceText = isShinjo ? string.Empty : FormatNumber(row.MoonPiece),
                    MoonStoneText = isShinjo ? string.Empty : FormatNumber(row.MoonStone)
                };
            }
        }
    }
}
