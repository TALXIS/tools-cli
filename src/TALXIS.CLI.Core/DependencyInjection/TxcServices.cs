using Microsoft.Extensions.DependencyInjection;

namespace TALXIS.CLI.Core.DependencyInjection;

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
    private static readonly object _gate = new();

    /// <summary>
    /// Installs the process-wide service provider. Fails fast if called twice —
    /// double-initialization means two composition roots are fighting for the
    /// same locator and would resolve services from different containers.
    /// Tests that need to rebuild the container must call <see cref="Reset"/>
    /// between initializations.
    /// </summary>
    public static void Initialize(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        lock (_gate)
        {
            if (_provider is not null && !ReferenceEquals(_provider, provider))
                throw new InvalidOperationException(
                    "TxcServices.Initialize has already been called. Only one composition root is allowed per process; call TxcServices.Reset() first if you intentionally want to replace it (tests only).");
            _provider = provider;
        }
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
    internal static void Reset()
    {
        lock (_gate)
        {
            _provider = null;
        }
    }
}
