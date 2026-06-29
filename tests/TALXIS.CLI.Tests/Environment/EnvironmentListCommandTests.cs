using Microsoft.Extensions.DependencyInjection;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Features.Environment;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Tests.Config.Commands;
using Xunit;

namespace TALXIS.CLI.Tests.Environment;

[Collection("Sequential")]
public class EnvironmentListCommandTests
{
    private static async Task SeedCredentialAsync(CommandTestHost host, string id)
    {
        var store = host.Provider.GetRequiredService<ICredentialStore>();
        await store.UpsertAsync(new Credential
        {
            Id = id,
            Kind = CredentialKind.InteractiveBrowser,
            TenantId = "tenant-guid",
            Cloud = CloudInstance.Public,
        }, default);
    }

    private static void SeedEnvironment(CommandTestHost host, string name, string url)
    {
        host.EnvironmentCatalog.Add(new PowerPlatformEnvironmentSummary(
            EnvironmentId: Guid.NewGuid(),
            DisplayName: name,
            EnvironmentUrl: new Uri(url),
            UniqueName: null,
            DomainName: null,
            OrganizationId: Guid.NewGuid(),
            EnvironmentType: EnvironmentType.Sandbox));
    }

    [Fact]
    public async Task List_SingleCredential_ReturnsSuccess()
    {
        using var host = new CommandTestHost();
        await SeedCredentialAsync(host, "only-cred");
        SeedEnvironment(host, "Dev", "https://dev.crm4.dynamics.com/");

        var exit = await new EnvironmentListCliCommand().RunAsync();

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task List_NoCredentials_ReturnsValidationError()
    {
        using var host = new CommandTestHost();

        var exit = await new EnvironmentListCliCommand().RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task List_MultipleCredentialsWithoutFlag_ReturnsValidationError()
    {
        using var host = new CommandTestHost();
        await SeedCredentialAsync(host, "cred-a");
        await SeedCredentialAsync(host, "cred-b");

        var exit = await new EnvironmentListCliCommand().RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task List_ExplicitCredential_ReturnsSuccess()
    {
        using var host = new CommandTestHost();
        await SeedCredentialAsync(host, "cred-a");
        await SeedCredentialAsync(host, "cred-b");
        SeedEnvironment(host, "Dev", "https://dev.crm4.dynamics.com/");

        var exit = await new EnvironmentListCliCommand { Credential = "cred-b" }.RunAsync();

        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task List_UnknownCredential_ReturnsValidationError()
    {
        using var host = new CommandTestHost();
        await SeedCredentialAsync(host, "cred-a");

        var exit = await new EnvironmentListCliCommand { Credential = "does-not-exist" }.RunAsync();

        Assert.Equal(2, exit);
    }
}
