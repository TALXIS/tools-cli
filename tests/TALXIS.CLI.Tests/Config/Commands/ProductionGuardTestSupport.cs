using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Features.Config.Profile;
using Xunit;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;

namespace TALXIS.CLI.Tests.Config.Commands;

/// <summary>Shared seeding for the destructive-operation guard tests.</summary>
internal static class ProductionGuardTestSupport
{
    public const string EnvUrl = "https://devbox.crm.dynamics.com";

    public static async Task SeedAsync(CommandTestHost host, EnvironmentType? type = null, string? displayName = null)
    {
        var creds = (ICredentialStore)host.Provider.GetService(typeof(ICredentialStore))!;
        var conns = (IConnectionStore)host.Provider.GetService(typeof(IConnectionStore))!;
        await creds.UpsertAsync(new Credential
        {
            Id = "cred",
            Kind = CredentialKind.InteractiveBrowser,
            TenantId = "contoso.onmicrosoft.com",
        }, default);
        await conns.UpsertAsync(new ConnectionModel
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = EnvUrl,
            EnvironmentType = type,
            DisplayName = displayName,
        }, default);
        Assert.Equal(0, await new ProfileCreateCliCommand { Name = "p", Auth = "cred", Connection = "conn" }.RunAsync());
    }
}
