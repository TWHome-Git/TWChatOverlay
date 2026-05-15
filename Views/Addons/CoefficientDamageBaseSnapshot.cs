using System;
using System.Collections.Generic;

namespace TWChatOverlay.Views.Addons
{
    /// <summary>
    /// 계수 계산기에서 대미지 계산기로 전달하는 기준 데이터 스냅샷입니다.
    /// </summary>
    public sealed class CoefficientDamageBaseSnapshot
    {
        public static CoefficientDamageBaseSnapshot Empty { get; } = new();

        public bool HasData { get; init; }
        public string CharacterName { get; init; } = string.Empty;
        public string CalculatorTypeName { get; init; } = string.Empty;

        public double PrimaryBaseSum { get; init; }
        public double PrimaryEnchantSum { get; init; }
        public double SecondarySum { get; init; }
        public double SecondaryEnchantSum { get; init; }
        public double TotalPrimarySum { get; init; }
        public double TotalCoefficient { get; init; }

        public IReadOnlyList<CoefficientSlotValueSnapshot> MainSlots { get; init; } = Array.Empty<CoefficientSlotValueSnapshot>();
        public IReadOnlyList<CoefficientSlotValueSnapshot> AccessorySlots { get; init; } = Array.Empty<CoefficientSlotValueSnapshot>();
    }

    /// <summary>
    /// 슬롯별 계수 입력 데이터 스냅샷입니다.
    /// </summary>
    public sealed class CoefficientSlotValueSnapshot
    {
        public string SlotName { get; init; } = string.Empty;
        public string SelectedEquipmentName { get; init; } = string.Empty;

        public double AttackValue { get; init; }
        public double AttackEnchant { get; init; }
        public double DefenseValue { get; init; }
        public double DefenseEnchant { get; init; }
        public double PrimaryStatValue { get; init; }
        public double SecondaryStatValue { get; init; }
        public double Coefficient { get; init; }
    }
}
