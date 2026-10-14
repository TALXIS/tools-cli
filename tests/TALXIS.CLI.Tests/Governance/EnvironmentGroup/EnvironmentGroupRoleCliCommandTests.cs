using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.PowerPlatform;
using TALXIS.CLI.Features.Governance.EnvironmentGroup;
using Xunit;

namespace TALXIS.CLI.Tests.Governance.EnvironmentGroup;

[Collection("TxcServicesSerial")]
public sealed class EnvironmentGroupRoleCliCommandTests
{
    [Fact]
    public async Task List_ReturnsSeededAssignments()
    {
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
        using var host = new EnvironmentGroupRoleCommandTestHost(handlers);
        var group = host.GroupClient.Add("Target group");
        host.RoleClient.Seed(group.Id, new PowerPlatformEnvironmentGroupRoleAssignment(
            "ra-1", group.Id, PowerPlatformPrincipalType.Group, Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, null));

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new EnvironmentGroupRoleListCliCommand { Format = "json", EnvironmentGroup = group.Id.ToString() }.RunAsync();
        }

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output.ToString());
        Assert.Equal(1, document.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task Add_ForGroupPrincipal_ResolvesRoleAndCallsClient()
    {
        var ownerRoleId = Guid.NewGuid();
        var readerRoleId = Guid.NewGuid();
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
        handlers.Enqueue(_ => EnvironmentGroupRoleCommandTestHost.JsonResponse(
            EnvironmentGroupRoleCommandTestHost.RoleDefinitionsPayload(ownerRoleId, readerRoleId)));

        using var host = new EnvironmentGroupRoleCommandTestHost(handlers);
        var group = host.GroupClient.Add("Target group");
        var principalObjectId = Guid.NewGuid();

        var exit = await new EnvironmentGroupRoleAddCliCommand
        {
            Format = "json",
            EnvironmentGroup = group.Id.ToString(),
            PrincipalType = PowerPlatformPrincipalType.Group,
            Principal = principalObjectId.ToString(),
            Role = "Owner",
        }.RunAsync();

        Assert.Equal(0, exit);
        var assignments = await host.RoleClient.ListAsync(null!, null!, group.Id, CancellationToken.None);
        Assert.Single(assignments);
        Assert.Equal(ownerRoleId, assignments[0].RoleDefinitionId);
        Assert.Equal(principalObjectId, assignments[0].PrincipalObjectId);
    }

    [Fact]
    public async Task Add_ForUserPrincipal_ResolvesPrincipalViaGraph()
    {
        var userId = Guid.NewGuid();
        var ownerRoleId = Guid.NewGuid();
        var readerRoleId = Guid.NewGuid();
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
        handlers.Enqueue(_ => EnvironmentGroupRoleCommandTestHost.JsonResponse(
            EnvironmentGroupRoleCommandTestHost.UserPayload(userId, "jdoe@contoso.com", "Jane Doe")));
        handlers.Enqueue(_ => EnvironmentGroupRoleCommandTestHost.JsonResponse(
            EnvironmentGroupRoleCommandTestHost.RoleDefinitionsPayload(ownerRoleId, readerRoleId)));

        using var host = new EnvironmentGroupRoleCommandTestHost(handlers);
        var group = host.GroupClient.Add("Target group");

        var exit = await new EnvironmentGroupRoleAddCliCommand
        {
            Format = "json",
            EnvironmentGroup = group.Id.ToString(),
            PrincipalType = PowerPlatformPrincipalType.User,
            Principal = "jdoe@contoso.com",
            Role = "Reader",
        }.RunAsync();

        Assert.Equal(0, exit);
        var assignments = await host.RoleClient.ListAsync(null!, null!, group.Id, CancellationToken.None);
        Assert.Single(assignments);
        Assert.Equal(userId, assignments[0].PrincipalObjectId);
        Assert.Equal(readerRoleId, assignments[0].RoleDefinitionId);
    }

    [Fact]
    public async Task Add_UnknownRole_ReturnsValidationError()
    {
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
        handlers.Enqueue(_ => EnvironmentGroupRoleCommandTestHost.JsonResponse(
            EnvironmentGroupRoleCommandTestHost.RoleDefinitionsPayload(Guid.NewGuid(), Guid.NewGuid())));

        using var host = new EnvironmentGroupRoleCommandTestHost(handlers);
        var group = host.GroupClient.Add("Target group");

        var exit = await new EnvironmentGroupRoleAddCliCommand
        {
            Format = "json",
            EnvironmentGroup = group.Id.ToString(),
            PrincipalType = PowerPlatformPrincipalType.Group,
            Principal = Guid.NewGuid().ToString(),
            Role = "DoesNotExist",
        }.RunAsync();

        Assert.Equal(2, exit);
    }

    [Fact]
    public async Task Add_AlreadyAssigned_IsIdempotentAndDoesNotDuplicate()
    {
        var ownerRoleId = Guid.NewGuid();
        var readerRoleId = Guid.NewGuid();
        var principalObjectId = Guid.NewGuid();
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
        handlers.Enqueue(_ => EnvironmentGroupRoleCommandTestHost.JsonResponse(
            EnvironmentGroupRoleCommandTestHost.RoleDefinitionsPayload(ownerRoleId, readerRoleId)));

        using var host = new EnvironmentGroupRoleCommandTestHost(handlers);
        var group = host.GroupClient.Add("Target group");
        host.RoleClient.Seed(group.Id, new PowerPlatformEnvironmentGroupRoleAssignment(
            "ra-existing", group.Id, PowerPlatformPrincipalType.Group, principalObjectId, ownerRoleId, DateTimeOffset.UtcNow, null));

        var exit = await new EnvironmentGroupRoleAddCliCommand
        {
            Format = "json",
            EnvironmentGroup = group.Id.ToString(),
            PrincipalType = PowerPlatformPrincipalType.Group,
            Principal = principalObjectId.ToString(),
            Role = "Owner",
        }.RunAsync();

        Assert.Equal(0, exit);
        var assignments = await host.RoleClient.ListAsync(null!, null!, group.Id, CancellationToken.None);
        Assert.Single(assignments);
    }

    [Fact]
    public async Task Remove_DeletesMatchingAssignment()
    {
        var ownerRoleId = Guid.NewGuid();
        var readerRoleId = Guid.NewGuid();
        var principalObjectId = Guid.NewGuid();
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
        handlers.Enqueue(_ => EnvironmentGroupRoleCommandTestHost.JsonResponse(
            EnvironmentGroupRoleCommandTestHost.RoleDefinitionsPayload(ownerRoleId, readerRoleId)));

        using var host = new EnvironmentGroupRoleCommandTestHost(handlers);
        var group = host.GroupClient.Add("Target group");
        host.RoleClient.Seed(group.Id, new PowerPlatformEnvironmentGroupRoleAssignment(
            "ra-existing", group.Id, PowerPlatformPrincipalType.Group, principalObjectId, ownerRoleId, DateTimeOffset.UtcNow, null));

        var exit = await new EnvironmentGroupRoleRemoveCliCommand
        {
            Format = "json",
            Yes = true,
            EnvironmentGroup = group.Id.ToString(),
            PrincipalType = PowerPlatformPrincipalType.Group,
            Principal = principalObjectId.ToString(),
            Role = "Owner",
        }.RunAsync();

        Assert.Equal(0, exit);
        Assert.Contains((group.Id, "ra-existing"), host.RoleClient.Removed);
        var remaining = await host.RoleClient.ListAsync(null!, null!, group.Id, CancellationToken.None);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task Remove_UnknownEnvironmentGroup_ReturnsValidationError()
    {
        var handlers = new Queue<Func<HttpRequestMessage, HttpResponseMessage>>();
        using var host = new EnvironmentGroupRoleCommandTestHost(handlers);

        var exit = await new EnvironmentGroupRoleRemoveCliCommand
        {
            Format = "json",
            Yes = true,
            EnvironmentGroup = Guid.NewGuid().ToString(),
            PrincipalType = PowerPlatformPrincipalType.Group,
            Principal = Guid.NewGuid().ToString(),
            Role = "Owner",
        }.RunAsync();

        Assert.Equal(2, exit);
    }
}
