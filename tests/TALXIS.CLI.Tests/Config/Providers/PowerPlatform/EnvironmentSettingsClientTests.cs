using System.Net;
using System.Text.Json.Nodes;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.PowerPlatform;

public sealed class EnvironmentSettingsClientTests
{
    #region ParseListResponse

    [Fact]
    public void ParseListResponse_NormalResponse_ReturnsFlattenedSettings()
    {
        var json = """
        {
            "objectResult": [
                {
                    "id": "00000000-0000-0000-0000-000000000001",
                    "tenantId": "00000000-0000-0000-0000-000000000002",
                    "isGroupCreationEnabled": true,
                    "maxRetryCount": 5,
                    "displayName": "Contoso"
                }
            ]
        }
        """;

        var result = EnvironmentSettingsClient.ParseListResponse(json);

        Assert.Equal(3, result.Count);

        var byName = result.ToDictionary(s => s.Name, s => s.Value);
        Assert.True((bool)byName["isGroupCreationEnabled"]!);
        Assert.Equal(5.0, (double)byName["maxRetryCount"]!);
        Assert.Equal("Contoso", (string)byName["displayName"]!);
    }

    [Fact]
    public void ParseListResponse_EmptyObjectResultArray_ReturnsEmpty()
    {
        var json = """{ "objectResult": [] }""";

        var result = EnvironmentSettingsClient.ParseListResponse(json);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseListResponse_MissingObjectResult_ReturnsEmpty()
    {
        var json = """{ "someOtherProperty": 42 }""";

        var result = EnvironmentSettingsClient.ParseListResponse(json);

        Assert.Empty(result);
    }

    [Fact]
    public void ParseListResponse_SkipsEnvelopeProperties()
    {
        var json = """
        {
            "objectResult": [
                {
                    "id": "envelope-id",
                    "tenantId": "envelope-tenant",
                    "someSetting": "value"
                }
            ]
        }
        """;

        var result = EnvironmentSettingsClient.ParseListResponse(json);

        Assert.Single(result);
        Assert.Equal("someSetting", result[0].Name);
        Assert.DoesNotContain(result, s => s.Name == "id");
        Assert.DoesNotContain(result, s => s.Name == "tenantId");
    }

    [Fact]
    public void ParseListResponse_OnlyProcessesFirstItem()
    {
        // Second array entry has a property that should NOT appear in results.
        var json = """
        {
            "objectResult": [
                {
                    "settingA": true
                },
                {
                    "settingB": false
                }
            ]
        }
        """;

        var result = EnvironmentSettingsClient.ParseListResponse(json);

        Assert.Single(result);
        Assert.Equal("settingA", result[0].Name);
        Assert.DoesNotContain(result, s => s.Name == "settingB");
    }

    #endregion

    #region CoerceValue

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    public void CoerceValue_BooleanStrings_ReturnsBoolNode(string input, bool expected)
    {
        var node = EnvironmentSettingsClient.CoerceValue(input);

        Assert.Equal(expected, node.GetValue<bool>());
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("42", 42)]
    [InlineData("-1", -1)]
    public void CoerceValue_IntegerStrings_ReturnsIntNode(string input, int expected)
    {
        var node = EnvironmentSettingsClient.CoerceValue(input);

        Assert.Equal(expected, node.GetValue<int>());
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("not-a-number")]
    [InlineData("")]
    public void CoerceValue_PlainStrings_ReturnsStringNode(string input)
    {
        var node = EnvironmentSettingsClient.CoerceValue(input);

        Assert.Equal(input, node.GetValue<string>());
    }

    #endregion

    #region ListAsync round-trip

    [Fact]
    public async Task ListAsync_RoundTrip_ParsesResponseCorrectly()
    {
        var responseJson = """
        {
            "objectResult": [
                {
                    "id": "env-id",
                    "tenantId": "tenant-id",
                    "isFeatureEnabled": true,
                    "maxItems": 100,
                    "label": "test-label"
                }
            ]
        }
        """;

        var tokens = new FakeAccessTokenService();
        var http = new FakeHttpClientFactoryWrapper(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson)
        });

        var sut = new EnvironmentSettingsClient(tokens, http);

        var result = await sut.ListAsync(
            new Connection
            {
                Id = "conn",
                Provider = ProviderKind.Dataverse,
                EnvironmentUrl = "https://contoso.crm.dynamics.com/",
                Cloud = CloudInstance.Public,
            },
            new Credential
            {
                Id = "cred",
                Kind = CredentialKind.InteractiveBrowser,
            },
            Guid.NewGuid(),
            selectFilter: null,
            CancellationToken.None);

        // id and tenantId should be excluded
        Assert.Equal(3, result.Count);

        var byName = result.ToDictionary(s => s.Name, s => s.Value);
        Assert.True((bool)byName["isFeatureEnabled"]!);
        Assert.Equal(100.0, (double)byName["maxItems"]!);
        Assert.Equal("test-label", (string)byName["label"]!);
    }

    #endregion

    #region Test doubles

    private sealed class FakeAccessTokenService : IAccessTokenService
    {
        public Task<string> AcquireForResourceAsync(Connection connection, Credential credential, Uri resourceUri, CancellationToken ct)
            => Task.FromResult("fake-token");
    }

    private sealed class FakeHttpClientFactoryWrapper : IHttpClientFactoryWrapper
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpClientFactoryWrapper(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public HttpClient Create() => new(new FakeHttpMessageHandler(_handler));
    }

    private sealed class FakeHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }

    #endregion
}
