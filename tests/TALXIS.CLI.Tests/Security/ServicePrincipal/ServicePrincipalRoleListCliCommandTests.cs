using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Security.ServicePrincipal;
using Xunit;

namespace TALXIS.CLI.Tests.Security.ServicePrincipal;

[Collection("TxcServicesSerial")]
public sealed class ServicePrincipalRoleListCliCommandTests
{
    [Fact]
    public async Task RunAsync_ReturnsTenantRoleAndAdminApplicationAssignment()
    {
        using var host = new SecurityCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => SecurityCommandTestHost.JsonResponse("""
            {
              "value": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "appId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                  "displayName": "Contoso CLI"
                }
              ]
            }
            """),
            _ => SecurityCommandTestHost.JsonResponse("""
            {
              "value": [
                {
                  "roleDefinitionId": "33333333-3333-3333-3333-333333333333",
                  "roleDefinitionName": "Tenant Reader",
                  "description": "Can read tenant settings.",
                  "assignableScopes": ["/tenants/tenant-id"]
                }
              ]
            }
            """),
            _ => SecurityCommandTestHost.JsonResponse("""
            {
              "value": [
                {
                  "roleAssignmentId": "assign-1",
                  "roleDefinitionId": "33333333-3333-3333-3333-333333333333",
                  "scope": "/tenants/tenant-id",
                  "principalType": "ApplicationUser",
                  "principalObjectId": "11111111-1111-1111-1111-111111111111"
                }
              ]
            }
            """),
            _ => SecurityCommandTestHost.JsonResponse("""
            [
              {
                "applicationId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
              }
            ]
            """)
        ]));

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new ServicePrincipalRoleListCliCommand
            {
                Format = "json",
                ServicePrincipal = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        var roles = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, roles.Length);
        Assert.Contains(roles, role => role.GetProperty("roleName").GetString() == "Tenant Reader");
        Assert.Contains(roles, role => role.GetProperty("roleName").GetString() == "admin-application");
    }
}
