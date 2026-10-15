using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectionPermissionMapperTests
{
    // Shape captured from a live tenant: a service principal's permission id
    // is NOT its object id.
    private const string PermissionList = """
        {"value":[
          {"name":"9fe11834-e8c9-4387-9854-2aad4296e231","id":"/providers/Microsoft.PowerApps/apis/shared_office365users/connections/c1/permissions/9fe11834-e8c9-4387-9854-2aad4296e231",
           "type":"Microsoft.PowerApps/apis/connections/permissions",
           "properties":{"roleName":"Owner","principal":{"id":"9fe11834-e8c9-4387-9854-2aad4296e231","displayName":"Admin","email":"admin@contoso.com","type":"User","tenantId":"t"}}},
          {"name":"9c5dbf38-1a9e-4306-94b3-2c3ef5df67d7","id":"/providers/Microsoft.PowerApps/apis/shared_office365users/connections/c1/permissions/9c5dbf38-1a9e-4306-94b3-2c3ef5df67d7",
           "properties":{"roleName":"CanView","principal":{"id":"25a5f191-c862-4e69-a24f-643a6b73bd46","displayName":"deploy-spn","type":"ServicePrincipal","tenantId":"t"}}}
        ]}
        """;

    [Fact]
    public void ParseList_ReadsRolesAndKeepsThePermissionIdApartFromThePrincipalId()
    {
        using var doc = JsonDocument.Parse(PermissionList);

        var permissions = ConnectionPermissionMapper.ParseList(doc.RootElement);

        Assert.Equal(2, permissions.Count);
        Assert.Equal("Owner", permissions[0].Role);
        var spn = permissions[1];
        Assert.Equal("9c5dbf38-1a9e-4306-94b3-2c3ef5df67d7", spn.PermissionId);
        Assert.Equal("25a5f191-c862-4e69-a24f-643a6b73bd46", spn.PrincipalId);
        Assert.Equal("ServicePrincipal", spn.PrincipalType);
    }

    [Fact]
    public void ParseList_IgnoresMalformedRows()
    {
        using var doc = JsonDocument.Parse("""{"value":[{"name":"x"},{"properties":{"roleName":"CanView"}}]}""");

        Assert.Empty(ConnectionPermissionMapper.ParseList(doc.RootElement));
    }

    [Theory]
    [InlineData(ConnectionPermissionRole.CanView, "CanView")]
    [InlineData(ConnectionPermissionRole.CanViewWithShare, "CanViewWithShare")]
    [InlineData(ConnectionPermissionRole.CanEdit, "CanEdit")]
    public void ToPlatformRole_UsesTheNamesThePlatformAccepts(ConnectionPermissionRole role, string expected)
        => Assert.Equal(expected, ConnectionPermissionMapper.ToPlatformRole(role));

    [Fact]
    public void BuildPut_ByObjectId_ProducesAnUpsertWithoutNotification()
    {
        var body = ConnectionPermissionMapper.BuildPut("25a5f191-c862-4e69-a24f-643a6b73bd46", null, "ServicePrincipal", "tenant", "CanView");

        var entry = body["put"]![0]!["properties"]!;
        Assert.Equal("CanView", (string?)entry["roleName"]);
        Assert.Equal("ServicePrincipal", (string?)entry["principal"]!["type"]);
        Assert.Equal("25a5f191-c862-4e69-a24f-643a6b73bd46", (string?)entry["principal"]!["id"]);
        Assert.Null(entry["principal"]!["email"]);
        Assert.Equal("DoNotNotify", (string?)entry["NotifyShareTargetOption"]);
        Assert.Empty(body["delete"]!.AsArray());
    }

    [Fact]
    public void BuildPut_ByEmail_OmitsTheId()
    {
        var body = ConnectionPermissionMapper.BuildPut(null, "dev@contoso.com", "User", null, "CanEdit");

        var principal = body["put"]![0]!["properties"]!["principal"]!;
        Assert.Equal("dev@contoso.com", (string?)principal["email"]);
        Assert.Null(principal["id"]);
    }

    [Fact]
    public void BuildDelete_IsKeyedByTheFullPermissionResourceId()
    {
        var body = ConnectionPermissionMapper.BuildDelete("shared_office365users", "c1", "9c5dbf38-1a9e-4306-94b3-2c3ef5df67d7");

        Assert.Empty(body["put"]!.AsArray());
        Assert.Equal(
            "/providers/Microsoft.PowerApps/apis/shared_office365users/connections/c1/permissions/9c5dbf38-1a9e-4306-94b3-2c3ef5df67d7",
            (string?)body["delete"]![0]!["id"]);
    }
}
