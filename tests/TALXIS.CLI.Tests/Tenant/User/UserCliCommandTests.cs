using System.Text.Json;
using TALXIS.CLI.Core;
using TALXIS.CLI.Features.Tenant.User;
using Xunit;

namespace TALXIS.CLI.Tests.Tenant.User;

[Collection("TxcServicesSerial")]
public sealed class UserCliCommandTests
{
    [Fact]
    public async Task RunAsync_List_WithFilter_ReturnsUsers()
    {
        using var host = new TenantPrincipalCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Contains("$filter=startswith(userPrincipalName,'alice') or startswith(displayName,'alice')", Uri.UnescapeDataString(request.RequestUri!.Query));
                return TenantPrincipalCommandTestHost.JsonResponse("""
                {
                  "value": [
                    {
                      "id": "11111111-1111-1111-1111-111111111111",
                      "displayName": "Alice Adams",
                      "userPrincipalName": "alice@contoso.com"
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
            exit = await new UserListCliCommand
            {
                Format = "json",
                Filter = "alice"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        var users = document.RootElement.EnumerateArray().ToArray();
        Assert.Single(users);
        Assert.Equal("alice@contoso.com", users[0].GetProperty("userPrincipalName").GetString());
    }

    [Fact]
    public async Task RunAsync_Get_MissingUser_ReturnsValidationError()
    {
        using var host = new TenantPrincipalCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            request =>
            {
                Assert.Contains("$filter=id eq 'missing@contoso.com' or userPrincipalName eq 'missing@contoso.com'", Uri.UnescapeDataString(request.RequestUri!.Query));
                return TenantPrincipalCommandTestHost.JsonResponse("""
                {
                  "value": []
                }
                """);
            }
        ]));

        var output = new StringWriter();
        int exit;
        using (OutputWriter.RedirectTo(output))
        {
            exit = await new UserGetCliCommand
            {
                Format = "json",
                User = "missing@contoso.com"
            }.RunAsync();
        }

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public async Task RunAsync_RoleAdd_AmbiguousRole_ReturnsValidationError()
    {
        using var host = new TenantPrincipalCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => TenantPrincipalCommandTestHost.JsonResponse("""
            {
              "value": [
                {
                  "id": "11111111-1111-1111-1111-111111111111",
                  "displayName": "Alice Adams",
                  "userPrincipalName": "alice@contoso.com"
                }
              ]
            }
            """),
            _ => TenantPrincipalCommandTestHost.JsonResponse("""
            {
              "value": [
                {
                  "roleDefinitionId": "22222222-2222-2222-2222-222222222222",
                  "roleDefinitionName": "Tenant Reader",
                  "description": "Read settings.",
                  "assignableScopes": ["/tenants/tenant-id"]
                },
                {
                  "roleDefinitionId": "33333333-3333-3333-3333-333333333333",
                  "roleDefinitionName": "Tenant Reader",
                  "description": "Read settings copy.",
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
            exit = await new UserRoleAddCliCommand
            {
                Format = "json",
                User = "alice@contoso.com",
                Role = "Tenant Reader"
            }.RunAsync();
        }

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output.ToString());
    }
}
