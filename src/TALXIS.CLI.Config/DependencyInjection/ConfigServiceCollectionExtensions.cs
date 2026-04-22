using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Headless;
using TALXIS.CLI.Config.Resolution;
using TALXIS.CLI.Config.Storage;

namespace TALXIS.CLI.Config.DependencyInjection;

public static class ConfigServiceCollectionExtensions
{
    /// <summary>
    /// Registers the txc config core services (stores, resolver, workspace discovery,
    /// headless detector) on <paramref name="services"/>. Does NOT register an
    /// <see cref="ICredentialVault"/> — the <c>config-vault</c> milestone adds that.
    /// </summary>
    public static IServiceCollection AddTxcConfigCore(this IServiceCollection services)
    {
        services.AddSingleton(_ => ConfigPaths.FromEnvironment());

        services.AddSingleton<IProfileStore, ProfileStore>();
        services.AddSingleton<IConnectionStore, ConnectionStore>();
        services.AddSingleton<ICredentialStore, CredentialStore>();
        services.AddSingleton<IGlobalConfigStore, GlobalConfigStore>();

        services.AddSingleton<IWorkspaceDiscovery, WorkspaceDiscovery>();
        services.AddSingleton<IEnvironmentReader>(ProcessEnvironmentReader.Instance);

        services.AddSingleton<IConfigurationResolver, ConfigurationResolver>();
        services.AddSingleton<IHeadlessDetector, HeadlessDetector>();

        return services;
    }
}
