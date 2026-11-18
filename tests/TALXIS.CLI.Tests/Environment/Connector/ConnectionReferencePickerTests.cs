using TALXIS.CLI.Features.Environment.Connector.Operation;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Connector;

public class ConnectionReferencePickerTests
{
    private static readonly ReferenceCandidate Bound = new("mtj_teams", "Teams (Matej)", "conn-1");
    private static readonly ReferenceCandidate OtherBound = new("mtj_teams_bot", "Teams (bot)", "conn-2");
    private static readonly ReferenceCandidate Unbound = new("mtj_teams_new", "Teams (new)", null);

    [Fact]
    public void TheOnlyBoundReference_IsPickedWithoutAsking()
    {
        var pick = ConnectionReferencePicker.Pick([Unbound, Bound], "shared_teams", requested: null);

        Assert.Same(Bound, pick.Reference);
        Assert.Null(pick.Status);
    }

    [Fact]
    public void SeveralBoundReferences_AskWhichOne()
    {
        var pick = ConnectionReferencePicker.Pick([Bound, Unbound, OtherBound], "shared_teams", requested: null);

        Assert.Null(pick.Reference);
        Assert.Equal(DynamicValuesStatus.NeedsChoice, pick.Status);
        Assert.Equal([Bound, OtherBound], pick.Bound);
        Assert.Contains("--connection-reference", pick.Message);
    }

    [Fact]
    public void NoBoundReference_NeedsAConnection()
    {
        var pick = ConnectionReferencePicker.Pick([Unbound], "shared_teams", requested: null);

        Assert.Null(pick.Reference);
        Assert.Equal(DynamicValuesStatus.NeedsConnection, pick.Status);
        Assert.Null(pick.InputError);
    }

    [Fact]
    public void ANamedReference_WinsOverTheOnlyBoundOne_IgnoringCase()
    {
        var pick = ConnectionReferencePicker.Pick([Bound, OtherBound], "shared_teams", requested: "MTJ_TEAMS_BOT");

        Assert.Same(OtherBound, pick.Reference);
    }

    [Fact]
    public void ANamedReferenceThatIsNotBound_NeedsAConnection()
    {
        var pick = ConnectionReferencePicker.Pick([Bound, Unbound], "shared_teams", requested: "mtj_teams_new");

        Assert.Null(pick.Reference);
        Assert.Equal(DynamicValuesStatus.NeedsConnection, pick.Status);
        Assert.Contains("mtj_teams_new", pick.Message);
    }

    [Fact]
    public void AnUnknownNamedReference_IsAnInputErrorListingTheAvailableOnes()
    {
        var pick = ConnectionReferencePicker.Pick([Bound, Unbound], "shared_teams", requested: "mtj_nope");

        Assert.Null(pick.Status);
        Assert.Contains("mtj_teams, mtj_teams_new", pick.InputError);
    }
}
