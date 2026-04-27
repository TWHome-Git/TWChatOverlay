using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TWChatOverlay.Services;

namespace TWChatOverlay.Models
{
    /// <summary>
    /// 장비 스탯의 최소/최대/상한 값을 표현합니다.
    /// </summary>
    public class StatValue
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public int Limit { get; set; }
        public bool HasValue => !(Min == 0 && Max == 0);

        public string RangeText => (Min == 0 && Max == 0) ? "-" : $"{Min}-{Max}";

        public string LimitText => Limit == 0 ? "-" : Limit.ToString();
    }

    /// <summary>
    /// 장비 데이터 한 건을 표현하는 모델입니다.
    /// </summary>
    public class EquipmentModel
    {
        public class CraftMaterial
        {
            [JsonPropertyName("name")]
            public string? Name { get; set; }

            [JsonPropertyName("count")]
            public int Count { get; set; }

            [JsonPropertyName("image")]
            public string? RawImagePath { get; set; }

            [JsonIgnore]
            public string? ImagePath
            {
                get
                {
                    return EquipmentImagePathResolver.Resolve(RawImagePath, "Item", "Equipment");
                }
            }
        }

        [JsonPropertyName("image")]
        public string? RawImagePath { get; set; }

        [JsonIgnore]
        public string? ImagePath
        {
            get
            {
                return EquipmentImagePathResolver.Resolve(RawImagePath, "Equipment", "Item");
            }
        }

        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("major_category")]
        public string? MajorCategory { get; set; }

        [JsonPropertyName("sub_category")]
        public string? SubCategory { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("stats")]
        public Dictionary<string, StatValue> Stats { get; set; } = new();

        [JsonPropertyName("합성")]
        public string? Synthesis { get; set; }

        [JsonPropertyName("조건")]
        public string? Requirement { get; set; }

        [JsonPropertyName("characters")]
        public List<string> Characters { get; set; } = new();

        [JsonPropertyName("attack_type")]
        [JsonConverter(typeof(StringOrStringArrayConverter))]
        public List<string> AttackTypes { get; set; } = new();

        [JsonPropertyName("재료")]
        public List<CraftMaterial>? Materials { get; set; }

        [JsonPropertyName("재료목록")]
        public List<CraftMaterial>? LegacyMaterials { get; set; }

        [JsonPropertyName("비고")]
        public string? Note { get; set; }

        [JsonIgnore]
        public List<CraftMaterial> CraftMaterials =>
            (Materials?.Count > 0 ? Materials : LegacyMaterials) ?? new List<CraftMaterial>();

        [JsonIgnore]
        public string? AttackType => AttackTypes.FirstOrDefault();

        public StatValue Stab => GetStat("찌르기");
        public StatValue Hack => GetStat("베기");
        public StatValue Def => GetStat("물리방어");
        public StatValue MR => GetStat("마법방어");
        public StatValue Int => GetStat("마법공격");
        public StatValue Dex => GetStat("명중");
        public StatValue Agi => GetStat("회피");
        public StatValue SmallAgi => GetStat("민첩");
        public StatValue Crit => GetStat("크리티컬");

        private StatValue GetStat(string key)
        {
            if (Stats != null && Stats.ContainsKey(key)) return Stats[key];
            return new StatValue { Min = 0, Max = 0, Limit = 0 };
        }

        private sealed class StringOrStringArrayConverter : JsonConverter<List<string>>
        {
            public override List<string> Read(ref Utf8JsonReader reader, System.Type typeToConvert, JsonSerializerOptions options)
            {
                var values = new List<string>();

                if (reader.TokenType == JsonTokenType.Null)
                    return values;

                if (reader.TokenType == JsonTokenType.String)
                {
                    var single = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(single))
                        values.Add(single.Trim());
                    return values;
                }

                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    while (reader.Read())
                    {
                        if (reader.TokenType == JsonTokenType.EndArray)
                            break;

                        if (reader.TokenType == JsonTokenType.String)
                        {
                            var s = reader.GetString();
                            if (!string.IsNullOrWhiteSpace(s))
                                values.Add(s.Trim());
                        }
                    }

                    return values;
                }

                using var _ = JsonDocument.ParseValue(ref reader);
                return values;
            }

            public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
            {
                writer.WriteStartArray();
                foreach (var item in value.Where(x => !string.IsNullOrWhiteSpace(x)))
                    writer.WriteStringValue(item);
                writer.WriteEndArray();
            }
        }
    }
}
