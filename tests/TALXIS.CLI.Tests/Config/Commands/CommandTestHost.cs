using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Storage;

namespace TALXIS.CLI.Tests.Config.Commands;

/// <summary>
/// Per-test TxcServices scope: boots an in-memory service provider backed
/// by a temp config dir + a fake in-memory vault, and tears it down on
/// dispose. Commands resolve their dependencies through <c>TxcServices</c>,
/// which is process-wide, so tests must guard the lifecycle carefully.
/// </summary>
internal sealed class CommandTestHost : IDisposable
{
    public TempConfigDir Temp { get; }
    public ServiceProvider Provider { get; }
    public FakeVault Vault { get; }

    public CommandTestHost()
    {
        Temp = new TempConfigDir();
        Vault = new FakeVault();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Temp.Paths);
        services.AddSingleton<IProfileStore, ProfileStore>();
        services.AddSingleton<IConnectionStore, ConnectionStore>();
        services.AddSingleton<ICredentialStore, CredentialStore>();
        services.AddSingleton<IGlobalConfigStore, GlobalConfigStore>();
        services.AddSingleton<ICredentialVault>(Vault);

        Provider = services.BuildServiceProvider();
        TxcServices.Initialize(Provider);
    }

    public void Dispose()
    {
        TxcServices.Reset();
        Provider.Dispose();
        Temp.Dispose();
    }

    internal sealed class FakeVault : ICredentialVault
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> Contents => _store;

        public Task<string?> GetSecretAsync(SecretRef reference, CancellationToken ct)
            => Task.FromResult(_store.TryGetValue(reference.Uri, out var v) ? v : null);

        public Task SetSecretAsync(SecretRef reference, string value, CancellationToken ct)
        {
            _store[reference.Uri] = value;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteSecretAsync(SecretRef reference, CancellationToken ct)
            => Task.FromResult(_store.Remove(reference.Uri));
    }
}
