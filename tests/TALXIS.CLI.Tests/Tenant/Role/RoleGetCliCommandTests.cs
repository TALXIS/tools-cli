using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Tenant.Role;
using Xunit;

namespace TALXIS.CLI.Tests.Tenant.Role;

[Collection("TxcServicesSerial")]
public sealed class RoleGetCliCommandTests
{
    [Fact]
    public async Task RunAsync_GuidSelector_ReturnsTenantRole()
    {
        using var host = new TenantCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => TenantCommandTestHost.JsonResponse("""
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
            exit = await new RoleGetCliCommand
            {
                Format = "json",
                Role = "11111111-1111-1111-1111-111111111111"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("Tenant Reader", document.RootElement.GetProperty("roleDefinitionName").GetString());
    }

    [Fact]
    public async Task RunAsync_NonTenantRoleSelector_ReturnsValidationError()
    {
        using var host = new TenantCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => TenantCommandTestHost.JsonResponse("""
            {
              "value": [
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
            exit = await new RoleGetCliCommand
            {
                Format = "json",
                Role = "Environment Maker"
            }.RunAsync();
        }

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output.ToString());
    }
}
