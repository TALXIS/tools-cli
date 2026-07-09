using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Tenant.Group;
using Xunit;

namespace TALXIS.CLI.Tests.Tenant.Group;

[Collection("TxcServicesSerial")]
public sealed class GroupCliCommandTests
{
    [Fact]
    public async Task RunAsync_List_WithFilter_ReturnsGroups()
    {
        using var host = new TenantPrincipalCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Contains("$filter=startswith(displayName,'Ops')", Uri.UnescapeDataString(request.RequestUri!.Query));
                return TenantPrincipalCommandTestHost.JsonResponse("""
                {
                  "value": [
                    {
                      "id": "44444444-4444-4444-4444-444444444444",
                      "displayName": "Ops Team"
                    }
                  ]
                }
                """);
            }
        ]));

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new GroupListCliCommand
            {
                Format = "json",
                Filter = "Ops"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        var groups = document.RootElement.EnumerateArray().ToArray();
        Assert.Single(groups);
        Assert.Equal("Ops Team", groups[0].GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task RunAsync_RoleList_AmbiguousGroup_ReturnsValidationError()
    {
        using var host = new TenantPrincipalCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => TenantPrincipalCommandTestHost.JsonResponse("""
            {
              "value": [
                {
                  "id": "44444444-4444-4444-4444-444444444444",
                  "displayName": "Ops Team"
                },
                {
                  "id": "55555555-5555-5555-5555-555555555555",
                  "displayName": "Ops Team"
                }
              ]
            }
            """)
        ]));

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

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task RunAsync_RoleRemove_RemovesAssignment()
    {
        using var host = new TenantPrincipalCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => TenantPrincipalCommandTestHost.JsonResponse("""
            {
              "value": [
                {
                  "id": "44444444-4444-4444-4444-444444444444",
                  "displayName": "Ops Team"
                }
              ]
            }
            """),
            _ => TenantPrincipalCommandTestHost.JsonResponse("""
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
            """),
            _ => TenantPrincipalCommandTestHost.JsonResponse("""
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
                return TenantPrincipalCommandTestHost.JsonResponse(string.Empty);
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
                Group = "Ops Team",
                Role = "Tenant Reader"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("role-removed", document.RootElement.GetProperty("status").GetString());
        Assert.Equal("Ops Team", document.RootElement.GetProperty("group").GetString());
    }
}
