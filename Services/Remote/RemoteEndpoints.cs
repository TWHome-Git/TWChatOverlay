namespace TWChatOverlay.Services
{
    /// <summary>
    /// 원격 데이터/릴리스 엔드포인트를 한 곳에서 관리합니다.
    /// (기존에 5~6개 서비스에 하드코딩되어 있던 URL을 통합)
    /// </summary>
    internal static class RemoteEndpoints
    {
        // === 데이터 저장소 (raw content, TWHomeDB) ===
        public const string DataRepoOwner = "TWHome-Git";
        public const string DataRepoName = "TWHomeDB";
        public const string DataRawBase = "https://raw.githubusercontent.com/" + DataRepoOwner + "/" + DataRepoName + "/main/";

        // 원본 파일명의 대소문자/확장자를 그대로 보존해야 함(raw 호스트는 경로가 대소문자 구분)
        public const string DropItem = DataRawBase + "DropItem.Json";
        public const string BossTimer = DataRawBase + "BossTimer.json";
        public const string EtaRanking = DataRawBase + "eta_ranking.json";
        public const string Manifest = DataRawBase + "manifest.json";
        public const string RecaptureSupplyImage = DataRawBase + "Recapture%20supplies.png";

        // === 웹 홈페이지 (에타 순위 / 장비 DB / 계산기 / 시뮬레이터 통합) ===
        public const string TwPageUrl = "https://twhome-git.github.io/TWPage/";

        // === 앱 저장소 (GitHub API, 자동 업데이트용) ===
        public const string AppRepoOwner = "TWHome-Git";
        public const string AppRepoName = "TWChatOverlay";
        public const string LatestReleaseApi = "https://api.github.com/repos/" + AppRepoOwner + "/" + AppRepoName + "/releases/latest";
        // 프리릴리즈(베타) 포함 목록 — 수동 업데이트에서 사용. /releases/latest는 프리릴리즈를 제외한다.
        public const string ReleaseListApi = "https://api.github.com/repos/" + AppRepoOwner + "/" + AppRepoName + "/releases?per_page=10";
    }
}
