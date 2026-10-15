using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectionJsonParserTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void ParseConnection_ReadsTheSummaryFields()
    {
        var connection = Parse(ConnectionFixtures.Connected("abc", "shared_office365users", "Mail"));

        var summary = ConnectionJsonParser.ParseConnection(connection, "shared_office365users");

        Assert.Equal("abc", summary.ConnectionId);
        Assert.Equal("Mail", summary.DisplayName);
        Assert.Equal(ConnectionStatusCodes.Connected, summary.ConnectionStatus);
        Assert.Null(summary.StatusMessage);
        Assert.Equal("User", summary.CreatedByType);
        Assert.Equal("admin@contoso.com", summary.CreatedBy);
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 17, 36, 41, 820, TimeSpan.Zero), summary.CreatedTime);
    }

    [Fact]
    public void ParseConnection_ToleratesAMissingPropertiesObject()
    {
        var summary = ConnectionJsonParser.ParseConnection(Parse("""{"name":"x"}"""), "shared_a");

        Assert.Equal("x", summary.ConnectionId);
        Assert.Null(summary.DisplayName);
        Assert.Null(summary.ConnectionStatus);
    }

    [Fact]
    public void ReadState_ReportsTheFirstErrorWithItsCode()
    {
        var state = ConnectionJsonParser.ReadState(Parse(ConnectionFixtures.Unauthenticated("c1", "shared_office365users", true)));

        Assert.Equal("Error", state.Status);
        Assert.Equal(ConnectionStatusCodes.Unauthenticated, state.ErrorCode);
        Assert.Equal("This connection is not authenticated.", state.Error);
        Assert.True(state.IsUnauthenticated);
    }

    [Fact]
    public void ReadState_OfAHealthyConnection_HasNoError()
    {
        var state = ConnectionJsonParser.ReadState(Parse("""{"properties":{"statuses":[{"status":"Connected"}]}}"""));

        Assert.Equal("Connected", state.Status);
        Assert.Null(state.Error);
        Assert.False(state.IsUnauthenticated);
    }

    [Fact]
    public void ReadState_WithoutStatuses_IsEmpty()
        => Assert.Equal(new ConnectionState(null, null, null), ConnectionJsonParser.ReadState(Parse("{}")));

    [Fact]
    public void ReadConsentInfo_ReadsBothLinks()
    {
        var info = ConnectionJsonParser.ReadConsentInfo(Parse(ConnectionFixtures.Unauthenticated("c1", "shared_office365users", true)));

        Assert.Equal("https://usa.consent.azure-apihub.net/firstPartyLogin?data=abc", info.FirstPartyLoginUri!.ToString());
        Assert.StartsWith("https://usa.consent.azure-apihub.net/login", info.ConsentLink);
    }

    [Fact]
    public void ReadConsentInfo_WithoutFirstPartyLogin_HasOnlyTheLink()
    {
        var info = ConnectionJsonParser.ReadConsentInfo(Parse(ConnectionFixtures.Unauthenticated("c1", "shared_office365users", false)));

        Assert.Null(info.FirstPartyLoginUri);
        Assert.NotNull(info.ConsentLink);
    }

    [Fact]
    public void ReadTestLink_TakesTheFirstTestLink()
    {
        var link = ConnectionJsonParser.ReadTestLink(Parse(ConnectionFixtures.Unauthenticated("c1", "shared_office365users", false)));

        Assert.EndsWith("/connections/c1/testconnection", link!.AbsolutePath);
    }

    [Fact]
    public void ReadTestLink_WithoutLinks_IsNull()
        => Assert.Null(ConnectionJsonParser.ReadTestLink(Parse("""{"properties":{}}""")));

    [Theory]
    [InlineData("/providers/Microsoft.PowerApps/apis/shared_teams", "shared_teams")]
    [InlineData("shared_teams", "shared_teams")]
    public void ApiIdOf_AcceptsEitherForm(string apiId, string expected)
        => Assert.Equal(expected, ConnectionJsonParser.ApiIdOf(Parse("{\"properties\":{\"apiId\":\"" + apiId + "\"}}")));

    [Fact]
    public void ApiIdOf_WithoutAnApiId_IsNull()
        => Assert.Null(ConnectionJsonParser.ApiIdOf(Parse("""{"properties":{}}""")));
}
