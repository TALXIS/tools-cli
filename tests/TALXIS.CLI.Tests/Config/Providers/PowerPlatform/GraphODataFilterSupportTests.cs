using TALXIS.CLI.Platform.PowerPlatform.Control.Graph;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.PowerPlatform;

public sealed class GraphODataFilterSupportTests
{
    [Fact]
    public void EscapeODataString_EscapesSingleQuotes()
    {
        Assert.Equal("O''Brien", GraphODataFilterSupport.EscapeODataString("O'Brien"));
    }

    [Fact]
    public void BuildIdentifierFilter_NonGuidInput_OmitsGuidTypedClauses()
    {
        var filter = GraphODataFilterSupport.BuildIdentifierFilter(
            "Contoso CLI", ["appId", "id"], ["displayName"]);

        Assert.Equal("displayName eq 'Contoso CLI'", filter);
    }

    [Fact]
    public void BuildIdentifierFilter_GuidInput_IncludesGuidTypedClausesInOrder()
    {
        var filter = GraphODataFilterSupport.BuildIdentifierFilter(
            "11111111-1111-1111-1111-111111111111", ["appId", "id"], ["displayName"]);

        Assert.Equal(
            "appId eq '11111111-1111-1111-1111-111111111111' or " +
            "id eq '11111111-1111-1111-1111-111111111111' or " +
            "displayName eq '11111111-1111-1111-1111-111111111111'",
            filter);
    }

    [Fact]
    public void BuildIdentifierFilter_SingleGuidProperty_MatchesUserFilterShape()
    {
        var filter = GraphODataFilterSupport.BuildIdentifierFilter(
            "22222222-2222-2222-2222-222222222222", ["id"], ["userPrincipalName"]);

        Assert.Equal(
            "id eq '22222222-2222-2222-2222-222222222222' or " +
            "userPrincipalName eq '22222222-2222-2222-2222-222222222222'",
            filter);
    }

    [Fact]
    public void BuildIdentifierFilter_EscapesQuotesInStringClause()
    {
        var filter = GraphODataFilterSupport.BuildIdentifierFilter(
            "O'Brien", ["id"], ["userPrincipalName"]);

        Assert.Equal("userPrincipalName eq 'O''Brien'", filter);
    }
}
