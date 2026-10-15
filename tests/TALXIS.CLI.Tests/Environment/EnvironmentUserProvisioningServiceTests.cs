using System.Net;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Core.Resolution;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;
using TALXIS.CLI.Tests.Shared;
using Xunit;
using ConnectionModel = TALXIS.CLI.Core.Model.Connection;

namespace TALXIS.CLI.Tests.Environment;

public sealed class EnvironmentUserProvisioningServiceTests
{
    [Fact]
    public async Task ProvisionUserAsync_ResolvesUpnViaGraph_ThenCallsBapAddUser()
    {
        var connection = new ConnectionModel
        {
            Id = "profile-connection",
            Provider = ProviderKind.Dataverse,
            Cloud = CloudInstance.Public,
            EnvironmentId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
        };
        var credential = new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser };
        var resolver = new FixedConfigurationResolver(new ResolvedProfileContext(
            new Profile { Id = "profile", ConnectionRef = connection.Id, CredentialRef = credential.Id },
            connection,
            credential,
            ResolutionSource.CommandLine));

        HttpRequestMessage? bapRequest = null;
        string? bapBody = null;
        var http = new FakeHttpClientFactoryWrapper(req =>
        {
            if (req.RequestUri!.Host.Contains("graph.microsoft.com"))
            {
                Assert.Contains("userPrincipalName%20eq%20%27someone%40contoso.com%27", req.RequestUri.AbsoluteUri);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    {
                      "value": [
                        {
                          "id": "11111111-1111-1111-1111-111111111111",
                          "displayName": "Someone",
                          "userPrincipalName": "someone@contoso.com"
                        }
                      ]
                    }
                    """)
                };
            }

            bapRequest = req;
            bapBody = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
        });

        var sut = new EnvironmentUserProvisioningService(
            resolver,
            new EnvironmentIdResolver(new EmptyEnvironmentCatalog()),
            new MicrosoftGraphClient(new FakeAccessTokenService(), http),
            new EnvironmentSettingsClient(new FakeAccessTokenService(), http),
            new FakeAccessTokenService(),
            http);

        var result = await sut.ProvisionUserAsync("profile", "someone@contoso.com", CancellationToken.None);

        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.AadObjectId);
        Assert.Equal("someone@contoso.com", result.UserPrincipalName);
        Assert.Equal("Someone", result.DisplayName);

        Assert.NotNull(bapRequest);
        Assert.Equal(HttpMethod.Post, bapRequest!.Method);
        Assert.Contains(
            "scopes/admin/environments/55555555-5555-5555-5555-555555555555/addUser?api-version=2021-04-01",
            bapRequest.RequestUri!.AbsoluteUri);
        Assert.Contains("11111111-1111-1111-1111-111111111111", bapBody);
    }

    [Fact]
    public async Task ProvisionUserAsync_ThrowsWhenGraphUserNotFound()
    {
        var connection = new ConnectionModel
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            Cloud = CloudInstance.Public,
            EnvironmentId = Guid.NewGuid(),
        };
        var credential = new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser };
        var resolver = new FixedConfigurationResolver(new ResolvedProfileContext(
            new Profile { Id = "profile", ConnectionRef = connection.Id, CredentialRef = credential.Id },
            connection,
            credential,
            ResolutionSource.CommandLine));

        var http = new FakeHttpClientFactoryWrapper(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"value\":[]}")
        });

        var sut = new EnvironmentUserProvisioningService(
            resolver,
            new EnvironmentIdResolver(new EmptyEnvironmentCatalog()),
            new MicrosoftGraphClient(new FakeAccessTokenService(), http),
            new EnvironmentSettingsClient(new FakeAccessTokenService(), http),
            new FakeAccessTokenService(),
            http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ProvisionUserAsync("profile", "missing@contoso.com", CancellationToken.None));

        Assert.Contains("was not found", ex.Message);
    }

    [Fact]
    public async Task ProvisionUserAsync_ThrowsWhenGraphMatchesMultipleUsers()
    {
        var connection = new ConnectionModel
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            Cloud = CloudInstance.Public,
            EnvironmentId = Guid.NewGuid(),
        };
        var credential = new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser };
        var resolver = new FixedConfigurationResolver(new ResolvedProfileContext(
            new Profile { Id = "profile", ConnectionRef = connection.Id, CredentialRef = credential.Id },
            connection,
            credential,
            ResolutionSource.CommandLine));

        var http = new FakeHttpClientFactoryWrapper(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
            {
              "value": [
                { "id": "11111111-1111-1111-1111-111111111111", "displayName": "A", "userPrincipalName": "a@contoso.com" },
                { "id": "22222222-2222-2222-2222-222222222222", "displayName": "B", "userPrincipalName": "b@contoso.com" }
              ]
            }
            """)
        });

        var sut = new EnvironmentUserProvisioningService(
            resolver,
            new EnvironmentIdResolver(new EmptyEnvironmentCatalog()),
            new MicrosoftGraphClient(new FakeAccessTokenService(), http),
            new EnvironmentSettingsClient(new FakeAccessTokenService(), http),
            new FakeAccessTokenService(),
            http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.ProvisionUserAsync("profile", "ambiguous", CancellationToken.None));

        Assert.Contains("Multiple Entra users matched", ex.Message);
    }

    [Fact]
    public async Task SelfElevateAsync_PostsApplyAdminRole_ForResolvedEnvironment()
    {
        var connection = new ConnectionModel
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            Cloud = CloudInstance.Public,
            EnvironmentId = Guid.NewGuid(),
        };
        var credential = new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser };
        var resolver = new FixedConfigurationResolver(new ResolvedProfileContext(
            new Profile { Id = "profile", ConnectionRef = connection.Id, CredentialRef = credential.Id },
            connection,
            credential,
            ResolutionSource.CommandLine));

        var environmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        HttpRequestMessage? capturedRequest = null;
        var http = new FakeHttpClientFactoryWrapper(req =>
        {
            capturedRequest = req;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
        });

        var sut = new EnvironmentUserProvisioningService(
            resolver,
            new EnvironmentIdResolver(new EmptyEnvironmentCatalog()),
            new MicrosoftGraphClient(new FakeAccessTokenService(), http),
            new EnvironmentSettingsClient(new FakeAccessTokenService(), http),
            new FakeAccessTokenService(),
            http);

        await sut.SelfElevateAsync(connection, credential, environmentId, CancellationToken.None);

        Assert.NotNull(capturedRequest);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Contains($"usermanagement/environments/{environmentId}/user/applyAdminRole", capturedRequest.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task SelfElevateAsync_ThrowsWhenApplyAdminRoleFails()
    {
        var connection = new ConnectionModel
        {
            Id = "conn",
            Provider = ProviderKind.Dataverse,
            Cloud = CloudInstance.Public,
            EnvironmentId = Guid.NewGuid(),
        };
        var credential = new Credential { Id = "cred", Kind = CredentialKind.InteractiveBrowser };
        var resolver = new FixedConfigurationResolver(new ResolvedProfileContext(
            new Profile { Id = "profile", ConnectionRef = connection.Id, CredentialRef = credential.Id },
            connection,
            credential,
            ResolutionSource.CommandLine));

        var http = new FakeHttpClientFactoryWrapper(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"error\":\"insufficient privileges\"}")
        });

        var sut = new EnvironmentUserProvisioningService(
            resolver,
            new EnvironmentIdResolver(new EmptyEnvironmentCatalog()),
            new MicrosoftGraphClient(new FakeAccessTokenService(), http),
            new EnvironmentSettingsClient(new FakeAccessTokenService(), http),
            new FakeAccessTokenService(),
            http);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SelfElevateAsync(connection, credential, Guid.NewGuid(), CancellationToken.None));

        Assert.Contains("self-elevation failed", ex.Message);
    }

}
