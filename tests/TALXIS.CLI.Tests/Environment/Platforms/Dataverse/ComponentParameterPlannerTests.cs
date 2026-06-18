using TALXIS.CLI.Core.Deployment;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

public class ComponentParameterPlannerTests
{
    private const string SampleConnectionId = "4445162937b84457a3465d2f0c2cab7e";

    private static DeploymentSettings Settings(
        IEnumerable<ConnectionReferenceSetting>? connections = null,
        IEnumerable<EnvironmentVariableSetting>? variables = null) => new()
    {
        ConnectionReferences = (connections ?? []).ToList(),
        EnvironmentVariables = (variables ?? []).ToList(),
    };

    [Fact]
    public void Plan_MapsConnectionReferenceFromSettings()
    {
        var settings = Settings(connections: [
            new ConnectionReferenceSetting { LogicalName = "tst_sp", ConnectionId = SampleConnectionId, ConnectorId = "/providers/x/file" }
        ]);

        var plan = ComponentParameterPlanner.Plan(
            settings,
            [new SolutionConnectionReference("tst_sp", "/providers/x/solution")],
            []);

        var planned = Assert.Single(plan.ConnectionReferences);
        Assert.Equal("tst_sp", planned.LogicalName);
        Assert.Equal(SampleConnectionId, planned.ConnectionId);
        Assert.Equal("/providers/x/file", planned.ConnectorId); // file value wins over solution's
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Plan_FallsBackToSolutionConnectorIdWhenFileOmitsIt()
    {
        var settings = Settings(connections: [
            new ConnectionReferenceSetting { LogicalName = "tst_sp", ConnectionId = SampleConnectionId }
        ]);

        var plan = ComponentParameterPlanner.Plan(
            settings,
            [new SolutionConnectionReference("tst_sp", "/providers/x/solution")],
            []);

        Assert.Equal("/providers/x/solution", Assert.Single(plan.ConnectionReferences).ConnectorId);
    }

    [Fact]
    public void Plan_WarnsAndSkipsConnectionNotInSolution()
    {
        var settings = Settings(connections: [
            new ConnectionReferenceSetting { LogicalName = "tst_ghost", ConnectionId = SampleConnectionId }
        ]);

        var plan = ComponentParameterPlanner.Plan(settings, [], []);

        Assert.Empty(plan.ConnectionReferences);
        Assert.Contains(plan.Warnings, w => w.Contains("tst_ghost") && w.Contains("not part of the solution"));
    }

    [Fact]
    public void Plan_WarnsAndSkipsConnectionWithBlankId()
    {
        var settings = Settings(connections: [
            new ConnectionReferenceSetting { LogicalName = "tst_sp", ConnectionId = "" }
        ]);

        var plan = ComponentParameterPlanner.Plan(
            settings,
            [new SolutionConnectionReference("tst_sp", null)],
            []);

        Assert.Empty(plan.ConnectionReferences);
        Assert.Contains(plan.Warnings, w => w.Contains("no ConnectionId"));
    }

    [Fact]
    public void Plan_PassesConnectionIdThroughVerbatim()
    {
        // connectionreference.connectionid is a string column — the id must be applied
        // exactly as supplied (no GUID round-trip that would re-introduce hyphens).
        var settings = Settings(connections: [
            new ConnectionReferenceSetting { LogicalName = "tst_sp", ConnectionId = "  f3d887a13d0d4faba017870352e3efce  " }
        ]);

        var plan = ComponentParameterPlanner.Plan(
            settings,
            [new SolutionConnectionReference("tst_sp", null)],
            []);

        Assert.Equal("f3d887a13d0d4faba017870352e3efce", Assert.Single(plan.ConnectionReferences).ConnectionId);
    }

    [Fact]
    public void Plan_MapsEnvironmentVariableAndCarriesValueId()
    {
        var settings = Settings(variables: [
            new EnvironmentVariableSetting { SchemaName = "tst_env", Value = "UAT" }
        ]);

        var plan = ComponentParameterPlanner.Plan(
            settings,
            [],
            [new SolutionEnvironmentVariable("tst_env", "value-id-1")]);

        var planned = Assert.Single(plan.EnvironmentVariables);
        Assert.Equal("tst_env", planned.SchemaName);
        Assert.Equal("UAT", planned.Value);
        Assert.Equal("value-id-1", planned.ValueId);
    }

    [Fact]
    public void Plan_PrefersValueComponentOverDefinitionForSameSchema()
    {
        var settings = Settings(variables: [
            new EnvironmentVariableSetting { SchemaName = "tst_env", Value = "UAT" }
        ]);

        var plan = ComponentParameterPlanner.Plan(
            settings,
            [],
            [
                new SolutionEnvironmentVariable("tst_env", null),       // definition, no id
                new SolutionEnvironmentVariable("tst_env", "value-id"), // value, has id
            ]);

        Assert.Equal("value-id", Assert.Single(plan.EnvironmentVariables).ValueId);
    }

    [Fact]
    public void Plan_WarnsAndSkipsVariableNotInSolution()
    {
        var settings = Settings(variables: [
            new EnvironmentVariableSetting { SchemaName = "tst_ghost", Value = "x" }
        ]);

        var plan = ComponentParameterPlanner.Plan(settings, [], []);

        Assert.Empty(plan.EnvironmentVariables);
        Assert.Contains(plan.Warnings, w => w.Contains("tst_ghost") && w.Contains("not part of the solution"));
    }

    [Fact]
    public void Plan_WarnsAndSkipsVariableWithBlankValue()
    {
        var settings = Settings(variables: [
            new EnvironmentVariableSetting { SchemaName = "tst_env", Value = "" }
        ]);

        var plan = ComponentParameterPlanner.Plan(
            settings,
            [],
            [new SolutionEnvironmentVariable("tst_env", null)]);

        Assert.Empty(plan.EnvironmentVariables);
        Assert.Contains(plan.Warnings, w => w.Contains("no value"));
    }

    [Fact]
    public void Plan_IsEmpty_WhenNothingApplies()
    {
        var plan = ComponentParameterPlanner.Plan(Settings(), [], []);

        Assert.True(plan.IsEmpty);
        Assert.Empty(plan.Warnings);
    }
}
