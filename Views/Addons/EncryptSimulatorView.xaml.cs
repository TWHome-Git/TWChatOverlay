using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace TWChatOverlay.Views.Addons
{
    /// <summary>
    /// Encrypt 강화 시뮬레이터 뷰입니다.
    /// </summary>
    public partial class EncryptSimulatorView : UserControl
    {
        private readonly record struct SuccessLogEntry(long AttemptsForSuccess, int InkBefore, int InkAfter, decimal Cost, decimal UnitCost, double TheoreticalRate, double ActualRate);
        private const string VianuEffectCostMan = "666";
        private const string VianuEclipseCostMan = "2046";
        private const string VianuSacredCostMan = "2946";
        private const string VianuEffectCostElso = "999";
        private const string VianuEclipseCostElso = "3096";
        private const string VianuSacredCostElso = "4419";
        private const string EtaSacredCostMan = "29668";
        private const string EtaSacredElsoCostMan = "44502";

        private bool _isUiReady;
        private int _currentInk;
        private long _totalAttempts;
        private long _successCount;
        private decimal _totalCost;
        private decimal _totalExpectedCost;
        private double _totalExpectedSuccesses;
        private double _totalSuccessVariance;
        private long _attemptsSinceLastSuccess;
        private Paragraph? _lastCumulativeParagraph;

        public EncryptSimulatorView()
        {
            InitializeComponent();
            _isUiReady = true;
            Loaded += IncSimulatorView_Loaded;
            if (ResultLogRichTextBox != null)
            {
                ResultLogRichTextBox.SizeChanged += (_, _) => UpdateRichTextPageWidth();
                ResultLogRichTextBox.Loaded += (_, _) => UpdateRichTextPageWidth();
            }

            this.SizeChanged += (_, _) => UpdateRichTextPageWidth();
            UpdateCostPresetByInkType();
            RefreshStatus();
        }

        private bool _autoRunning = false;
        private CancellationTokenSource? _autoCts;

        private async void AutoRunButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_autoRunning)
            {
                _autoRunning = true;
                AutoRunButton.Content = "취소";
                AutoRunButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x33, 0x33));

                _autoCts = new CancellationTokenSource();
                var token = _autoCts.Token;

                if (!TryGetUnitCost(out decimal unitCost))
                {
                    MessageBox.Show("비용을 올바르게 입력해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    StopAutoRun();
                    return;
                }

                if (!int.TryParse(TargetInkTextBox.Text.Trim(), out int targetInk) || targetInk < 1)
                {
                    MessageBox.Show("목표 인크를 1 이상의 숫자로 입력해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    StopAutoRun();
                    return;
                }

                if (int.TryParse(StartInkTextBox?.Text?.Trim(), out int startInk) && startInk >= 0)
                    _currentInk = startInk;

                HideCumulativeLog();

                try
                {
                    bool isEta = IsEta;
                    const int batchSize = 10;
                    const int delayMs = 0;
                    int iter = 0;
                    while (!token.IsCancellationRequested && _currentInk < targetInk)
                    {
                        _totalAttempts++;
                        _totalCost += unitCost;
                        _attemptsSinceLastSuccess++;

                        double chance = GetSuccessChance(_currentInk, isEta);
                        AccumulateLuckStats(chance);
                        if (Random.Shared.NextDouble() < chance)
                        {
                            int inkBefore = _currentInk;
                            _successCount++;
                            _currentInk++;
                            decimal costForOneSuccess = unitCost * _attemptsSinceLastSuccess;
                            double theoreticalRate = chance * 100.0;
                            double actualRate = _totalAttempts > 0 ? (double)_successCount / _totalAttempts * 100.0 : 0.0;
                            var entry = new SuccessLogEntry(_attemptsSinceLastSuccess, inkBefore, _currentInk, costForOneSuccess, unitCost, theoreticalRate, actualRate);
                            AppendExpectedCostSuccessLog(entry);
                            _attemptsSinceLastSuccess = 0;
                        }

                        iter += batchSize;
                        if ((iter % (batchSize * 10)) == 0)
                            RefreshStatus();

                        try { await Task.Delay(delayMs, token); } catch (TaskCanceledException) { break; }
                    }
                }
                catch (TaskCanceledException) { }
                finally
                {
                    StopAutoRun();
                }
            }
            else
            {
                _autoCts?.Cancel();
            }
        }

        private void StopAutoRun()
        {
            _autoCts?.Dispose();
            _autoCts = null;
            _autoRunning = false;
            AutoRunButton.Content = "자동실행";
            AutoRunButton.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4B, 0x3A, 0x74));
            UpdateExpectedCumulativeLog();
            ShowCumulativeLog();
            RefreshStatus();
        }

        private void HideCumulativeLog()
        {
            if (ResultLogRichTextBox?.Document == null) return;
            if (_lastCumulativeParagraph != null)
                ResultLogRichTextBox.Document.Blocks.Remove(_lastCumulativeParagraph);
        }

        private void ShowCumulativeLog()
        {
            UpdateExpectedCumulativeLog();
        }

        private bool IsEta => EtaRadio?.IsChecked == true;

        private void InkType_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isUiReady) return;
            UpdateCostPresetByInkType();
            RefreshStatus();
        }

        private void IncSimulatorView_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isUiReady) return;
            RefreshStatus();
            Dispatcher.BeginInvoke(UpdateRichTextPageWidth, DispatcherPriority.Loaded);
        }

        private void DiscountCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isUiReady) return;
            RefreshStatus();
        }

        private void ElsoCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isUiReady) return;
            UpdateCostPresetByInkType();
            RefreshStatus();
        }

        private void CostPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string tag }) return;
            BaseCostTextBox.Text = tag;
            RefreshStatus();
        }

        private void UpdateCostPresetByInkType()
        {
            if (CostPresetGrid == null ||
                EclipsePresetButton == null ||
                SacredPresetButton == null ||
                EffectPresetButton == null ||
                BaseCostTextBox == null ||
                BaseCostUnitTextBlock == null)
            {
                return;
            }

            bool isElsoMode = ElsoCheckBox?.IsChecked == true;
            BaseCostUnitTextBlock.Text = isElsoMode ? "1회 비용 (엘소)" : "1회 비용 (만원)";

            if (IsEta)
            {
                CostPresetGrid.Columns = 1;
                EclipsePresetButton.Visibility = Visibility.Collapsed;
                EffectPresetButton.Visibility = Visibility.Collapsed;
                SacredPresetButton.Visibility = Visibility.Visible;
                SacredPresetButton.Content = "세크리드";
                SacredPresetButton.Tag = isElsoMode ? EtaSacredElsoCostMan : EtaSacredCostMan;
                BaseCostTextBox.Text = SacredPresetButton.Tag.ToString();
                return;
            }

            CostPresetGrid.Columns = 3;
            EclipsePresetButton.Visibility = Visibility.Visible;
            SacredPresetButton.Visibility = Visibility.Visible;
            EffectPresetButton.Visibility = Visibility.Visible;

            EclipsePresetButton.Content = "이클립스";
            EclipsePresetButton.Tag = isElsoMode ? VianuEclipseCostElso : VianuEclipseCostMan;
            SacredPresetButton.Content = "세크리드";
            SacredPresetButton.Tag = isElsoMode ? VianuSacredCostElso : VianuSacredCostMan;
            EffectPresetButton.Content = "효과";
            EffectPresetButton.Tag = isElsoMode ? VianuEffectCostElso : VianuEffectCostMan;
            BaseCostTextBox.Text = EffectPresetButton.Tag.ToString();
        }

        private void StartInkTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox tb) return;
            if (int.TryParse(tb.Text.Trim(), out int start) && start >= 0)
            {
                _currentInk = start;
                RefreshStatus();
            }
            else
            {
                tb.Text = _currentInk.ToString();
            }
        }

        private void RunBatch_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(StartInkTextBox?.Text?.Trim(), out int startInk) && startInk >= 0)
                _currentInk = startInk;

            if (!TryGetUnitCost(out decimal unitCost))
            {
                MessageBox.Show("비용을 올바르게 입력해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(ManualRunCountTextBox?.Text?.Trim(), out int count) || count <= 0)
            {
                MessageBox.Show("수동 인크립트 횟수를 1 이상의 숫자로 입력해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            HideCumulativeLog();
            bool isEta = IsEta;
            var successLogs = RunAttempts(count, unitCost, isEta);
            foreach (var entry in successLogs)
                AppendExpectedCostSuccessLog(entry);
            UpdateExpectedCumulativeLog();
            ShowCumulativeLog();
            RefreshStatus();
        }

        private async void RunToTarget_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(StartInkTextBox?.Text?.Trim(), out int startInk) && startInk >= 0)
                _currentInk = startInk;

            if (!TryGetUnitCost(out decimal unitCost))
            {
                MessageBox.Show("비용을 올바르게 입력해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(TargetInkTextBox.Text.Trim(), out int targetInk) || targetInk < 1)
            {
                MessageBox.Show("목표 인크를 1 이상의 숫자로 입력해주세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_currentInk >= targetInk)
            {
                MessageBox.Show("이미 목표 인크 이상입니다.", "안내", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            HideCumulativeLog();

            bool isEta = IsEta;
            const int delayMs = 25;
            while (_currentInk < targetInk)
            {
                _totalAttempts++;
                _totalCost += unitCost;
                _attemptsSinceLastSuccess++;

                double chance = GetSuccessChance(_currentInk, isEta);
                AccumulateLuckStats(chance);
                if (Random.Shared.NextDouble() < chance)
                {
                    int inkBefore = _currentInk;
                    _successCount++;
                    _currentInk++;
                    decimal costForOneSuccess = unitCost * _attemptsSinceLastSuccess;
                    double theoreticalRate = chance * 100.0;
                    double actualRate = _totalAttempts > 0 ? (double)_successCount / _totalAttempts * 100.0 : 0.0;
                    var entry = new SuccessLogEntry(_attemptsSinceLastSuccess, inkBefore, _currentInk, costForOneSuccess, unitCost, theoreticalRate, actualRate);
                    AppendExpectedCostSuccessLog(entry);
                    _attemptsSinceLastSuccess = 0;
                }

                RefreshStatus();
                await Task.Delay(delayMs);
            }

            UpdateExpectedCumulativeLog();
            RefreshStatus();
        }

        private void Reset_Click(object sender, RoutedEventArgs e)
        {
            _currentInk = 0;
            _totalAttempts = 0;
            _successCount = 0;
            _totalCost = 0m;
            _totalExpectedCost = 0m;
            _totalExpectedSuccesses = 0d;
            _totalSuccessVariance = 0d;
            _attemptsSinceLastSuccess = 0;
            ResultLogRichTextBox?.Document.Blocks.Clear();
            _lastCumulativeParagraph = null;
            RefreshStatus();
        }

        private List<SuccessLogEntry> RunAttempts(int count, decimal unitCost, bool isEta)
        {
            var successLogs = new List<SuccessLogEntry>();

            for (int i = 0; i < count; i++)
            {
                _totalAttempts++;
                _totalCost += unitCost;
                _attemptsSinceLastSuccess++;

                double chance = GetSuccessChance(_currentInk, isEta);
                AccumulateLuckStats(chance);
                if (Random.Shared.NextDouble() < chance)
                {
                    int inkBefore = _currentInk;
                    _successCount++;
                    _currentInk++;
                    decimal costForOneSuccess = unitCost * _attemptsSinceLastSuccess;
                    double theoreticalRate = chance * 100.0;
                    double actualRate = _totalAttempts > 0 ? (double)_successCount / _totalAttempts * 100.0 : 0.0;
                    successLogs.Add(new SuccessLogEntry(_attemptsSinceLastSuccess, inkBefore, _currentInk, costForOneSuccess, unitCost, theoreticalRate, actualRate));
                    _attemptsSinceLastSuccess = 0;
                }
            }

            return successLogs;
        }

        private (int CurrentInk, long Attempts, long Successes, long Destroys, decimal Cost, long AttemptsSinceLastSuccess, List<SuccessLogEntry> SuccessLogs) SimulateUntilTarget(int targetInk, decimal unitCost, bool isEta, long attemptsSinceLastSuccess, long baseTotalAttempts, long baseSuccessCount)
        {
            int currentInk = _currentInk;
            long attempts = 0;
            long successes = 0;
            long destroys = 0;
            decimal cost = 0m;
            long localAttemptsSinceSuccess = attemptsSinceLastSuccess;
            var successLogs = new List<SuccessLogEntry>();

            const long maxAttempts = 5_000_000;

            while (currentInk < targetInk && attempts < maxAttempts)
            {
                attempts++;
                cost += unitCost;
                localAttemptsSinceSuccess++;

                double chance = GetSuccessChance(currentInk, isEta);
                if (Random.Shared.NextDouble() < chance)
                {
                    int inkBefore = currentInk;
                    successes++;
                    currentInk++;
                    decimal costForOneSuccess = unitCost * localAttemptsSinceSuccess;
                    double theoreticalRate = chance * 100.0;
                    long totalAttemptsAtSuccess = baseTotalAttempts + attempts;
                    long totalSuccessAtSuccess = baseSuccessCount + successes;
                    double actualRate = totalAttemptsAtSuccess > 0 ? (double)totalSuccessAtSuccess / totalAttemptsAtSuccess * 100.0 : 0.0;
                    successLogs.Add(new SuccessLogEntry(localAttemptsSinceSuccess, inkBefore, currentInk, costForOneSuccess, unitCost, theoreticalRate, actualRate));
                    localAttemptsSinceSuccess = 0;
                }
            }

            return (currentInk, attempts, successes, destroys, cost, localAttemptsSinceSuccess, successLogs);
        }

        private double GetSuccessChance(int currentInk)
            => GetSuccessChance(currentInk, IsEta);

        private static double GetSuccessChance(int currentInk, bool isEta)
        {
            if (isEta)
                return 0.01;

            return Math.Max(0.0001, 0.0007 - (currentInk * 0.00005));
        }

        private bool TryGetUnitCost(out decimal unitCost)
        {
            unitCost = 0m;
            string raw = BaseCostTextBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            raw = raw.Replace("만원", string.Empty, StringComparison.OrdinalIgnoreCase)
                     .Replace("만", string.Empty, StringComparison.OrdinalIgnoreCase)
                     .Replace("엘소", string.Empty, StringComparison.OrdinalIgnoreCase)
                     .Trim();

            bool parsed = decimal.TryParse(raw, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal costInMan)
                          || decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out costInMan);

            if (!parsed || costInMan < 0m)
                return false;

            bool isElsoMode = ElsoCheckBox?.IsChecked == true;
            decimal cost = isElsoMode ? costInMan : costInMan * 10_000m;

            unitCost = DiscountCheckBox.IsChecked == true ? Math.Round(cost * 0.8m, 0, MidpointRounding.AwayFromZero) : cost;
            return true;
        }

        private void RefreshStatus()
        {
            double chance = GetSuccessChance(_currentInk);
            ChanceInfoText.Text = $"현재 성공 확률: {chance * 100:F3}% ({_currentInk} -> {_currentInk + 1})";

            if (TryGetUnitCost(out decimal unitCost))
            {
                bool isElsoMode = ElsoCheckBox?.IsChecked == true;
                CostInfoText.Text = isElsoMode ? $"적용 비용: {unitCost:N0} 엘소" : $"적용 비용: {unitCost / 10_000m:N0}만원";
            }
            else
                CostInfoText.Text = "적용 비용: 입력 필요";

            CurrentInkText.Text = $"+{_currentInk}";
            CurrentChanceText.Text = $"({chance * 100:F2}%)";
            if (StartInkTextBox != null && !StartInkTextBox.IsFocused)
                StartInkTextBox.Text = _currentInk.ToString();
            AttemptsAndSuccessText.Text = $"{_successCount:N0} / {_totalAttempts:N0}";
            DestroyAndCostText.Text = FormatKoreanCost(_totalCost);
            LuckInfoText.Text = BuildLuckText();
            LuckGraphText.Text = BuildLuckGraphText();
        }

        private void AccumulateLuckStats(double chance)
        {
            if (chance <= 0d || double.IsNaN(chance) || double.IsInfinity(chance))
            {
                return;
            }

            _totalExpectedSuccesses += chance;
            _totalSuccessVariance += chance * (1d - chance);
        }

        private string BuildLuckText()
        {
            if (!TryGetLuckStats(out double percentile, out _))
            {
                return "행운 지표: 계산 대기";
            }

            int rank = CalculateLuckRank(percentile);
            return $"행운 지표: 상위 {percentile:F2}%{Environment.NewLine}10000명 중 {rank:N0}번째로 운이 좋습니다.";
        }

        private string BuildLuckGraphText()
        {
            if (!TryGetLuckStats(out _, out double z))
            {
                return "정규분포 그래프: 계산 대기";
            }

            const int pointCount = 33;
            const string levels = "▁▂▃▄▅▆▇█";
            var graph = new char[pointCount];
            double minX = -3.0d;
            double maxX = 3.0d;

            for (int i = 0; i < pointCount; i++)
            {
                double x = minX + (maxX - minX) * i / (pointCount - 1);
                double y = Math.Exp(-0.5d * x * x);
                int level = (int)Math.Round((levels.Length - 1) * y, MidpointRounding.AwayFromZero);
                level = Math.Clamp(level, 0, levels.Length - 1);
                graph[i] = levels[level];
            }

            double clampedZ = Math.Clamp(z, minX, maxX);
            int markerIndex = (int)Math.Round((clampedZ - minX) / (maxX - minX) * (pointCount - 1), MidpointRounding.AwayFromZero);
            markerIndex = Math.Clamp(markerIndex, 0, pointCount - 1);
            graph[markerIndex] = '◆';

            return $"정규분포: {new string(graph)}";
        }

        private static int CalculateLuckRank(double percentile, int population = 10_000)
        {
            if (population <= 0)
                return 0;

            double normalized = Math.Clamp(percentile, 0d, 100d) / 100d;
            int rank = (int)Math.Round(normalized * population, MidpointRounding.AwayFromZero);
            return Math.Clamp(rank, 1, population);
        }

        private bool TryGetLuckStats(out double percentile, out double z)
        {
            percentile = 0d;
            z = 0d;
            if (_totalAttempts <= 0 || _totalSuccessVariance <= 0d)
            {
                return false;
            }

            z = (_successCount - _totalExpectedSuccesses) / Math.Sqrt(_totalSuccessVariance);
            percentile = Math.Clamp(StandardNormalCdf(z) * 100d, 0d, 100d);
            return true;
        }

        private void ResultLogRichTextBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not RichTextBox richTextBox) return;

            if (e.Delta < 0)
                richTextBox.LineDown();
            else
                richTextBox.LineUp();

            e.Handled = true;
        }

        private static double StandardNormalCdf(double z)
        {
            double absZ = Math.Abs(z);
            double t = 1.0d / (1.0d + 0.2316419d * absZ);
            double d = 0.3989422804014327d * Math.Exp(-0.5d * absZ * absZ);
            double poly = ((((1.330274429d * t - 1.821255978d) * t + 1.781477937d) * t - 0.356563782d) * t + 0.319381530d) * t;
            double cdf = 1.0d - d * poly;
            return z >= 0.0d ? cdf : 1.0d - cdf;
        }

        private void AppendExpectedCostSuccessLog(SuccessLogEntry entry)
        {
            if (ResultLogRichTextBox == null) return;

            var successParagraph = new Paragraph()
            {
                Margin = new Thickness(0, 0, 0, 6),
                FontWeight = FontWeights.SemiBold
            };

            string prefix = $"• [ {entry.AttemptsForSuccess:N0}번째 | {entry.InkBefore}→{entry.InkAfter} 인크 | 비용 {FormatKoreanCost(entry.Cost)}";
            successParagraph.Inlines.Add(new Run(prefix));

            if (TryCalculateExpectedCost(entry, out decimal expectedCost))
            {
                decimal expectedCostDiff = expectedCost - entry.Cost;
                _totalExpectedCost += expectedCost;

                successParagraph.Inlines.Add(new Run(" | 기대값 "));
                successParagraph.Inlines.Add(new Run(FormatSignedKoreanCost(expectedCostDiff))
                {
                    Foreground = expectedCostDiff >= 0m ? Brushes.LimeGreen : Brushes.IndianRed
                });
            }
            else
            {
                successParagraph.Inlines.Add(new Run(" | 기대값 N/A"));
            }

            successParagraph.Inlines.Add(new Run(" ]"));

            ResultLogRichTextBox.Document.Blocks.Add(successParagraph);
            ResultLogRichTextBox.ScrollToEnd();
        }

        private void UpdateExpectedCumulativeLog()
        {
            if (ResultLogRichTextBox == null) return;

            if (_lastCumulativeParagraph != null)
                ResultLogRichTextBox.Document.Blocks.Remove(_lastCumulativeParagraph);

            decimal displayExpectedCost = _totalExpectedCost;
            if (_attemptsSinceLastSuccess > 0 &&
                TryGetUnitCost(out decimal currentUnitCost) &&
                TryGetExpectedCostForInk(_currentInk, currentUnitCost, IsEta, out decimal currentStageExpectedCost))
            {
                displayExpectedCost += currentStageExpectedCost;
            }

            decimal totalExpectedCostDiff = displayExpectedCost - _totalCost;

            _lastCumulativeParagraph = new Paragraph
            {
                Margin = new Thickness(0, 2, 0, 8),
                Foreground = new SolidColorBrush(Color.FromRgb(0x8B, 0x94, 0x9E))
            };

            _lastCumulativeParagraph.Inlines.Add(new Run($"▼ 누적 합산 ({_totalAttempts:N0}회 시도) ")
            {
                Foreground = Brushes.Gold,
                FontWeight = FontWeights.SemiBold
            });
            _lastCumulativeParagraph.Inlines.Add(new LineBreak());

            _lastCumulativeParagraph.Inlines.Add(new Run("누적 비용: "));
            _lastCumulativeParagraph.Inlines.Add(new Run(FormatKoreanCost(_totalCost))
            {
                Foreground = Brushes.White
            });
            _lastCumulativeParagraph.Inlines.Add(new Run("  |  "));

            _lastCumulativeParagraph.Inlines.Add(new Run("누적 기대 비용 합: "));
            _lastCumulativeParagraph.Inlines.Add(new Run(FormatKoreanCost(displayExpectedCost))
            {
                Foreground = Brushes.White
            });
            _lastCumulativeParagraph.Inlines.Add(new Run("\n"));

            _lastCumulativeParagraph.Inlines.Add(new Run("기대값 차이: "));
            _lastCumulativeParagraph.Inlines.Add(new Run(FormatSignedKoreanCost(totalExpectedCostDiff))
            {
                Foreground = totalExpectedCostDiff >= 0m ? Brushes.LimeGreen : Brushes.IndianRed
            });

            ResultLogRichTextBox.Document.Blocks.Add(_lastCumulativeParagraph);
            UpdateRichTextPageWidth();
        }

        private void UpdateRichTextPageWidth()
        {
            if (ResultLogRichTextBox?.Document == null) return;

            Dispatcher.BeginInvoke(() =>
            {
                double width = ResultLogRichTextBox.ActualWidth - 24;
                if (double.IsNaN(width) || width < 0) width = 0;
                ResultLogRichTextBox.Document.PageWidth = Math.Max(0.0, width);
            }, DispatcherPriority.Loaded);
        }

        private static string FormatKoreanCost(decimal value)
        {
            long amount = (long)Math.Floor(value);
            long eok = amount / 100_000_000;
            long man = (amount % 100_000_000) / 10_000;
            return $"{eok:N0}억 {man:N0}만";
        }
        private static bool TryCalculateExpectedCost(SuccessLogEntry entry, out decimal expectedCost)
        {
            expectedCost = 0m;
            if (entry.UnitCost <= 0m ||
                entry.TheoreticalRate <= 0 ||
                double.IsNaN(entry.TheoreticalRate) ||
                double.IsInfinity(entry.TheoreticalRate))
            {
                return false;
            }

            decimal expectedAttempts = 100m / (decimal)entry.TheoreticalRate;
            expectedCost = entry.UnitCost * expectedAttempts;
            return true;
        }

        private static bool TryGetExpectedCostForInk(int currentInk, decimal unitCost, bool isEta, out decimal expectedCost)
        {
            expectedCost = 0m;
            if (unitCost <= 0m)
            {
                return false;
            }

            double chance = GetSuccessChance(currentInk, isEta);
            if (chance <= 0 || double.IsNaN(chance) || double.IsInfinity(chance))
            {
                return false;
            }

            expectedCost = unitCost / (decimal)chance;
            return true;
        }

        private static string FormatSignedKoreanCost(decimal value)
        {
            string sign = value > 0m ? "+" : value < 0m ? "-" : string.Empty;
            return $"{sign}{FormatKoreanCost(Math.Abs(value))}";
        }
    }
}

