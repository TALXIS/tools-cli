using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Features.Config.Auth;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Commands.Auth;

[Collection("TxcServicesSerial")]
public sealed class AuthAddFederatedCommandTests
{
    [Fact]
    public async Task AddFederated_PersistsCredentialWithoutVaultSecret()
    {
        using var host = new CommandTestHost();

        var sw = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(sw))
            exit = await new AuthAddFederatedCliCommand
            {
                Alias = "ci",
                Tenant = "contoso.onmicrosoft.com",
                ApplicationId = "11111111-1111-1111-1111-111111111111",
                Description = "GitHub Actions deploy",
            }.RunAsync();

        Assert.Equal(0, exit);

        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        var cred = await store.GetAsync("ci", default);
        Assert.NotNull(cred);
        Assert.Equal(CredentialKind.WorkloadIdentityFederation, cred!.Kind);
        Assert.Equal("contoso.onmicrosoft.com", cred.TenantId);
        Assert.Equal("11111111-1111-1111-1111-111111111111", cred.ApplicationId);
        Assert.Equal(CloudInstance.Public, cred.Cloud);
        Assert.Equal("GitHub Actions deploy", cred.Description);
        Assert.Null(cred.SecretRef);
        Assert.Empty(host.Vault.Contents);

        using var doc = JsonDocument.Parse(sw.ToString());
        Assert.Equal("ci", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("workload-identity-federation", doc.RootElement.GetProperty("kind").GetString());
        Assert.False(doc.RootElement.TryGetProperty("secretRef", out _));
    }

    [Fact]
    public async Task AddFederated_WorksInHeadlessMode_SinceFederationIsPermittedThere()
    {
        using var host = new CommandTestHost(headless: true);

        var exit = await new AuthAddFederatedCliCommand
        {
            Alias = "ci-hl",
            Tenant = "tenant-guid",
            ApplicationId = "app-guid",
        }.RunAsync();

        Assert.Equal(0, exit);

        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        var cred = await store.GetAsync("ci-hl", default);
        Assert.NotNull(cred);
        Assert.Equal(CredentialKind.WorkloadIdentityFederation, cred!.Kind);
        Assert.Empty(host.Vault.Contents);
    }

    [Theory]
    [InlineData("   ", "app-guid")]
    [InlineData("tenant-guid", "   ")]
    public async Task AddFederated_FailsWhenTenantOrApplicationIdIsBlank(string tenant, string applicationId)
    {
        using var host = new CommandTestHost();

        var exit = await new AuthAddFederatedCliCommand
        {
            Alias = "ci-invalid",
            Tenant = tenant,
            ApplicationId = applicationId,
        }.RunAsync();

        Assert.Equal(1, exit);

        var store = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        Assert.Null(await store.GetAsync("ci-invalid", default));
        Assert.Empty(host.Vault.Contents);
    }
}
