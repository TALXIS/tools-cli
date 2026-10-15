using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.Dataverse.Application.Services;
using Xunit;

namespace TALXIS.CLI.Tests.Dataverse;

public class ConnectionReferencePlannerTests
{
    private static ConnectionReferenceRecord Reference(string apiId = "shared_office365users", string? connectionId = null, string? display = "Mail")
        => new(Guid.NewGuid(), "new_mail", display, apiId, connectionId, ConnectionBindingStatus.Unbound, null, false, null);

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

    [Theory]
    [InlineData(0, false, ConnectionOutcomeStatus.Created)]
    [InlineData(0, true, ConnectionOutcomeStatus.Created)]
    [InlineData(2, false, ConnectionOutcomeStatus.Updated)]
    [InlineData(1, false, ConnectionOutcomeStatus.Unchanged)]
    [InlineData(1, true, ConnectionOutcomeStatus.Updated)]
    public void OutcomeOf_CountsAnAdditionToTheSolutionAsAnUpdate(int action, bool addedToSolution, ConnectionOutcomeStatus expected)
        => Assert.Equal(expected, ConnectionReferencePlanner.OutcomeOf((ConnectionReferencePlanner.CreateAction)action, addedToSolution));
}
