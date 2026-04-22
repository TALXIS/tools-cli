using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Providers.Dataverse.Msal;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.Dataverse;

public sealed class DataverseMsalClientFactoryTests
{
    [Fact]
    public void BuildPublicClient_UsesPacClientIdAndLocalhostRedirect()
    {
        var factory = new DataverseMsalClientFactory();
        var connection = new Connection
        {
            Id = "dev",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm.dynamics.com/",
        };

        var client = factory.BuildPublicClient(connection);
        Assert.Equal(DataverseMsalClientFactory.PublicClientId, client.AppConfig.ClientId);
        Assert.Equal("https://login.microsoftonline.com/organizations/", client.Authority);
    }

    [Fact]
    public void BuildPublicClient_InfersSovereignCloud_FromHostSuffix()
    {
        var factory = new DataverseMsalClientFactory();
        var connection = new Connection
        {
            Id = "dod",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm.appsplatform.us/",
        };

        var client = factory.BuildPublicClient(connection);
        Assert.StartsWith("https://login.microsoftonline.us/", client.Authority);
    }

    [Fact]
    public void BuildPublicClient_UsesExplicitTenant_WhenProvided()
    {
        var factory = new DataverseMsalClientFactory();
        var connection = new Connection
        {
            Id = "dev",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm.dynamics.com/",
            TenantId = "11111111-1111-1111-1111-111111111111",
        };

        var client = factory.BuildPublicClient(connection);
        Assert.EndsWith("/11111111-1111-1111-1111-111111111111/", client.Authority);
    }

    [Fact]
    public void BuildConfidentialClient_RequiresApplicationId()
    {
        var factory = new DataverseMsalClientFactory();
        var connection = new Connection
        {
            Id = "dev",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm.dynamics.com/",
        };
        var credential = new Credential
        {
            Id = "spn",
            Kind = CredentialKind.ClientSecret,
            TenantId = "tenant",
        };

        Assert.Throws<InvalidOperationException>(() =>
            factory.BuildConfidentialClient(connection, credential, new ConfidentialClientMaterial { ClientSecret = "x" }));
    }

    [Fact]
    public void BuildConfidentialClient_RequiresSomeMaterial()
    {
        var factory = new DataverseMsalClientFactory();
        var connection = new Connection
        {
            Id = "dev",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm.dynamics.com/",
        };
        var credential = new Credential
        {
            Id = "spn",
            Kind = CredentialKind.ClientSecret,
            TenantId = "tenant",
            ApplicationId = "22222222-2222-2222-2222-222222222222",
        };

        Assert.Throws<InvalidOperationException>(() =>
            factory.BuildConfidentialClient(connection, credential, new ConfidentialClientMaterial()));
    }

    [Fact]
    public void BuildConfidentialClient_WithSecret_PinsClientIdAndAuthority()
    {
        var factory = new DataverseMsalClientFactory();
        var connection = new Connection
        {
            Id = "dev",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm.dynamics.com/",
            TenantId = "11111111-1111-1111-1111-111111111111",
        };
        var credential = new Credential
        {
            Id = "spn",
            Kind = CredentialKind.ClientSecret,
            ApplicationId = "22222222-2222-2222-2222-222222222222",
        };

        var client = factory.BuildConfidentialClient(
            connection,
            credential,
            new ConfidentialClientMaterial { ClientSecret = "super-secret" });
        Assert.Equal("22222222-2222-2222-2222-222222222222", client.AppConfig.ClientId);
        Assert.EndsWith("/11111111-1111-1111-1111-111111111111/", client.Authority);
    }
}
