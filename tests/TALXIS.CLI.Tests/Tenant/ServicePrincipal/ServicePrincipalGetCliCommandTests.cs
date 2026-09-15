using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Tenant.ServicePrincipal;
using Xunit;

namespace TALXIS.CLI.Tests.Tenant.ServicePrincipal;

[Collection("TxcServicesSerial")]
public sealed class ServicePrincipalGetCliCommandTests
{
    [Fact]
    public async Task RunAsync_ClientIdSelector_ReturnsServicePrincipal()
    {
        using var host = new TenantCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            request =>
            {
                Assert.Contains("appId eq 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'", Uri.UnescapeDataString(request.RequestUri!.Query), StringComparison.Ordinal);
                return TenantCommandTestHost.JsonResponse("""
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
            exit = await new ServicePrincipalGetCliCommand
            {
                Format = "json",
                ServicePrincipal = "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("Contoso CLI", document.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task RunAsync_DisplayNameSelector_DoesNotSendGuidTypedIdOrAppIdClause()
    {
        // Microsoft Graph rejects the entire $filter with 400 if any clause compares a
        // GUID-typed property (id, appId) to a non-GUID literal, even combined with "or".
        using var host = new TenantCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            request =>
            {
                var query = Uri.UnescapeDataString(request.RequestUri!.Query);
                Assert.DoesNotContain("appId eq", query, StringComparison.Ordinal);
                Assert.DoesNotContain("id eq", query, StringComparison.Ordinal);
                Assert.Contains("displayName eq 'Contoso CLI'", query, StringComparison.Ordinal);
                return TenantCommandTestHost.JsonResponse("""
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
            exit = await new ServicePrincipalGetCliCommand
            {
                Format = "json",
                ServicePrincipal = "Contoso CLI"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("Contoso CLI", document.RootElement.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task RunAsync_AmbiguousDisplayName_ReturnsValidationErrorAndCandidates()
    {
        using var host = new TenantCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => TenantCommandTestHost.JsonResponse("""
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
                exit = await new ServicePrincipalGetCliCommand
                {
                    Format = "json",
                    ServicePrincipal = "Contoso CLI"
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
