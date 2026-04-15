using System.Collections.Generic;

namespace TWChatOverlay.Models
{
    /// <summary>
    /// 캐릭터별 계수 계산기 데이터를 저장하는 모델입니다.
    /// 키: "캐릭터명::타입명" (예: "나야트레이::Stab")
    /// </summary>
    public class CoefficientSaveData
    {
        public Dictionary<string, CoefficientSlotSnapshot[]> Entries { get; set; } = new();
    }

    /// <summary>
    /// 하나의 슬롯에 대한 저장 스냅샷입니다.
    /// </summary>
    public class CoefficientSlotSnapshot
    {
        public string SlotName { get; set; } = "";
        public string? SelectedEquipment { get; set; }
        public double AttackValue { get; set; }
        public double AttackEnchant { get; set; }
        public double DefenseValue { get; set; }
        public double DefenseEnchant { get; set; }
        public double AccessoryValue1 { get; set; }
        public double AccessoryValue2 { get; set; }
        public double TitleValue { get; set; }
        public double CoreValue { get; set; }
        public double PrimaryStatValue { get; set; }
        public double SecondaryStatValue { get; set; }
    }
}
