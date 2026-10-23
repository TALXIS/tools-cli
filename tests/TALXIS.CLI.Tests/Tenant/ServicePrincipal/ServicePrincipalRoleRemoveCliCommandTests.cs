using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Tenant.ServicePrincipal;
using Xunit;

namespace TALXIS.CLI.Tests.Tenant.ServicePrincipal;

[Collection("TxcServicesSerial")]
public sealed class ServicePrincipalRoleRemoveCliCommandTests
{
    [Fact]
    public async Task RunAsync_AdminApplicationRole_RemovesAssignment()
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
            [
              {
                "applicationId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
              }
            ]
            """),
            request =>
            {
                Assert.Equal(HttpMethod.Delete, request.Method);
                Assert.Contains("adminApplications/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
                return new HttpResponseMessage(System.Net.HttpStatusCode.NoContent);
            }
        ]));

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new ServicePrincipalRoleRemoveCliCommand
            {
                Format = "json",
                ServicePrincipal = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                Role = "admin-application",
                Yes = true
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("role-removed", document.RootElement.GetProperty("status").GetString());
    }
}
