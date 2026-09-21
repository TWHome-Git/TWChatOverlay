using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TWChatOverlay.Models;

namespace TWChatOverlay.Services
{
    // === 핵심 서비스 인터페이스 (테스트에서 대체 가능하도록) ===

    public interface IConfigService
    {
        bool SettingsFileExists();
        ChatSettings Load();
        void Save(ChatSettings settings);
        void SaveDeferred(ChatSettings settings);
    }

    public interface IUpdateService
    {
        Task<UpdateCheckResult> CheckForUpdateAsync(bool forceInstallLatest, bool showNoUpdateMessage);
    }

    // === 기존 정적 서비스를 감싸는 어댑터(프록시) ===
    // 정적 구현을 유지하면서도 DI/테스트를 위한 인터페이스 진입점을 제공한다.

    public sealed class ConfigServiceProxy : IConfigService
    {
        public bool SettingsFileExists() => ConfigService.SettingsFileExists();
        public ChatSettings Load() => ConfigService.Load();
        public void Save(ChatSettings settings) => ConfigService.Save(settings);
        public void SaveDeferred(ChatSettings settings) => ConfigService.SaveDeferred(settings);
    }

    public sealed class UpdateServiceProxy : IUpdateService
    {
        public Task<UpdateCheckResult> CheckForUpdateAsync(bool forceInstallLatest, bool showNoUpdateMessage)
            => UpdateService.CheckForUpdateAsync(forceInstallLatest, showNoUpdateMessage);
    }

    /// <summary>시작 시점에만 알 수 있는 사실. 설정 파일이 없었는지(진짜 최초 실행)는 Load가 파일을 만들기 전에 잡아야 한다.</summary>
    public sealed record StartupState(bool SettingsFileMissing);

    /// <summary>
    /// 경량 DI 컨테이너의 컴포지션 루트. App.OnStartup에서 설정을 읽은 뒤 초기화한다.
    ///
    /// 규칙:
    ///  - 상태(캐시·감시자·이벤트)를 가진 서비스는 정적 클래스로 만들지 않는다. 인스턴스 + 인터페이스로 만들고 여기 등록한다.
    ///    (예: IIdTagService, IOverlayOpacityService) 순수 함수만 있는 도우미(LogParser, WindowPlacement 등)는 정적이어도 된다.
    ///  - MainWindow가 쓰는 핵심 서비스(로그·경험치·버프·알림)는 App이 여기 등록하고 MainWindow는 Get으로 꺼낸다.
    ///  - 아직 정적으로 남은 상태형 서비스(UiLockService, ToastStackService, ContentTimerService 등)는 점진 전환 대상이다.
    /// </summary>
    public static class AppServices
    {
        private static IServiceProvider? _provider;

        public static IServiceProvider Provider =>
            _provider ?? throw new InvalidOperationException("AppServices가 초기화되지 않았습니다. App.OnStartup에서 Initialize를 호출하세요.");

        public static bool IsInitialized => _provider != null;

        public static void Initialize(Action<IServiceCollection>? configure = null)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfigService, ConfigServiceProxy>();
            services.AddSingleton<IUpdateService, UpdateServiceProxy>();
            configure?.Invoke(services);
            _provider = services.BuildServiceProvider();
        }

        public static T Get<T>() where T : notnull => Provider.GetRequiredService<T>();

        /// <summary>초기화 전(도움말 이미지 생성 모드 등)이면 null. 컨테이너 없이도 돌아야 하는 코드에서 쓴다.</summary>
        public static T? TryGet<T>() where T : class => _provider?.GetService<T>();
    }
}
