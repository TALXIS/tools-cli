using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Security.Role;
using Xunit;

namespace TALXIS.CLI.Tests.Security.Role;

[Collection("TxcServicesSerial")]
public sealed class RoleListCliCommandTests
{
    [Fact]
    public async Task RunAsync_ListsOnlyTenantAssignableRoles()
    {
        using var host = new SecurityCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => SecurityCommandTestHost.JsonResponse("""
            {
              "value": [
                {
                  "roleDefinitionId": "11111111-1111-1111-1111-111111111111",
                  "roleDefinitionName": "Tenant Reader",
                  "description": "Can read tenant settings.",
                  "assignableScopes": ["/tenants/tenant-id"]
                },
                {
                  "roleDefinitionId": "22222222-2222-2222-2222-222222222222",
                  "roleDefinitionName": "Environment Maker",
                  "description": "Environment-only role.",
                  "assignableScopes": ["/environments/env-id"]
                }
              ]
            }
            """)
        ]));

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new RoleListCliCommand { Format = "json" }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        var roles = document.RootElement.EnumerateArray().ToArray();
        Assert.Single(roles);
        Assert.Equal("Tenant Reader", roles[0].GetProperty("roleDefinitionName").GetString());
    }

    [Fact]
    public async Task RunAsync_FilterFurtherNarrowsTenantRoles()
    {
        using var host = new SecurityCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => SecurityCommandTestHost.JsonResponse("""
            {
              "value": [
                {
                  "roleDefinitionId": "11111111-1111-1111-1111-111111111111",
                  "roleDefinitionName": "Tenant Reader",
                  "description": "Can read tenant settings.",
                  "assignableScopes": ["/tenants/tenant-id"]
                },
                {
                  "roleDefinitionId": "33333333-3333-3333-3333-333333333333",
                  "roleDefinitionName": "Tenant Admin",
                  "description": "Can manage tenant settings.",
                  "assignableScopes": ["/tenants/tenant-id"]
                }
              ]
            }
            """)
        ]));

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new RoleListCliCommand { Format = "json", Filter = "reader" }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        var roles = document.RootElement.EnumerateArray().ToArray();
        Assert.Single(roles);
        Assert.Equal("Tenant Reader", roles[0].GetProperty("roleDefinitionName").GetString());
    }
}
