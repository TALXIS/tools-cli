using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public sealed class DataverseSecurityPrincipalManagerTests
{
    [Fact]
    public void ResolveOptionalSingle_ReturnsNull_WhenNoMatchesExist()
    {
        var result = DataverseSecurityPrincipalManager.ResolveOptionalSingle(
            Array.Empty<DataverseRoleRecord>(),
            "Dataverse role",
            "Salesperson",
            static role => new DataverseLookupCandidate(role.Id, role.Name, role.BusinessUnitName));

        Assert.Null(result);
    }

    [Fact]
    public void ResolveOptionalSingle_ThrowsAmbiguousMatchException_WithCandidates()
    {
        var matches = new[]
        {
            new DataverseRoleRecord(Guid.Parse("11111111-1111-1111-1111-111111111111"), "Salesperson", Guid.Empty, "BU 1"),
            new DataverseRoleRecord(Guid.Parse("22222222-2222-2222-2222-222222222222"), "Salesperson", Guid.Empty, "BU 2")
        };

        var ex = Assert.Throws<DataverseAmbiguousMatchException>(() =>
            DataverseSecurityPrincipalManager.ResolveOptionalSingle(
                matches,
                "Dataverse role",
                "Salesperson",
                static role => new DataverseLookupCandidate(role.Id, role.Name, role.BusinessUnitName)));

        Assert.Equal("Salesperson", ex.Identifier);
        Assert.Equal(2, ex.Candidates.Count);
    }

    [Theory]
    [InlineData(DataverseTeamType.Owner)]
    [InlineData(DataverseTeamType.Access)]
    [InlineData(DataverseTeamType.AadSecurityGroup)]
    [InlineData(DataverseTeamType.AadOfficeGroup)]
    public void TeamType_RoundTrips_ThroughDataverseOptionValues(DataverseTeamType teamType)
    {
        var value = DataverseSecurityPrincipalManager.ToTeamTypeValue(teamType);
        var roundTrip = DataverseSecurityPrincipalManager.FromTeamTypeValue(value);

        Assert.Equal(teamType, roundTrip);
    }

    [Theory]
    [InlineData(DataverseTeamMembershipType.MembersAndGuests)]
    [InlineData(DataverseTeamMembershipType.Members)]
    [InlineData(DataverseTeamMembershipType.Owners)]
    [InlineData(DataverseTeamMembershipType.Guests)]
    public void MembershipType_RoundTrips_ThroughDataverseOptionValues(DataverseTeamMembershipType membershipType)
    {
        var value = DataverseSecurityPrincipalManager.ToMembershipTypeValue(membershipType);
        var roundTrip = DataverseSecurityPrincipalManager.FromMembershipTypeValue(value);

        Assert.Equal(membershipType, roundTrip);
    }

    [Fact]
    public void EnsureTeamMembershipCanBeManaged_RejectsAadManagedTeams()
    {
        var team = new DataverseTeamRecord(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "AAD-backed",
            DataverseTeamType.AadSecurityGroup,
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            DataverseTeamMembershipType.Members,
            Guid.Empty,
            "Root BU",
            false,
            false);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DataverseSecurityPrincipalManager.EnsureTeamMembershipCanBeManaged(team, "Adding"));

        Assert.Contains("managed in Entra ID", ex.Message);
    }
}
