using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 캐릭터별 계수 계산기 데이터를 JSON 파일로 저장하고 불러오는 기능
    /// </summary>
    public static class CoefficientDataService
    {
        private static readonly string FilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "coefficient_data.json");
        private static readonly JsonSerializerOptions _options = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
        };

        private static CoefficientSaveData? _cache;

        /// <summary>
        /// 전체 데이터를 파일로 저장
        /// </summary>
        public static void Save(CoefficientSaveData data)
        {
            if (data == null) return;

            try
            {
                ApplyCurrentAliasesToLegacyFields(data);

                _cache = data;
                string json = JsonSerializer.Serialize(data, _options);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"계수 데이터 저장 중 오류 발생: {ex.Message}");
            }
        }

        /// <summary>
        /// 파일로부터 전체 데이터를 불러옴
        /// </summary>
        public static CoefficientSaveData Load()
        {
            if (_cache != null) return _cache;

            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    _cache = JsonSerializer.Deserialize<CoefficientSaveData>(json, _options) ?? new CoefficientSaveData();
                    ApplyLegacyFieldsToCurrentAliases(_cache);
                    return _cache;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"계수 데이터 로드 중 오류 발생: {ex.Message}");
            }

            _cache = new CoefficientSaveData();
            return _cache;
        }

        private static void ApplyCurrentAliasesToLegacyFields(CoefficientSaveData data)
        {
            foreach (var entry in data.Entries)
            {
                foreach (var snap in entry.Value)
                {
                    if (snap.AccessoryValue1 == 0 && snap.AccessoryValue2 == 0 && snap.TitleValue == 0 && snap.CoreValue == 0)
                    {
                        continue;
                    }

                    snap.AttackValue = snap.AccessoryValue1 != 0 ? snap.AccessoryValue1 : snap.AttackValue;
                    snap.AttackEnchant = snap.AccessoryValue2 != 0 ? snap.AccessoryValue2 : snap.AttackEnchant;
                    snap.DefenseValue = snap.TitleValue != 0 ? snap.TitleValue : snap.DefenseValue;
                    snap.DefenseEnchant = snap.CoreValue != 0 ? snap.CoreValue : snap.DefenseEnchant;
                }
            }
        }

        private static void ApplyLegacyFieldsToCurrentAliases(CoefficientSaveData data)
        {
            foreach (var entry in data.Entries)
            {
                foreach (var snap in entry.Value)
                {
                    bool aliasEmpty = snap.AccessoryValue1 == 0 && snap.AccessoryValue2 == 0 && snap.TitleValue == 0 && snap.CoreValue == 0;
                    bool legacyPresent = snap.AttackValue != 0 || snap.AttackEnchant != 0 || snap.DefenseValue != 0 || snap.DefenseEnchant != 0;
                    if (!aliasEmpty || !legacyPresent)
                    {
                        continue;
                    }

                    snap.AccessoryValue1 = snap.AttackValue;
                    snap.AccessoryValue2 = snap.AttackEnchant;
                    snap.TitleValue = snap.DefenseValue;
                    snap.CoreValue = snap.DefenseEnchant;
                }
            }
        }
    }
}
