using System.Collections.Generic;

namespace TWChatOverlay.Models
{
    /// <summary>
    /// 캐릭터별 계수 계산기 데이터를 저장하는 모델입니다.
    /// 키: "캐릭터명::타입명" (예: "나야트레이::Stab")
    /// </summary>
    public class CoefficientSaveData
    {
        public string? LastSelectedCharacterName { get; set; }
        public string? LastSelectedCalculatorType { get; set; }
        public double LastDexValue { get; set; }
        public Dictionary<string, DamageCalculatorSaveState> DamageCalculatorEntries { get; set; } = new();
        public Dictionary<string, CoefficientSlotSnapshot[]> Entries { get; set; } = new();
        public Dictionary<string, AvatarEnhancementSnapshot> AvatarEnhancementEntries { get; set; } = new();
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

    /// <summary>
    /// 아바타 강화 토글 상태 저장 스냅샷입니다.
    /// </summary>
    public class AvatarEnhancementSnapshot
    {
        public bool MainEnhanceEnabled { get; set; }
        public bool SubEnhanceEnabled { get; set; }
    }

    /// <summary>
    /// 대미지 계산기 화면의 저장 상태입니다.
    /// </summary>
    public class DamageCalculatorSaveState
    {
        public bool HelmetAbilityEnabled { get; set; }
        public double WeaponAdditionalDamage { get; set; }
        public double SkillMultiplier { get; set; }
        public double CriticalMultiplier { get; set; }
        public double HitCount { get; set; } = 1;
        public double EtaLevel { get; set; } = 1;
        public double EtaAwakeningDamageIncrease { get; set; }
        public double ElementValue { get; set; } = 210;

        public bool WeakPointEnabled { get; set; }
        public int JudgementIndex { get; set; }
        public int EtaLinkCriticalLevel { get; set; }
        public bool ClubFinalDamageEnabled { get; set; }
        public int CoreSetValue { get; set; }
        public int EtaLinkFinalDamageLevel { get; set; }
        public double SienaValue { get; set; }
        public bool EnemyTakenDamageWeaponEnabled { get; set; }
        public bool ComboBonusEnabled { get; set; }
        public double SpecialDamageReductionRate { get; set; }

        public string SelectedAnaisVariant { get; set; } = "마법";
        public bool Group1Snowman { get; set; }
        public bool Group1Illumi { get; set; }
        public bool Group1IsabelDamage { get; set; }
        public bool Group1IsabelSpecial { get; set; }
        public bool Group1IsabelBattle { get; set; }
        public string Group1EtcValue { get; set; } = "0";

        public bool Group2Gaegakbi { get; set; }
        public bool Group2ClubTypeP { get; set; }
        public bool Group2ExplorePoint { get; set; }
        public bool Group2TwPower { get; set; }
        public bool Group2Ham { get; set; }
        public bool Group2Event { get; set; }
        public string Group2EtcValue { get; set; } = "0";

        public bool Group4TitleDamage { get; set; }
        public bool Group4Fever { get; set; }
        public int Group4WeaponAbilityIndex { get; set; }
        public int Group4WristAbilityIndex { get; set; }
        public int Group4HandAbilityIndex { get; set; }
        public int Group4LunariaAbilityIndex { get; set; }
        public int Group4DeepRuneIndex { get; set; }
        public string Group4EtcValue { get; set; } = "0";

        public int Group5ArtifactIndex { get; set; }
        public int Group5WristExtraIndex { get; set; }
        public int Group5LunariaExtraIndex { get; set; }

        public int Group11SniperIndex { get; set; }
        public int Group11GemOptionIndex { get; set; }
        public int Group11WeaponExtraIndex { get; set; }
        public string Group11TraitValue { get; set; } = "0";
        public int MonsterSelectedIndex { get; set; }
    }
}
