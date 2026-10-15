using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Features.Environment.Connection.Permission;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement.Commands;

[Collection("TxcServicesSerial")]
public sealed class ConnectionPermissionCliCommandTests
{
    [Fact]
    public async Task Add_AServicePrincipal_DefaultsToCanViewAndNeedsNoConnector()
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(
            new ConnectionPermissionAddCliCommand { ConnectionId = "c1", ServicePrincipal = "app-id" });

        Assert.Equal(0, exit);
        Assert.Null(host.Permissions.LastConnector);
        Assert.Equal(new ConnectionPrincipalSpec(ConnectionPrincipalKind.ServicePrincipal, "app-id"), host.Permissions.LastPrincipal);
        Assert.Equal(ConnectionPermissionRole.CanView, host.Permissions.LastRole);
        Assert.Contains("\"status\": \"assigned\"", output);
        Assert.Contains("\"role\": \"can-view\"", output);
    }

    [Fact]
    public async Task Add_ParsesTheKebabCaseRole()
    {
        using var host = new ConnectionCommandTestHost();

        await ConnectionCommandTestHost.RunAsync(
            new ConnectionPermissionAddCliCommand { ConnectionId = "c1", Group = "grp", Role = "can-view-with-share", Connector = "shared_office365users" });

        Assert.Equal(ConnectionPermissionRole.CanViewWithShare, host.Permissions.LastRole);
        Assert.Equal("shared_office365users", host.Permissions.LastConnector);
    }

    [Theory]
    [InlineData("CanView")]
    [InlineData("owner")]
    [InlineData("can-use")]
    public async Task Add_ARoleThatIsNotKebabCaseOrNotGrantable_ExitsTwo(string role)
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, _) = await ConnectionCommandTestHost.RunAsync(
            new ConnectionPermissionAddCliCommand { ConnectionId = "c1", User = "u@x.com", Role = role });

        Assert.Equal(2, exit);
        Assert.Null(host.Permissions.LastPrincipal);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("u@x.com", "app", null)]
    [InlineData("u@x.com", null, "grp")]
    [InlineData("u@x.com", "app", "grp")]
    public async Task Add_NeedsExactlyOnePrincipal(string? user, string? servicePrincipal, string? group)
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectionPermissionAddCliCommand
        {
            ConnectionId = "c1", User = user, ServicePrincipal = servicePrincipal, Group = group,
        });

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output);
        Assert.Null(host.Permissions.LastPrincipal);
    }

    [Fact]
    public async Task Add_AnUnchangedGrant_SaysSo()
    {
        using var host = new ConnectionCommandTestHost();
        host.Permissions.AddStatus = ConnectionOutcomeStatus.Unchanged;

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(
            new ConnectionPermissionAddCliCommand { ConnectionId = "c1", User = "alice@contoso.com" });

        Assert.Equal(0, exit);
        Assert.Contains("\"status\": \"unchanged\"", output);
    }

    [Fact]
    public async Task Remove_WithoutYes_RefusesInANonInteractiveRun()
    {
        using var host = new ConnectionCommandTestHost(headless: true);

        var (exit, _) = await ConnectionCommandTestHost.RunAsync(
            new ConnectionPermissionRemoveCliCommand { ConnectionId = "c1", User = "alice@contoso.com" });

        Assert.Equal(2, exit);
        Assert.False(host.Permissions.Removed);
    }

    [Fact]
    public async Task Remove_NeedsExactlyOnePrincipalEvenWithYes()
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, _) = await ConnectionCommandTestHost.RunAsync(
            new ConnectionPermissionRemoveCliCommand { ConnectionId = "c1", Yes = true });

        Assert.Equal(2, exit);
        Assert.False(host.Permissions.Removed);
    }

    [Fact]
    public async Task Remove_WithYes_RevokesAndReportsRemoved()
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(
            new ConnectionPermissionRemoveCliCommand { ConnectionId = "c1", User = "alice@contoso.com", Yes = true });

        Assert.Equal(0, exit);
        Assert.True(host.Permissions.Removed);
        Assert.Contains("\"status\": \"removed\"", output);
    }

    [Fact]
    public async Task List_WithoutAConnector_PassesNull()
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectionPermissionListCliCommand { ConnectionId = "c1" });

        Assert.Equal(0, exit);
        Assert.Null(host.Permissions.LastConnector);
        Assert.Contains("\"role\": \"can-view\"", output);
    }
}
