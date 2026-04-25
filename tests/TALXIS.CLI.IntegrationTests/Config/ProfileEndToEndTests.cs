using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace TALXIS.CLI.IntegrationTests.Config;

/// <summary>
/// End-to-end integration test for the <c>txc config</c> command surface.
/// Uses an isolated <c>TXC_CONFIG_DIR</c> so the test never touches the
/// developer's real <c>~/.txc</c>. Exercises the full profile lifecycle:
/// connection create → auth add-service-principal → profile create →
/// profile select → profile show → profile list → profile delete.
///
/// The SPN credential is registered via the <c>--secret-from-env</c>
/// code path so the test stays fully non-interactive and portable across
/// local dev, CI, and headless runners. No live Dataverse call is made —
/// <c>profile validate</c> is intentionally omitted because it would try
/// to reach the fake environment URL.
/// </summary>
[Collection("Sequential")]
public class ProfileEndToEndTests : IDisposable
{
    private readonly string _configDir;
    private readonly IReadOnlyDictionary<string, string?> _env;

    public ProfileEndToEndTests()
    {
        _configDir = Path.Combine(Path.GetTempPath(), "txc-e2e-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_configDir);
        _env = new Dictionary<string, string?>
        {
            ["TXC_CONFIG_DIR"] = _configDir,
            // Force a fallback secret vault so the test never prompts for
            // Keychain access on macOS or tries to reach libsecret on a
            // headless Linux runner.
            ["TXC_PLAINTEXT_FALLBACK"] = "1",
            ["TXC_TOKEN_CACHE_MODE"] = "file",
            ["TXC_NON_INTERACTIVE"] = "1",
            ["TXC_E2E_TEST_SECRET"] = "not-a-real-secret-placeholder-12345",
        };
    }

    public void Dispose()
    {
        try { Directory.Delete(_configDir, recursive: true); } catch { }
    }

    [Fact]
    public async Task Profile_Lifecycle_RoundTrip()
    {
        // 1. Create a connection.
        var create = await CliRunner.RunRawAsync(
            new[] { "config", "connection", "create", "e2e-conn",
                    "--provider", "dataverse",
                    "--environment", "https://contoso.crm4.dynamics.com/",
                    "--cloud", "Public" },
            env: _env);
        Assert.True(create.ExitCode == 0, $"connection create failed: {create.Error}\n{create.Output}");

        // 2. Register a client-secret credential via the env-var path.
        var auth = await CliRunner.RunRawAsync(
            new[] { "config", "auth", "add-service-principal",
                    "--alias", "e2e-sp",
                    "--tenant", "11111111-1111-1111-1111-111111111111",
                    "--application-id", "22222222-2222-2222-2222-222222222222",
                    "--secret-from-env", "TXC_E2E_TEST_SECRET" },
            env: _env);
        Assert.True(auth.ExitCode == 0, $"auth add-service-principal failed: {auth.Error}\n{auth.Output}");

        // 3. Bind them into a profile.
        var profile = await CliRunner.RunRawAsync(
            new[] { "config", "profile", "create",
                    "--name", "e2e-profile",
                    "--auth", "e2e-sp",
                    "--connection", "e2e-conn" },
            env: _env);
        Assert.True(profile.ExitCode == 0, $"profile create failed: {profile.Error}\n{profile.Output}");

        // 4. List should contain the new profile.
        var list = await CliRunner.RunRawAsync(new[] { "config", "profile", "list" }, env: _env);
        Assert.Equal(0, list.ExitCode);
        Assert.Contains("e2e-profile", list.Output);

        // 5. Select it.
        var select = await CliRunner.RunRawAsync(
            new[] { "config", "profile", "select", "e2e-profile" }, env: _env);
        Assert.Equal(0, select.ExitCode);

        // 6. Show includes connection + credential refs.
        var show = await CliRunner.RunRawAsync(
            new[] { "config", "profile", "show", "e2e-profile" }, env: _env);
        Assert.Equal(0, show.ExitCode);
        Assert.Contains("e2e-sp", show.Output);
        Assert.Contains("e2e-conn", show.Output);

        // 7. Cleanup — delete profile, credential (with vault secret), and connection.
        var delProfile = await CliRunner.RunRawAsync(
            new[] { "config", "profile", "delete", "e2e-profile", "--yes" }, env: _env);
        Assert.Equal(0, delProfile.ExitCode);

        var delAuth = await CliRunner.RunRawAsync(
            new[] { "config", "auth", "delete", "e2e-sp", "--yes" }, env: _env);
        Assert.Equal(0, delAuth.ExitCode);

        var delConn = await CliRunner.RunRawAsync(
            new[] { "config", "connection", "delete", "e2e-conn", "--yes" }, env: _env);
        Assert.Equal(0, delConn.ExitCode);
    }

    [Fact]
    public async Task Profile_Show_MissingProfile_FailsFast()
    {
        // No profile exists; show should fail with a clear non-zero exit
        // rather than hanging or prompting.
        var result = await CliRunner.RunRawAsync(
            new[] { "config", "profile", "show", "does-not-exist" }, env: _env);
        Assert.NotEqual(0, result.ExitCode);
    }
}
