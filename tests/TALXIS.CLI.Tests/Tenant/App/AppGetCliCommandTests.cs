using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Tenant.App;
using Xunit;

namespace TALXIS.CLI.Tests.Tenant.App;

[Collection("TxcServicesSerial")]
public sealed class AppGetCliCommandTests
{
    [Fact]
    public async Task RunAsync_ClientIdSelector_ReturnsServicePrincipal()
    {
        using var host = new TenantAppCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            request =>
            {
                Assert.Contains("appId eq 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'", Uri.UnescapeDataString(request.RequestUri!.Query), StringComparison.Ordinal);
                return TenantAppCommandTestHost.JsonResponse("""
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
            exit = await new AppGetCliCommand
            {
                Format = "json",
                App = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("Contoso CLI", document.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task RunAsync_AmbiguousDisplayName_ReturnsValidationErrorAndCandidates()
    {
        using var host = new TenantAppCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => TenantAppCommandTestHost.JsonResponse("""
            {
              "value": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "appId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                  "displayName": "Contoso CLI"
                },
                {
                  "id": "22222222-2222-2222-2222-222222222222",
                  "appId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                  "displayName": "Contoso CLI"
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
                exit = await new AppGetCliCommand
                {
                    Format = "json",
                    App = "Contoso CLI"
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
