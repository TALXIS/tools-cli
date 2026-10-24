using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Governance.EnvironmentGroup;
using Xunit;

namespace TALXIS.CLI.Tests.Governance.EnvironmentGroup;

[Collection("TxcServicesSerial")]
public sealed class EnvironmentGroupCliCommandTests
{
    [Fact]
    public async Task List_ReturnsAllSeededGroups()
    {
        using var host = new GovernanceCommandTestHost();
        host.Client.Add("Production groups");
        host.Client.Add("Sandboxes");

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new EnvironmentGroupListCliCommand { Format = "json" }.RunAsync();
        }

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal(2, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Create_ReturnsCreatedGroupId()
    {
        using var host = new GovernanceCommandTestHost();

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new EnvironmentGroupCreateCliCommand
            {
                Format = "json",
                DisplayName = "Finance environments",
                Description = "All Finance department environments.",
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("created", document.RootElement.GetProperty("status").GetString());
        Assert.True(Guid.TryParse(document.RootElement.GetProperty("id").GetString(), out _));
    }

    [Fact]
    public async Task Get_ByDisplayName_ResolvesUniqueMatch()
    {
        using var host = new GovernanceCommandTestHost();
        var group = host.Client.Add("Finance environments");

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new EnvironmentGroupGetCliCommand { Format = "json", EnvironmentGroup = "Finance environments" }.RunAsync();
        }

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal(group.Id.ToString(), document.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Get_ByDisplayName_AmbiguousMatch_ReturnsValidationError()
    {
        using var host = new GovernanceCommandTestHost();
        host.Client.Add("Duplicate");
        host.Client.Add("Duplicate");

        var exit = await new EnvironmentGroupGetCliCommand { Format = "json", EnvironmentGroup = "Duplicate" }.RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Get_UnknownId_ReturnsValidationError()
    {
        using var host = new GovernanceCommandTestHost();

        var exit = await new EnvironmentGroupGetCliCommand { Format = "json", EnvironmentGroup = Guid.NewGuid().ToString() }.RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Update_ChangesOnlySuppliedFields()
    {
        using var host = new GovernanceCommandTestHost();
        var group = host.Client.Add("Old name", "Old description");

        var exit = await new EnvironmentGroupUpdateCliCommand
        {
            Format = "json",
            EnvironmentGroup = group.Id.ToString(),
            DisplayName = "New name",
        }.RunAsync();

        Assert.Equal(0, exit);
        var updated = await host.Client.GetAsync(null!, null!, group.Id, CancellationToken.None);
        Assert.Equal("New name", updated!.DisplayName);
        Assert.Equal("Old description", updated.Description);
    }

    [Fact]
    public async Task Update_NoFieldsSupplied_ReturnsValidationError()
    {
        using var host = new GovernanceCommandTestHost();
        var group = host.Client.Add("Name");

        var exit = await new EnvironmentGroupUpdateCliCommand { Format = "json", EnvironmentGroup = group.Id.ToString() }.RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Delete_RemovesGroup()
    {
        using var host = new GovernanceCommandTestHost();
        var group = host.Client.Add("Deletable");

        var exit = await new EnvironmentGroupDeleteCliCommand { Format = "json", Yes = true, EnvironmentGroup = group.Id.ToString() }.RunAsync();

        Assert.Equal(0, exit);
        Assert.Contains(group.Id, host.Client.Deleted);
    }

    [Fact]
    public async Task Delete_WhenGroupHasMembers_ReturnsActionableError()
    {
        var client = new GovernanceCommandTestHost.FakeEnvironmentGroupClient();
        using var host = new GovernanceCommandTestHost(client);
        var group = client.Add("Has members", environmentIds: [Guid.NewGuid()]);
        client.DeleteException = new InvalidOperationException("EnvironmentsInEnvironmentGroup: group has member environments.");

        var exit = await new EnvironmentGroupDeleteCliCommand { Format = "json", Yes = true, EnvironmentGroup = group.Id.ToString() }.RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task EnvironmentAdd_CallsClientWithResolvedGroupId()
    {
        using var host = new GovernanceCommandTestHost();
        var group = host.Client.Add("Target group");
        var environmentId = Guid.NewGuid();

        var exit = await new EnvironmentGroupEnvironmentAddCliCommand
        {
            Format = "json",
            EnvironmentGroup = group.DisplayName,
            Environment = environmentId,
        }.RunAsync();

        Assert.Equal(0, exit);
        Assert.Contains((group.Id, environmentId), host.Client.AddedEnvironments);
    }

    [Fact]
    public async Task EnvironmentRemove_CallsClientWithResolvedGroupId()
    {
        using var host = new GovernanceCommandTestHost();
        var environmentId = Guid.NewGuid();
        var group = host.Client.Add("Target group", environmentIds: [environmentId]);

        var exit = await new EnvironmentGroupEnvironmentRemoveCliCommand
        {
            Format = "json",
            EnvironmentGroup = group.Id.ToString(),
            Environment = environmentId,
        }.RunAsync();

        Assert.Equal(0, exit);
        Assert.Contains((group.Id, environmentId), host.Client.RemovedEnvironments);
    }
}
