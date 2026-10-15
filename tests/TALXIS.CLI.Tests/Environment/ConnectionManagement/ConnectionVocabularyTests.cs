using TALXIS.CLI.Core.Platforms.PowerPlatform;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectionVocabularyTests
{
    [Theory]
    [InlineData(ConnectionPermissionRole.CanView, "can-view", "CanView")]
    [InlineData(ConnectionPermissionRole.CanViewWithShare, "can-view-with-share", "CanViewWithShare")]
    [InlineData(ConnectionPermissionRole.CanEdit, "can-edit", "CanEdit")]
    [InlineData(ConnectionPermissionRole.Owner, "owner", "Owner")]
    public void Roles_HaveOneTableForBothVocabularies(ConnectionPermissionRole role, string cli, string platform)
    {
        Assert.Equal(cli, ConnectionRoleNames.ToCli(role));
        Assert.Equal(platform, ConnectionRoleNames.ToPlatform(role));
        Assert.True(ConnectionRoleNames.TryFromPlatform(platform, out var fromPlatform));
        Assert.Equal(role, fromPlatform);
    }

    [Theory]
    [InlineData("can-view", ConnectionPermissionRole.CanView)]
    [InlineData("can-view-with-share", ConnectionPermissionRole.CanViewWithShare)]
    [InlineData("can-edit", ConnectionPermissionRole.CanEdit)]
    public void TryParseCli_AcceptsTheKebabCaseRoles(string text, ConnectionPermissionRole expected)
    {
        Assert.True(ConnectionRoleNames.TryParseCli(text, out var role));
        Assert.Equal(expected, role);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CanView")]
    [InlineData("can_view")]
    [InlineData("CAN-EDIT")]
    [InlineData("owner")]
    [InlineData("can-use")]
    public void TryParseCli_RejectsEverythingElse(string? text)
        => Assert.False(ConnectionRoleNames.TryParseCli(text, out _));

    [Fact]
    public void TryFromPlatform_RejectsUnknownRoles()
        => Assert.False(ConnectionRoleNames.TryFromPlatform("CanDance", out _));

    [Theory]
    [InlineData(ConnectionOutcomeStatus.Created, "created")]
    [InlineData(ConnectionOutcomeStatus.Updated, "updated")]
    [InlineData(ConnectionOutcomeStatus.Unchanged, "unchanged")]
    [InlineData(ConnectionOutcomeStatus.ConsentRequired, "consent-required")]
    [InlineData(ConnectionOutcomeStatus.Failed, "failed")]
    [InlineData(ConnectionOutcomeStatus.Assigned, "assigned")]
    [InlineData(ConnectionOutcomeStatus.Removed, "removed")]
    [InlineData(ConnectionOutcomeStatus.Deleted, "deleted")]
    public void OutcomeStatuses_SerialiseToKebabCase(ConnectionOutcomeStatus status, string expected)
        => Assert.Equal(expected, status.ToKebabCase());

    [Theory]
    [InlineData("shared_teams", "shared_teams")]
    [InlineData("/providers/Microsoft.PowerApps/apis/shared_teams", "shared_teams")]
    [InlineData("/providers/Microsoft.PowerApps/apis/shared_teams/", "shared_teams")]
    public void ConnectorIds_ToShort_AcceptsBothForms(string input, string expected)
    {
        Assert.Equal(expected, ConnectorIds.ToShort(input));
        Assert.Equal("/providers/Microsoft.PowerApps/apis/shared_teams", ConnectorIds.ToPath(input));
    }

    [Theory]
    [InlineData("/providers/Microsoft.PowerApps/apis/shared_almlab-5fx-5fa7d7/", "shared_almlab-5fx-5fa7d7")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ConnectorIds_ToShortOrEmpty_ToleratesAbsentValues(string? stored, string expected)
        => Assert.Equal(expected, ConnectorIds.ToShortOrEmpty(stored));

    [Fact]
    public void ConnectorIds_ToShort_RejectsABlankConnector()
        => Assert.Throws<ArgumentException>(() => ConnectorIds.ToShort(" "));

    [Fact]
    public void ConnectorKind_NamesBothKinds()
    {
        Assert.Equal(ConnectorKind.Custom, ConnectorKind.Of(true));
        Assert.Equal(ConnectorKind.BuiltIn, ConnectorKind.Of(false));
        Assert.True(ConnectorKind.IsKnown("custom"));
        Assert.False(ConnectorKind.IsKnown("weird"));
        Assert.False(ConnectorKind.IsKnown(null));
    }

    [Fact]
    public void StatusCodes_MatchConnectedWhateverTheCasing()
    {
        Assert.True(ConnectionStatusCodes.IsConnected("connected"));
        Assert.False(ConnectionStatusCodes.IsConnected("Error"));
        Assert.False(ConnectionStatusCodes.IsConnected(null));
    }
}
