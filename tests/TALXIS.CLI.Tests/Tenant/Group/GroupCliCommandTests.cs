using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Tenant.Group;
using Xunit;

namespace TALXIS.CLI.Tests.Tenant.Group;

[Collection("TxcServicesSerial")]
public sealed class GroupCliCommandTests
{
    // Groups are resolved by raw Entra object id only - never through Microsoft Graph
    // (see TenantRoleResolver's group-resolution remarks for why). These tests confirm
    // no Graph/HTTP call is ever attempted for a non-GUID --group value, and that a
    // valid GUID flows straight through to the Power Platform RBAC calls.

    [Fact]
    public async Task RunAsync_RoleList_NonGuidGroup_ReturnsValidationErrorWithoutAnyHttpCall()
    {
        using var host = new TenantCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>());

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new GroupRoleListCliCommand
            {
                Format = "json",
                Group = "Ops Team"
            }.RunAsync();
        }

        Assert.NotEqual(0, exit);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task RunAsync_RoleRemove_ByObjectId_RemovesAssignmentWithoutGraphCall()
    {
        using var host = new TenantCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            request =>
            {
                Assert.Contains("roleDefinitions", request.RequestUri!.ToString());
                return TenantCommandTestHost.JsonResponse("""
                {
                  "value": [
                    {
                      "roleDefinitionId": "66666666-6666-6666-6666-666666666666",
                      "roleDefinitionName": "Tenant Reader",
                      "description": "Read settings.",
                      "assignableScopes": ["/tenants/tenant-id"]
                    }
                  ]
                }
                """);
            },
            _ => TenantCommandTestHost.JsonResponse("""
            {
              "value": [
                {
                  "roleAssignmentId": "assignment-1",
                  "scope": "/tenants/tenant-id",
                  "principalType": "Group",
                  "principalObjectId": "44444444-4444-4444-4444-444444444444",
                  "roleDefinitionId": "66666666-6666-6666-6666-666666666666"
                }
              ]
            }
            """),
            request =>
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.Contains("authorization/roleAssignments/assignment-1", request.RequestUri!.ToString());
                return TenantCommandTestHost.JsonResponse(string.Empty);
            }
        ]));

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new GroupRoleRemoveCliCommand
            {
                Format = "json",
                Yes = true,
                Group = "44444444-4444-4444-4444-444444444444",
                Role = "Tenant Reader"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("role-removed", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("44444444-4444-4444-4444-444444444444", document.RootElement.GetProperty("group").GetString());
    }
}
