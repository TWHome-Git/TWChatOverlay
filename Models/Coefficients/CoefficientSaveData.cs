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

        public bool AttackDamage1FactorSnowman { get; set; }

        public bool AttackDamage1FactorIllumi { get; set; }

        public bool AttackDamage1FactorIsabelDamage { get; set; }

        public bool AttackDamage1FactorIsabelSpecial { get; set; }

        public bool AttackDamage1FactorIsabelBattle { get; set; }

        public string AttackDamage1FactorEtcValue { get; set; } = "0";

        public bool AttackDamage2FactorAwakening { get; set; }

        public bool AttackDamage2FactorClubTypeP { get; set; }

        public bool AttackDamage2FactorExplorePoint { get; set; }

        public bool AttackDamage2FactorTwPower { get; set; }

        public bool AttackDamage2FactorHam { get; set; }

        public bool AttackDamage2FactorEvent { get; set; }

        public string AttackDamage2FactorEtcValue { get; set; } = "0";

        public bool AdditionalFactorTitleDamage { get; set; }

        public bool AdditionalFactorFever { get; set; }

        public int AdditionalFactorWeaponAbilityIndex { get; set; }

        public int AdditionalFactorWristAbilityIndex { get; set; }

        public int AdditionalFactorHandAbilityIndex { get; set; }

        public int AdditionalFactorLunariaAbilityIndex { get; set; }

        public int AdditionalFactorDeepRuneIndex { get; set; }

        public string AdditionalFactorEtcValue { get; set; } = "0";

        public int SeriesAttackDamageArtifactIndex { get; set; }

        public int SeriesAttackDamageWristExtraIndex { get; set; }

        public int SeriesAttackDamageLunariaExtraIndex { get; set; }

        public int AdditionalDamageSniperIndex { get; set; }

        public int AdditionalDamageGemOptionIndex { get; set; }

        public int AdditionalDamageWeaponExtraIndex { get; set; }

        public string AdditionalDamageTraitValue { get; set; } = "0";
        public int MonsterSelectedIndex { get; set; }
    }
}
