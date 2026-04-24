using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Headless;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Core.Storage;
using TALXIS.CLI.Core.Vault;
using TALXIS.CLI.Core.Identity;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;

namespace TALXIS.CLI.Tests.Config.Commands;/// <summary>
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
    public FakeConnectionProvider Provider_Dataverse { get; }
    public FakePowerPlatformEnvironmentCatalog EnvironmentCatalog { get; }

    public CommandTestHost(
        bool headless = false,
        InteractiveLoginResult? loginResult = null,
        string? currentDirectory = null,
        FakeConnectionProvider? dataverseProvider = null,
        FakePowerPlatformEnvironmentCatalog? environmentCatalog = null)
    {
        Temp = new TempConfigDir();
        Vault = new FakeVault();
        Headless = new FakeHeadless(headless);
        Login = new FakeInteractiveLogin(loginResult
            ?? new InteractiveLoginResult(
                "tomas@contoso.com",
                "tenant-guid",
                "account-guid",
                MsalClientFactory.PublicClientId));
        Provider_Dataverse = dataverseProvider ?? new FakeConnectionProvider(ProviderKind.Dataverse);
        EnvironmentCatalog = environmentCatalog ?? new FakePowerPlatformEnvironmentCatalog();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Temp.Paths);
        var envReader = new FakeEnvironmentReader(currentDirectory ?? Directory.GetCurrentDirectory());
        services.AddSingleton<IEnvironmentReader>(envReader);
        services.AddSingleton<IProfileStore, ProfileStore>();
        services.AddSingleton<IConnectionStore, ConnectionStore>();
        services.AddSingleton<ICredentialStore, CredentialStore>();
        services.AddSingleton<IGlobalConfigStore, GlobalConfigStore>();
        services.AddSingleton<TALXIS.CLI.Core.Bootstrapping.ConnectionUpsertService>();
        services.AddSingleton<ICredentialVault>(Vault);
        services.AddSingleton<IHeadlessDetector>(Headless);
        services.AddSingleton<IWorkspaceDiscovery, WorkspaceDiscovery>();
        services.AddSingleton<IConfigurationResolver, ConfigurationResolver>();
        services.AddSingleton<IInteractiveLoginService>(Login);
        services.AddSingleton<IPowerPlatformEnvironmentCatalog>(EnvironmentCatalog);
        services.AddSingleton(_ =>
            MsalTokenCacheBinder
                .CreateForTestingAsync(VaultOptions.MsalTokenCache(envReader), Temp.Paths)
                .GetAwaiter()
                .GetResult());
        services.AddSingleton<ITokenCacheStore>(sp => sp.GetRequiredService<MsalTokenCacheBinder>());
        services.AddSingleton<IConnectionProvider>(Provider_Dataverse);
        services.AddSingleton<
            TALXIS.CLI.Core.Bootstrapping.IConnectionProviderBootstrapper,
            TALXIS.CLI.Platform.Dataverse.Runtime.Bootstrapping.DataverseConnectionProviderBootstrapper>();

        Provider = services.BuildServiceProvider();
        TxcServices.Initialize(Provider);
    }

    public void Dispose()
    {
        TxcServices.Reset();
        Provider.Dispose();
        Temp.Dispose();
    }

    internal sealed class FakeConnectionProvider : IConnectionProvider
    {
        private static readonly HashSet<CredentialKind> DefaultKinds = new()
        {
            CredentialKind.InteractiveBrowser,
            CredentialKind.DeviceCode,
            CredentialKind.ClientSecret,
            CredentialKind.ClientCertificate,
            CredentialKind.WorkloadIdentityFederation,
            CredentialKind.ManagedIdentity,
            CredentialKind.AzureCli,
        };

        public FakeConnectionProvider(ProviderKind kind) { ProviderKind = kind; }

        public ProviderKind ProviderKind { get; }
        public IReadOnlySet<CredentialKind> SupportedCredentialKinds => DefaultKinds;
        public int Calls { get; private set; }
        public ValidationMode? LastMode { get; private set; }
        public Func<ConnectionModel, Credential, ValidationMode, Task>? Behavior { get; set; }

        public Task ValidateAsync(ConnectionModel connection, Credential credential, ValidationMode mode, CancellationToken ct)
        {
            Calls++;
            LastMode = mode;
            if (Behavior is not null) return Behavior(connection, credential, mode);
            return Task.CompletedTask;
        }
    }

    internal sealed class FakeEnvironmentReader : IEnvironmentReader
    {
        private readonly string _cwd;
        public FakeEnvironmentReader(string cwd) { _cwd = cwd; }
        public string? Get(string name)
        {
            // Force plaintext fallback so tests work on headless Linux CI
            // where no keyring / secret service is available.
            if (name == Core.Vault.VaultOptions.LinuxPlaintextEnvVar && OperatingSystem.IsLinux())
                return "true";
            return System.Environment.GetEnvironmentVariable(name);
        }
        public string GetCurrentDirectory() => _cwd;
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

        public Task ClearAsync(CancellationToken ct)
        {
            _store.Clear();
            return Task.CompletedTask;
        }
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

    internal sealed class FakePowerPlatformEnvironmentCatalog : IPowerPlatformEnvironmentCatalog
    {
        private readonly Dictionary<string, PowerPlatformEnvironmentSummary> _environments =
            new(StringComparer.OrdinalIgnoreCase);

        public Exception? Failure { get; set; }

        public void Add(PowerPlatformEnvironmentSummary environment)
            => _environments[Normalize(environment.EnvironmentUrl)] = environment;

        public Task<IReadOnlyList<PowerPlatformEnvironmentSummary>> ListAsync(
            ConnectionModel connection,
            Credential credential,
            CancellationToken ct)
        {
            if (Failure is not null) throw Failure;
            IReadOnlyList<PowerPlatformEnvironmentSummary> result = _environments.Values.ToList();
            return Task.FromResult(result);
        }

        public Task<PowerPlatformEnvironmentSummary?> TryGetByEnvironmentUrlAsync(
            ConnectionModel connection,
            Credential credential,
            Uri environmentUrl,
            CancellationToken ct)
        {
            if (Failure is not null) throw Failure;
            _environments.TryGetValue(Normalize(environmentUrl), out var environment);
            return Task.FromResult(environment);
        }

        private static string Normalize(Uri uri)
            => uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/";
    }
}
