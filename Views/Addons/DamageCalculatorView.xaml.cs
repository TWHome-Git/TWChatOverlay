using System;
using System.Collections.Generic;
using System.Globalization;
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
            public double TraitAttackDamageValue { get; set; }
            public DamageReferenceData.CharacterModifierEntry? SelectedModifier { get; set; }
            public double TraitEnemyTakenDamagePercent { get; set; }
            public double TraitAdditionalDamagePercent { get; set; }
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

        private readonly AttackDamage1FactorState _attackDamage1FactorState = new();
        private readonly AttackDamage2FactorState _attackDamage2FactorState = new();
        private readonly AdditionalFactorState _additionalFactorState = new();
        private readonly SeriesAttackDamageState _seriesAttackDamageState = new();
        private readonly AdditionalDamageState _additionalDamageState = new();
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
                InitializeMonsterComboBox();
                InitializePercentageComboBoxes();
                SetAnaisVariantSelection("마법");
                WeakPointToggle.IsChecked = false;
                _calc.SkillMultiplier = 0;
                _calc.SkillCriticalMultiplier = 0;
                _calc.HitCount = 1;
                LoadDamageCalculatorState();
                ApplySnapshot(CoefficientDamageBaseSnapshot.Empty);
                RefreshDamageSummaryTexts();
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
            if (TotalCoefficientText != null)
                TotalCoefficientText.Text = Math.Round(finalCoefficient).ToString("F0", CultureInfo.CurrentCulture);
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
            double total = GetAttackDamage1FactorValue() + GetAttackDamage2FactorValue() + GetAttackDamage3FactorValue() + AddtionalFactor();
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

        private string GetAttackDamage1FactorSummary()
        {
            double value = 0;
            if (_attackDamage1FactorState.Snowman) value += 20;
            if (_attackDamage1FactorState.Illumi) value += 10;
            if (_attackDamage1FactorState.IsabelDamage) value += 10;
            if (_attackDamage1FactorState.IsabelSpecial) value += 10;
            if (_attackDamage1FactorState.IsabelBattle) value += 10;
            value += ReadTextValue(_attackDamage1FactorState.EtcValue);
            return $"{value:0.#}%";
        }

        private string GetAttackDamage2FactorSummary()
        {
            double value = 0;
            if (_attackDamage2FactorState.Awakening) value += 5;
            if (_attackDamage2FactorState.ClubTypeP) value += 5;
            if (_attackDamage2FactorState.ExplorePoint) value += 5;
            if (_attackDamage2FactorState.TwPower) value += 5;
            if (_attackDamage2FactorState.Ham) value += 10;
            if (_attackDamage2FactorState.Event) value += 10;
            value += ReadTextValue(_attackDamage2FactorState.EtcValue);
            return $"{value:0.#}%";
        }

        private string GetTraitAttackDamageSummary()
        {
            return $"{_calc.TraitAttackDamageValue:0.#}%";
        }

        private void UpdateTraitAttackDamageSummaryText(string? selectedModifierText)
        {
            if (TraitAttackDamageSummaryText != null)
                TraitAttackDamageSummaryText.Text = $"{_calc.TraitAttackDamageValue:0.#}%";

            if (SelectedModifierText != null)
                SelectedModifierText.Text = selectedModifierText ?? string.Empty;
        }

        private string GetAdditionalFactorSummary()
        {
            double value = 0;
            if (_additionalFactorState.TitleDamage) value += 20;
            value += _additionalFactorState.WeaponAbilityIndex switch { 0 => 0, 1 => 9, 2 => 10, 3 => 11, _ => 0 };
            if (_additionalFactorState.Fever) value += 10;
            value += _additionalFactorState.WristAbilityIndex switch { 0 => 0, 1 => 9, 2 => 10, 3 => 11, _ => 0 };
            value += _additionalFactorState.HandAbilityIndex switch { 0 => 0, 1 => 7, 2 => 8, 3 => 9, _ => 0 };
            value += _additionalFactorState.LunariaAbilityIndex switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5, 6 => 6, 7 => 7, 8 => 8, 9 => 9, 10 => 10, _ => 0 };
            value += _additionalFactorState.DeepRuneIndex switch { 0 => 0, 1 => 3, 2 => 6, 3 => 9, _ => 0 };
            value += ReadTextValue(_additionalFactorState.EtcValue);
            return $"{value:0.#}%";
        }

        private string GetAttackDamageCombinedSummary()
        {
            double total = 0;
            total += ReadTextValue(GetAttackDamage1FactorSummary().TrimEnd('%'));
            total += ReadTextValue(GetAttackDamage2FactorSummary().TrimEnd('%'));
            total += ReadTextValue(GetTraitAttackDamageSummary().TrimEnd('%'));
            total += ReadTextValue(GetAdditionalFactorSummary().TrimEnd('%'));
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

        private readonly record struct DamageRangeResult(
            double InnerMin,
            double InnerMax,
            double MiddleMin,
            double MiddleMax,
            double MinimumDamage,
            double MaximumDamage);

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
                if (MonsterSummaryText != null)
                    MonsterSummaryText.Text = "대미지 -";
                return;
            }

            var normalRange = CalculateDamageRange(entry.Value, 1.0);
            var strongRange = CalculateDamageRange(entry.Value, 0.5);
            var passiveRange = CalculateDamageRange(entry.Value, 0.85);

            IntermediateDamageText.Text = $"1차 INT: {normalRange.InnerMin:N0} / {normalRange.InnerMax:N0}\n2차 INT: {normalRange.MiddleMin:N0} / {normalRange.MiddleMax:N0}";
            MinimumDamageText.Text = $"{normalRange.MinimumDamage:N0}";
            MaximumDamageText.Text = $"{normalRange.MaximumDamage:N0}";
            if (MonsterSummaryText != null)
            {
                MonsterSummaryText.Text =
                    $"일반 대미지 {normalRange.MinimumDamage:N0}~{normalRange.MaximumDamage:N0} | " +
                    $"강타 대미지 {strongRange.MinimumDamage:N0}~{strongRange.MaximumDamage:N0} | " +
                    $"방무 패시브 대미지 {passiveRange.MinimumDamage:N0}~{passiveRange.MaximumDamage:N0}";
            }
        }

        private DamageRangeResult CalculateDamageRange(DamageReferenceData.MonsterEntry entry, double defenseMultiplier)
        {
            double monsterDefense = (entry.StatDefense + entry.FixedDefense) * defenseMultiplier;
            double monsterFixedDamageReduction = entry.FixedDamageReduction;
            double monsterAttributeFactor = GetMonsterAttributeFactor(GetTextBoxValue(ElementValueTextBox), entry.AttributeValue);
            double monsterDamageReductionFactor = GetMonsterDamageReductionFactor(entry.DamageReductionRate);

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

            double minimumDamage = Math.Max(1, Math.Floor(middleMin * attackDamageFactor));
            double maximumDamage = Math.Max(1, Math.Floor(middleMax * attackDamageFactor));

            return new DamageRangeResult(innerMin, innerMax, middleMin, middleMax, minimumDamage, maximumDamage);
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
            double total = GetAttackDamage1FactorValue() + GetAttackDamage2FactorValue() + GetAttackDamage3FactorValue() + AddtionalFactor();
            return 1 + total / 100.0;
        }

        private double GetAttackDamage1FactorValue()
        {
            double value = 0;
            if (_attackDamage1FactorState.Snowman) value += 20;
            if (_attackDamage1FactorState.Illumi) value += 10;
            if (_attackDamage1FactorState.IsabelDamage) value += 10;
            if (_attackDamage1FactorState.IsabelSpecial) value += 10;
            if (_attackDamage1FactorState.IsabelBattle) value += 10;
            return Math.Min(value + ReadTextValue(_attackDamage1FactorState.EtcValue), 50);
        }

        private double GetAttackDamage2FactorValue()
        {
            double value = 0;
            if (_attackDamage2FactorState.Awakening) value += 5;
            if (_attackDamage2FactorState.ClubTypeP) value += 5;
            if (_attackDamage2FactorState.ExplorePoint) value += 5;
            if (_attackDamage2FactorState.TwPower) value += 5;
            if (_attackDamage2FactorState.Ham) value += 10;
            if (_attackDamage2FactorState.Event) value += 10;
            return Math.Min(value + ReadTextValue(_attackDamage2FactorState.EtcValue), 30);
        }

        private double GetAttackDamage3FactorValue()
        {
            return Math.Min(_calc.TraitAttackDamageValue, 65);
        }

        private double AddtionalFactor()
        {
            double value = 0;
            if (_additionalFactorState.TitleDamage) value += 20;
            value += _additionalFactorState.WeaponAbilityIndex switch { 0 => 0, 1 => 9, 2 => 10, 3 => 11, _ => 0 };
            if (_additionalFactorState.Fever) value += 10;
            value += _additionalFactorState.WristAbilityIndex switch { 0 => 0, 1 => 9, 2 => 10, 3 => 11, _ => 0 };
            value += _additionalFactorState.HandAbilityIndex switch { 0 => 0, 1 => 7, 2 => 8, 3 => 9, _ => 0 };
            value += _additionalFactorState.LunariaAbilityIndex switch { 0 => 0, 1 => 1, 2 => 2, 3 => 3, 4 => 4, 5 => 5, 6 => 6, 7 => 7, 8 => 8, 9 => 9, 10 => 10, _ => 0 };
            value += _additionalFactorState.DeepRuneIndex switch { 0 => 0, 1 => 3, 2 => 6, 3 => 9, _ => 0 };
            return Math.Min(value + ReadTextValue(_additionalFactorState.EtcValue), 80);
        }

        private string GetSeriesAttackDamageSummary()
        {
            double value = _seriesAttackDamageState.ArtifactIndex switch { 0 => 15, 1 => 20, 2 => 30, 3 => 35, _ => 15 };
            value += _seriesAttackDamageState.WristExtraIndex switch { 0 => 25, 1 => 26, 2 => 27, 3 => 28, _ => 25 };
            value += _seriesAttackDamageState.LunariaExtraIndex;
            return $"{value:0.#}%";
        }

        private string GetAdditionalDamageSummary()
        {
            double sniper = GetSniperAdditionalDamagePercent(_additionalDamageState.SniperIndex);
            double gem = _additionalDamageState.GemOptionIndex switch { 0 => 0, 1 => 45, 2 => 46, 3 => 47, 4 => 48, _ => 0 };
            double weapon = Math.Clamp(_additionalDamageState.WeaponExtraIndex, 0, 100);
            double trait = ReadTextValue(_additionalDamageState.TraitValue);
            double characterTraitAdditionalDamage = _calc.TraitAdditionalDamagePercent;
            return $"{sniper + gem + weapon + trait + characterTraitAdditionalDamage:0.#}%";
        }

        private string GetFinalDamageSummary()
        {
            double club = ClubFinalDamageToggle?.IsChecked == true ? 5 : 0;
            double core = GetTextBoxValue(CoreSetTextBox);
            double etaFinal = GetEtaLinkFinalPercent(GetComboIndex(EtaLinkFinalDamageComboBox));
            return $"{club + core + etaFinal:0.#}%";
        }

        private string GetCriticalDamageSummary()
        {
            double weakPoint = WeakPointToggle?.IsChecked == true ? 40 : 0;
            double judgement = GetComboIndex(JudgementComboBox) * 0.75;
            double etaCrit = GetEtaLinkCriticalPercent(GetComboIndex(EtaLinkCriticalComboBox));
            return $"{weakPoint + judgement + etaCrit:0.#}%";
        }

        private string GetCurrentTraitDisplayName()
        {
            if (_calc.SelectedModifier == null)
                return "특성 없음";

            return _calc.SelectedModifier.Value.Name;
        }

        private enum AttackDamageSectionKind
        {
            AttackDamage1Factor,
            AttackDamage2Factor,
            TraitAttackDamage,
            AdditionalFactor
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

        private void UpdateAttackDamage2FactorState()
        {
            _attackDamage2FactorState.Awakening = _attackDamage2FactorState.Awakening;
            _attackDamage2FactorState.ClubTypeP = _attackDamage2FactorState.ClubTypeP;
            _attackDamage2FactorState.ExplorePoint = _attackDamage2FactorState.ExplorePoint;
            _attackDamage2FactorState.TwPower = _attackDamage2FactorState.TwPower;
            _attackDamage2FactorState.Ham = _attackDamage2FactorState.Ham;
            _attackDamage2FactorState.Event = _attackDamage2FactorState.Event;
        }

        private void ApplyCharacterModifier(string characterName, string calculatorTypeName)
        {
            if (string.IsNullOrWhiteSpace(characterName))
            {
                _calc.SelectedModifier = null;
                _calc.TraitAttackDamageValue = 0;
                _calc.TraitEnemyTakenDamagePercent = 0;
                _calc.TraitAdditionalDamagePercent = 0;
                AnaisVariantPanel.Visibility = Visibility.Collapsed;
                TraitEnemyTakenDamageValueText.Text = "0";
                TraitStatReductionValueText.Text = "0";
                UpdateTraitAttackDamageSummaryText("특성 값 없음");
                UpdateCharacterTraitAdditionalDamageText();
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
                    SetAnaisVariantSelection(_calc.SelectedAnisVariant);
                }

                resolvedName = isMagicDefense
                    ? "아나이스 비호"
                    : _calc.SelectedAnisVariant == "파괴"
                        ? "아나이스 파괴"
                        : "아나이스 마법";
                if (!isMagicAttack)
                {
                    _calc.SelectedAnisVariant = "비호";
                    SetAnaisVariantSelection(null);
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
                _calc.TraitAttackDamageValue = 0;
                TraitEnemyTakenDamageValueText.Text = "0";
                TraitStatReductionValueText.Text = "0";
                _calc.TraitEnemyTakenDamagePercent = 0;
                _calc.TraitAdditionalDamagePercent = 0;
                UpdateTraitAttackDamageSummaryText("특성 값 없음");
                UpdateCharacterTraitAdditionalDamageText();
                return;
            }

            _calc.TraitEnemyTakenDamagePercent = _calc.SelectedModifier.Value.DamageAmplification;
            TraitEnemyTakenDamageValueText.Text = $"{_calc.TraitEnemyTakenDamagePercent:0.##}%";
            TraitStatReductionValueText.Text = $"{_calc.SelectedModifier.Value.SkillReduction:0.##}%";
            _calc.TraitAdditionalDamagePercent = _calc.SelectedModifier.Value.AdditionalDamage;
            _calc.TraitAttackDamageValue = _calc.SelectedModifier.Value.AttackPower;
            UpdateTraitAttackDamageSummaryText(GetCurrentTraitDisplayName());
            UpdateCharacterTraitAdditionalDamageText();
        }

        private static DamageReferenceData.CharacterModifierEntry? ResolveModifier(string modifierName, bool isAnais, bool isMagicDefense)
        {
            if (string.IsNullOrWhiteSpace(modifierName))
                return null;

            var modifier = DamageReferenceData.CharacterModifierEntries.FirstOrDefault(entry =>
                string.Equals(entry.Name, modifierName, StringComparison.Ordinal));
            return string.IsNullOrWhiteSpace(modifier.Name) ? null : modifier;
        }

        private void AnaisVariantComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRestoringDamageState || sender is not ComboBox)
                return;

            string variant = GetSelectedAnaisVariant();
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

        private void SetAnaisVariantSelection(string? variant)
        {
            if (AnaisVariantComboBox == null)
                return;

            AnaisVariantComboBox.SelectedIndex = string.Equals(variant, "파괴", StringComparison.Ordinal) ? 1 : 0;
        }

        private string GetSelectedAnaisVariant()
        {
            if (AnaisVariantComboBox?.SelectedItem is ComboBoxItem comboItem)
            {
                string selected = comboItem.Content?.ToString() ?? string.Empty;
                if (string.Equals(selected, "파괴", StringComparison.Ordinal))
                    return "파괴";
                if (string.Equals(selected, "마법", StringComparison.Ordinal))
                    return "마법";
            }

            if (AnaisVariantComboBox?.SelectedIndex == 1)
                return "파괴";

            if (AnaisVariantComboBox?.SelectedIndex == 0)
                return "마법";

            return _calc.SelectedAnisVariant;
        }

        private void InitializeMonsterComboBox()
        {
            if (MonsterSelectComboBox == null)
                return;

            MonsterSelectComboBox.ItemsSource = DamageReferenceData.MonsterEntries.Select(entry => entry.Name).ToArray();
            if (MonsterSelectComboBox.Items.Count == 0)
            {
                MonsterSelectComboBox.SelectedIndex = -1;
                return;
            }

            int selectedIndex = Math.Clamp(_monster.SelectedIndex, 0, MonsterSelectComboBox.Items.Count - 1);
            MonsterSelectComboBox.SelectedIndex = selectedIndex;
            _monster.SelectedIndex = selectedIndex;
        }

        private void InitializePercentageComboBoxes()
        {
            PopulatePercentageComboBox(JudgementComboBox, Enumerable.Range(0, 41).Select(GetJudgementPercentText));
            PopulatePercentageComboBox(EtaLinkCriticalComboBox, Enumerable.Range(0, 21).Select(i => $"{GetEtaLinkCriticalPercent(i):0.#}%"));
            PopulatePercentageComboBox(EtaLinkFinalDamageComboBox, Enumerable.Range(0, 6).Select(i => $"{GetEtaLinkFinalPercent(i):0.#}%"));
        }

        private static void PopulatePercentageComboBox(ComboBox? comboBox, IEnumerable<string> items)
        {
            if (comboBox == null)
                return;

            comboBox.Items.Clear();
            foreach (var item in items)
                comboBox.Items.Add(item);
        }

        private static string GetJudgementPercentText(int level)
        {
            double value = Math.Max(0, level) * 0.75;
            return $"{value:0.##}%";
        }

        private void MonsterSelectComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MonsterSelectComboBox == null)
                return;

            int selectedIndex = MonsterSelectComboBox.SelectedIndex;
            _monster.SelectedIndex = selectedIndex < 0 ? 0 : selectedIndex;
            UpdateMonsterSummaryText();
            SaveDamageCalculatorState();
        }

        private void OpenAttackDamage1FactorWindow_Click(object sender, RoutedEventArgs e)
        {
            OpenAttackDamageSettingsWindow();
        }

        private void OpenAttackDamage2FactorWindow_Click(object sender, RoutedEventArgs e)
        {
            OpenAttackDamageSettingsWindow();
        }

        private void OpenAdditionalFactorWindow_Click(object sender, RoutedEventArgs e)
        {
            OpenAttackDamageSettingsWindow();
        }

        private void OpenAttackDamageSettingsWindow()
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

            var attackDamage1FactorPanel = CreateAttackDamageSectionPanel(AttackDamageSectionKind.AttackDamage1Factor, "공격 피해량(이자벨)", "50%", out var attackDamage1FactorSave);
            var attackDamage2FactorPanel = CreateAttackDamageSectionPanel(AttackDamageSectionKind.AttackDamage2Factor, "공격 피해량(일반)", "30%", out var attackDamage2FactorSave);
            var traitAttackDamagePanel = CreateAttackDamageSectionPanel(AttackDamageSectionKind.TraitAttackDamage, "공격 피해량(스킬)", "65%", out var traitAttackDamageSave);
            var additionalFactorPanel = CreateAttackDamageSectionPanel(AttackDamageSectionKind.AdditionalFactor, "공격 피해량(캐릭터)", "80%", out var additionalFactorSave);

            Grid.SetColumn(attackDamage1FactorPanel, 0);
            Grid.SetColumn(attackDamage2FactorPanel, 1);
            Grid.SetColumn(traitAttackDamagePanel, 2);
            Grid.SetColumn(additionalFactorPanel, 3);
            outerGrid.Children.Add(attackDamage1FactorPanel);
            outerGrid.Children.Add(attackDamage2FactorPanel);
            outerGrid.Children.Add(traitAttackDamagePanel);
            outerGrid.Children.Add(additionalFactorPanel);

            window.Closing += (_, _) =>
            {
                attackDamage1FactorSave();
                attackDamage2FactorSave();
                traitAttackDamageSave();
                additionalFactorSave();
                RefreshDamageSummaryTexts();
                SaveDamageCalculatorState();
            };

            window.ShowDialog();
        }

        private Border CreateAttackDamageSectionPanel(AttackDamageSectionKind kind, string displayTitle, string subtitle, out Action saveAction)
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

            void RefreshAttackDamage1FactorSubtitle() => RefreshSubtitle(GetAttackDamage1FactorValue());
            void RefreshAttackDamage2FactorSubtitle() => RefreshSubtitle(GetAttackDamage2FactorValue());
            void RefreshTraitAttackDamageSubtitle() => RefreshSubtitle(GetAttackDamage3FactorValue());
            void RefreshAdditionalFactorSubtitle() => RefreshSubtitle(AddtionalFactor());

            saveAction = () => { };
            if (kind == AttackDamageSectionKind.AttackDamage1Factor)
            {
                var snowmanToggle = CreateToggle("눈사람 (20%)", _attackDamage1FactorState.Snowman);
                var illumiToggle = CreateToggle("일루미 (10%)", _attackDamage1FactorState.Illumi);
                snowmanToggle.Checked += (_, _) => illumiToggle.IsChecked = false;
                illumiToggle.Checked += (_, _) => snowmanToggle.IsChecked = false;
                snowmanToggle.Checked += (_, _) => { _attackDamage1FactorState.Snowman = true; _attackDamage1FactorState.Illumi = false; RefreshAttackDamage1FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                snowmanToggle.Unchecked += (_, _) => { _attackDamage1FactorState.Snowman = false; RefreshAttackDamage1FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                illumiToggle.Checked += (_, _) => { _attackDamage1FactorState.Illumi = true; _attackDamage1FactorState.Snowman = false; RefreshAttackDamage1FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                illumiToggle.Unchecked += (_, _) => { _attackDamage1FactorState.Illumi = false; RefreshAttackDamage1FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                var isabelDamageToggle = CreateToggle("이자벨 대미지 (10%)", _attackDamage1FactorState.IsabelDamage);
                var isabelSpecialToggle = CreateToggle("이자벨 특선 대미지 (10%)", _attackDamage1FactorState.IsabelSpecial);
                var isabelBattleToggle = CreateToggle("이자벨 전투 (10%)", _attackDamage1FactorState.IsabelBattle);
                isabelDamageToggle.Checked += (_, _) => { _attackDamage1FactorState.IsabelDamage = true; RefreshAttackDamage1FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                isabelDamageToggle.Unchecked += (_, _) => { _attackDamage1FactorState.IsabelDamage = false; RefreshAttackDamage1FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                isabelSpecialToggle.Checked += (_, _) => { _attackDamage1FactorState.IsabelSpecial = true; RefreshAttackDamage1FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                isabelSpecialToggle.Unchecked += (_, _) => { _attackDamage1FactorState.IsabelSpecial = false; RefreshAttackDamage1FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                isabelBattleToggle.Checked += (_, _) => { _attackDamage1FactorState.IsabelBattle = true; RefreshAttackDamage1FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                isabelBattleToggle.Unchecked += (_, _) => { _attackDamage1FactorState.IsabelBattle = false; RefreshAttackDamage1FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                panel.Children.Add(CreateLabeledToggleRow("눈사람 (20%)", snowmanToggle));
                panel.Children.Add(CreateLabeledToggleRow("일루미 (10%)", illumiToggle));
                panel.Children.Add(CreateLabeledToggleRow("이자벨 대미지 (10%)", isabelDamageToggle));
                panel.Children.Add(CreateLabeledToggleRow("이자벨 특선 대미지 (10%)", isabelSpecialToggle));
                panel.Children.Add(CreateLabeledToggleRow("이자벨 전투 (10%)", isabelBattleToggle));
                panel.Children.Add(CreateLabeledTextBoxRow("기타", _attackDamage1FactorState.EtcValue, out var etcText));
                etcText.TextChanged += (_, _) => { _attackDamage1FactorState.EtcValue = etcText.Text.Trim(); RefreshAttackDamage1FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                RefreshAttackDamage1FactorSubtitle();
                saveAction = () =>
                {
                    _attackDamage1FactorState.Snowman = snowmanToggle.IsChecked == true;
                    _attackDamage1FactorState.Illumi = illumiToggle.IsChecked == true;
                    _attackDamage1FactorState.IsabelDamage = isabelDamageToggle.IsChecked == true;
                    _attackDamage1FactorState.IsabelSpecial = isabelSpecialToggle.IsChecked == true;
                    _attackDamage1FactorState.IsabelBattle = isabelBattleToggle.IsChecked == true;
                    _attackDamage1FactorState.EtcValue = etcText.Text.Trim();
                    RefreshAttackDamage1FactorSubtitle();
                    UpdateCategorySummaryText();
                };
            }
            else if (kind == AttackDamageSectionKind.AttackDamage2Factor)
            {
                var awakeningToggle = CreateToggle("개각비 (5%)", _attackDamage2FactorState.Awakening);
                var clubTypePToggle = CreateToggle("클럽 Type-P (5%)", _attackDamage2FactorState.ClubTypeP);
                var exploreToggle = CreateToggle("탐험 포인트 공증 (5%)", _attackDamage2FactorState.ExplorePoint);
                var twPowerToggle = CreateToggle("테일즈위버 기운 (5%)", _attackDamage2FactorState.TwPower);
                var hamToggle = CreateToggle("괴력의 햄 (10%)", _attackDamage2FactorState.Ham);
                var eventToggle = CreateToggle("이벤트 (10%)", _attackDamage2FactorState.Event);
                awakeningToggle.Checked += (_, _) => { _attackDamage2FactorState.Awakening = true; RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                awakeningToggle.Unchecked += (_, _) => { _attackDamage2FactorState.Awakening = false; RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                clubTypePToggle.Checked += (_, _) => { _attackDamage2FactorState.ClubTypeP = true; RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                clubTypePToggle.Unchecked += (_, _) => { _attackDamage2FactorState.ClubTypeP = false; RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                exploreToggle.Checked += (_, _) => { _attackDamage2FactorState.ExplorePoint = true; RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                exploreToggle.Unchecked += (_, _) => { _attackDamage2FactorState.ExplorePoint = false; RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                twPowerToggle.Checked += (_, _) => { _attackDamage2FactorState.TwPower = true; RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                twPowerToggle.Unchecked += (_, _) => { _attackDamage2FactorState.TwPower = false; RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                hamToggle.Checked += (_, _) => { _attackDamage2FactorState.Ham = true; RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                hamToggle.Unchecked += (_, _) => { _attackDamage2FactorState.Ham = false; RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                eventToggle.Checked += (_, _) => { _attackDamage2FactorState.Event = true; RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                eventToggle.Unchecked += (_, _) => { _attackDamage2FactorState.Event = false; RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                panel.Children.Add(CreateLabeledToggleRow("개각비 (5%)", awakeningToggle));
                panel.Children.Add(CreateLabeledToggleRow("클럽 Type-P (5%)", clubTypePToggle));
                panel.Children.Add(CreateLabeledToggleRow("탐험 포인트 공증 (5%)", exploreToggle));
                panel.Children.Add(CreateLabeledToggleRow("테일즈위버 기운 (5%)", twPowerToggle));
                panel.Children.Add(CreateLabeledToggleRow("괴력의 햄 (10%)", hamToggle));
                panel.Children.Add(CreateLabeledToggleRow("이벤트 (10%)", eventToggle));
                panel.Children.Add(CreateLabeledTextBoxRow("기타", _attackDamage2FactorState.EtcValue, out var etcText));
                etcText.TextChanged += (_, _) => { _attackDamage2FactorState.EtcValue = etcText.Text.Trim(); RefreshAttackDamage2FactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                RefreshAttackDamage2FactorSubtitle();
                saveAction = () =>
                {
                    _attackDamage2FactorState.Awakening = awakeningToggle.IsChecked == true;
                    _attackDamage2FactorState.ClubTypeP = clubTypePToggle.IsChecked == true;
                    _attackDamage2FactorState.ExplorePoint = exploreToggle.IsChecked == true;
                    _attackDamage2FactorState.TwPower = twPowerToggle.IsChecked == true;
                    _attackDamage2FactorState.Ham = hamToggle.IsChecked == true;
                    _attackDamage2FactorState.Event = eventToggle.IsChecked == true;
                    _attackDamage2FactorState.EtcValue = etcText.Text.Trim();
                    RefreshAttackDamage2FactorSubtitle();
                    UpdateCategorySummaryText();
                };
            }
            else if (kind == AttackDamageSectionKind.TraitAttackDamage)
            {
                var trait = new TextBlock
                {
                    Text = $"{GetCurrentTraitDisplayName()} : {_calc.TraitAttackDamageValue:0.#}%",
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Margin = new Thickness(0, 6, 0, 0),
                    TextAlignment = TextAlignment.Left
                };
                panel.Children.Add(trait);
                RefreshTraitAttackDamageSubtitle();
                saveAction = () => { };
            }
            else
            {
                var titleToggle = CreateToggle("칭호 (20%)", _additionalFactorState.TitleDamage);
                var feverToggle = CreateToggle("피버 (10%)", _additionalFactorState.Fever);
                titleToggle.Checked += (_, _) => { _additionalFactorState.TitleDamage = true; RefreshAdditionalFactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                titleToggle.Unchecked += (_, _) => { _additionalFactorState.TitleDamage = false; RefreshAdditionalFactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                feverToggle.Checked += (_, _) => { _additionalFactorState.Fever = true; RefreshAdditionalFactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                feverToggle.Unchecked += (_, _) => { _additionalFactorState.Fever = false; RefreshAdditionalFactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                panel.Children.Add(CreateLabeledToggleRow("칭호 (20%)", titleToggle));
                panel.Children.Add(CreateLabeledToggleRow("피버 (10%)", feverToggle));
                panel.Children.Add(CreateLabeledComboRow("무기 어빌리티", new[] { "없음 (0%)", "심연 (9%)", "상실 (10%)", "야성 (11%)" }, _additionalFactorState.WeaponAbilityIndex, out var weaponAbilityCombo));
                panel.Children.Add(CreateLabeledComboRow("손목 어빌리티", new[] { "없음 (0%)", "심연 (9%)", "상실 (10%)", "야성 (11%)" }, _additionalFactorState.WristAbilityIndex, out var wristCombo, 100));
                panel.Children.Add(CreateLabeledComboRow("손 어빌리티", new[] { "없음 (0%)", "심연 (7%)", "상실 (8%)", "야성 (9%)" }, _additionalFactorState.HandAbilityIndex, out var handCombo));
                panel.Children.Add(CreateLabeledComboRow("루나리아 어빌리티", new[] { "0%", "1%", "2%", "3%", "4%", "5%", "6%", "7%", "8%", "9%", "10%" }, _additionalFactorState.LunariaAbilityIndex, out var lunariaCombo));
                panel.Children.Add(CreateLabeledComboRow("심화 룬", new[] { "0렙 (0%)", "1렙 (3%)", "2렙 (6%)", "3렙 (9%)" }, _additionalFactorState.DeepRuneIndex, out var deepRuneCombo));
                panel.Children.Add(CreateLabeledTextBoxRow("기타", _additionalFactorState.EtcValue, out var etcText));
                wristCombo.SelectionChanged += (_, _) => { _additionalFactorState.WristAbilityIndex = Math.Max(0, wristCombo.SelectedIndex); RefreshAdditionalFactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                weaponAbilityCombo.SelectionChanged += (_, _) => { _additionalFactorState.WeaponAbilityIndex = Math.Max(0, weaponAbilityCombo.SelectedIndex); RefreshAdditionalFactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                handCombo.SelectionChanged += (_, _) => { _additionalFactorState.HandAbilityIndex = Math.Max(0, handCombo.SelectedIndex); RefreshAdditionalFactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                lunariaCombo.SelectionChanged += (_, _) => { _additionalFactorState.LunariaAbilityIndex = Math.Max(0, lunariaCombo.SelectedIndex); RefreshAdditionalFactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                deepRuneCombo.SelectionChanged += (_, _) => { _additionalFactorState.DeepRuneIndex = Math.Max(0, deepRuneCombo.SelectedIndex); RefreshAdditionalFactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                etcText.TextChanged += (_, _) => { _additionalFactorState.EtcValue = etcText.Text.Trim(); RefreshAdditionalFactorSubtitle(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
                RefreshAdditionalFactorSubtitle();
                saveAction = () =>
                {
                    _additionalFactorState.TitleDamage = titleToggle.IsChecked == true;
                    _additionalFactorState.Fever = feverToggle.IsChecked == true;
                    _additionalFactorState.WristAbilityIndex = Math.Max(0, wristCombo.SelectedIndex);
                    _additionalFactorState.WeaponAbilityIndex = Math.Max(0, weaponAbilityCombo.SelectedIndex);
                    _additionalFactorState.HandAbilityIndex = Math.Max(0, handCombo.SelectedIndex);
                    _additionalFactorState.LunariaAbilityIndex = Math.Max(0, lunariaCombo.SelectedIndex);
                    _additionalFactorState.DeepRuneIndex = Math.Max(0, deepRuneCombo.SelectedIndex);
                    _additionalFactorState.EtcValue = etcText.Text.Trim();
                    RefreshAdditionalFactorSubtitle();
                    UpdateCategorySummaryText();
                };
            }

            return border;
        }

        private void BuildDamageInlineSections()
        {
            BuildAttackDamage1FactorInlineSection();
            BuildAttackDamage2FactorInlineSection();
            BuildAdditionalFactorInlineSection();
            BuildSeriesAttackDamageInlineSection();
            BuildAdditionalDamageInlineSection();
        }

        private void BuildAttackDamage1FactorInlineSection()
        {
            if (AttackDamage1FactorInlineHost == null)
                return;

            AttackDamage1FactorInlineHost.Children.Clear();

            var snowmanToggle = CreateToggle("눈사람 (20%)", _attackDamage1FactorState.Snowman);
            var illumiToggle = CreateToggle("일루미 (10%)", _attackDamage1FactorState.Illumi);
            snowmanToggle.Checked += (_, _) => illumiToggle.IsChecked = false;
            illumiToggle.Checked += (_, _) => snowmanToggle.IsChecked = false;
            snowmanToggle.Checked += (_, _) => { _attackDamage1FactorState.Snowman = true; _attackDamage1FactorState.Illumi = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            snowmanToggle.Unchecked += (_, _) => { _attackDamage1FactorState.Snowman = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            illumiToggle.Checked += (_, _) => { _attackDamage1FactorState.Illumi = true; _attackDamage1FactorState.Snowman = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            illumiToggle.Unchecked += (_, _) => { _attackDamage1FactorState.Illumi = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };

            var isabelDamageToggle = CreateToggle("이자벨 대미지 (10%)", _attackDamage1FactorState.IsabelDamage);
            var isabelSpecialToggle = CreateToggle("이자벨 특선 대미지 (10%)", _attackDamage1FactorState.IsabelSpecial);
            var isabelBattleToggle = CreateToggle("이자벨 전투 (10%)", _attackDamage1FactorState.IsabelBattle);
            isabelDamageToggle.Checked += (_, _) => { _attackDamage1FactorState.IsabelDamage = true; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            isabelDamageToggle.Unchecked += (_, _) => { _attackDamage1FactorState.IsabelDamage = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            isabelSpecialToggle.Checked += (_, _) => { _attackDamage1FactorState.IsabelSpecial = true; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            isabelSpecialToggle.Unchecked += (_, _) => { _attackDamage1FactorState.IsabelSpecial = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            isabelBattleToggle.Checked += (_, _) => { _attackDamage1FactorState.IsabelBattle = true; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            isabelBattleToggle.Unchecked += (_, _) => { _attackDamage1FactorState.IsabelBattle = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };

            AttackDamage1FactorInlineHost.Children.Add(CreateLabeledToggleRow("눈사람 (20%)", snowmanToggle));
            AttackDamage1FactorInlineHost.Children.Add(CreateLabeledToggleRow("일루미 (10%)", illumiToggle));
            AttackDamage1FactorInlineHost.Children.Add(CreateLabeledToggleRow("이자벨 대미지 (10%)", isabelDamageToggle));
            AttackDamage1FactorInlineHost.Children.Add(CreateLabeledToggleRow("이자벨 특선 대미지 (10%)", isabelSpecialToggle));
            AttackDamage1FactorInlineHost.Children.Add(CreateLabeledToggleRow("이자벨 전투 (10%)", isabelBattleToggle));
            AttackDamage1FactorInlineHost.Children.Add(CreateLabeledTextBoxRow("기타", _attackDamage1FactorState.EtcValue, out var etcText));
            etcText.TextChanged += (_, _) => { _attackDamage1FactorState.EtcValue = etcText.Text.Trim(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
        }

        private void BuildAttackDamage2FactorInlineSection()
        {
            if (AttackDamage2FactorInlineHost == null)
                return;

            AttackDamage2FactorInlineHost.Children.Clear();

            var awakeningToggle = CreateToggle("개각비 (5%)", _attackDamage2FactorState.Awakening);
            var clubTypePToggle = CreateToggle("클럽 Type-P (5%)", _attackDamage2FactorState.ClubTypeP);
            var exploreToggle = CreateToggle("탐험 포인트 공증 (5%)", _attackDamage2FactorState.ExplorePoint);
            var twPowerToggle = CreateToggle("테일즈위버 기운 (5%)", _attackDamage2FactorState.TwPower);
            var hamToggle = CreateToggle("괴력의 햄 (10%)", _attackDamage2FactorState.Ham);
            var eventToggle = CreateToggle("이벤트 (10%)", _attackDamage2FactorState.Event);
            awakeningToggle.Checked += (_, _) => { _attackDamage2FactorState.Awakening = true; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            awakeningToggle.Unchecked += (_, _) => { _attackDamage2FactorState.Awakening = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            clubTypePToggle.Checked += (_, _) => { _attackDamage2FactorState.ClubTypeP = true; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            clubTypePToggle.Unchecked += (_, _) => { _attackDamage2FactorState.ClubTypeP = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            exploreToggle.Checked += (_, _) => { _attackDamage2FactorState.ExplorePoint = true; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            exploreToggle.Unchecked += (_, _) => { _attackDamage2FactorState.ExplorePoint = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            twPowerToggle.Checked += (_, _) => { _attackDamage2FactorState.TwPower = true; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            twPowerToggle.Unchecked += (_, _) => { _attackDamage2FactorState.TwPower = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            hamToggle.Checked += (_, _) => { _attackDamage2FactorState.Ham = true; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            hamToggle.Unchecked += (_, _) => { _attackDamage2FactorState.Ham = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            eventToggle.Checked += (_, _) => { _attackDamage2FactorState.Event = true; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            eventToggle.Unchecked += (_, _) => { _attackDamage2FactorState.Event = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };

            AttackDamage2FactorInlineHost.Children.Add(CreateLabeledToggleRow("개각비 (5%)", awakeningToggle));
            AttackDamage2FactorInlineHost.Children.Add(CreateLabeledToggleRow("클럽 Type-P (5%)", clubTypePToggle));
            AttackDamage2FactorInlineHost.Children.Add(CreateLabeledToggleRow("탐험 포인트 공증 (5%)", exploreToggle));
            AttackDamage2FactorInlineHost.Children.Add(CreateLabeledToggleRow("테일즈위버 기운 (5%)", twPowerToggle));
            AttackDamage2FactorInlineHost.Children.Add(CreateLabeledToggleRow("괴력의 햄 (10%)", hamToggle));
            AttackDamage2FactorInlineHost.Children.Add(CreateLabeledToggleRow("이벤트 (10%)", eventToggle));
            AttackDamage2FactorInlineHost.Children.Add(CreateLabeledTextBoxRow("기타", _attackDamage2FactorState.EtcValue, out var etcText));
            etcText.TextChanged += (_, _) => { _attackDamage2FactorState.EtcValue = etcText.Text.Trim(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
        }

        private void BuildAdditionalFactorInlineSection()
        {
            if (AdditionalFactorInlineHost == null)
                return;

            AdditionalFactorInlineHost.Children.Clear();

            var titleToggle = CreateToggle("칭호 (20%)", _additionalFactorState.TitleDamage);
            var feverToggle = CreateToggle("피버 (10%)", _additionalFactorState.Fever);
            titleToggle.Checked += (_, _) => { _additionalFactorState.TitleDamage = true; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            titleToggle.Unchecked += (_, _) => { _additionalFactorState.TitleDamage = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            feverToggle.Checked += (_, _) => { _additionalFactorState.Fever = true; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            feverToggle.Unchecked += (_, _) => { _additionalFactorState.Fever = false; RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };

            AdditionalFactorInlineHost.Children.Add(CreateLabeledToggleRow("칭호 (20%)", titleToggle));
            AdditionalFactorInlineHost.Children.Add(CreateLabeledToggleRow("피버 (10%)", feverToggle));
            AdditionalFactorInlineHost.Children.Add(CreateLabeledComboRow("무기 어빌리티", new[] { "없음 (0%)", "심연 (9%)", "상실 (10%)", "야성 (11%)" }, _additionalFactorState.WeaponAbilityIndex, out var weaponAbilityCombo));
            AdditionalFactorInlineHost.Children.Add(CreateLabeledComboRow("손목 어빌리티", new[] { "없음 (0%)", "심연 (9%)", "상실 (10%)", "야성 (11%)" }, _additionalFactorState.WristAbilityIndex, out var wristCombo));
            AdditionalFactorInlineHost.Children.Add(CreateLabeledComboRow("손 어빌리티", new[] { "없음 (0%)", "심연 (7%)", "상실 (8%)", "야성 (9%)" }, _additionalFactorState.HandAbilityIndex, out var handCombo));
            AdditionalFactorInlineHost.Children.Add(CreateLabeledComboRow("루나리아 어빌리티", new[] { "0%", "1%", "2%", "3%", "4%", "5%", "6%", "7%", "8%", "9%", "10%" }, _additionalFactorState.LunariaAbilityIndex, out var lunariaCombo));
            AdditionalFactorInlineHost.Children.Add(CreateLabeledComboRow("심화 룬", new[] { "0렙 (0%)", "1렙 (3%)", "2렙 (6%)", "3렙 (9%)" }, _additionalFactorState.DeepRuneIndex, out var deepRuneCombo));
            AdditionalFactorInlineHost.Children.Add(CreateLabeledTextBoxRow("기타", _additionalFactorState.EtcValue, out var etcText));

            wristCombo.SelectionChanged += (_, _) => { _additionalFactorState.WristAbilityIndex = Math.Max(0, wristCombo.SelectedIndex); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            weaponAbilityCombo.SelectionChanged += (_, _) => { _additionalFactorState.WeaponAbilityIndex = Math.Max(0, weaponAbilityCombo.SelectedIndex); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            handCombo.SelectionChanged += (_, _) => { _additionalFactorState.HandAbilityIndex = Math.Max(0, handCombo.SelectedIndex); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            lunariaCombo.SelectionChanged += (_, _) => { _additionalFactorState.LunariaAbilityIndex = Math.Max(0, lunariaCombo.SelectedIndex); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            deepRuneCombo.SelectionChanged += (_, _) => { _additionalFactorState.DeepRuneIndex = Math.Max(0, deepRuneCombo.SelectedIndex); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            etcText.TextChanged += (_, _) => { _additionalFactorState.EtcValue = etcText.Text.Trim(); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
        }

        private void BuildSeriesAttackDamageInlineSection()
        {
            if (SeriesAttackDamageInlineHost == null)
                return;

            SeriesAttackDamageInlineHost.Children.Clear();
            SeriesAttackDamageInlineHost.Children.Add(CreateLabeledCombo("아티팩트", new[] { "프시키 (15%)", "아크론 (20%)", "이클립스 (30%)", "에테리얼 (35%)" }, _seriesAttackDamageState.ArtifactIndex, out var artifactCombo));
            SeriesAttackDamageInlineHost.Children.Add(CreateLabeledCombo("손목 부가", new[] { "25%", "26%", "27%", "28%" }, _seriesAttackDamageState.WristExtraIndex, out var wristCombo));
            SeriesAttackDamageInlineHost.Children.Add(CreateLabeledCombo("루나리아 부가", new[] { "0%", "1%", "2%", "3%", "4%", "5%", "6%", "7%", "8%", "9%", "10%" }, _seriesAttackDamageState.LunariaExtraIndex, out var lunariaCombo));

            artifactCombo.SelectionChanged += (_, _) => { _seriesAttackDamageState.ArtifactIndex = Math.Max(0, artifactCombo.SelectedIndex); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            wristCombo.SelectionChanged += (_, _) => { _seriesAttackDamageState.WristExtraIndex = Math.Max(0, wristCombo.SelectedIndex); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            lunariaCombo.SelectionChanged += (_, _) => { _seriesAttackDamageState.LunariaExtraIndex = Math.Max(0, lunariaCombo.SelectedIndex); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
        }

        private void BuildAdditionalDamageInlineSection()
        {
            if (AdditionalDamageInlineHost == null)
                return;

            AdditionalDamageInlineHost.Children.Clear();
            AdditionalDamageInlineHost.Children.Add(CreateLabeledCombo("저격 연마", new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" }, _additionalDamageState.SniperIndex, out var sniperCombo));
            AdditionalDamageInlineHost.Children.Add(CreateLabeledCombo("장비 강화석 옵션", new[] { "0%", "45%", "46%", "47%", "48%" }, _additionalDamageState.GemOptionIndex, out var gemCombo));
            AdditionalDamageInlineHost.Children.Add(CreateLabeledTextBoxRow("무기 부가", _additionalDamageState.WeaponExtraIndex.ToString(CultureInfo.CurrentCulture), out var weaponText));

            sniperCombo.SelectionChanged += (_, _) => { _additionalDamageState.SniperIndex = Math.Max(0, sniperCombo.SelectedIndex); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            gemCombo.SelectionChanged += (_, _) => { _additionalDamageState.GemOptionIndex = Math.Max(0, gemCombo.SelectedIndex); RefreshDamageSummaryTexts(); UpdateCategorySummaryText(); };
            weaponText.PreviewTextInput += NumberTextBox_PreviewTextInput;
            weaponText.PreviewKeyDown += NumberTextBox_PreviewKeyDown;
            DataObject.AddPastingHandler(weaponText, NumberTextBox_OnPaste);
            weaponText.LostFocus += (_, _) =>
            {
                if (!double.TryParse(weaponText.Text, out double value))
                    value = 0;

                weaponText.Text = Math.Clamp(value, 0, 100).ToString("0", CultureInfo.CurrentCulture);
                _additionalDamageState.WeaponExtraIndex = (int)Math.Clamp(value, 0, 100);
                RefreshDamageSummaryTexts();
                UpdateCategorySummaryText();
            };
        }

        private void OpenSeriesAttackDamageWindow_Click(object sender, RoutedEventArgs e)
        {
            var window = CreateSettingsWindow("계열 공격력 설정 (상한 73%)");
            var panel = CreateWindowRootPanel(window);

            panel.Children.Add(CreateLabeledCombo("아티팩트", new[] { "프시키 (15%)", "아크론 (20%)", "이클립스 (30%)", "에테리얼 (35%)" }, _seriesAttackDamageState.ArtifactIndex, out var artifactCombo));
            panel.Children.Add(CreateLabeledCombo("손목 부가", new[] { "25%", "26%", "27%", "28%" }, _seriesAttackDamageState.WristExtraIndex, out var wristCombo));
            panel.Children.Add(CreateLabeledCombo("루나리아 부가", new[] { "0%", "1%", "2%", "3%", "4%", "5%", "6%", "7%", "8%", "9%", "10%" }, _seriesAttackDamageState.LunariaExtraIndex, out var lunariaCombo));

            AddSaveButton(panel, () =>
            {
                _seriesAttackDamageState.ArtifactIndex = Math.Max(0, artifactCombo.SelectedIndex);
                _seriesAttackDamageState.WristExtraIndex = Math.Max(0, wristCombo.SelectedIndex);
                _seriesAttackDamageState.LunariaExtraIndex = Math.Max(0, lunariaCombo.SelectedIndex);
                RefreshDamageSummaryTexts();
                UpdateCategorySummaryText();
                window.DialogResult = true;
                window.Close();
            });

            window.Closing += (_, _) =>
            {
                _seriesAttackDamageState.ArtifactIndex = Math.Max(0, artifactCombo.SelectedIndex);
                _seriesAttackDamageState.WristExtraIndex = Math.Max(0, wristCombo.SelectedIndex);
                _seriesAttackDamageState.LunariaExtraIndex = Math.Max(0, lunariaCombo.SelectedIndex);
                RefreshDamageSummaryTexts();
                SaveDamageCalculatorState();
            };

            window.ShowDialog();
        }

        private void OpenAdditionalDamageWindow_Click(object sender, RoutedEventArgs e)
        {
            var window = CreateSettingsWindow("추가 피해량 설정");
            var panel = CreateWindowRootPanel(window);

            panel.Children.Add(CreateLabeledCombo("저격 연마", new[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" }, _additionalDamageState.SniperIndex, out var sniperCombo));
            panel.Children.Add(CreateLabeledCombo("장비 강화석 옵션", new[] { "0%", "45%", "46%", "47%", "48%" }, _additionalDamageState.GemOptionIndex, out var gemCombo));
            panel.Children.Add(CreateLabeledTextBoxRow("무기 부가", _additionalDamageState.WeaponExtraIndex.ToString(CultureInfo.CurrentCulture), out var weaponText));
            panel.Children.Add(CreateLabeledReadOnlyValue("캐릭터 고유 값", _additionalDamageState.TraitValue));

            AddSaveButton(panel, () =>
            {
                _additionalDamageState.SniperIndex = Math.Max(0, sniperCombo.SelectedIndex);
                _additionalDamageState.GemOptionIndex = Math.Max(0, gemCombo.SelectedIndex);
                if (!double.TryParse(weaponText.Text, out double weaponValue))
                    weaponValue = 0;
                _additionalDamageState.WeaponExtraIndex = (int)Math.Clamp(weaponValue, 0, 100);
                RefreshDamageSummaryTexts();
                UpdateCategorySummaryText();
                window.DialogResult = true;
                window.Close();
            });

            window.Closing += (_, _) =>
            {
                _additionalDamageState.SniperIndex = Math.Max(0, sniperCombo.SelectedIndex);
                _additionalDamageState.GemOptionIndex = Math.Max(0, gemCombo.SelectedIndex);
                if (!double.TryParse(weaponText.Text, out double weaponValue))
                    weaponValue = 0;
                _additionalDamageState.WeaponExtraIndex = (int)Math.Clamp(weaponValue, 0, 100);
                RefreshDamageSummaryTexts();
                SaveDamageCalculatorState();
            };

            window.ShowDialog();
        }

        private void RefreshDamageSummaryTexts()
        {
            AttackDamage1FactorSummaryText.Text = $"{GetAttackDamage1FactorValue():0.#}% / 50%";
            AttackDamage2FactorSummaryText.Text = $"{GetAttackDamage2FactorValue():0.#}% / 30%";
            AdditionalFactorSummaryText.Text = $"{AddtionalFactor():0.#}% / 80%";
            if (SeriesAttackDamageSummaryText != null)
                SeriesAttackDamageSummaryText.Text = $"계열 공격력 {GetSeriesAttackDamageSummary()} / 73%";
            if (AdditionalDamageSummaryText != null)
                AdditionalDamageSummaryText.Text = $"추가 피해량 {GetAdditionalDamageSummary()}";
            if (AdditionalDamageTraitValueText != null)
                AdditionalDamageTraitValueText.Text = GetCharacterTraitAdditionalDamageText();
            if (FinalDamageSummaryText != null)
                FinalDamageSummaryText.Text = $"최종 대미지 {GetFinalDamageSummary()}";
            if (CriticalDamageSummaryText != null)
                CriticalDamageSummaryText.Text = $"치명타 배율 {GetCriticalDamageSummary()}";
            UpdateMonsterSummaryText();
        }

        private void UpdateCharacterTraitAdditionalDamageText()
        {
            if (AdditionalDamageTraitValueText == null)
                return;

            AdditionalDamageTraitValueText.Text = GetCharacterTraitAdditionalDamageText();
        }

        private string GetCharacterTraitAdditionalDamageText()
        {
            if (_calc.SelectedModifier == null)
                return "0%";

            return $"{_calc.SelectedModifier.Value.AdditionalDamage:0.#}%";
        }

        private void UpdateMonsterSummaryText()
        {
            if (MonsterSelectComboBox != null && MonsterSelectComboBox.Items.Count > 0)
            {
                int selectedIndex = Math.Clamp(_monster.SelectedIndex, 0, MonsterSelectComboBox.Items.Count - 1);
                if (MonsterSelectComboBox.SelectedIndex != selectedIndex)
                    MonsterSelectComboBox.SelectedIndex = selectedIndex;
            }

            if (MonsterSummaryText == null)
                return;

            UpdateDamageRangeText();
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
            if (WeakPointToggle == null)
                return;

            SaveDamageCalculatorState();
        }

        private void UpdateJudgementUi()
        {
            if (JudgementComboBox == null)
                return;
        }

        private void UpdateEtaLinkCriticalUi()
        {
            if (EtaLinkCriticalComboBox == null)
                return;
        }

        private void UpdateEtaLinkFinalDamageUi()
        {
            if (EtaLinkFinalDamageComboBox == null)
                return;
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
                Owner = Application.Current?.MainWindow,
                Topmost = true,
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
            checkBox.LayoutTransform = new System.Windows.Media.ScaleTransform(0.75, 0.75);
            return checkBox;
        }

        private static Border CreateToggleRow(string label, bool isChecked, out CheckBox checkBox)
        {
            var grid = new Grid
            {
                Margin = new Thickness(0, 0, 0, 1)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

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
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });

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
                Width = 50,
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

        private static Border CreateLabeledComboRow(string label, string[] options, int selectedIndex, out ComboBox comboBox, double comboWidth = 124)
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

        private static Border CreateLabeledCombo(string label, string[] options, int selectedIndex, out ComboBox comboBox, double comboWidth = 124)
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

        private sealed class AttackDamage1FactorState
        {
            public bool Snowman { get; set; }
            public bool Illumi { get; set; }
            public bool IsabelDamage { get; set; }
            public bool IsabelSpecial { get; set; }
            public bool IsabelBattle { get; set; }
            public string EtcValue { get; set; } = "0";
        }

        private sealed class AttackDamage2FactorState
        {
            public bool Awakening { get; set; }
            public bool ClubTypeP { get; set; }
            public bool ExplorePoint { get; set; }
            public bool TwPower { get; set; }
            public bool Ham { get; set; }
            public bool Event { get; set; }
            public string EtcValue { get; set; } = "0";
        }

        private sealed class AdditionalFactorState
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

        private sealed class SeriesAttackDamageState
        {
            public int ArtifactIndex { get; set; }
            public int WristExtraIndex { get; set; }
            public int LunariaExtraIndex { get; set; }
        }

        private sealed class AdditionalDamageState
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
            RefreshDamageSummaryTexts();
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
            RefreshDamageSummaryTexts();
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void EtaLinkCriticalComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateEtaLinkCriticalUi();
            RefreshDamageSummaryTexts();
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void EtaLinkFinalDamageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateEtaLinkFinalDamageUi();
            RefreshDamageSummaryTexts();
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void ComboBonusToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (ComboBonusToggle == null)
                return;

            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void FinalDamageOption_Changed(object sender, RoutedEventArgs e)
        {
            RefreshDamageSummaryTexts();
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void CategoryToggle_Changed(object sender, RoutedEventArgs e)
        {
            RefreshDamageSummaryTexts();
            UpdateCategorySummaryText();
            SaveDamageCalculatorState();
        }

        private void SienaTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshDamageSummaryTexts();
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
                if (SpecialDamageReductionTextBoxControl != null)
                    SpecialDamageReductionTextBoxControl.Text = Math.Clamp(state.SpecialDamageReductionRate, 0, 50).ToString("0", CultureInfo.CurrentCulture);

                _calc.SelectedAnisVariant = state.SelectedAnaisVariant;
                SetAnaisVariantSelection(_calc.SelectedAnisVariant);
                _attackDamage1FactorState.Snowman = state.AttackDamage1FactorSnowman;
                _attackDamage1FactorState.Illumi = state.AttackDamage1FactorIllumi;
                _attackDamage1FactorState.IsabelDamage = state.AttackDamage1FactorIsabelDamage;
                _attackDamage1FactorState.IsabelSpecial = state.AttackDamage1FactorIsabelSpecial;
                _attackDamage1FactorState.IsabelBattle = state.AttackDamage1FactorIsabelBattle;
                _attackDamage1FactorState.EtcValue = state.AttackDamage1FactorEtcValue;

                _attackDamage2FactorState.Awakening = state.AttackDamage2FactorAwakening;
                _attackDamage2FactorState.ClubTypeP = state.AttackDamage2FactorClubTypeP;
                _attackDamage2FactorState.ExplorePoint = state.AttackDamage2FactorExplorePoint;
                _attackDamage2FactorState.TwPower = state.AttackDamage2FactorTwPower;
                _attackDamage2FactorState.Ham = state.AttackDamage2FactorHam;
                _attackDamage2FactorState.Event = state.AttackDamage2FactorEvent;
                _attackDamage2FactorState.EtcValue = state.AttackDamage2FactorEtcValue;

                _additionalFactorState.TitleDamage = state.AdditionalFactorTitleDamage;
                _additionalFactorState.Fever = state.AdditionalFactorFever;
                _additionalFactorState.WeaponAbilityIndex = state.AdditionalFactorWeaponAbilityIndex;
                _additionalFactorState.WristAbilityIndex = state.AdditionalFactorWristAbilityIndex;
                _additionalFactorState.HandAbilityIndex = state.AdditionalFactorHandAbilityIndex;
                _additionalFactorState.LunariaAbilityIndex = state.AdditionalFactorLunariaAbilityIndex;
                _additionalFactorState.DeepRuneIndex = state.AdditionalFactorDeepRuneIndex;
                _additionalFactorState.EtcValue = state.AdditionalFactorEtcValue;

                _seriesAttackDamageState.ArtifactIndex = state.SeriesAttackDamageArtifactIndex;
                _seriesAttackDamageState.WristExtraIndex = state.SeriesAttackDamageWristExtraIndex;
                _seriesAttackDamageState.LunariaExtraIndex = state.SeriesAttackDamageLunariaExtraIndex;

                _additionalDamageState.SniperIndex = state.AdditionalDamageSniperIndex;
                _additionalDamageState.GemOptionIndex = state.AdditionalDamageGemOptionIndex;
                _additionalDamageState.WeaponExtraIndex = state.AdditionalDamageWeaponExtraIndex;
                _additionalDamageState.TraitValue = state.AdditionalDamageTraitValue;
                _monster.SelectedIndex = state.MonsterSelectedIndex;
                InitializeMonsterComboBox();
                BuildDamageInlineSections();

                UpdateWeakPointUi();
                UpdateJudgementUi();
                UpdateEtaLinkCriticalUi();
                UpdateEtaLinkFinalDamageUi();
                UpdateEtaUi();
                RefreshDamageSummaryTexts();
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

            state.AttackDamage1FactorSnowman = _attackDamage1FactorState.Snowman;
            state.AttackDamage1FactorIllumi = _attackDamage1FactorState.Illumi;
            state.AttackDamage1FactorIsabelDamage = _attackDamage1FactorState.IsabelDamage;
            state.AttackDamage1FactorIsabelSpecial = _attackDamage1FactorState.IsabelSpecial;
            state.AttackDamage1FactorIsabelBattle = _attackDamage1FactorState.IsabelBattle;
            state.AttackDamage1FactorEtcValue = _attackDamage1FactorState.EtcValue;

            state.AttackDamage2FactorAwakening = _attackDamage2FactorState.Awakening;
            state.AttackDamage2FactorClubTypeP = _attackDamage2FactorState.ClubTypeP;
            state.AttackDamage2FactorExplorePoint = _attackDamage2FactorState.ExplorePoint;
            state.AttackDamage2FactorTwPower = _attackDamage2FactorState.TwPower;
            state.AttackDamage2FactorHam = _attackDamage2FactorState.Ham;
            state.AttackDamage2FactorEvent = _attackDamage2FactorState.Event;
            state.AttackDamage2FactorEtcValue = _attackDamage2FactorState.EtcValue;

            state.AdditionalFactorTitleDamage = _additionalFactorState.TitleDamage;
            state.AdditionalFactorFever = _additionalFactorState.Fever;
            state.AdditionalFactorWeaponAbilityIndex = _additionalFactorState.WeaponAbilityIndex;
            state.AdditionalFactorWristAbilityIndex = _additionalFactorState.WristAbilityIndex;
            state.AdditionalFactorHandAbilityIndex = _additionalFactorState.HandAbilityIndex;
            state.AdditionalFactorLunariaAbilityIndex = _additionalFactorState.LunariaAbilityIndex;
            state.AdditionalFactorDeepRuneIndex = _additionalFactorState.DeepRuneIndex;
            state.AdditionalFactorEtcValue = _additionalFactorState.EtcValue;

            state.SeriesAttackDamageArtifactIndex = _seriesAttackDamageState.ArtifactIndex;
            state.SeriesAttackDamageWristExtraIndex = _seriesAttackDamageState.WristExtraIndex;
            state.SeriesAttackDamageLunariaExtraIndex = _seriesAttackDamageState.LunariaExtraIndex;

            state.AdditionalDamageSniperIndex = _additionalDamageState.SniperIndex;
            state.AdditionalDamageGemOptionIndex = _additionalDamageState.GemOptionIndex;
            state.AdditionalDamageWeaponExtraIndex = _additionalDamageState.WeaponExtraIndex;
            state.AdditionalDamageTraitValue = _additionalDamageState.TraitValue;
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
                AttackDamage1FactorSnowman = state.AttackDamage1FactorSnowman,
                AttackDamage1FactorIllumi = state.AttackDamage1FactorIllumi,
                AttackDamage1FactorIsabelDamage = state.AttackDamage1FactorIsabelDamage,
                AttackDamage1FactorIsabelSpecial = state.AttackDamage1FactorIsabelSpecial,
                AttackDamage1FactorIsabelBattle = state.AttackDamage1FactorIsabelBattle,
                AttackDamage1FactorEtcValue = state.AttackDamage1FactorEtcValue,
                AttackDamage2FactorAwakening = state.AttackDamage2FactorAwakening,
                AttackDamage2FactorClubTypeP = state.AttackDamage2FactorClubTypeP,
                AttackDamage2FactorExplorePoint = state.AttackDamage2FactorExplorePoint,
                AttackDamage2FactorTwPower = state.AttackDamage2FactorTwPower,
                AttackDamage2FactorHam = state.AttackDamage2FactorHam,
                AttackDamage2FactorEvent = state.AttackDamage2FactorEvent,
                AttackDamage2FactorEtcValue = state.AttackDamage2FactorEtcValue,
                AdditionalFactorTitleDamage = state.AdditionalFactorTitleDamage,
                AdditionalFactorFever = state.AdditionalFactorFever,
                AdditionalFactorWeaponAbilityIndex = state.AdditionalFactorWeaponAbilityIndex,
                AdditionalFactorWristAbilityIndex = state.AdditionalFactorWristAbilityIndex,
                AdditionalFactorHandAbilityIndex = state.AdditionalFactorHandAbilityIndex,
                AdditionalFactorLunariaAbilityIndex = state.AdditionalFactorLunariaAbilityIndex,
                AdditionalFactorDeepRuneIndex = state.AdditionalFactorDeepRuneIndex,
                AdditionalFactorEtcValue = state.AdditionalFactorEtcValue,
                SeriesAttackDamageArtifactIndex = state.SeriesAttackDamageArtifactIndex,
                SeriesAttackDamageWristExtraIndex = state.SeriesAttackDamageWristExtraIndex,
                SeriesAttackDamageLunariaExtraIndex = state.SeriesAttackDamageLunariaExtraIndex,
                AdditionalDamageSniperIndex = state.AdditionalDamageSniperIndex,
                AdditionalDamageGemOptionIndex = state.AdditionalDamageGemOptionIndex,
                AdditionalDamageWeaponExtraIndex = state.AdditionalDamageWeaponExtraIndex,
                AdditionalDamageTraitValue = state.AdditionalDamageTraitValue,
                MonsterSelectedIndex = state.MonsterSelectedIndex
            };
        }
    }
}
