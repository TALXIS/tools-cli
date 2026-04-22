using Microsoft.Extensions.DependencyInjection;

namespace TALXIS.CLI.Config.DependencyInjection;

/// <summary>
/// Process-wide service locator. <see cref="Initialize"/> is called once from
/// <c>Program.Main</c> so every <c>[CliCommand]</c> handler can resolve services
/// without fighting the command framework for constructor injection. This is
/// the single bootstrap entry point used both by the normal CLI pipeline and
/// by the <c>__txc_internal_package_deployer</c> subprocess branch.
/// </summary>
public static class TxcServices
{
    private static IServiceProvider? _provider;

    public static void Initialize(IServiceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public static bool IsInitialized => _provider is not null;

    public static T Get<T>() where T : notnull
    {
        if (_provider is null)
            throw new InvalidOperationException("TxcServices.Initialize has not been called.");
        return _provider.GetRequiredService<T>();
    }

    public static T? GetOptional<T>() where T : class
    {
        return _provider?.GetService<T>();
    }

    public static IEnumerable<T> GetAll<T>() where T : notnull
    {
        if (_provider is null)
            throw new InvalidOperationException("TxcServices.Initialize has not been called.");
        return _provider.GetServices<T>();
    }

    // Exposed only for test teardown.
    internal static void Reset() => _provider = null;
}
