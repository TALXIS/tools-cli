using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Tenant.ServicePrincipal;
using Xunit;

namespace TALXIS.CLI.Tests.Tenant.ServicePrincipal;

[Collection("TxcServicesSerial")]
public sealed class ServicePrincipalRoleAddCliCommandTests
{
    [Fact]
    public async Task RunAsync_AmbiguousRole_ReturnsValidationErrorAndCandidates()
    {
        using var host = new TenantCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => TenantCommandTestHost.JsonResponse("""
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
            _ => TenantCommandTestHost.JsonResponse("""
            {
              "value": [
                {
                  "roleDefinitionId": "33333333-3333-3333-3333-333333333333",
                  "roleDefinitionName": "Owner",
                  "description": "First owner role.",
                  "assignableScopes": ["/tenants/tenant-id"]
                },
                {
                  "roleDefinitionId": "44444444-4444-4444-4444-444444444444",
                  "roleDefinitionName": "Owner",
                  "description": "Second owner role.",
                  "assignableScopes": ["/tenants/tenant-id"]
                }
              ]
            }
            """)
        ]));

        var output = new StringWriter();
        var error = new StringWriter();
        var originalError = Console.Error;
        int exit;

        try
        {
            Console.SetError(error);
            using (OutputWriter.RedirectTo(output))
            {
                exit = await new ServicePrincipalRoleAddCliCommand
                {
                    Format = "json",
                    ServicePrincipal = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                    Role = "Owner"
                }.RunAsync();
            }
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output.ToString());
        Assert.DoesNotContain("{", error.ToString());
    }
}
