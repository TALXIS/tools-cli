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
        using var host = new TenantCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            request =>
            {
                Assert.Equal(HttpMethod.Get, request.Method);
                Assert.Contains("$filter=startswith(userPrincipalName,'alice') or startswith(displayName,'alice')", Uri.UnescapeDataString(request.RequestUri!.Query));
                return TenantCommandTestHost.JsonResponse("""
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
        using var host = new TenantCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            request =>
            {
                Assert.Contains("$filter=userPrincipalName eq 'missing@contoso.com'", Uri.UnescapeDataString(request.RequestUri!.Query));
                return TenantCommandTestHost.JsonResponse("""
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
    public async Task RunAsync_Get_ByObjectId_IncludesGuidTypedIdClause()
    {
        using var host = new TenantCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            request =>
            {
                var query = Uri.UnescapeDataString(request.RequestUri!.Query);
                Assert.Contains("$filter=id eq '11111111-1111-1111-1111-111111111111' or userPrincipalName eq '11111111-1111-1111-1111-111111111111'", query);
                return TenantCommandTestHost.JsonResponse("""
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
            exit = await new UserGetCliCommand
            {
                Format = "json",
                User = "11111111-1111-1111-1111-111111111111"
            }.RunAsync();
        }

        Assert.Equal(0, exit);
        var document = JsonDocument.Parse(output.ToString());
        Assert.Equal("alice@contoso.com", document.RootElement.GetProperty("userPrincipalName").GetString());
    }

    [Fact]
    public async Task RunAsync_RoleAdd_AmbiguousRole_ReturnsValidationError()
    {
        using var host = new TenantCommandTestHost(new Queue<Func<HttpRequestMessage, HttpResponseMessage>>([
            _ => TenantCommandTestHost.JsonResponse("""
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
            _ => TenantCommandTestHost.JsonResponse("""
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
