using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Resolution;
using TALXIS.CLI.Config.Storage;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Resolution;

public class PrecedenceTests
{
    [Fact]
    public async Task CommandLineBeatsEverything()
    {
        using var dir = new TempConfigDir();
        var (resolver, env) = await SetupAsync(dir, env: new() {
            ["TXC_PROFILE"] = "from-env",
        }, globalActive: "from-global", workspaceDefault: "from-workspace",
           profiles: new[] { "from-flag", "from-env", "from-workspace", "from-global" });

        var ctx = await resolver.ResolveAsync("from-flag", CancellationToken.None);
        Assert.Equal("from-flag", ctx.Profile!.Id);
        Assert.Equal(ResolutionSource.CommandLine, ctx.Source);
    }

    [Fact]
    public async Task EnvVarBeatsWorkspaceAndGlobal()
    {
        using var dir = new TempConfigDir();
        var (resolver, _) = await SetupAsync(dir, env: new() {
            ["TXC_PROFILE"] = "from-env",
        }, globalActive: "from-global", workspaceDefault: "from-workspace",
           profiles: new[] { "from-env", "from-workspace", "from-global" });

        var ctx = await resolver.ResolveAsync(null, CancellationToken.None);
        Assert.Equal("from-env", ctx.Profile!.Id);
        Assert.Equal(ResolutionSource.EnvironmentVariable, ctx.Source);
    }

    [Fact]
    public async Task WorkspaceBeatsGlobal()
    {
        using var dir = new TempConfigDir();
        var (resolver, _) = await SetupAsync(dir, env: new(),
            globalActive: "from-global", workspaceDefault: "from-workspace",
            profiles: new[] { "from-workspace", "from-global" });

        var ctx = await resolver.ResolveAsync(null, CancellationToken.None);
        Assert.Equal("from-workspace", ctx.Profile!.Id);
        Assert.Equal(ResolutionSource.Workspace, ctx.Source);
    }

    [Fact]
    public async Task GlobalUsedWhenNoOverrides()
    {
        using var dir = new TempConfigDir();
        var (resolver, _) = await SetupAsync(dir, env: new(),
            globalActive: "from-global", workspaceDefault: null,
            profiles: new[] { "from-global" });

        var ctx = await resolver.ResolveAsync(null, CancellationToken.None);
        Assert.Equal("from-global", ctx.Profile!.Id);
        Assert.Equal(ResolutionSource.Global, ctx.Source);
    }

    [Fact]
    public async Task ThrowsWhenNothingResolvable()
    {
        using var dir = new TempConfigDir();
        var (resolver, _) = await SetupAsync(dir, env: new(),
            globalActive: null, workspaceDefault: null, profiles: Array.Empty<string>());

        await Assert.ThrowsAsync<ConfigurationResolutionException>(
            () => resolver.ResolveAsync(null, CancellationToken.None));
    }

    [Fact]
    public async Task ThrowsWhenReferencedProfileMissing()
    {
        using var dir = new TempConfigDir();
        var (resolver, _) = await SetupAsync(dir, env: new(),
            globalActive: "ghost", workspaceDefault: null, profiles: Array.Empty<string>());

        var ex = await Assert.ThrowsAsync<ConfigurationResolutionException>(
            () => resolver.ResolveAsync(null, CancellationToken.None));
        Assert.Contains("ghost", ex.Message);
    }

    private static async Task<(ConfigurationResolver resolver, FakeEnv env)> SetupAsync(
        TempConfigDir dir,
        Dictionary<string, string?> env,
        string? globalActive,
        string? workspaceDefault,
        string[] profiles)
    {
        var profileStore = new ProfileStore(dir.Paths);
        var connectionStore = new ConnectionStore(dir.Paths);
        var credentialStore = new CredentialStore(dir.Paths);
        var globalStore = new GlobalConfigStore(dir.Paths);

        // Create a single connection + credential so every profile resolves.
        await connectionStore.UpsertAsync(new Connection { Id = "conn", Provider = ProviderKind.Dataverse, EnvironmentUrl = "https://x/" }, CancellationToken.None);
        await credentialStore.UpsertAsync(new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser }, CancellationToken.None);

        foreach (var id in profiles)
            await profileStore.UpsertAsync(new Profile { Id = id, ConnectionRef = "conn", CredentialRef = "cred" }, CancellationToken.None);

        if (globalActive is not null)
            await globalStore.SaveAsync(new GlobalConfig { ActiveProfile = globalActive }, CancellationToken.None);

        string cwd;
        if (workspaceDefault is not null)
        {
            cwd = Directory.CreateTempSubdirectory("txc-ws-test-").FullName;
            var txc = Path.Combine(cwd, ".txc");
            Directory.CreateDirectory(txc);
            await File.WriteAllTextAsync(Path.Combine(txc, "workspace.json"),
                $"{{ \"defaultProfile\": \"{workspaceDefault}\" }}");
        }
        else
        {
            // Use a directory guaranteed not to have a .txc/workspace.json up-chain.
            cwd = Directory.CreateTempSubdirectory("txc-ws-empty-").FullName;
        }

        var fakeEnv = new FakeEnv(env, cwd);
        var resolver = new ConfigurationResolver(
            profileStore, connectionStore, credentialStore, globalStore,
            new WorkspaceDiscovery(), fakeEnv);
        return (resolver, fakeEnv);
    }

    private sealed class FakeEnv : IEnvironmentReader
    {
        private readonly Dictionary<string, string?> _env;
        private readonly string _cwd;
        public FakeEnv(Dictionary<string, string?> env, string cwd) { _env = env; _cwd = cwd; }
        public string? Get(string name) => _env.TryGetValue(name, out var v) ? v : null;
        public string GetCurrentDirectory() => _cwd;
    }
}
