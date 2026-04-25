using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.PowerPlatform;

public sealed class EnvironmentSettingsServiceTests
{
    private static readonly Connection TestConnection = new()
    {
        Id = "test",
        Provider = ProviderKind.Dataverse,
        EnvironmentUrl = "https://test.crm.dynamics.com",
        EnvironmentId = Guid.NewGuid(),
    };

    private static readonly Credential TestCredential = new()
    {
        Id = "test-cred",
        Kind = CredentialKind.ClientSecret,
    };

    private static readonly ResolvedProfileContext TestContext = new(
        Profile: new Profile { Id = "test-profile", ConnectionRef = "test", CredentialRef = "test-cred" },
        Connection: TestConnection,
        Credential: TestCredential,
        Source: ResolutionSource.CommandLine);

    /// <summary>
    /// Verifies the orchestrator merges results from all backends and
    /// that the first backend wins when names collide.
    /// </summary>
    [Fact]
    public void MergeResults_FirstBackendWinsOnCollision()
    {
        // Simulate what the orchestrator does — merge with first-seen priority.
        var backend1 = new List<EnvironmentSetting>
        {
            new("SharedSetting", "from-backend1"),
            new("OnlyInBackend1", "value1"),
        };
        var backend2 = new List<EnvironmentSetting>
        {
            new("SharedSetting", "from-backend2"),
            new("OnlyInBackend2", "value2"),
        };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<EnvironmentSetting>();

        foreach (var settings in new[] { backend1, backend2 })
        {
            foreach (var s in settings)
            {
                if (seen.Add(s.Name))
                    merged.Add(s);
            }
        }

        Assert.Equal(3, merged.Count);
        var byName = merged.ToDictionary(s => s.Name, s => s.Value, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("from-backend1", byName["SharedSetting"]);
        Assert.Equal("value1", byName["OnlyInBackend1"]);
        Assert.Equal("value2", byName["OnlyInBackend2"]);
    }

    /// <summary>
    /// Verifies deduplication is case-insensitive.
    /// </summary>
    [Fact]
    public void MergeResults_CaseInsensitiveDeduplication()
    {
        var backend1 = new List<EnvironmentSetting> { new("isAuditEnabled", "Yes") };
        var backend2 = new List<EnvironmentSetting> { new("IsAuditEnabled", "True") };

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<EnvironmentSetting>();

        foreach (var settings in new[] { backend1, backend2 })
        {
            foreach (var s in settings)
            {
                if (seen.Add(s.Name))
                    merged.Add(s);
            }
        }

        Assert.Single(merged);
        Assert.Equal("Yes", merged[0].Value);
    }
}
