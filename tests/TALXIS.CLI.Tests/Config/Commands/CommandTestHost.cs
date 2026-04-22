using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.DependencyInjection;
using TALXIS.CLI.Config.Headless;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Resolution;
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
    public FakeHeadless Headless { get; }
    public FakeInteractiveLogin Login { get; }

    public CommandTestHost(
        bool headless = false,
        InteractiveLoginResult? loginResult = null)
    {
        Temp = new TempConfigDir();
        Vault = new FakeVault();
        Headless = new FakeHeadless(headless);
        Login = new FakeInteractiveLogin(loginResult
            ?? new InteractiveLoginResult("tomas@contoso.com", "tenant-guid"));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Temp.Paths);
        services.AddSingleton<IEnvironmentReader>(ProcessEnvironmentReader.Instance);
        services.AddSingleton<IProfileStore, ProfileStore>();
        services.AddSingleton<IConnectionStore, ConnectionStore>();
        services.AddSingleton<ICredentialStore, CredentialStore>();
        services.AddSingleton<IGlobalConfigStore, GlobalConfigStore>();
        services.AddSingleton<ICredentialVault>(Vault);
        services.AddSingleton<IHeadlessDetector>(Headless);
        services.AddSingleton<IInteractiveLoginService>(Login);

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

    internal sealed class FakeHeadless : IHeadlessDetector
    {
        public FakeHeadless(bool isHeadless) { IsHeadless = isHeadless; Reason = isHeadless ? "test harness" : null; }
        public bool IsHeadless { get; }
        public string? Reason { get; }
    }

    internal sealed class FakeInteractiveLogin : IInteractiveLoginService
    {
        private readonly InteractiveLoginResult _result;
        public int Calls { get; private set; }
        public string? LastTenant { get; private set; }
        public CloudInstance? LastCloud { get; private set; }

        public FakeInteractiveLogin(InteractiveLoginResult result) { _result = result; }

        public Task<InteractiveLoginResult> LoginAsync(string? tenantId, CloudInstance cloud, CancellationToken ct)
        {
            Calls++;
            LastTenant = tenantId;
            LastCloud = cloud;
            return Task.FromResult(_result);
        }
    }
}

