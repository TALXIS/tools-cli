using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Security.ServicePrincipal;
using Xunit;

namespace TALXIS.CLI.Tests.Security.ServicePrincipal;

[Collection("TxcServicesSerial")]
public sealed class ServicePrincipalListCliCommandTests
{
    [Fact]
    public async Task RunAsync_FilteredList_ReturnsServicePrincipals()
    {
        using var host = new SecurityCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            request =>
            {
                Assert.Contains("$filter=startswith(displayName,'Contoso')", Uri.UnescapeDataString(request.RequestUri!.Query), StringComparison.Ordinal);
                return SecurityCommandTestHost.JsonResponse("""
                {
                  "value": [
                    {
                      "id": "11111111-1111-1111-1111-111111111111",
                      "appId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                      "displayName": "Contoso CLI"
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
            exit = await new ServicePrincipalListCliCommand { Format = "json", Filter = "Contoso" }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        var apps = document.RootElement.EnumerateArray().ToArray();
        Assert.Single(apps);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", apps[0].GetProperty("appId").GetString());
        Assert.Equal("11111111-1111-1111-1111-111111111111", apps[0].GetProperty("id").GetString());
        Assert.Equal("Contoso CLI", apps[0].GetProperty("displayName").GetString());
    }
}
