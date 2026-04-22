using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Providers.Dataverse;
using TALXIS.CLI.Config.Providers.Dataverse.Msal;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.Dataverse;

public sealed class DataverseConnectionProviderTests
{
    private static DataverseConnectionProvider NewProvider(IDataverseLiveChecker? live = null) =>
        new(new DataverseMsalClientFactory(),
            live ?? new NotYetImplementedDataverseLiveChecker(),
            NullLogger<DataverseConnectionProvider>.Instance);

    private static Connection ValidConnection(string id = "dev") => new()
    {
        Id = id,
        Provider = ProviderKind.Dataverse,
        EnvironmentUrl = "https://contoso.crm.dynamics.com/",
    };

    [Fact]
    public void ProviderKind_IsDataverse() =>
        Assert.Equal(ProviderKind.Dataverse, NewProvider().ProviderKind);

    [Fact]
    public void SupportedCredentialKinds_ExcludesPat()
    {
        var provider = NewProvider();
        Assert.DoesNotContain(CredentialKind.Pat, provider.SupportedCredentialKinds);
        Assert.Contains(CredentialKind.ClientSecret, provider.SupportedCredentialKinds);
        Assert.Contains(CredentialKind.InteractiveBrowser, provider.SupportedCredentialKinds);
        Assert.Contains(CredentialKind.WorkloadIdentityFederation, provider.SupportedCredentialKinds);
    }

    [Fact]
    public async Task ValidateAsync_Succeeds_ForWellFormedInteractive()
    {
        var provider = NewProvider();
        var credential = new Credential { Id = "u", Kind = CredentialKind.InteractiveBrowser };
        await provider.ValidateAsync(ValidConnection(), credential, ValidationMode.Structural, default);
    }

    [Fact]
    public async Task ValidateAsync_Throws_WhenProviderMismatches()
    {
        var provider = NewProvider();
        var connection = new Connection
        {
            Id = "azure",
            Provider = ProviderKind.Azure,
            EnvironmentUrl = "https://contoso.crm.dynamics.com/",
        };
        var credential = new Credential { Id = "u", Kind = CredentialKind.InteractiveBrowser };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ValidateAsync(connection, credential, ValidationMode.Structural, default));
    }

    [Fact]
    public async Task ValidateAsync_Throws_WhenEnvironmentUrlMissing()
    {
        var provider = NewProvider();
        var connection = ValidConnection();
        connection.EnvironmentUrl = null;
        var credential = new Credential { Id = "u", Kind = CredentialKind.InteractiveBrowser };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ValidateAsync(connection, credential, ValidationMode.Structural, default));
    }

    [Fact]
    public async Task ValidateAsync_Throws_WhenEnvironmentUrlRelative()
    {
        var provider = NewProvider();
        var connection = ValidConnection();
        connection.EnvironmentUrl = "/api/data/v9.2";
        var credential = new Credential { Id = "u", Kind = CredentialKind.InteractiveBrowser };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ValidateAsync(connection, credential, ValidationMode.Structural, default));
    }

    [Fact]
    public async Task ValidateAsync_Throws_ForUnsupportedKind()
    {
        var provider = NewProvider();
        var credential = new Credential { Id = "u", Kind = CredentialKind.Pat };
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ValidateAsync(ValidConnection(), credential, ValidationMode.Structural, default));
    }
}
