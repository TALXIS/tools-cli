using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Services;
using Xunit;

namespace TALXIS.CLI.Tests.Dataverse;

public class ConnectionReferencePlannerTests
{
    private static ConnectionReferenceRecord Reference(string apiId = "shared_office365users", string? connectionId = null, string? display = "Mail")
        => new(Guid.NewGuid(), "new_mail", display, apiId, connectionId, ConnectionBindingStatus.Unbound, null, false, null);

    [Theory]
    [InlineData("/providers/Microsoft.PowerApps/apis/shared_teams", "shared_teams")]
    [InlineData("/providers/Microsoft.PowerApps/apis/shared_almlab-5fx-5fa7d7/", "shared_almlab-5fx-5fa7d7")]
    [InlineData("shared_teams", "shared_teams")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ToShortApiId_ReadsThePathForm(string? stored, string expected)
        => Assert.Equal(expected, ConnectionReferencePlanner.ToShortApiId(stored));

    [Fact]
    public void ToStoredConnectorId_UsesThePathForm()
        => Assert.Equal("/providers/Microsoft.PowerApps/apis/shared_teams", ConnectionReferencePlanner.ToStoredConnectorId("shared_teams"));

    [Theory]
    [InlineData(null, null, false, ConnectionBindingStatus.Unbound)]
    [InlineData("", "Connected", true, ConnectionBindingStatus.Unbound)]
    [InlineData("c1", "Connected", true, ConnectionBindingStatus.Bound)]
    [InlineData("c1", "connected", true, ConnectionBindingStatus.Bound)]
    [InlineData("c1", "Error", true, ConnectionBindingStatus.Broken)]
    [InlineData("c1", null, true, ConnectionBindingStatus.Broken)]
    [InlineData("c1", null, false, ConnectionBindingStatus.Unknown)]
    public void Classify_MapsTheConnectionStateToABindingStatus(string? bound, string? status, bool readable, string expected)
        => Assert.Equal(expected, ConnectionReferencePlanner.Classify(bound, status, readable));

    [Fact]
    public void PlanCreate_NewReference_Creates()
        => Assert.Equal(
            ConnectionReferencePlanner.CreateAction.Create,
            ConnectionReferencePlanner.PlanCreate(null, "shared_office365users", "c1"));

    [Fact]
    public void PlanCreate_SameConnectorAndNoNewBinding_IsUnchanged()
        => Assert.Equal(
            ConnectionReferencePlanner.CreateAction.Unchanged,
            ConnectionReferencePlanner.PlanCreate(Reference(connectionId: "c1"), "shared_office365users", null));

    [Fact]
    public void PlanCreate_SameBinding_IsUnchanged()
        => Assert.Equal(
            ConnectionReferencePlanner.CreateAction.Unchanged,
            ConnectionReferencePlanner.PlanCreate(Reference(connectionId: "c1"), "SHARED_OFFICE365USERS", "c1"));

    [Fact]
    public void PlanCreate_DifferentBinding_Binds()
        => Assert.Equal(
            ConnectionReferencePlanner.CreateAction.Bind,
            ConnectionReferencePlanner.PlanCreate(Reference(connectionId: "c1"), "shared_office365users", "c2"));

    [Fact]
    public void PlanCreate_UnboundReferenceGivenAConnection_Binds()
        => Assert.Equal(
            ConnectionReferencePlanner.CreateAction.Bind,
            ConnectionReferencePlanner.PlanCreate(Reference(), "shared_office365users", "c2"));

    [Fact]
    public void PlanCreate_AnotherConnectorsReference_IsAConflict()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            ConnectionReferencePlanner.PlanCreate(Reference("shared_teams"), "shared_office365users", null));

        Assert.Contains("shared_teams", ex.Message);
    }

    [Theory]
    [InlineData("c1", null, true)]
    [InlineData(null, "Mail", true)]
    [InlineData("c1", "Mail", true)]
    [InlineData("c2", null, false)]
    [InlineData(null, "Other", false)]
    [InlineData(null, null, true)]
    public void IsNoOp_ComparesOnlyWhatWasProvided(string? connectionId, string? displayName, bool expected)
        => Assert.Equal(expected, ConnectionReferencePlanner.IsNoOp(Reference(connectionId: "c1"), connectionId, displayName));

    [Fact]
    public void TryTranslateBindingFailure_ConnectionNotFound_BecomesAValidationError()
    {
        var raw = new InvalidOperationException("Request failed with: 404 {\"error\":{\"code\":\"ConnectionNotFound\"}}");

        Assert.True(ConnectionReferencePlanner.TryTranslateBindingFailure(raw, "c9", "shared_x", out var friendly));

        var ex = Assert.IsType<ArgumentException>(friendly);
        Assert.Contains("'c9'", ex.Message);
        Assert.Contains("connection list --connector shared_x", ex.Message);
    }

    [Fact]
    public void TryTranslateBindingFailure_AuthorizationFailed_PointsAtSharing()
    {
        var raw = new InvalidOperationException("... ConnectionAuthorizationFailed: the caller does not have the minimum required permission");

        Assert.True(ConnectionReferencePlanner.TryTranslateBindingFailure(raw, "c9", "shared_x", out var friendly));

        var ex = Assert.IsType<InvalidOperationException>(friendly);
        Assert.Contains("connection permission add c9 --connector shared_x", ex.Message);
    }

    [Fact]
    public void TryTranslateBindingFailure_LeavesOtherErrorsAlone()
    {
        var raw = new InvalidOperationException("The server is on fire.");

        Assert.False(ConnectionReferencePlanner.TryTranslateBindingFailure(raw, "c9", "shared_x", out var friendly));
        Assert.Same(raw, friendly);
    }
}
