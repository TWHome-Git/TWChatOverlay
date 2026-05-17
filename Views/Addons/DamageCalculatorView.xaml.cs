using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TWChatOverlay.Models;
using TWChatOverlay.Services;

namespace TWChatOverlay.Views.Addons
{
    /// <summary>
    /// 계수 계산기 스냅샷을 기준으로 대미지 계산 옵션을 구성하는 뷰입니다.
    /// </summary>
    public partial class DamageCalculatorView : UserControl
    {
        private sealed class DamageCalculationValues
        {
            public double Group3TraitAttackDamageValue { get; set; }
            public DamageReferenceData.CharacterModifierEntry? SelectedModifier { get; set; }
            public double TraitEnemyTakenDamagePercent { get; set; }
            public string SelectedAnisVariant { get; set; } = "마법";
            public double EtaAwakeningDamageIncrease { get; set; }
            public double CurrentStatCoefficient { get; set; }
            public double CurrentEquipmentCoefficient { get; set; }
            public double CurrentDexCorrection { get; set; }
            public double CurrentFinalCoefficient { get; set; }
            public double SkillMultiplier { get; set; }
            public double SkillCriticalMultiplier { get; set; }
            public double HitCount { get; set; } = 1;
        }

        private readonly CoefficientCalculatorView? _coefficientView;
        private bool _subscribed;

        private readonly Group1State _group1 = new();
        private readonly Group2State _group2 = new();
        private readonly Group4State _group4 = new();
        private readonly Group5State _group5 = new();
        private readonly Group11State _group11 = new();
        private readonly MonsterState _monster = new();
        private readonly DamageCalculationValues _calc = new();
        private bool _isRestoringDamageState;
        private readonly CoefficientSaveData _saveData = CoefficientDataService.Load();
        private DamageCalculatorSaveState _damageState = new();
        private string _currentDamageStateKey = string.Empty;
        private static bool _appExitHooked;

        private TextBox? SpecialDamageReductionTextBoxControl => FindName("SpecialDamageReductionTextBox") as TextBox;

        public DamageCalculatorView()
        {
            InitializeComponent();
            _isRestoringDamageState = true;
            try
            {
                _currentDamageStateKey = BuildDamageStateKey(_saveData.LastSelectedCharacterName, _saveData.LastSelectedCalculatorType);
                _damageState = LoadDamageStateForKey(_currentDamageStateKey);
                AnaisMagicRadioButton.IsChecked = true;
                WeakPointToggle.IsChecked = false;
            _calc.SkillMultiplier = 0;
            _calc.SkillCriticalMultiplier = 0;
            _calc.HitCount = 1;
                LoadDamageCalculatorState();
                ApplySnapshot(CoefficientDamageBaseSnapshot.Empty);
                RefreshGroupSummaryTexts();
            }
            finally
            {
                _isRestoringDamageState = false;
            }

            Unloaded += (_, _) => SaveDamageCalculatorState();
            HookApplicationExitSave();
        }

        public DamageCalculatorView(CoefficientCalculatorView coefficientView)
            : this()
        {
            _coefficientView = coefficientView;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_coefficientView == null || _subscribed)
                return;

            _coefficientView.SnapshotChanged += CoefficientView_SnapshotChanged;
            _subscribed = true;
            ApplySnapshot(_coefficientView.CurrentSnapshot);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            SaveDamageCalculatorState();

            if (_coefficientView == null || !_subscribed)
                return;

            _coefficientView.SnapshotChanged -= CoefficientView_SnapshotChanged;
            _subscribed = false;
        }

        private void HookApplicationExitSave()
        {
            if (_appExitHooked || Application.Current == null)
                return;

            Application.Current.Exit += (_, _) =>
            {
                try
                {
                    SaveDamageCalculatorState();
                }
                catch
                {
                }
            };

            _appExitHooked = true;
        }

        private void CoefficientView_SnapshotChanged(CoefficientDamageBaseSnapshot snapshot)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ApplySnapshot(snapshot));
                return;
            }

            ApplySnapshot(snapshot);
        }

        private void ApplySnapshot(CoefficientDamageBaseSnapshot snapshot)
        {
            bool hasData = snapshot.HasData;
            NoDataPanel.Visibility = hasData ? Visibility.Collapsed : Visibility.Visible;
            DataPanel.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;

            if (!hasData)
                return;

            string snapshotKey = BuildDamageStateKey(snapshot.CharacterName, snapshot.CalculatorTypeName);
            if (!string.IsNullOrWhiteSpace(snapshotKey) &&
                !string.Equals(snapshotKey, _currentDamageStateKey, StringComparison.Ordinal))
            {
                SaveDamageCalculatorState();
                _currentDamageStateKey = snapshotKey;
                _damageState = LoadDamageStateForKey(snapshotKey);
                LoadDamageCalculatorState();
            }

            CharacterNameText.Text = snapshot.CharacterName;
            CalculatorTypeText.Text = snapshot.CalculatorTypeName;
            TotalCoefficientText.Text = snapshot.TotalCoefficient.ToString("F2", CultureInfo.CurrentCulture);
            TotalPrimaryText.Text = snapshot.TotalPrimarySum.ToString("F0", CultureInfo.CurrentCulture);
            PrimaryEnchantText.Text = snapshot.PrimaryEnchantSum.ToString("F0", CultureInfo.CurrentCulture);
            SecondarySummaryText.Text =
                $"{snapshot.SecondarySum.ToString("F0", CultureInfo.CurrentCulture)} / {snapshot.SecondaryEnchantSum.ToString("F0", CultureInfo.CurrentCulture)}";

            UpdateCoefficientBreakdown(snapshot);
            ApplyCharacterModifier(snapshot.CharacterName, snapshot.CalculatorTypeName);
            UpdateWeakPointUi();
            UpdateCategorySummaryText();
            UpdateDamageRangeText();
        }

        private void UpdateCoefficientBreakdown(CoefficientDamageBaseSnapshot snapshot)
        {
            SyncBasicOptionValues();

            double statCoefficient = snapshot.StatCoefficient;
            double equipmentCoefficient = Math.Max(0, snapshot.TotalCoefficient - statCoefficient);
            double dexValue = snapshot.DexValue;
            double correction = Math.Floor(statCoefficient + dexValue * 3.0) / 18.0;

            double bonus = Math.Floor(equipmentCoefficient / 25.0 * (0.05 + 0.03 * 5)) * 25.0;
            double finalCoefficient = Math.Floor(statCoefficient + equipmentCoefficient) + bonus;

            _calc.CurrentStatCoefficient = statCoefficient;
            _calc.CurrentEquipmentCoefficient = equipmentCoefficient;
            _calc.CurrentDexCorrection = correction;
            _calc.CurrentFinalCoefficient = finalCoefficient;

            if (StatCoefficientText != null)
                StatCoefficientText.Text = Math.Round(statCoefficient).ToString("F0", CultureInfo.CurrentCulture);
            if (EquipmentCoefficientText != null)
                EquipmentCoefficientText.Text = Math.Round(equipmentCoefficient).ToString("F0", CultureInfo.CurrentCulture);
            if (DexCorrectionText != null)
                DexCorrectionText.Text = Math.Round(correction).ToString("F0", CultureInfo.CurrentCulture);
            if (FinalCoefficientBreakdownText != null)
                FinalCoefficientBreakdownText.Text = Math.Round(finalCoefficient).ToString("F0", CultureInfo.CurrentCulture);

        }

        private void UpdateCategorySummaryText()
        {
            if (CategorySummaryText == null)
                return;

            var sb = new StringBuilder();
            sb.AppendLine($"1투구 {GetHelmetAbilityText()}");
            sb.AppendLine($"2치피 {GetCriticalDamageFactor()}");
            sb.AppendLine($"3콤보 {GetComboBonusFactor()}");
            sb.AppendLine($"4공피 {GetAttackDamageFactor()}");
            sb.AppendLine($"5특감 {GetSpecialDamageReductionFactor()}");
            sb.AppendLine($"6최종 {GetFinalDamageFactor()}");
            sb.AppendLine($"7시에나 {GetSienaFactor()}");
            sb.AppendLine($"8계열 {GetSeriesAttackFactor()}");
            sb.AppendLine($"9받피 {GetDamageAmplificationFactor()}");
            sb.AppendLine($"10증폭 {GetWeaponAmplificationFactor()}");
            sb.AppendLine($"11에타 {GetEtaLevelFactor()}");
            sb.AppendLine($"12속성 {GetMonsterAttributeFactorText()}");
            sb.AppendLine($"13피감 {GetMonsterDamageReductionFactorText()}");
            sb.AppendLine($"14방어 {GetMonsterDefenseText()}");
            sb.AppendLine($"15고정피감 {GetMonsterFixedDamageReductionText()}");
            sb.AppendLine($"16스킬 {GetSkillMultiplierText()}");
            sb.AppendLine($"17크리 {GetSkillCriticalMultiplierText()}");

            CategorySummaryText.Text = sb.ToString().TrimEnd();
            UpdateDamageRangeText();
        }

        private string GetHelmetAbilityText() => HelmetAbilityToggle?.IsChecked == true ? "ON 10" : "OFF 0";

        private string GetCriticalDamageFactor()
        {
            double weakPoint = WeakPointToggle?.IsChecked == true ? 40 : 0;
            double judgement = GetComboIndex(JudgementComboBox) * 0.75;
            double etaCrit = GetEtaLinkCriticalPercent(GetComboIndex(EtaLinkCriticalComboBox));
            double total = weakPoint + judgement + etaCrit;
            return $"1 + {total:0.#}% = {1 + total / 100:0.##}";
        }

        private string GetComboBonusFactor() => ComboBonusToggle?.IsChecked == true ? "1.15" : "1.0";

        private string GetAttackDamageFactor()
        {
            double total = GetGroup1TotalPercent() + GetGroup2TotalPercent() + GetGroup3TotalPercent() + GetGroup4TotalPercent();
            return $"1 + {total:0.#}% = {1 + total / 100:0.##}";
        }

        private string GetSpecialDamageReductionFactor()
        {
            double reduction = GetTextBoxValue(SpecialDamageReductionTextBoxControl);
            reduction = Math.Clamp(reduction, 0, 50);
            return $"1 - {reduction:0.#}% = {1 - reduction / 100:0.##}";
        }

        private static double GetEtaLinkCriticalPercent(int level)
        {
            level = Math.Clamp(level, 0, 20);
            return level * 1.5;
        }

        private static double GetEtaLinkFinalPercent(int level)
        {
            level = Math.Clamp(level, 0, 5);
            return level * 4.0;
        }

        private string GetGroup1DamageSummary()
        {
            double value = 0;
            if (_group1.Snowman) value += 20;
            if (_group1.Illumi) value += 10;
            if (_group1.IsabelDamage) value += 10;
            if (_group1.IsabelSpecial) value += 10;
            if (_group1.IsabelBattle) value += 10;
            value += ReadTextValue(_group1.EtcValue);
            return $"{value:0.#}%";
        }

        private string GetGroup2DamageSummary()
        {
            double value = 0;
            if (_group2.Gaegakbi) value += 5;
            if (_group2.ClubTypeP) value += 5;
            if (_group2.ExplorePoint) value += 5;
            if (_group2.TwPower) value += 5;
            if (_group2.Ham) value += 10;
            if (_group2.Event) value += 10;
            value += ReadTextValue(_group2.EtcValue);
            return $"{value:0.#}%";
        }

        private string GetGroup3DamageSummary()
        {
            return $"{_calc.Group3TraitAttackDamageValue:0.#}%";
        }

        private string GetGroup4DamageSummary()
        {
            double value = 0;
            if (_group4.TitleDamage) value += 20;
            value += _group4.WeaponAbilityIndex switch { 0 => 0, 1 => 9, 2 => 10, 3 => 11, _ => 0 };
            if (_group4.Fever) value += 10;
            value += _group4.WristAbilityIndex switch { 0 => 0, 1 => 9, 2 => 10, 3 => 11, _ => 0 };
            value += _group4.HandAbilityIndex switch { 0 => 0, 1 => 7, 2 => 8, 3 => 9, _ => 0 };
            value += _group4.LunariaAbilityIndex switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5, 6 => 6, 7 => 7, 8 => 8, 9 => 9, 10 => 10, _ => 0 };
            value += _group4.DeepRuneIndex switch { 0 => 0, 1 => 3, 2 => 6, 3 => 9, _ => 0 };
            value += ReadTextValue(_group4.EtcValue);
            return $"{value:0.#}%";
        }

        private string GetGroup4CombinedSummary()
        {
            double total = 0;
            total += ReadTextValue(GetGroup1DamageSummary().TrimEnd('%'));
            total += ReadTextValue(GetGroup2DamageSummary().TrimEnd('%'));
            total += ReadTextValue(GetGroup3DamageSummary().TrimEnd('%'));
            total += ReadTextValue(GetGroup4DamageSummary().TrimEnd('%'));
            return $"{total:0.#}%";
        }

        private string GetFinalDamageFactor()
        {
            double club = ClubFinalDamageToggle?.IsChecked == true ? 5 : 0;
            double core = GetTextBoxValue(CoreSetTextBox);
            double etaFinal = GetEtaLinkFinalPercent(GetComboIndex(EtaLinkFinalDamageComboBox));
            double total = club + core + etaFinal;
            return $"1 + {total:0.#}% = {1 + total / 100:0.##}";
        }

        private string GetSienaFactor()
        {
            double value = GetTextBoxValue(SienaTextBox);
            return $"1 + {value:0.#}% = {1 + value / 100:0.##}";
        }

        private string GetSeriesAttackFactor()
        {
            double value = ReadTextValue(GetSeriesAttackDamageSummary().TrimEnd('%'));
            return $"1 + {value:0.#}% = {1 + value / 100:0.##}";
        }

        private string GetDamageAmplificationFactor()
        {
            double value = _calc.TraitEnemyTakenDamagePercent;
            return $"1 + {value:0.##}% = {1 + value / 100:0.##}";
        }

        private string GetWeaponAmplificationFactor() => EnemyTakenDamageWeaponToggle?.IsChecked == true ? "1.1" : "1.0";

        private string GetEtaLevelFactor() => $"{GetEtaLevelValue():0.#}레벨 / {_calc.EtaAwakeningDamageIncrease:0.##}";

        private string GetMonsterAttributeFactorText()
        {
            var entry = GetSelectedMonsterEntry(_monster.SelectedIndex);
            if (entry == null)
                return "1.0";

            double currentAttribute = GetTextBoxValue(ElementValueTextBox);
            return $"{GetMonsterAttributeFactor(currentAttribute, entry.Value.AttributeValue):0.##}";
        }

        private string GetMonsterDamageReductionFactorText()
        {
            var entry = GetSelectedMonsterEntry(_monster.SelectedIndex);
            if (entry == null)
                return "1.0";

            return $"{GetMonsterDamageReductionFactor(entry.Value.DamageReductionRate):0.##}";
        }

        private string GetMonsterDefenseText()
        {
            var entry = GetSelectedMonsterEntry(_monster.SelectedIndex);
            if (entry == null)
                return "0";

            double defense = entry.Value.StatDefense + entry.Value.FixedDefense;
            return $"{defense:0.##}";
        }

        private string GetMonsterFixedDamageReductionText()
        {
            var entry = GetSelectedMonsterEntry(_monster.SelectedIndex);
            if (entry == null)
                return "0";

            return $"{entry.Value.FixedDamageReduction:0.##}";
        }

        private string GetSkillMultiplierText() => $"{GetTextBoxValue(SkillMultiplierTextBox) / 100.0:0.#}";

        private string GetSkillCriticalMultiplierText() => $"{GetTextBoxValue(CriticalMultiplierTextBox) / 100.0:0.#}";

        private void UpdateDamageRangeText()
        {
            if (IntermediateDamageText == null || MinimumDamageText == null || MaximumDamageText == null)
                return;

            var entry = GetSelectedMonsterEntry(_monster.SelectedIndex);
            if (entry == null)
            {
                IntermediateDamageText.Text = "-";
                MinimumDamageText.Text = "-";
                MaximumDamageText.Text = "-";
                return;
            }

            double monsterDefense = entry.Value.StatDefense + entry.Value.FixedDefense;
            double monsterFixedDamageReduction = entry.Value.FixedDamageReduction;
            double monsterAttributeFactor = GetMonsterAttributeFactor(GetTextBoxValue(ElementValueTextBox), entry.Value.AttributeValue);
            double monsterDamageReductionFactor = GetMonsterDamageReductionFactor(entry.Value.DamageReductionRate);

            double baseCoefficientMin = _calc.CurrentFinalCoefficient + 1 - monsterDefense;
            double baseCoefficientMax = _calc.CurrentFinalCoefficient + 1 + Math.Floor(_calc.CurrentDexCorrection) - monsterDefense;

            double skillFactor = GetTextBoxValue(SkillMultiplierTextBox) / 100.0;
            double helmetFactor = HelmetAbilityToggle?.IsChecked == true ? 0.1 : 0.0;
            double critFactor = GetGlobalCriticalMultiplierValue();
            double comboFactor = ComboBonusToggle?.IsChecked == true ? 1.15 : 1.0;
            double finalFactor = GetFinalDamageFactorValue();
            double specialFactor = GetSpecialDamageReductionFactorValue();
            double sienaFactor = GetSienaFactorValue();
            double etaFactor = Math.Max(0, _calc.EtaAwakeningDamageIncrease);
            double seriesAttackFactor = GetSeriesAttackFactorValue();
            double damageAmpFactor = GetDamageAmplificationFactorValue();
            double weaponAmpFactor = GetWeaponAmplificationFactorValue();
            double attackDamageFactor = GetAttackDamageFactorValue();

            double innerMin = Math.Floor(baseCoefficientMin * (skillFactor + helmetFactor) * (GetTextBoxValue(CriticalMultiplierTextBox) / 100.0) * critFactor * comboFactor * monsterAttributeFactor);
            double innerMax = Math.Floor(baseCoefficientMax * (skillFactor + helmetFactor) * (GetTextBoxValue(CriticalMultiplierTextBox) / 100.0) * critFactor * comboFactor * monsterAttributeFactor);

            double middleMin = Math.Floor((innerMin * finalFactor * monsterDamageReductionFactor - monsterFixedDamageReduction) * specialFactor * sienaFactor * etaFactor * seriesAttackFactor * damageAmpFactor * weaponAmpFactor);
            double middleMax = Math.Floor((innerMax * finalFactor * monsterDamageReductionFactor - monsterFixedDamageReduction) * specialFactor * sienaFactor * etaFactor * seriesAttackFactor * damageAmpFactor * weaponAmpFactor);

            double minimumDamage = Math.Floor(middleMin * attackDamageFactor);
            double maximumDamage = Math.Floor(middleMax * attackDamageFactor);

            IntermediateDamageText.Text = $"1차 INT: {innerMin:N0} / {innerMax:N0}\n2차 INT: {middleMin:N0} / {middleMax:N0}";
            MinimumDamageText.Text = $"{minimumDamage:N0}";
            MaximumDamageText.Text = $"{maximumDamage:N0}";
        }

        private string BuildDamageFormula(bool useMaximum)
        {
            return "-";
        }

        private double GetGlobalCriticalMultiplierValue()
        {
            double weakPoint = WeakPointToggle?.IsChecked == true ? 40 : 0;
            double judgement = GetComboIndex(JudgementComboBox) * 0.75;
            double etaCrit = GetEtaLinkCriticalPercent(GetComboIndex(EtaLinkCriticalComboBox));
            return 1 + (weakPoint + judgement + etaCrit) / 100.0;
        }

        private double GetFinalDamageFactorValue()
        {
            double club = ClubFinalDamageToggle?.IsChecked == true ? 5 : 0;
            double core = GetTextBoxValue(CoreSetTextBox);
            double etaFinal = GetEtaLinkFinalPercent(GetComboIndex(EtaLinkFinalDamageComboBox));
            return 1 + (club + core + etaFinal) / 100.0;
        }

        private double GetSpecialDamageReductionFactorValue()
        {
            double reduction = GetTextBoxValue(SpecialDamageReductionTextBoxControl);
            reduction = Math.Clamp(reduction, 0, 50);
            return 1 - (reduction / 100.0);
        }

        private double GetSienaFactorValue()
        {
            double value = GetTextBoxValue(SienaTextBox);
            return 1 + value / 100.0;
        }

        private double GetSeriesAttackFactorValue()
        {
            double value = ReadTextValue(GetSeriesAttackDamageSummary().TrimEnd('%'));
            return 1 + value / 100.0;
        }

        private double GetDamageAmplificationFactorValue()
        {
            double value = _calc.TraitEnemyTakenDamagePercent;
            return 1 + value / 100.0;
        }

        private double GetWeaponAmplificationFactorValue()
            => EnemyTakenDamageWeaponToggle?.IsChecked == true ? 1.1 : 1.0;

        private double GetAttackDamageFactorValue()
        {
            double total = GetGroup1TotalPercent() + GetGroup2TotalPercent() + GetGroup3TotalPercent() + GetGroup4TotalPercent();
            return 1 + total / 100.0;
        }

        private double GetGroup1TotalPercent()
        {
            double value = 0;
            if (_group1.Snowman) value += 20;
            if (_group1.Illumi) value += 10;
            if (_group1.IsabelDamage) value += 10;
            if (_group1.IsabelSpecial) value += 10;
            if (_group1.IsabelBattle) value += 10;
            return Math.Min(value + ReadTextValue(_group1.EtcValue), 50);
        }

        private double GetGroup2TotalPercent()
        {
            double value = 0;
            if (_group2.Gaegakbi) value += 5;
            if (_group2.ClubTypeP) value += 5;
            if (_group2.ExplorePoint) value += 5;
            if (_group2.TwPower) value += 5;
            if (_group2.Ham) value += 10;
            if (_group2.Event) value += 10;
            return Math.Min(value + ReadTextValue(_group2.EtcValue), 30);
        }

        private double GetGroup3TotalPercent()
        {
            return Math.Min(_calc.Group3TraitAttackDamageValue, 65);
        }

        private double GetGroup4TotalPercent()
        {
            double value = 0;
            if (_group4.TitleDamage) value += 20;
            value += _group4.WeaponAbilityIndex switch { 0 => 0, 1 => 9, 2 => 10, 3 => 11, _ => 0 };
            if (_group4.Fever) value += 10;
            value += _group4.WristAbilityIndex switch { 0 => 0, 1 => 9, 2 => 10, 3 => 11, _ => 0 };
            value += _group4.HandAbilityIndex switch { 0 => 0, 1 => 7, 2 => 8, 3 => 9, _ => 0 };
            value += _group4.LunariaAbilityIndex switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5, 6 => 6, 7 => 7, 8 => 8, 9 => 9, 10 => 10, _ => 0 };
            value += _group4.DeepRuneIndex switch { 0 => 0, 1 => 3, 2 => 6, 3 => 9, _ => 0 };
            return Math.Min(value + ReadTextValue(_group4.EtcValue), 80);
        }

        private string GetSeriesAttackDamageSummary()
        {
            double value = _group5.ArtifactIndex switch { 0 => 15, 1 => 20, 2 => 30, 3 => 35, _ => 15 };
            value += _group5.WristExtraIndex switch { 0 => 25, 1 => 26, 2 => 27, 3 => 28, _ => 25 };
            value += _group5.LunariaExtraIndex;
            return $"{value:0.#}%";
        }

        private string GetFinalDamageSummary()
        {
            double club = ClubFinalDamageToggle?.IsChecked == true ? 5 : 0;
            double core = GetTextBoxValue(CoreSetTextBox);
            double etaFinal = GetEtaLinkFinalPercent(GetComboIndex(EtaLinkFinalDamageComboBox));
            return $"{club + core + etaFinal:0.#}%";
        }

        private string GetCurrentTraitDisplayName()
        {
            if (_calc.SelectedModifier == null)
                return "특성 없음";

            return _calc.SelectedModifier.Value.Name;
        }

        private enum AttackGroupKind
        {
            Group1,
            Group2,
            Group3,
            Group4
        }

        private static string BoolPercent(bool value, double percent) => value ? $"{percent:0.#}%" : "0%";

        private static int GetComboIndex(ComboBox? comboBox) => comboBox?.SelectedIndex ?? 0;
        private static double GetTextBoxValue(TextBox? textBox) => ReadTextValue(textBox?.Text);
        private static double ReadTextValue(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            return double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double value)
                ? value
                : 0;
        }

        private double GetEtaLevelValue()
        {
            if (!int.TryParse(EtaLevelTextBox?.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int etaLevel))
                etaLevel = 1;

            etaLevel = Math.Clamp(etaLevel, 1, 100);
            return etaLevel;
        }

        private static DamageReferenceData.MonsterEntry? GetSelectedMonsterEntry(int index)
        {
            var entries = DamageReferenceData.MonsterEntries;
            if (index < 0 || index >= entries.Length)
                return null;

            return entries[index];
        }

        private static TextBlock AddMonsterDetailRow(Grid grid, int row, string label)
        {
            while (grid.RowDefinitions.Count <= row)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                Text = label,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8B, 0x94, 0x9E)),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            };
            Grid.SetRow(labelBlock, row);
            Grid.SetColumn(labelBlock, 0);

            var valueBlock = new TextBlock
            {
                Text = "0",
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(valueBlock, row);
            Grid.SetColumn(valueBlock, 1);

            grid.Children.Add(labelBlock);
            grid.Children.Add(valueBlock);
            return valueBlock;
        }

        private void UpdateGroup2State()
        {
            _group2.Gaegakbi = _group2.Gaegakbi;
            _group2.ClubTypeP = _group2.ClubTypeP;
            _group2.ExplorePoint = _group2.ExplorePoint;
            _group2.TwPower = _group2.TwPower;
            _group2.Ham = _group2.Ham;
            _group2.Event = _group2.Event;
        }

        private void ApplyCharacterModifier(string characterName, string calculatorTypeName)
        {
            if (string.IsNullOrWhiteSpace(characterName))
            {
                _calc.SelectedModifier = null;
                _calc.Group3TraitAttackDamageValue = 0;
                AnaisVariantPanel.Visibility = Visibility.Collapsed;
                SelectedModifierText.Text = string.Empty;
                TraitEnemyTakenDamageValueText.Text = "0";
                TraitStatReductionValueText.Text = "0";
                return;
            }

            bool isAnais = string.Equals(characterName, "아나이스", StringComparison.Ordinal);
            bool isMagicAttack = calculatorTypeName.Contains("마법공격", StringComparison.Ordinal);
            bool isMagicDefense = calculatorTypeName.Contains("마법방어", StringComparison.Ordinal) || calculatorTypeName.Contains("신성", StringComparison.Ordinal);

            string resolvedName;
            if (isAnais)
            {
                AnaisVariantPanel.Visibility = isMagicAttack ? Visibility.Visible : Visibility.Collapsed;
                if (isMagicAttack)
                {
                    string variant = GetSelectedAnaisVariant();
                    if (variant != "파괴")
                        variant = "마법";

                    _calc.SelectedAnisVariant = variant;
                    SetAnaisRadioState(_calc.SelectedAnisVariant);
                }

                resolvedName = isMagicDefense
                    ? "아나이스 비호"
                    : _calc.SelectedAnisVariant == "파괴"
                        ? "아나이스 파괴"
                        : "아나이스 마법";
                if (!isMagicAttack)
                {
                    _calc.SelectedAnisVariant = "비호";
                    SetAnaisRadioState(null);
                }
            }
            else
            {
                AnaisVariantPanel.Visibility = Visibility.Collapsed;
                resolvedName = characterName;
            }

            _calc.SelectedModifier = ResolveModifier(resolvedName, isAnais, isMagicDefense);
            if (_calc.SelectedModifier == null && isAnais)
            {
                _calc.SelectedModifier = ResolveModifier(isMagicDefense ? "아나이스 비호" : "아나이스 마법", false, false);
            }

            if (_calc.SelectedModifier == null)
            {
                _calc.Group3TraitAttackDamageValue = 0;
                SelectedModifierText.Text = "특성 값 없음";
                TraitEnemyTakenDamageValueText.Text = "0";
                TraitStatReductionValueText.Text = "0";
                _calc.TraitEnemyTakenDamagePercent = 0;
                return;
            }

            SelectedModifierText.Text = _calc.SelectedModifier.Value.Name;
            _calc.TraitEnemyTakenDamagePercent = _calc.SelectedModifier.Value.DamageAmplification;
            TraitEnemyTakenDamageValueText.Text = _calc.TraitEnemyTakenDamagePercent.ToString("0.##", CultureInfo.CurrentCulture);
            TraitStatReductionValueText.Text = _calc.SelectedModifier.Value.SkillReduction.ToString("0.##", CultureInfo.CurrentCulture);
            _calc.Group3TraitAttackDamageValue = _calc.SelectedModifier.Value.AttackPower;
        }

        private static DamageReferenceData.CharacterModifierEntry? ResolveModifier(string modifierName, bool isAnais, bool isMagicDefense)
        {
            if (string.IsNullOrWhiteSpace(modifierName))
                return null;

            var modifier = DamageReferenceData.CharacterModifierEntries.FirstOrDefault(entry =>
                string.Equals(entry.Name, modifierName, StringComparison.Ordinal));
            return string.IsNullOrWhiteSpace(modifier.Name) ? null : modifier;
        }

        private void AnaisVariantRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton radioButton)
                return;

            string variant = radioButton.Content?.ToString() ?? string.Empty;
            if (!string.Equals(variant, "마법", StringComparison.Ordinal) &&
                !string.Equals(variant, "파괴", StringComparison.Ordinal))
                return;

            _calc.SelectedAnisVariant = variant;
            if (!string.IsNullOrWhiteSpace(CharacterNameText.Text) && CharacterNameText.Text == "아나이스")
            {
                ApplyCharacterModifier(CharacterNameText.Text, CalculatorTypeText.Text);
            }
            SaveDamageCalculatorState();
        }

        private void SetAnaisRadioState(string? variant)
        {
            if (AnaisMagicRadioButton == null || AnaisDestructionRadioButton == null)
                return;

            bool isMagic = string.Equals(variant, "마법", StringComparison.Ordinal);
            bool isDestruction = string.Equals(variant, "파괴", StringComparison.Ordinal);

            AnaisMagicRadioButton.IsChecked = isMagic;
            AnaisDestructionRadioButton.IsChecked = isDestruction;
        }

        private string GetSelectedAnaisVariant()
        {
            if (AnaisDestructionRadioButton?.IsChecked == true)
                return "파괴";

            if (AnaisMagicRadioButton?.IsChecked == true)
                return "마법";

            return _calc.SelectedAnisVariant;
        }

        private void OpenGroup1Window_Click(object sender, RoutedEventArgs e)
        {
            OpenUnifiedGroupSettingsWindow();
        }

        private void OpenGroup2Window_Click(object sender, RoutedEventArgs e)
        {
            OpenUnifiedGroupSettingsWindow();
        }

        private void OpenGroup4Window_Click(object sender, RoutedEventArgs e)
        {
            OpenUnifiedGroupSettingsWindow();
        }

        private void OpenUnifiedGroupSettingsWindow()
        {
            var window = CreateSettingsWindow("공격 피해량 설정");
            window.Topmost = true;
            window.Width = 1000;
            window.MinWidth = 1000;
            window.Height = 300;
            var root = CreateWindowRootPanel(window, 600, 300);

            var outerGrid = new Grid();
            outerGrid.Margin = new Thickness(0);
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(outerGrid);

            var group1Panel = CreateGroupPanel(AttackGroupKind.Group1, "공격 피해량(이자벨)", "50%", out var group1Save);
            var group2Panel = CreateGroupPanel(AttackGroupKind.Group2, "공격 피해량(일반)", "30%", out var group2Save);
            var group3Panel = CreateGroupPanel(AttackGroupKind.Group3, "공격 피해량(스킬)", "65%", out var group3Save);
            var group4Panel = CreateGroupPanel(AttackGroupKind.Group4, "공격 피해량(캐릭터)", "80%", out var group4Save);

            Grid.SetColumn(group1Panel, 0);
            Grid.SetColumn(group2Panel, 1);
            Grid.SetColumn(group3Panel, 2);
            Grid.SetColumn(group4Panel, 3);
            outerGrid.Children.Add(group1Panel);
            outerGrid.Children.Add(group2Panel);
            outerGrid.Children.Add(group3Panel);
            outerGrid.Children.Add(group4Panel);

            window.Closing += (_, _) =>
            {
                group1Save();
                group2Save();
                group3Save();
                group4Save();
                RefreshGroupSummaryTexts();
                SaveDamageCalculatorState();
            };

            window.ShowDialog();
        }

        private Border CreateGroupPanel(AttackGroupKind kind, string displayTitle, string subtitle, out Action saveAction)
        {
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x11, 0x18, 0x21)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 6, 0),
                MinWidth = 220,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };

            var panel = new StackPanel();
            border.Child = panel;
            var subtitleText = new TextBlock
            {
                Text = subtitle,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xE0, 0x66)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(new TextBlock
            {
                Text = displayTitle,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0xA6, 0xFF)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(subtitleText);

            void RefreshSubtitle(double total)
            {
                subtitleText.Text = $"{Math.Min(total, subtitle switch
                {
                    "50%" => 50,
                    "30%" => 30,
                    "65%" => 65,
                    "80%" => 80,
                    _ => 0
                }):0.#}% / {subtitle}";
            }

            void RefreshGroup1Subtitle() => RefreshSubtitle(GetGroup1TotalPercent());
            void RefreshGroup2Subtitle() => RefreshSubtitle(GetGroup2TotalPercent());
            void RefreshGroup3Subtitle() => RefreshSubtitle(GetGroup3TotalPercent());
            void RefreshGroup4Subtitle() => RefreshSubtitle(GetGroup4TotalPercent());

            saveAction = () => { };
            if (kind == AttackGroupKind.Group1)
            {
                var snowmanToggle = CreateToggle("눈사람 (20%)", _group1.Snowman);
                var illumiToggle = CreateToggle("일루미 (10%)", _group1.Illumi);
                snowmanToggle.Checked += (_, _) => illumiToggle.IsChecked = false;
                illumiToggle.Checked += (_, _) => snowmanToggle.IsChecked = false;
                snowmanToggle.Checked += (_, _) => { _group1.Snowman = true; _group1.Illumi = false; RefreshGroup1Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                snowmanToggle.Unchecked += (_, _) => { _group1.Snowman = false; RefreshGroup1Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                illumiToggle.Checked += (_, _) => { _group1.Illumi = true; _group1.Snowman = false; RefreshGroup1Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                illumiToggle.Unchecked += (_, _) => { _group1.Illumi = false; RefreshGroup1Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                var isabelDamageToggle = CreateToggle("이자벨 대미지 (10%)", _group1.IsabelDamage);
                var isabelSpecialToggle = CreateToggle("이자벨 특선 대미지 (10%)", _group1.IsabelSpecial);
                var isabelBattleToggle = CreateToggle("이자벨 전투 (10%)", _group1.IsabelBattle);
                isabelDamageToggle.Checked += (_, _) => { _group1.IsabelDamage = true; RefreshGroup1Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                isabelDamageToggle.Unchecked += (_, _) => { _group1.IsabelDamage = false; RefreshGroup1Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                isabelSpecialToggle.Checked += (_, _) => { _group1.IsabelSpecial = true; RefreshGroup1Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                isabelSpecialToggle.Unchecked += (_, _) => { _group1.IsabelSpecial = false; RefreshGroup1Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                isabelBattleToggle.Checked += (_, _) => { _group1.IsabelBattle = true; RefreshGroup1Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                isabelBattleToggle.Unchecked += (_, _) => { _group1.IsabelBattle = false; RefreshGroup1Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                panel.Children.Add(CreateLabeledToggleRow("눈사람 (20%)", snowmanToggle));
                panel.Children.Add(CreateLabeledToggleRow("일루미 (10%)", illumiToggle));
                panel.Children.Add(CreateLabeledToggleRow("이자벨 대미지 (10%)", isabelDamageToggle));
                panel.Children.Add(CreateLabeledToggleRow("이자벨 특선 대미지 (10%)", isabelSpecialToggle));
                panel.Children.Add(CreateLabeledToggleRow("이자벨 전투 (10%)", isabelBattleToggle));
                panel.Children.Add(CreateLabeledTextBoxRow("기타", _group1.EtcValue, out var etcText));
                etcText.TextChanged += (_, _) => { _group1.EtcValue = etcText.Text.Trim(); RefreshGroup1Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                RefreshGroup1Subtitle();
                saveAction = () =>
                {
                    _group1.Snowman = snowmanToggle.IsChecked == true;
                    _group1.Illumi = illumiToggle.IsChecked == true;
                    _group1.IsabelDamage = isabelDamageToggle.IsChecked == true;
                    _group1.IsabelSpecial = isabelSpecialToggle.IsChecked == true;
                    _group1.IsabelBattle = isabelBattleToggle.IsChecked == true;
                    _group1.EtcValue = etcText.Text.Trim();
                    RefreshGroup1Subtitle();
                    UpdateCategorySummaryText();
                };
            }
            else if (kind == AttackGroupKind.Group2)
            {
                var gaegakbiToggle = CreateToggle("개각비 (5%)", _group2.Gaegakbi);
                var clubTypePToggle = CreateToggle("클럽 Type-P (5%)", _group2.ClubTypeP);
                var exploreToggle = CreateToggle("탐험 포인트 공증 (5%)", _group2.ExplorePoint);
                var twPowerToggle = CreateToggle("테일즈위버 기운 (5%)", _group2.TwPower);
                var hamToggle = CreateToggle("괴력의 햄 (10%)", _group2.Ham);
                var eventToggle = CreateToggle("이벤트 (10%)", _group2.Event);
                gaegakbiToggle.Checked += (_, _) => { _group2.Gaegakbi = true; RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                gaegakbiToggle.Unchecked += (_, _) => { _group2.Gaegakbi = false; RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                clubTypePToggle.Checked += (_, _) => { _group2.ClubTypeP = true; RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                clubTypePToggle.Unchecked += (_, _) => { _group2.ClubTypeP = false; RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                exploreToggle.Checked += (_, _) => { _group2.ExplorePoint = true; RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                exploreToggle.Unchecked += (_, _) => { _group2.ExplorePoint = false; RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                twPowerToggle.Checked += (_, _) => { _group2.TwPower = true; RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                twPowerToggle.Unchecked += (_, _) => { _group2.TwPower = false; RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                hamToggle.Checked += (_, _) => { _group2.Ham = true; RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                hamToggle.Unchecked += (_, _) => { _group2.Ham = false; RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                eventToggle.Checked += (_, _) => { _group2.Event = true; RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                eventToggle.Unchecked += (_, _) => { _group2.Event = false; RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                panel.Children.Add(CreateLabeledToggleRow("개각비 (5%)", gaegakbiToggle));
                panel.Children.Add(CreateLabeledToggleRow("클럽 Type-P (5%)", clubTypePToggle));
                panel.Children.Add(CreateLabeledToggleRow("탐험 포인트 공증 (5%)", exploreToggle));
                panel.Children.Add(CreateLabeledToggleRow("테일즈위버 기운 (5%)", twPowerToggle));
                panel.Children.Add(CreateLabeledToggleRow("괴력의 햄 (10%)", hamToggle));
                panel.Children.Add(CreateLabeledToggleRow("이벤트 (10%)", eventToggle));
                panel.Children.Add(CreateLabeledTextBoxRow("기타", _group2.EtcValue, out var etcText));
                etcText.TextChanged += (_, _) => { _group2.EtcValue = etcText.Text.Trim(); RefreshGroup2Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                RefreshGroup2Subtitle();
                saveAction = () =>
                {
                    _group2.Gaegakbi = gaegakbiToggle.IsChecked == true;
                    _group2.ClubTypeP = clubTypePToggle.IsChecked == true;
                    _group2.ExplorePoint = exploreToggle.IsChecked == true;
                    _group2.TwPower = twPowerToggle.IsChecked == true;
                    _group2.Ham = hamToggle.IsChecked == true;
                    _group2.Event = eventToggle.IsChecked == true;
                    _group2.EtcValue = etcText.Text.Trim();
                    RefreshGroup2Subtitle();
                    UpdateCategorySummaryText();
                };
            }
            else if (kind == AttackGroupKind.Group3)
            {
                var trait = new TextBlock
                {
                    Text = $"{GetCurrentTraitDisplayName()} : {_calc.Group3TraitAttackDamageValue:0.#}%",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextAlignment = TextAlignment.Left
                };
                panel.Children.Add(trait);
                RefreshGroup3Subtitle();
                saveAction = () => { };
            }
            else
            {
                var titleToggle = CreateToggle("칭호 (20%)", _group4.TitleDamage);
                var feverToggle = CreateToggle("피버 (10%)", _group4.Fever);
                titleToggle.Checked += (_, _) => { _group4.TitleDamage = true; RefreshGroup4Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                titleToggle.Unchecked += (_, _) => { _group4.TitleDamage = false; RefreshGroup4Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                feverToggle.Checked += (_, _) => { _group4.Fever = true; RefreshGroup4Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                feverToggle.Unchecked += (_, _) => { _group4.Fever = false; RefreshGroup4Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                panel.Children.Add(CreateLabeledToggleRow("칭호 (20%)", titleToggle));
                panel.Children.Add(CreateLabeledToggleRow("피버 (10%)", feverToggle));
                panel.Children.Add(CreateLabeledComboRow("무기 어빌리티", new[] { "없음 (0%)", "심연 (9%)", "상실 (10%)", "야성 (11%)" }, _group4.WeaponAbilityIndex, out var weaponAbilityCombo));
                panel.Children.Add(CreateLabeledComboRow("손목 어빌리티", new[] { "없음 (0%)", "심연 (9%)", "상실 (10%)", "야성 (11%)" }, _group4.WristAbilityIndex, out var wristCombo, 100));
                panel.Children.Add(CreateLabeledComboRow("손 어빌리티", new[] { "없음 (0%)", "심연 (7%)", "상실 (8%)", "야성 (9%)" }, _group4.HandAbilityIndex, out var handCombo));
                panel.Children.Add(CreateLabeledComboRow("루나리아 어빌리티", new[] { "0%", "1%", "2%", "3%", "4%", "5%", "6%", "7%", "8%", "9%", "10%" }, _group4.LunariaAbilityIndex, out var lunariaCombo));
                panel.Children.Add(CreateLabeledComboRow("심화 룬", new[] { "0렙 (0%)", "1렙 (3%)", "2렙 (6%)", "3렙 (9%)" }, _group4.DeepRuneIndex, out var deepRuneCombo));
                panel.Children.Add(CreateLabeledTextBoxRow("기타", _group4.EtcValue, out var etcText));
                wristCombo.SelectionChanged += (_, _) => { _group4.WristAbilityIndex = Math.Max(0, wristCombo.SelectedIndex); RefreshGroup4Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                weaponAbilityCombo.SelectionChanged += (_, _) => { _group4.WeaponAbilityIndex = Math.Max(0, weaponAbilityCombo.SelectedIndex); RefreshGroup4Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                handCombo.SelectionChanged += (_, _) => { _group4.HandAbilityIndex = Math.Max(0, handCombo.SelectedIndex); RefreshGroup4Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                lunariaCombo.SelectionChanged += (_, _) => { _group4.LunariaAbilityIndex = Math.Max(0, lunariaCombo.SelectedIndex); RefreshGroup4Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                deepRuneCombo.SelectionChanged += (_, _) => { _group4.DeepRuneIndex = Math.Max(0, deepRuneCombo.SelectedIndex); RefreshGroup4Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                etcText.TextChanged += (_, _) => { _group4.EtcValue = etcText.Text.Trim(); RefreshGroup4Subtitle(); RefreshGroupSummaryTexts(); UpdateCategorySummaryText(); };
                RefreshGroup4Subtitle();
                saveAction = () =>
                {
                    _group4.TitleDamage = titleToggle.IsChecked == true;
                    _group4.Fever = feverToggle.IsChecked == true;
                    _group4.WristAbilityIndex = Math.Max(0, wristCombo.SelectedIndex);
                    _group4.WeaponAbilityIndex = Math.Max(0, weaponAbilityCombo.SelectedIndex);
                    _group4.HandAbilityIndex = Math.Max(0, handCombo.SelectedIndex);
                    _group4.LunariaAbilityIndex = Math.Max(0, lunariaCombo.SelectedIndex);
                    _group4.DeepRuneIndex = Math.Max(0, deepRuneCombo.SelectedIndex);
                    _group4.EtcValue = etcText.Text.Trim();
                    RefreshGroup4Subtitle();
                    UpdateCategorySummaryText();
                };
            }

            return border;
        }

        private void OpenGroup5Window_Click(object sender, RoutedEventArgs e)
        {
            var window = CreateSettingsWindow("그룹5 설정 (상한 73%)");
            var panel = CreateWindowRootPanel(window);

            panel.Children.Add(CreateLabeledCombo("아티팩트", new[] { "프시키 (15%)", "아크론 (20%)", "이클립스 (30%)", "에테리얼 (35%)" }, _group5.ArtifactIndex, out var artifactCombo));
            panel.Children.Add(CreateLabeledCombo("손목 부가", new[] { "25%", "26%", "27%", "28%" }, _group5.WristExtraIndex, out var wristCombo));
            panel.Children.Add(CreateLabeledCombo("루나리아 부가", new[] { "0%", "1%", "2%", "3%", "4%", "5%", "6%", "7%", "8%", "9%", "10%" }, _group5.LunariaExtraIndex, out var lunariaCombo));

            AddSaveButton(panel, () =>
            {
                _group5.ArtifactIndex = Math.Max(0, artifactCombo.SelectedIndex);
                _group5.WristExtraIndex = Math.Max(0, wristCombo.SelectedIndex);
                _group5.LunariaExtraIndex = Math.Max(0, lunariaCombo.SelectedIndex);
                RefreshGroupSummaryTexts();
                UpdateCategorySummaryText();
                window.DialogResult = true;
                window.Close();
            });

            window.Closing += (_, _) =>
            {
                _group5.ArtifactIndex = Math.Max(0, artifactCombo.SelectedIndex);
                _group5.WristExtraIndex = Math.Max(0, wristCombo.SelectedIndex);
                _group5.LunariaExtraIndex = Math.Max(0, lunariaCombo.SelectedIndex);
                RefreshGroupSummaryTexts();
                SaveDamageCalculatorState();
            };

            window.ShowDialog();
        }

        private void OpenGroup11Window_Click(object sender, RoutedEventArgs e)
        {
            var window = CreateSettingsWindow("그룹11 설정 - 추가 피해");
            var panel = CreateWindowRootPanel(window);

            panel.Children.Add(CreateLabeledCombo("저격 연마", new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" }, _group11.SniperIndex, out var sniperCombo));
            panel.Children.Add(CreateLabeledCombo("장비 강화석 옵션", new[] { "0%", "45%", "46%", "47%", "48%" }, _group11.GemOptionIndex, out var gemCombo));
            panel.Children.Add(CreateLabeledCombo("무기 부가", new[] { "0%", "18%", "19%", "20%", "21%" }, _group11.WeaponExtraIndex, out var weaponCombo));
            panel.Children.Add(CreateLabeledReadOnlyValue("캐릭터 고유 값", _group11.TraitValue));

            AddSaveButton(panel, () =>
            {
                _group11.SniperIndex = Math.Max(0, sniperCombo.SelectedIndex);
                _group11.GemOptionIndex = Math.Max(0, gemCombo.SelectedIndex);
                _group11.WeaponExtraIndex = Math.Max(0, weaponCombo.SelectedIndex);
                RefreshGroupSummaryTexts();
                UpdateCategorySummaryText();
                window.DialogResult = true;
                window.Close();
            });

            window.Closing += (_, _) =>
            {
                _group11.SniperIndex = Math.Max(0, sniperCombo.SelectedIndex);
                _group11.GemOptionIndex = Math.Max(0, gemCombo.SelectedIndex);
                _group11.WeaponExtraIndex = Math.Max(0, weaponCombo.SelectedIndex);
                RefreshGroupSummaryTexts();
                SaveDamageCalculatorState();
            };

            window.ShowDialog();
        }

        private void OpenMonsterWindow_Click(object sender, RoutedEventArgs e)
        {
            var window = CreateSettingsWindow("몬스터 설정");
            var panel = CreateWindowRootPanel(window);

            var monsterNames = DamageReferenceData.MonsterEntries.Select(x => x.Name).ToArray();
            panel.Children.Add(CreateLabeledCombo("몬스터", monsterNames, _monster.SelectedIndex, out var monsterCombo));

            var detailGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            detailGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var detailName = AddMonsterDetailRow(detailGrid, 0, "이름");
            var detailStatDefense = AddMonsterDetailRow(detailGrid, 1, "스탯 방어력");
            var detailFixedDefense = AddMonsterDetailRow(detailGrid, 2, "고정 방어력");
            var detailFixedDamageReduction = AddMonsterDetailRow(detailGrid, 3, "고정 피해 감소");
            var detailDamageReductionRate = AddMonsterDetailRow(detailGrid, 4, "피해 감소율");
            var detailAttribute = AddMonsterDetailRow(detailGrid, 5, "속성");
            var detailHp = AddMonsterDetailRow(detailGrid, 6, "HP(솔로)");
            panel.Children.Add(detailGrid);

            void RefreshMonsterDetail()
            {
                var entry = GetSelectedMonsterEntry(monsterCombo.SelectedIndex);
                if (entry == null)
                {
                    detailName.Text = "-";
                    detailStatDefense.Text = "0";
                    detailFixedDefense.Text = "0";
                    detailFixedDamageReduction.Text = "0";
                    detailDamageReductionRate.Text = "0%";
                    detailAttribute.Text = "0";
                    detailHp.Text = "0";
                    return;
                }

                detailName.Text = entry.Value.Name;
                detailStatDefense.Text = entry.Value.StatDefense.ToString("0.##", CultureInfo.CurrentCulture);
                detailFixedDefense.Text = entry.Value.FixedDefense.ToString("0.##", CultureInfo.CurrentCulture);
                detailFixedDamageReduction.Text = entry.Value.FixedDamageReduction.ToString("0.##", CultureInfo.CurrentCulture);
                detailDamageReductionRate.Text = $"{entry.Value.DamageReductionRate:0.##}%";
                detailAttribute.Text = entry.Value.AttributeValue.ToString("0.##", CultureInfo.CurrentCulture);
                detailHp.Text = entry.Value.HpSolo.ToString("0", CultureInfo.CurrentCulture);
            }

            monsterCombo.SelectionChanged += (_, _) =>
            {
                _monster.SelectedIndex = Math.Max(0, monsterCombo.SelectedIndex);
                RefreshMonsterDetail();
                UpdateMonsterSummaryText();
                SaveDamageCalculatorState();
            };

            RefreshMonsterDetail();
            AddSaveButton(panel, () =>
            {
                _monster.SelectedIndex = Math.Max(0, monsterCombo.SelectedIndex);
                UpdateMonsterSummaryText();
                SaveDamageCalculatorState();
                window.DialogResult = true;
                window.Close();
            });

            window.Closing += (_, _) =>
            {
                _monster.SelectedIndex = Math.Max(0, monsterCombo.SelectedIndex);
                UpdateMonsterSummaryText();
                SaveDamageCalculatorState();
            };

            window.ShowDialog();
        }

        private void RefreshGroupSummaryTexts()
        {
            Group1SummaryText.Text = $"그룹1:{OnOff(_group1.Snowman)}/{OnOff(_group1.Illumi)} / 그룹2:{CountTrue(_group2.Gaegakbi, _group2.ClubTypeP, _group2.ExplorePoint, _group2.TwPower, _group2.Ham, _group2.Event)}개 / 그룹4:{IndexToName(_group4.WristAbilityIndex, new[] { "없음", "심연", "상실", "야성" })}/{IndexToName(_group4.WeaponAbilityIndex, new[] { "없음", "심연", "상실", "야성" })}/{IndexToName(_group4.HandAbilityIndex, new[] { "없음", "심연", "상실", "야성" })}";
            Group5SummaryText.Text = $"아티팩트:{IndexToName(_group5.ArtifactIndex, new[] { "프시키", "아크론", "이클립스", "에테리얼" })}";
            Group11SummaryText.Text = $"저격:{IndexToName(_group11.SniperIndex, new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" })}렙 / 추가대미지:{GetSniperAdditionalDamagePercent(_group11.SniperIndex):0.#}% / 강화석:{IndexToName(_group11.GemOptionIndex, new[] { "0", "45", "46", "47", "48" })}%";
            UpdateMonsterSummaryText();
        }

        private void UpdateMonsterSummaryText()
        {
            if (MonsterSummaryText == null)
                return;

            var entry = GetSelectedMonsterEntry(_monster.SelectedIndex);
            double currentAttribute = GetTextBoxValue(ElementValueTextBox);
            MonsterSummaryText.Text = entry == null
                ? "몬스터: -"
                : $"몬스터:{entry.Value.Name} / 방어:{entry.Value.StatDefense:0.##} / 고정방어:{entry.Value.FixedDefense:0.##} / 고정피감:{entry.Value.FixedDamageReduction:0.##} / 피감율:{entry.Value.DamageReductionRate:0.##}% / 속성:{entry.Value.AttributeValue:0.##} / HP:{entry.Value.HpSolo:0} / 속성보정:{GetMonsterAttributeFactor(currentAttribute, entry.Value.AttributeValue):0.##} / 피감펙터:{GetMonsterDamageReductionFactor(entry.Value.DamageReductionRate):0.##}";
        }

        private static double GetMonsterAttributeFactor(double currentAttribute, double monsterAttribute)
        {
            double factor = 1.0 + ((currentAttribute - monsterAttribute) * 0.00625);
            return Math.Clamp(factor, 1.0, 1.5);
        }

        private static double GetMonsterDamageReductionFactor(double damageReductionRate)
        {
            double factor = 1.0 - (damageReductionRate / 100.0);
            return Math.Clamp(factor, 0.0, 1.0);
        }

        private static double GetSniperAdditionalDamagePercent(int sniperIndex)
        {
            return sniperIndex switch
            {
                0 => 0,
                1 => 5,
                2 => 10,
                3 => 15,
                4 => 20,
                5 => 25,
                6 => 28,
                7 => 31,
                8 => 34,
                9 => 37,
                10 => 40,
                _ => 0
            };
        }

        private void UpdateWeakPointUi()
        {
            if (WeakPointToggle == null || WeakPointValueText == null)
                return;

            WeakPointToggle.Content = WeakPointToggle.IsChecked == true ? "ON" : "OFF";
            bool weakPointOn = WeakPointToggle.IsChecked == true;
            WeakPointValueText.Text = weakPointOn ? "40%" : "0%";
            SaveDamageCalculatorState();
        }

        private void UpdateJudgementUi()
        {
            if (JudgementComboBox == null || JudgementValueText == null)
                return;

            int judgementValue = JudgementComboBox.SelectedIndex < 0 ? 0 : JudgementComboBox.SelectedIndex;
            double judgementPercent = judgementValue * 0.75;
            JudgementValueText.Text = $"{judgementPercent:0.#}%";
        }

        private void UpdateEtaLinkCriticalUi()
        {
            if (EtaLinkCriticalComboBox == null || EtaLinkCriticalValueText == null)
                return;

            int level = Math.Clamp(EtaLinkCriticalComboBox.SelectedIndex, 0, 20);
            EtaLinkCriticalValueText.Text = $"{GetEtaLinkCriticalPercent(level):0.#}%";
        }

        private void UpdateEtaLinkFinalDamageUi()
        {
            if (EtaLinkFinalDamageComboBox == null || EtaLinkFinalDamageValueText == null)
                return;

            int level = Math.Clamp(EtaLinkFinalDamageComboBox.SelectedIndex, 0, 5);
            EtaLinkFinalDamageValueText.Text = $"{GetEtaLinkFinalPercent(level):0.#}%";
        }

        private static string NormalizeEtaLevelText(string text)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int etaLevel))
                etaLevel = 1;

            etaLevel = Math.Clamp(etaLevel, 1, 100);
            return etaLevel.ToString(CultureInfo.CurrentCulture);
        }

        private static Window CreateSettingsWindow(string title)
        {
            return new Window
            {
                Title = title,
                Width = 1560,
                MinWidth = 1440,
                Height = 840,
                ResizeMode = ResizeMode.CanMinimize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = System.Windows.Media.Brushes.Transparent,
                AllowsTransparency = false,
                SizeToContent = SizeToContent.Manual
            };
        }

        private static StackPanel CreateWindowRootPanel(Window window, double? minWidth = null, double? minHeight = null)
        {
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x11, 0x18, 0x21)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12)
            };

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            var panel = new StackPanel();
            scrollViewer.Content = panel;
            border.Child = scrollViewer;
            window.Content = border;
            if (minWidth.HasValue)
                window.MinWidth = Math.Max(window.MinWidth, minWidth.Value);
            if (minHeight.HasValue)
                window.MinHeight = Math.Max(window.MinHeight, minHeight.Value);
            return panel;
        }

        private static CheckBox CreateToggle(string label, bool isChecked)
        {
            var checkBox = new CheckBox
            {
                Content = string.Empty,
                IsChecked = isChecked,
                Margin = new Thickness(0),
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC9, 0xD1, 0xD9)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
                Padding = new Thickness(0)
            };
            if (Application.Current?.TryFindResource("ToggleSwitchCheckBoxStyle") is Style toggleStyle)
                checkBox.Style = toggleStyle;
            checkBox.LayoutTransform = new System.Windows.Media.ScaleTransform(0.8, 0.8);
            return checkBox;
        }

        private static Border CreateToggleRow(string label, bool isChecked, out CheckBox checkBox)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 1)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });

            var textBlock = new TextBlock
            {
                Text = label,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                TextAlignment = TextAlignment.Left
            };
            Grid.SetColumn(textBlock, 0);
            grid.Children.Add(textBlock);

            checkBox = new CheckBox
            {
                Content = string.Empty,
                IsChecked = isChecked,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC9, 0xD1, 0xD9)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 9.5,
                Padding = new Thickness(0),
                Margin = new Thickness(0)
            };
            if (Application.Current?.TryFindResource("ToggleSwitchCheckBoxStyle") is Style toggleStyle)
                checkBox.Style = toggleStyle;
            Grid.SetColumn(checkBox, 1);
            grid.Children.Add(checkBox);

            return new Border { Child = grid };
        }

        private static Border CreateLabeledToggleRow(string label, CheckBox toggle)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 1)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });

            var textBlock = new TextBlock
            {
                Text = label,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                TextAlignment = TextAlignment.Left
            };
            Grid.SetColumn(textBlock, 0);
            grid.Children.Add(textBlock);

            toggle.HorizontalAlignment = HorizontalAlignment.Right;
            toggle.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(toggle, 1);
            grid.Children.Add(toggle);
            return new Border { Child = grid };
        }

        private static Border CreateLabeledTextBoxRow(string label, string value, out TextBox textBox)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 1)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBlock = new TextBlock
            {
                Text = label,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                TextAlignment = TextAlignment.Left
            };
            Grid.SetColumn(textBlock, 0);
            grid.Children.Add(textBlock);

            textBox = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(value) ? "0" : value,
                Height = 18,
                Width = 72,
                FontSize = 10,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0D, 0x11, 0x17)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(1, 0, 1, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (Application.Current?.TryFindResource("SimTextBoxStyle") is Style textStyle)
                textBox.Style = textStyle;
            Grid.SetColumn(textBox, 1);
            grid.Children.Add(textBox);
            return new Border { Child = grid };
        }

        private static Border CreateLabeledComboRow(string label, string[] options, int selectedIndex, out ComboBox comboBox, double comboWidth = 100)
        {
            return CreateLabeledCombo(label, options, selectedIndex, out comboBox, comboWidth);
        }

        private static Border CreateLabeledReadOnlyValue(string label, string value)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 1)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var textBlock = new TextBlock
            {
                Text = label,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                TextAlignment = TextAlignment.Left
            };
            Grid.SetColumn(textBlock, 0);
            grid.Children.Add(textBlock);

            var valueBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(value) ? "0" : value,
                Foreground = System.Windows.Media.Brushes.White,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0),
                TextWrapping = TextWrapping.NoWrap
            };
            Grid.SetColumn(valueBlock, 1);
            grid.Children.Add(valueBlock);
            return new Border { Child = grid };
        }

        private static Border CreateLabeledCombo(string label, string[] options, int selectedIndex, out ComboBox comboBox, double comboWidth = 100)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 1)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(comboWidth) });

            var textBlock = new TextBlock
            {
                Text = label,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                TextAlignment = TextAlignment.Left
            };
            Grid.SetColumn(textBlock, 0);
            grid.Children.Add(textBlock);

            comboBox = new ComboBox
            {
                Height = 20,
                Width = comboWidth,
                FontSize = 10,
                IsEditable = false,
                StaysOpenOnEdit = false,
                IsTextSearchEnabled = false,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0D, 0x11, 0x17)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x30, 0x36, 0x3D)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(2, 0, 2, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Panel.SetZIndex(comboBox, 10);
            if (Application.Current?.TryFindResource("DarkComboBoxStyle") is Style comboStyle)
                comboBox.Style = comboStyle;
            foreach (var option in options)
                comboBox.Items.Add(option);
            comboBox.SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, comboBox.Items.Count - 1));
            Grid.SetColumn(comboBox, 1);
            grid.Children.Add(comboBox);
            return new Border { Child = grid };
        }

        private static void AddLabeledToggleRow(Panel panel, CheckBox toggle)
        {
            if (toggle != null)
            {
                var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                if (toggle.Content is string text)
                {
                    toggle.Content = string.Empty;
                    var label = new TextBlock
                    {
                        Text = text,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontSize = 10,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 8, 0)
                    };
                    Grid.SetColumn(label, 0);
                    grid.Children.Add(label);
                }
                toggle.HorizontalAlignment = HorizontalAlignment.Right;
                toggle.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(toggle, 1);
                grid.Children.Add(toggle);
                panel.Children.Add(grid);
            }
        }

        private static void AddLabeledComboRow(Panel panel, Border comboBorder)
        {
            if (comboBorder != null)
            {
                comboBorder.HorizontalAlignment = HorizontalAlignment.Right;
                comboBorder.Margin = new Thickness(0, 0, 0, 4);
            }
            panel.Children.Add(comboBorder);
        }

        private static void AddLabeledTextBoxRow(Panel panel, Border textBoxBorder)
        {
            if (textBoxBorder != null)
            {
                textBoxBorder.HorizontalAlignment = HorizontalAlignment.Right;
                textBoxBorder.Margin = new Thickness(0, 0, 0, 4);
            }
            panel.Children.Add(textBoxBorder);
        }

        private static void AddSaveButton(Panel panel, Action saveAction)
        {
            var saveButton = new Button
            {
                Content = "저장",
                Height = 34,
                Width = 140,
                Margin = new Thickness(0, 8, 0, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4B, 0x3A, 0x74)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6E, 0x40, 0xC9)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            if (Application.Current?.TryFindResource("SimButtonStyle") is Style buttonStyle)
                saveButton.Style = buttonStyle;
            saveButton.Click += (_, _) => saveAction();
            panel.Children.Add(saveButton);
        }

        private static bool ReadToggle(Panel panel, int index)
        {
            if (index < 0 || index >= panel.Children.Count)
                return false;

            return panel.Children[index] is CheckBox checkbox && checkbox.IsChecked == true;
        }

        private static string OnOff(bool value) => value ? "ON" : "OFF";
        private static int CountTrue(params bool[] values) => values.Count(static x => x);
        private static string NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? "0" : value;
        private static string IndexToName(int index, string[] names)
        {
            if (index < 0 || index >= names.Length)
                return "-";
            return names[index];
        }

        private sealed class Group1State
        {
            public bool Snowman { get; set; }
            public bool Illumi { get; set; }
            public bool IsabelDamage { get; set; }
            public bool IsabelSpecial { get; set; }
            public bool IsabelBattle { get; set; }
            public string EtcValue { get; set; } = "0";
        }

        private sealed class Group2State
        {
            public bool Gaegakbi { get; set; }
            public bool ClubTypeP { get; set; }
            public bool ExplorePoint { get; set; }
            public bool TwPower { get; set; }
            public bool Ham { get; set; }
            public bool Event { get; set; }
            public string EtcValue { get; set; } = "0";
        }

        private sealed class Group4State
        {
            public bool TitleDamage { get; set; }
            public bool Fever { get; set; }
            public int WeaponAbilityIndex { get; set; }
            public int WristAbilityIndex { get; set; }
            public int HandAbilityIndex { get; set; }
            public int LunariaAbilityIndex { get; set; }
            public int DeepRuneIndex { get; set; }
            public string EtcValue { get; set; } = "0";
        }

        private sealed class Group5State
        {
            public int ArtifactIndex { get; set; }
            public int WristExtraIndex { get; set; }
            public int LunariaExtraIndex { get; set; }
        }

        private sealed class Group11State
        {
            public int SniperIndex { get; set; }
            public int GemOptionIndex { get; set; }
            public int WeaponExtraIndex { get; set; }
            public string TraitValue { get; set; } = "0";
        }

        private sealed class MonsterState
        {
            public int SelectedIndex { get; set; }
        }

        private void BasicOption_Changed(object sender, RoutedEventArgs e)
        {
            SyncBasicOptionValues();
            UpdateCategorySummaryText();
        }

        private void BasicOption_TextChanged(object sender, TextChangedEventArgs e)
        {
            SyncBasicOptionValues();
            UpdateCategorySummaryText();
        }

        private void WeakPointToggle_Changed(object sender, RoutedEventArgs e)
        {
            UpdateWeakPointUi();
            UpdateCategorySummaryText();
        }

        private void NumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
        }

        private void NumberTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space || e.Key == Key.Decimal || e.Key == Key.OemPeriod || e.Key == Key.OemComma)
                e.Handled = true;
        }

        private void NumberTextBox_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            string text = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            if (!Regex.IsMatch(text, @"^\d+$"))
                e.CancelCommand();
        }

        private void NumberTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            if (!double.TryParse(textBox.Text, out double value))
                value = 0;

            textBox.Text = Math.Max(0, value).ToString("0", CultureInfo.CurrentCulture);
            SyncBasicOptionValues();
            UpdateCategorySummaryText();
        }

        private void SyncBasicOptionValues()
        {
            _calc.SkillMultiplier = ReadNumber(SkillMultiplierTextBox, 0);
            _calc.SkillCriticalMultiplier = ReadNumber(CriticalMultiplierTextBox, 0);
            _calc.HitCount = Math.Max(1, ReadNumber(HitCountTextBox, 1));
        }

        private static double ReadNumber(TextBox? textBox, double fallback)
        {
            if (textBox == null)
                return fallback;

            if (!double.TryParse(textBox.Text, out double value))
                return fallback;

            return Math.Max(0, value);
        }

        private void JudgementComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateJudgementUi();
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void EtaLinkCriticalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateEtaLinkCriticalUi();
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void EtaLinkFinalDamageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateEtaLinkFinalDamageUi();
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void ComboBonusToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (ComboBonusToggle == null || ComboBonusValueText == null)
                return;

            ComboBonusToggle.Content = ComboBonusToggle.IsChecked == true ? "ON" : "OFF";
            ComboBonusValueText.Text = ComboBonusToggle.IsChecked == true ? "15%" : "0%";
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void FinalDamageOption_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void CategoryToggle_Changed(object sender, RoutedEventArgs e)
        {
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void SienaTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void SpecialDamageReductionTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void EtaLevelTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, @"^\d+$");
        }

        private void EtaLevelTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Space || e.Key == Key.Decimal || e.Key == Key.OemPeriod || e.Key == Key.DeadCharProcessed)
            {
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Back || e.Key == Key.Delete || e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Tab)
                return;
        }

        private void EtaLevelTextBox_OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.SourceDataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            string text = e.SourceDataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            if (!Regex.IsMatch(text, @"^\d+$"))
            {
                e.CancelCommand();
                return;
            }
        }

        private void EtaLevelTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (EtaLevelTextBox == null || !EtaLevelTextBox.IsEnabled)
                return;

            EtaLevelTextBox.Text = NormalizeEtaLevelText(EtaLevelTextBox.Text);
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void EtaLevelTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (EtaLevelTextBox == null || !EtaLevelTextBox.IsEnabled)
                return;

            UpdateEtaUi();
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void UpdateEtaUi()
        {
            if (EtaLevelTextBox == null)
                return;

            int etaLevel = 1;
            if (!int.TryParse(EtaLevelTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out int parsed))
                parsed = 1;

            etaLevel = Math.Clamp(parsed, 1, 100);
            EtaLevelTextBox.Text = etaLevel.ToString(CultureInfo.CurrentCulture);

            var entry = DamageReferenceData.EtaLevels.FirstOrDefault(x => x.Level == etaLevel);
            _calc.EtaAwakeningDamageIncrease = entry.Level == etaLevel ? entry.AwakeningDamageIncrease : 0;
            EtaLevelTextBox.ToolTip = $"계수: {_calc.EtaAwakeningDamageIncrease:0.##}";
        }

        private void LoadDamageCalculatorState()
        {
            _isRestoringDamageState = true;
            var state = _damageState;

            try
            {
                HelmetAbilityToggle.IsChecked = state.HelmetAbilityEnabled;
                WeaponAdditionalDamageTextBox.Text = state.WeaponAdditionalDamage.ToString("0", CultureInfo.CurrentCulture);
                SkillMultiplierTextBox.Text = state.SkillMultiplier.ToString("0", CultureInfo.CurrentCulture);
                CriticalMultiplierTextBox.Text = state.CriticalMultiplier.ToString("0", CultureInfo.CurrentCulture);
                HitCountTextBox.Text = Math.Max(1, state.HitCount).ToString("0", CultureInfo.CurrentCulture);
                EtaLevelTextBox.Text = Math.Clamp(state.EtaLevel, 1, 100).ToString("0", CultureInfo.CurrentCulture);
                ElementValueTextBox.Text = state.ElementValue.ToString("0", CultureInfo.CurrentCulture);

                WeakPointToggle.IsChecked = state.WeakPointEnabled;
                if (JudgementComboBox != null)
                    JudgementComboBox.SelectedIndex = Math.Max(0, state.JudgementIndex);
                if (EtaLinkCriticalComboBox != null)
                    EtaLinkCriticalComboBox.SelectedIndex = Math.Clamp(state.EtaLinkCriticalLevel, 0, 20);
                ClubFinalDamageToggle.IsChecked = state.ClubFinalDamageEnabled;
                CoreSetTextBox.Text = Math.Clamp(state.CoreSetValue, 0, 20).ToString(CultureInfo.CurrentCulture);
                if (EtaLinkFinalDamageComboBox != null)
                    EtaLinkFinalDamageComboBox.SelectedIndex = Math.Clamp(state.EtaLinkFinalDamageLevel, 0, 5);
                SienaTextBox.Text = state.SienaValue.ToString("0", CultureInfo.CurrentCulture);
                EnemyTakenDamageWeaponToggle.IsChecked = state.EnemyTakenDamageWeaponEnabled;
                ComboBonusToggle.IsChecked = state.ComboBonusEnabled;
                ComboBonusToggle.Content = state.ComboBonusEnabled ? "ON" : "OFF";
                ComboBonusValueText.Text = state.ComboBonusEnabled ? "15%" : "0%";
                if (SpecialDamageReductionTextBoxControl != null)
                    SpecialDamageReductionTextBoxControl.Text = Math.Clamp(state.SpecialDamageReductionRate, 0, 50).ToString("0", CultureInfo.CurrentCulture);

                _calc.SelectedAnisVariant = state.SelectedAnaisVariant;
                SetAnaisRadioState(_calc.SelectedAnisVariant);
                _group1.Snowman = state.Group1Snowman;
                _group1.Illumi = state.Group1Illumi;
                _group1.IsabelDamage = state.Group1IsabelDamage;
                _group1.IsabelSpecial = state.Group1IsabelSpecial;
                _group1.IsabelBattle = state.Group1IsabelBattle;
                _group1.EtcValue = state.Group1EtcValue;

                _group2.Gaegakbi = state.Group2Gaegakbi;
                _group2.ClubTypeP = state.Group2ClubTypeP;
                _group2.ExplorePoint = state.Group2ExplorePoint;
                _group2.TwPower = state.Group2TwPower;
                _group2.Ham = state.Group2Ham;
                _group2.Event = state.Group2Event;
                _group2.EtcValue = state.Group2EtcValue;

                _group4.TitleDamage = state.Group4TitleDamage;
                _group4.Fever = state.Group4Fever;
                _group4.WeaponAbilityIndex = state.Group4WeaponAbilityIndex;
                _group4.WristAbilityIndex = state.Group4WristAbilityIndex;
                _group4.HandAbilityIndex = state.Group4HandAbilityIndex;
                _group4.LunariaAbilityIndex = state.Group4LunariaAbilityIndex;
                _group4.DeepRuneIndex = state.Group4DeepRuneIndex;
                _group4.EtcValue = state.Group4EtcValue;

                _group5.ArtifactIndex = state.Group5ArtifactIndex;
                _group5.WristExtraIndex = state.Group5WristExtraIndex;
                _group5.LunariaExtraIndex = state.Group5LunariaExtraIndex;

                _group11.SniperIndex = state.Group11SniperIndex;
                _group11.GemOptionIndex = state.Group11GemOptionIndex;
                _group11.WeaponExtraIndex = state.Group11WeaponExtraIndex;
                _group11.TraitValue = state.Group11TraitValue;
                _monster.SelectedIndex = state.MonsterSelectedIndex;

                UpdateWeakPointUi();
                UpdateJudgementUi();
                UpdateEtaLinkCriticalUi();
                UpdateEtaLinkFinalDamageUi();
                UpdateEtaUi();
                RefreshGroupSummaryTexts();
                UpdateMonsterSummaryText();
            }
            finally
            {
                _isRestoringDamageState = false;
            }
        }

        private void SaveDamageCalculatorState()
        {
            if (_isRestoringDamageState)
                return;

            if (string.IsNullOrWhiteSpace(_currentDamageStateKey))
            {
                _currentDamageStateKey = BuildDamageStateKey(CharacterNameText?.Text, CalculatorTypeText?.Text);
            }

            var state = _damageState;
            state.HelmetAbilityEnabled = HelmetAbilityToggle?.IsChecked == true;
            state.WeaponAdditionalDamage = GetTextBoxValue(WeaponAdditionalDamageTextBox);
            state.SkillMultiplier = GetTextBoxValue(SkillMultiplierTextBox);
            state.CriticalMultiplier = GetTextBoxValue(CriticalMultiplierTextBox);
            state.HitCount = GetTextBoxValue(HitCountTextBox);
            state.EtaLevel = GetTextBoxValue(EtaLevelTextBox);
            state.EtaAwakeningDamageIncrease = _calc.EtaAwakeningDamageIncrease;
            state.ElementValue = GetTextBoxValue(ElementValueTextBox);
            state.WeakPointEnabled = WeakPointToggle?.IsChecked == true;
            state.JudgementIndex = JudgementComboBox?.SelectedIndex ?? 0;
            state.EtaLinkCriticalLevel = EtaLinkCriticalComboBox?.SelectedIndex ?? 0;
            state.ClubFinalDamageEnabled = ClubFinalDamageToggle?.IsChecked == true;
            state.CoreSetValue = (int)Math.Clamp(GetTextBoxValue(CoreSetTextBox), 0, 20);
            state.EtaLinkFinalDamageLevel = EtaLinkFinalDamageComboBox?.SelectedIndex ?? 0;
            state.SienaValue = GetTextBoxValue(SienaTextBox);
            state.EnemyTakenDamageWeaponEnabled = EnemyTakenDamageWeaponToggle?.IsChecked == true;
            state.ComboBonusEnabled = ComboBonusToggle?.IsChecked == true;
            state.SpecialDamageReductionRate = Math.Clamp(GetTextBoxValue(SpecialDamageReductionTextBoxControl), 0, 50);
            state.SelectedAnaisVariant = _calc.SelectedAnisVariant;

            state.Group1Snowman = _group1.Snowman;
            state.Group1Illumi = _group1.Illumi;
            state.Group1IsabelDamage = _group1.IsabelDamage;
            state.Group1IsabelSpecial = _group1.IsabelSpecial;
            state.Group1IsabelBattle = _group1.IsabelBattle;
            state.Group1EtcValue = _group1.EtcValue;

            state.Group2Gaegakbi = _group2.Gaegakbi;
            state.Group2ClubTypeP = _group2.ClubTypeP;
            state.Group2ExplorePoint = _group2.ExplorePoint;
            state.Group2TwPower = _group2.TwPower;
            state.Group2Ham = _group2.Ham;
            state.Group2Event = _group2.Event;
            state.Group2EtcValue = _group2.EtcValue;

            state.Group4TitleDamage = _group4.TitleDamage;
            state.Group4Fever = _group4.Fever;
            state.Group4WeaponAbilityIndex = _group4.WeaponAbilityIndex;
            state.Group4WristAbilityIndex = _group4.WristAbilityIndex;
            state.Group4HandAbilityIndex = _group4.HandAbilityIndex;
            state.Group4LunariaAbilityIndex = _group4.LunariaAbilityIndex;
            state.Group4DeepRuneIndex = _group4.DeepRuneIndex;
            state.Group4EtcValue = _group4.EtcValue;

            state.Group5ArtifactIndex = _group5.ArtifactIndex;
            state.Group5WristExtraIndex = _group5.WristExtraIndex;
            state.Group5LunariaExtraIndex = _group5.LunariaExtraIndex;

            state.Group11SniperIndex = _group11.SniperIndex;
            state.Group11GemOptionIndex = _group11.GemOptionIndex;
            state.Group11WeaponExtraIndex = _group11.WeaponExtraIndex;
            state.Group11TraitValue = _group11.TraitValue;
            state.MonsterSelectedIndex = _monster.SelectedIndex;

            var cloned = CloneDamageState(state);
            if (!string.IsNullOrWhiteSpace(_currentDamageStateKey))
            {
                _saveData.DamageCalculatorEntries[_currentDamageStateKey] = cloned;
            }

            CoefficientDataService.Save(_saveData);
        }

        private DamageCalculatorSaveState LoadDamageStateForKey(string key)
        {
            if (!string.IsNullOrWhiteSpace(key) && _saveData.DamageCalculatorEntries.TryGetValue(key, out var keyedState))
                return CloneDamageState(keyedState);

            return new DamageCalculatorSaveState();
        }

        private static string BuildDamageStateKey(string? characterName, string? calculatorTypeName)
        {
            if (string.IsNullOrWhiteSpace(characterName) || string.IsNullOrWhiteSpace(calculatorTypeName))
                return string.Empty;

            string normalizedType = NormalizeDamageCalculatorTypeName(calculatorTypeName);
            return string.IsNullOrWhiteSpace(normalizedType) ? string.Empty : $"{characterName}::{normalizedType}";
        }

        private static string NormalizeDamageCalculatorTypeName(string calculatorTypeName)
        {
            return calculatorTypeName switch
            {
                "찌르기" => "Stab",
                "베기" => "Hack",
                "마법공격" => "MagicAttack",
                "마법방어" => "MagicDefense",
                "물리복합" => "PhysicalHybrid",
                "마법베기" => "MagicHack",
                "신성" => "MagicDefense",
                _ => calculatorTypeName
            };
        }

        private static DamageCalculatorSaveState CloneDamageState(DamageCalculatorSaveState state)
        {
            return new DamageCalculatorSaveState
            {
                HelmetAbilityEnabled = state.HelmetAbilityEnabled,
                WeaponAdditionalDamage = state.WeaponAdditionalDamage,
                SkillMultiplier = state.SkillMultiplier,
                CriticalMultiplier = state.CriticalMultiplier,
                HitCount = state.HitCount,
                EtaLevel = state.EtaLevel,
                EtaAwakeningDamageIncrease = state.EtaAwakeningDamageIncrease,
                ElementValue = state.ElementValue,
                WeakPointEnabled = state.WeakPointEnabled,
                JudgementIndex = state.JudgementIndex,
                EtaLinkCriticalLevel = state.EtaLinkCriticalLevel,
                ClubFinalDamageEnabled = state.ClubFinalDamageEnabled,
                CoreSetValue = state.CoreSetValue,
                EtaLinkFinalDamageLevel = state.EtaLinkFinalDamageLevel,
                SienaValue = state.SienaValue,
                EnemyTakenDamageWeaponEnabled = state.EnemyTakenDamageWeaponEnabled,
                ComboBonusEnabled = state.ComboBonusEnabled,
                SpecialDamageReductionRate = state.SpecialDamageReductionRate,
                SelectedAnaisVariant = state.SelectedAnaisVariant,
                Group1Snowman = state.Group1Snowman,
                Group1Illumi = state.Group1Illumi,
                Group1IsabelDamage = state.Group1IsabelDamage,
                Group1IsabelSpecial = state.Group1IsabelSpecial,
                Group1IsabelBattle = state.Group1IsabelBattle,
                Group1EtcValue = state.Group1EtcValue,
                Group2Gaegakbi = state.Group2Gaegakbi,
                Group2ClubTypeP = state.Group2ClubTypeP,
                Group2ExplorePoint = state.Group2ExplorePoint,
                Group2TwPower = state.Group2TwPower,
                Group2Ham = state.Group2Ham,
                Group2Event = state.Group2Event,
                Group2EtcValue = state.Group2EtcValue,
                Group4TitleDamage = state.Group4TitleDamage,
                Group4Fever = state.Group4Fever,
                Group4WeaponAbilityIndex = state.Group4WeaponAbilityIndex,
                Group4WristAbilityIndex = state.Group4WristAbilityIndex,
                Group4HandAbilityIndex = state.Group4HandAbilityIndex,
                Group4LunariaAbilityIndex = state.Group4LunariaAbilityIndex,
                Group4DeepRuneIndex = state.Group4DeepRuneIndex,
                Group4EtcValue = state.Group4EtcValue,
                Group5ArtifactIndex = state.Group5ArtifactIndex,
                Group5WristExtraIndex = state.Group5WristExtraIndex,
                Group5LunariaExtraIndex = state.Group5LunariaExtraIndex,
                Group11SniperIndex = state.Group11SniperIndex,
                Group11GemOptionIndex = state.Group11GemOptionIndex,
                Group11WeaponExtraIndex = state.Group11WeaponExtraIndex,
                Group11TraitValue = state.Group11TraitValue,
                MonsterSelectedIndex = state.MonsterSelectedIndex
            };
        }
    }
}
