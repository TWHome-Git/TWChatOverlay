using System.Collections.Generic;
using System.Threading.Tasks;

namespace TWChatOverlay.Services
{
    /// <summary>
    /// ETA 프로필 조회용 호환 facade입니다. 랭킹 데이터 로드/캐시는 EtaRankingService가 담당합니다.
    /// </summary>
    public static class EtaProfileResolver
    {
        public static void InitializeAsync()
            => AppServices.Get<EtaRankingService>().InitializeAsync();

        public static Task EnsureLoadedAsync()
            => AppServices.Get<EtaRankingService>().EnsureLoadedAsync();

        public static Task<bool> ForceRefreshAsync()
            => AppServices.Get<EtaRankingService>().ForceRefreshAsync();

        public static bool TryGetProfile(string userId, out EtaProfile profile)
            => AppServices.Get<EtaRankingService>().TryGetProfile(userId, out profile);

        public static IReadOnlyList<EtaRankingEntry> GetRankings(string? characterName = null)
            => AppServices.Get<EtaRankingService>().GetRankings(characterName);

        public static void DeleteCache()
            => AppServices.Get<EtaRankingService>().DeleteCache();

        public readonly record struct EtaProfile(int Level, string CharacterName);
        public readonly record struct EtaRankingEntry(int CharacterCode, string CharacterName, string UserId, int Level, int Essence, int OriginalOrder);
    }
}
