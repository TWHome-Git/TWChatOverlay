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

    /// <summary>
    /// 경량 DI 컨테이너의 컴포지션 루트. App 시작 시 초기화한다.
    /// 기존 정적 호출부는 그대로 두고, 여기서부터 핵심 서비스를 인터페이스로 해석하도록 점진 전환한다.
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
    }
}
