using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using TALXIS.CLI.Core.Browser;
using TALXIS.CLI.Core.DependencyInjection;
using TALXIS.CLI.Core.Model;

namespace TALXIS.CLI.Features.Ui.Tests;

internal sealed class UiCommandTestHost : IDisposable
{
    public UiCommandTestHost()
    {
        BrowserManager = new FakeBrowserSessionManager();
        Resolver = new FakeConfigurationResolver();

        var services = new ServiceCollection();
        services.AddSingleton<TALXIS.CLI.Core.Abstractions.IConfigurationResolver>(Resolver);
        services.AddSingleton<IBrowserSessionManager>(BrowserManager);
        Provider = services.BuildServiceProvider();

        TxcServices.Reset();
        TxcServices.Initialize(Provider);
    }

    public ServiceProvider Provider { get; }
    public FakeBrowserSessionManager BrowserManager { get; }
    public FakeConfigurationResolver Resolver { get; }

    public void Dispose()
    {
        TxcServices.Reset();
        Provider.Dispose();
        TALXIS.CLI.Core.OutputContext.Reset();
    }

    internal sealed class FakeConfigurationResolver : TALXIS.CLI.Core.Abstractions.IConfigurationResolver
    {
        public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct)
        {
            var profileId = profileName ?? "default-profile";
            return Task.FromResult(new ResolvedProfileContext(
                new Profile { Id = profileId, ConnectionRef = "conn", CredentialRef = "cred" },
                new Connection
                {
                    Id = "conn",
                    Provider = ProviderKind.Dataverse,
                    EnvironmentId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    EnvironmentUrl = "https://contoso.crm4.dynamics.com/",
                    TenantId = "tenant"
                },
                new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser },
                ResolutionSource.CommandLine));
        }
    }

    internal sealed class FakeBrowserSessionManager : IBrowserSessionManager
    {
        public BrowserLaunchOptions? LastLaunchOptions { get; private set; }
        public BrowserSession Session { get; set; } = new(
            "session-1",
            "dev",
            "ws://127.0.0.1:9222/devtools/browser/test",
            "https://contoso.crm4.dynamics.com/main.aspx?appid=test",
            DateTime.UtcNow,
            1234,
            false,
            "chromium",
            "/tmp/user-data");

        public JsonElement EvalResult { get; set; } = JsonDocument.Parse("\"hello\"").RootElement.Clone();

        public Task<BrowserSession> LaunchAsync(BrowserLaunchOptions options, CancellationToken ct)
        {
            LastLaunchOptions = options;
            Session = Session with
            {
                ProfileName = options.ProfileName,
                AppUrl = options.AppUrl,
                Headless = options.Headless,
            };
            return Task.FromResult(Session);
        }

        public Task<BrowserSession?> AttachAsync(string cdpEndpoint, CancellationToken ct)
            => Task.FromResult<BrowserSession?>(Session);

        public Task CloseAsync(string sessionId, CancellationToken ct) => Task.CompletedTask;

        public Task<BrowserSession?> GetActiveSessionAsync(CancellationToken ct)
            => Task.FromResult<BrowserSession?>(Session);

        public Task<BrowserSession?> GetSessionAsync(string sessionId, CancellationToken ct)
            => Task.FromResult<BrowserSession?>(Session);

        public Task<IReadOnlyList<BrowserSession>> ListSessionsAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<BrowserSession>>(new[] { Session });

        public Task<string?> GetCurrentUrlAsync(string sessionId, CancellationToken ct)
            => Task.FromResult<string?>(Session.AppUrl);

        public Task<JsonElement> EvaluateAsync(string sessionId, string script, CancellationToken ct)
            => Task.FromResult(EvalResult);
    }
}
