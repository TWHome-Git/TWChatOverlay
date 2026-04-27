using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// 장비 데이터 조회/갱신 기능의 계약을 정의합니다.
    /// </summary>
    public interface IEquipmentService
    {
        Task<List<EquipmentModel>> GetEquipmentsAsync();
        Task<bool> ForceRefreshAsync();
    }

    /// <summary>
    /// 장비 데이터를 원격 저장소와 로컬 캐시에서 읽어오는 서비스입니다.
    /// </summary>
    public class EquipmentService : IEquipmentService
    {
        private const string EquipmentDataUrl = "https://raw.githubusercontent.com/TWHome-Git/TWHomeDB/main/EquipmentData.json";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        private static readonly RemoteJsonCacheClient CacheClient = new(
            "EquipmentService",
            EquipmentDataUrl,
            CacheTtl,
            HttpClient,
            refreshAnchorHourLocal: 11,
            forceRemoteCheckOnFirstCall: true);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        /// <summary>
        /// 장비 목록을 캐시 우선 전략으로 조회합니다.
        /// </summary>
        public async Task<List<EquipmentModel>> GetEquipmentsAsync()
        {
            try
            {
                string? json = await CacheClient.GetJsonAsync(forceRefresh: false);

                if (string.IsNullOrWhiteSpace(json))
                {
                    Debug.WriteLine("[EquipmentService] Equipment data source not found.");
                    return new List<EquipmentModel>();
                }

                return DeserializeEquipments(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Service Error] {ex.Message}");
                return new List<EquipmentModel>();
            }
        }

        /// <summary>
        /// 원격 데이터를 강제로 다시 받아 캐시를 갱신합니다.
        /// </summary>
        public async Task<bool> ForceRefreshAsync()
        {
            try
            {
                string? json = await CacheClient.GetJsonAsync(forceRefresh: true);
                if (string.IsNullOrWhiteSpace(json))
                    return false;

                var data = DeserializeEquipments(json);
                return data.Count > 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EquipmentService] Force refresh failed: {ex.Message}");
                return false;
            }
        }

        private static List<EquipmentModel> DeserializeEquipments(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new List<EquipmentModel>();

            string normalized = NormalizeJson(json);

            try
            {
                var list = JsonSerializer.Deserialize<List<EquipmentModel>>(normalized, JsonOptions);
                if (list != null && list.Count > 0)
                    return list;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"[EquipmentService] Array parse failed, trying wrapped payload. {ex.Message}");
            }

            try
            {
                var wrapped = JsonSerializer.Deserialize<EquipmentPayload>(normalized, JsonOptions);
                if (wrapped?.Items != null && wrapped.Items.Count > 0)
                    return wrapped.Items;
            }
            catch (Exception ex)
            {
                AppLogger.Error($"[EquipmentService] Wrapped payload parse failed: {ex.Message}");
            }

            return new List<EquipmentModel>();
        }

        private static string NormalizeJson(string json)
        {
            string normalized = json.Trim();
            if (normalized.Length > 0 && normalized[0] == '\uFEFF')
            {
                normalized = normalized.Substring(1);
            }

            return normalized.Replace("\uFEFF", string.Empty);
        }

        private sealed class EquipmentPayload
        {
            [JsonPropertyName("items")]
            public List<EquipmentModel> Items { get; set; } = new();
        }
    }

    // (VisibilityConverter removed from here - moved to its own UI service file.)
}
