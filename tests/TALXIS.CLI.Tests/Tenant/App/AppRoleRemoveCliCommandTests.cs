using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Tenant.App;
using Xunit;

namespace TALXIS.CLI.Tests.Tenant.App;

[Collection("TxcServicesSerial")]
public sealed class AppRoleRemoveCliCommandTests
{
    [Fact]
    public async Task RunAsync_AdminApplicationRole_RemovesAssignment()
    {
        using var host = new TenantAppCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => TenantAppCommandTestHost.JsonResponse("""
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
            _ => TenantAppCommandTestHost.JsonResponse("""
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
            exit = await new AppRoleRemoveCliCommand
            {
                Format = "json",
                App = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                Role = "admin-application",
                Yes = true
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("role-removed", document.RootElement.GetProperty("status").GetString());
    }
}
