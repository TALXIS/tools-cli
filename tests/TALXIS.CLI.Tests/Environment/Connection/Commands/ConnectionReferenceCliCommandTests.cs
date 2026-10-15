using System.Text.Json;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.ConnectionReference;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement.Commands;

[Collection("TxcServicesSerial")]
public sealed class ConnectionReferenceCliCommandTests
{
    private static readonly string[] GetFields =
        ["id", "logicalName", "displayName", "apiId", "connectionId", "bindingStatus", "isManaged"];

    private static HashSet<string> Properties(string json)
        => JsonDocument.Parse(json).RootElement.EnumerateObject().Select(p => p.Name).ToHashSet();

    [Fact]
    public async Task Create_PrintsTheStatusAndTheRowIdAlongsideTheFieldsOfGet()
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectionReferenceCreateCliCommand
        {
            LogicalName = "new_mail", Connector = "shared_office365users", DisplayName = "Mail",
        });

        Assert.Equal(0, exit);
        Assert.Contains("\"status\": \"created\"", output);
        Assert.Contains($"\"id\": \"{FakeReferences.ReferenceId}\"", output);
        Assert.Superset(GetFields.Append("status").ToHashSet(), Properties(output));
    }

    [Fact]
    public async Task Update_HasTheSameShapeAsCreateAndGet()
    {
        using var host = new ConnectionCommandTestHost();

        var (_, updated) = await ConnectionCommandTestHost.RunAsync(
            new ConnectionReferenceUpdateCliCommand { LogicalName = "new_mail", ConnectionId = "c1" });
        var (_, fetched) = await ConnectionCommandTestHost.RunAsync(
            new ConnectionReferenceGetCliCommand { LogicalName = "new_mail" });

        Assert.Contains("\"status\": \"updated\"", updated);
        Assert.Equal(Properties(fetched).Append("status").ToHashSet(), Properties(updated));
    }

    [Fact]
    public async Task Delete_ReportsTheDeletedStatus()
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(
            new ConnectionReferenceDeleteCliCommand { LogicalName = "new_mail", Yes = true });

        Assert.Equal(0, exit);
        Assert.True(host.References.Deleted);
        Assert.Contains("\"status\": \"deleted\"", output);
    }

    [Fact]
    public async Task Delete_WithoutYes_RefusesInANonInteractiveRun()
    {
        using var host = new ConnectionCommandTestHost(headless: true);

        var (exit, _) = await ConnectionCommandTestHost.RunAsync(new ConnectionReferenceDeleteCliCommand { LogicalName = "new_mail" });

        Assert.Equal(2, exit);
        Assert.False(host.References.Deleted);
    }
}
