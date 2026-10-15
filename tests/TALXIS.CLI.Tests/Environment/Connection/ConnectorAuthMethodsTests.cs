using System.Text.Json;
using TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectorAuthMethodsTests
{
    private static IReadOnlyList<AuthMethod> Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return ConnectorAuthMethodReader.Read(doc.RootElement);
    }

    private static IReadOnlyDictionary<string, string> P(params (string, string)[] pairs)
        => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    [Fact]
    public void Read_ParameterSets_ReturnsEveryNamedSet()
    {
        var methods = Read(ConnectionFixtures.DataverseConnector());

        Assert.Equal(["Oauth", "ServicePrincipalOauth", "CertOauth"], methods.Select(m => m.Name));
        Assert.All(methods, m => Assert.True(m.IsParameterSet));
        Assert.Equal("Service Principal", methods[1].DisplayName);
    }

    [Fact]
    public void Read_ConnectorWithoutParameters_IsAuthenticationFree()
    {
        var method = Assert.Single(Read(ConnectionFixtures.NoAuthConnector()));

        Assert.Equal(ConnectorAuthMethodReader.NoAuthMethodName, method.Name);
        Assert.False(method.RequiresInput);
        Assert.False(method.IsInteractiveOAuth);
    }

    [Fact]
    public void Read_FlatOAuthParameter_IsInteractiveOAuth()
    {
        var method = Assert.Single(Read(ConnectionFixtures.OAuthConnector()));

        Assert.False(method.IsParameterSet);
        Assert.True(method.IsInteractiveOAuth);
    }

    [Fact]
    public void Read_FlagsSecretsAndRequiredInputs()
    {
        var sp = Read(ConnectionFixtures.DataverseConnector()).Single(m => m.Name == "ServicePrincipalOauth");

        Assert.True(sp.Parameters.Single(p => p.Name == "token:clientSecret").IsSecret);
        Assert.False(sp.Parameters.Single(p => p.Name == "token:clientId").IsSecret);
        Assert.Equal(
            ["token:TenantId", "token:clientId", "token:clientSecret"],
            sp.Parameters.Where(p => p.IsRequiredInput).Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.False(sp.IsInteractiveOAuth);
    }

    [Fact]
    public void Select_NoParameters_PicksTheInteractiveOAuthSet()
    {
        var methods = Read(ConnectionFixtures.DataverseConnector());

        Assert.Equal("Oauth", ConnectionRequestBuilder.Select(methods, null, [], "dv").Name);
    }

    [Fact]
    public void Select_ParametersOfOneMethod_PicksThatMethod()
    {
        var methods = Read(ConnectionFixtures.DataverseConnector());

        var picked = ConnectionRequestBuilder.Select(
            methods, null, ["token:clientId", "token:clientSecret", "token:TenantId"], "dv");

        Assert.Equal("ServicePrincipalOauth", picked.Name);
    }

    [Fact]
    public void Select_ExplicitName_IsCaseInsensitive()
    {
        var methods = Read(ConnectionFixtures.DataverseConnector());

        Assert.Equal("CertOauth", ConnectionRequestBuilder.Select(methods, "certoauth", [], "dv").Name);
    }

    [Fact]
    public void Select_UnknownMethod_ListsTheAvailableOnes()
    {
        var methods = Read(ConnectionFixtures.DataverseConnector());

        var ex = Assert.Throws<ArgumentException>(() => ConnectionRequestBuilder.Select(methods, "magic", [], "dv"));

        Assert.Contains("ServicePrincipalOauth (Service Principal)", ex.Message);
    }

    [Fact]
    public void Select_AmbiguousParameters_AsksForAnExplicitMethod()
    {
        var methods = Read(ConnectionFixtures.DataverseConnector());

        // The tenant id exists on both the service principal and certificate sets (different casing).
        var ex = Assert.Throws<ArgumentException>(() => ConnectionRequestBuilder.Select(methods, null, ["token:tenantId", "token:TenantId"], "dv"));

        Assert.Contains("--auth-method", ex.Message);
    }

    [Fact]
    public void Validate_RejectsAnUnknownParameter_AndNamesTheValidOnes()
    {
        var sp = Read(ConnectionFixtures.DataverseConnector()).Single(m => m.Name == "ServicePrincipalOauth");

        var ex = Assert.Throws<ArgumentException>(() =>
            ConnectionRequestBuilder.Validate(sp, P(("token:clientid", "a"), ("nope", "b")), "dv"));

        Assert.Contains("'nope'", ex.Message);
        Assert.Contains("token:clientSecret", ex.Message);
    }

    [Fact]
    public void Validate_RejectsMissingRequiredParameters()
    {
        var sp = Read(ConnectionFixtures.DataverseConnector()).Single(m => m.Name == "ServicePrincipalOauth");

        var ex = Assert.Throws<ArgumentException>(() =>
            ConnectionRequestBuilder.Validate(sp, P(("token:clientId", "a")), "dv"));

        Assert.Contains("token:clientSecret", ex.Message);
        Assert.Contains("token:TenantId", ex.Message);
    }

    [Fact]
    public void BuildBody_ParameterSet_UsesConnectionParametersSetWithCanonicalNames()
    {
        var sp = Read(ConnectionFixtures.DataverseConnector()).Single(m => m.Name == "ServicePrincipalOauth");

        var body = ConnectionRequestBuilder.BuildBody(
            "Dv SPN", ConnectionFixtures.EnvironmentId, sp,
            P(("token:CLIENTID", "app"), ("token:clientSecret", "s3cret"), ("token:tenantid", "t")), includeConsentRedirect: false);

        var props = body["properties"]!;
        Assert.Equal("Dv SPN", (string?)props["displayName"]);
        Assert.Equal(ConnectionFixtures.EnvironmentId.ToString(), (string?)props["environment"]!["name"]);
        Assert.Equal("ServicePrincipalOauth", (string?)props["connectionParametersSet"]!["name"]);
        Assert.Equal("app", (string?)props["connectionParametersSet"]!["values"]!["token:clientId"]!["value"]);
        Assert.Equal("t", (string?)props["connectionParametersSet"]!["values"]!["token:TenantId"]!["value"]);
        Assert.Null(props["consentInfo"]);
        Assert.Null(props["connectionParameters"]);
    }

    [Fact]
    public void BuildBody_FlatConnector_UsesConnectionParameters_AndConsentRedirectWhenAsked()
    {
        var method = Read(ConnectionFixtures.OAuthConnector()).Single();

        var body = ConnectionRequestBuilder.BuildBody(
            "Mail", ConnectionFixtures.EnvironmentId, method, P(), includeConsentRedirect: true);

        var props = body["properties"]!;
        Assert.NotNull(props["connectionParameters"]);
        Assert.Null(props["connectionParametersSet"]);
        Assert.Equal("https://www.microsoft.com/", (string?)props["consentInfo"]!["redirectUrl"]);
    }
}
