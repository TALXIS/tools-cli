using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Browser;

namespace TALXIS.CLI.Platform.Playwright.DependencyInjection;

public static class PlaywrightServiceCollectionExtensions
{
    public static IServiceCollection AddTxcPlaywright(this IServiceCollection services)
    {
        services.AddTransient<StorageStateManager>();
        services.AddTransient<SessionRecoveryService>();
        services.AddTransient<IBrowserSessionManager, BrowserSessionManager>();
        return services;
    }
}
