using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using TWChatOverlay.Models;

namespace TWChatOverlay.Views.Addons
{
    /// <summary>
    /// 장비 상세(재료/총 제작비/비고) 정보를 보여주는 팝업 창입니다.
    /// </summary>
    public partial class EquipmentDetailWindow : Window
    {
        private static readonly Regex InvalidNumberCharsRegex = new(@"[^0-9\.,]", RegexOptions.Compiled);
        private readonly List<MaterialInputRow> _materials;

        public EquipmentDetailWindow(EquipmentModel equipment)
        {
            InitializeComponent();

            DataContext = equipment;
            _materials = (equipment.CraftMaterials ?? new List<EquipmentModel.CraftMaterial>())
                .Select(x => new MaterialInputRow(x))
                .ToList();

            MaterialsControl.ItemsSource = _materials;
            NoteTextBlock.Text = BuildNoteText(equipment);
            ConfigureLayoutForMaterials();

            UpdateTotalCost();
        }

        private void MaterialPriceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                string normalized = NormalizeEokInput(textBox.Text);
                if (!string.Equals(textBox.Text, normalized, StringComparison.Ordinal))
                {
                    int caret = Math.Min(normalized.Length, textBox.CaretIndex);
                    textBox.Text = normalized;
                    textBox.CaretIndex = caret;
                }
            }

            UpdateTotalCost();
        }

        private void MaterialPriceTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is not TextBox textBox)
                return;

            textBox.Text = TryParseEok(textBox.Text, out decimal eok)
                ? eok.ToString("N2", CultureInfo.InvariantCulture)
                : "0.00";
        }

        private void UpdateTotalCost()
        {
            decimal totalEok = 0m;

            foreach (var row in _materials)
            {
                if (TryParseEok(row.UnitPriceText, out decimal unitPriceInEok))
                {
                    totalEok += unitPriceInEok * Math.Max(0, row.Material.Count);
                }
            }

            TotalCostTextBlock.Text = $"총 {totalEok:N2}억 Seed";
        }

        private static bool TryParseEok(string? text, out decimal value)
        {
            value = 0m;

            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = text.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
            if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
                return false;

            value = decimal.Round(value, 2, MidpointRounding.AwayFromZero);
            return true;
        }

        private static string NormalizeEokInput(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            string s = InvalidNumberCharsRegex.Replace(input, string.Empty);
            s = s.Replace(',', '.');

            int dotIdx = s.IndexOf('.');
            if (dotIdx >= 0)
            {
                int secondDot = s.IndexOf('.', dotIdx + 1);
                while (secondDot >= 0)
                {
                    s = s.Remove(secondDot, 1);
                    secondDot = s.IndexOf('.', dotIdx + 1);
                }

                int decimals = s.Length - dotIdx - 1;
                if (decimals > 2)
                    s = s.Substring(0, dotIdx + 3);
            }

            return s;
        }

        private void ConfigureLayoutForMaterials()
        {
            bool hasMaterials = _materials.Count > 0;

            CraftSectionBorder.Visibility = hasMaterials ? Visibility.Visible : Visibility.Collapsed;
            TopSectionRow.Height = hasMaterials ? GridLength.Auto : new GridLength(0);
            SectionGapRow.Height = hasMaterials ? new GridLength(12) : new GridLength(0);
            NoteSectionRow.Height = new GridLength(1, GridUnitType.Star);
        }

        private static string BuildNoteText(EquipmentModel equipment)
        {
            if (!string.IsNullOrWhiteSpace(equipment.Note))
                return equipment.Note;

            var extras = new List<string>();
            if (!string.IsNullOrWhiteSpace(equipment.Requirement))
                extras.Add($"조건: {equipment.Requirement}");
            if (!string.IsNullOrWhiteSpace(equipment.Synthesis))
                extras.Add($"합성: {equipment.Synthesis}");

            return extras.Count > 0
                ? string.Join(Environment.NewLine, extras)
                : "비고 정보가 없습니다.";
        }

        private sealed class MaterialInputRow
        {
            public MaterialInputRow(EquipmentModel.CraftMaterial material)
            {
                Material = material;
            }

            public EquipmentModel.CraftMaterial Material { get; }
            public string UnitPriceText { get; set; } = "0.00";
        }
    }
}
