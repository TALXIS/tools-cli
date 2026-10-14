using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Features.Governance.PolicyRule;
using Xunit;

namespace TALXIS.CLI.Tests.Governance.PolicyRule;

[Collection("TxcServicesSerial")]
public sealed class PolicyRuleCliCommandTests
{
    [Fact]
    public async Task List_ReturnsAllSeededPolicies()
    {
        using var host = new PolicyRuleCommandTestHost();
        host.Client.Add("Block risky connectors");
        host.Client.Add("Finance connector policy");

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new PolicyRuleListCliCommand { Format = "json" }.RunAsync();
        }

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal(2, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Create_WithAllowConnector_BuildsConnectorManagementRuleSet()
    {
        using var host = new PolicyRuleCommandTestHost();

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new PolicyRuleCreateCliCommand
            {
                Format = "json",
                Name = "Finance connector policy",
                AllowConnector = new[] { "shared_office365", "shared_sql=ExecuteProcedure,GetRows" },
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("created", document.RootElement.GetProperty("status").GetString());

        var policy = Assert.Single(host.Client.ListAsync(null!, null!, default).Result);
        var ruleSet = Assert.Single(policy.RuleSets);
        Assert.Equal(PowerPlatformPolicyRuleSet.ConnectorManagementRuleSetId, ruleSet.Id);

        var inputs = PowerPlatformAdvancedConnectorPolicyInputs.FromInputsJson(ruleSet.InputsJson);
        Assert.Equal(2, inputs.AllowedConnectorList.Count);
        Assert.Equal(PowerPlatformAllowedConnectorRule.AllAllowedMode, inputs.AllowedConnectorList[0].AllowedActionsMode);
        Assert.Equal(PowerPlatformAllowedConnectorRule.SomeAllowedMode, inputs.AllowedConnectorList[1].AllowedActionsMode);
        Assert.Equal(new[] { "ExecuteProcedure", "GetRows" }, inputs.AllowedConnectorList[1].AllowedActions);
    }

    [Fact]
    public async Task Create_WithBothAllowConnectorAndInputsJson_ReturnsValidationError()
    {
        using var host = new PolicyRuleCommandTestHost();

        var exit = await new PolicyRuleCreateCliCommand
        {
            Format = "json",
            Name = "Bad policy",
            AllowConnector = new[] { "shared_office365" },
            RuleSetInputsJson = "{}",
        }.RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Update_ByName_RenamesAndAddsRuleSet()
    {
        using var host = new PolicyRuleCommandTestHost();
        var policy = host.Client.Add("Old name");

        var exit = await new PolicyRuleUpdateCliCommand
        {
            Format = "json",
            Policy = "Old name",
            Name = "New name",
            AllowConnector = new[] { "shared_office365" },
        }.RunAsync();

        Assert.Equal(0, exit);
        var updated = (await host.Client.GetAsync(null!, null!, policy.Id, default))!;
        Assert.Equal("New name", updated.Name);
        Assert.Single(updated.RuleSets);
    }

    [Fact]
    public async Task Update_WithNoFields_ReturnsValidationError()
    {
        using var host = new PolicyRuleCommandTestHost();
        var policy = host.Client.Add("Policy");

        var exit = await new PolicyRuleUpdateCliCommand { Format = "json", Policy = policy.Id.ToString() }.RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task RemoveRule_RemovesTargetedRuleSetOnly()
    {
        using var host = new PolicyRuleCommandTestHost();
        var ruleSet = new PowerPlatformPolicyRuleSet(PowerPlatformPolicyRuleSet.ConnectorManagementRuleSetId, "1", "{}");
        var policy = host.Client.Add("Policy", new[] { ruleSet });

        var exit = await new PolicyRuleRemoveRuleCliCommand { Format = "json", Policy = policy.Id.ToString() }.RunAsync();

        Assert.Equal(0, exit);
        var updated = (await host.Client.GetAsync(null!, null!, policy.Id, default))!;
        Assert.Empty(updated.RuleSets);
        Assert.Contains((policy.Id, PowerPlatformPolicyRuleSet.ConnectorManagementRuleSetId), host.Client.RemovedRuleSets);
    }

    [Fact]
    public async Task Assign_ToEnvironmentGroup_RecordsOverrides()
    {
        using var host = new PolicyRuleCommandTestHost();
        var policy = host.Client.Add("Policy");
        var groupId = Guid.NewGuid();
        var excludedEnv = Guid.NewGuid();

        var exit = await new PolicyRuleAssignCliCommand
        {
            Format = "json",
            Policy = policy.Id.ToString(),
            EnvironmentGroup = groupId,
            ExcludeEnvironment = new[] { excludedEnv },
        }.RunAsync();

        Assert.Equal(0, exit);
        var call = Assert.Single(host.Client.GroupAssignments);
        Assert.Equal((policy.Id, groupId), (call.PolicyId, call.GroupId));
        var overrideEntry = Assert.Single(call.Overrides!);
        Assert.Equal(PowerPlatformPolicyBehaviorType.Exclude, overrideEntry.BehaviorType);
        Assert.Equal(excludedEnv, overrideEntry.ResourceId);
    }

    [Fact]
    public async Task Assign_ToEnvironment_Succeeds()
    {
        using var host = new PolicyRuleCommandTestHost();
        var policy = host.Client.Add("Policy");
        var environmentId = Guid.NewGuid();

        var exit = await new PolicyRuleAssignCliCommand
        {
            Format = "json",
            Policy = policy.Id.ToString(),
            Environment = environmentId,
        }.RunAsync();

        Assert.Equal(0, exit);
        Assert.Single(host.Client.EnvironmentAssignments);
    }

    [Fact]
    public async Task Assign_WithNeitherScope_ReturnsValidationError()
    {
        using var host = new PolicyRuleCommandTestHost();
        var policy = host.Client.Add("Policy");

        var exit = await new PolicyRuleAssignCliCommand { Format = "json", Policy = policy.Id.ToString() }.RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Assign_WithBothScopes_ReturnsValidationError()
    {
        using var host = new PolicyRuleCommandTestHost();
        var policy = host.Client.Add("Policy");

        var exit = await new PolicyRuleAssignCliCommand
        {
            Format = "json",
            Policy = policy.Id.ToString(),
            EnvironmentGroup = Guid.NewGuid(),
            Environment = Guid.NewGuid(),
        }.RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Assign_ExcludeEnvironmentWithEnvironmentScope_ReturnsValidationError()
    {
        using var host = new PolicyRuleCommandTestHost();
        var policy = host.Client.Add("Policy");

        var exit = await new PolicyRuleAssignCliCommand
        {
            Format = "json",
            Policy = policy.Id.ToString(),
            Environment = Guid.NewGuid(),
            ExcludeEnvironment = new[] { Guid.NewGuid() },
        }.RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task AssignmentList_FiltersByPolicy()
    {
        using var host = new PolicyRuleCommandTestHost();
        var policyA = host.Client.Add("Policy A");
        var policyB = host.Client.Add("Policy B");
        await host.Client.AssignToEnvironmentAsync(null!, null!, policyA.Id, Guid.NewGuid(), null, default);
        await host.Client.AssignToEnvironmentAsync(null!, null!, policyB.Id, Guid.NewGuid(), null, default);

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new PolicyRuleAssignmentListCliCommand { Format = "json", Policy = policyA.Id }.RunAsync();
        }

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, document.RootElement.GetArrayLength());
        Assert.Equal(policyA.Id.ToString(), document.RootElement[0].GetProperty("policyId").GetString());
    }

    [Fact]
    public async Task AssignmentList_WithMultipleFilters_ReturnsValidationError()
    {
        using var host = new PolicyRuleCommandTestHost();

        var exit = await new PolicyRuleAssignmentListCliCommand
        {
            Format = "json",
            Policy = Guid.NewGuid(),
            Environment = Guid.NewGuid(),
        }.RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Get_UnknownPolicy_ReturnsValidationError()
    {
        using var host = new PolicyRuleCommandTestHost();

        var exit = await new PolicyRuleGetCliCommand { Format = "json", Policy = Guid.NewGuid().ToString() }.RunAsync();

        Assert.Equal(2, exit);
    }
}
