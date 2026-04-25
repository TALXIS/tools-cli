using System.Text.Json;
using TALXIS.CLI.Platform.PowerPlatform.Control;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Providers.PowerPlatform;

public sealed class OrganizationTableSettingsBackendTests
{
    [Fact]
    public void ParseOrganizationRow_PrefersFormattedValues()
    {
        var json = """
        {
            "isauditenabled": true,
            "isauditenabled@OData.Community.Display.V1.FormattedValue": "Yes",
            "maxuploadfilesize": 131072,
            "maxuploadfilesize@OData.Community.Display.V1.FormattedValue": "131,072"
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = OrganizationTableSettingsBackend.ParseOrganizationRow(doc.RootElement);

        var byName = result.ToDictionary(s => s.Name, s => s.Value);
        Assert.Equal("Yes", byName["isauditenabled"]);
        Assert.Equal("131,072", byName["maxuploadfilesize"]);
    }

    [Fact]
    public void ParseOrganizationRow_StripsAnnotationKeys()
    {
        var json = """
        {
            "name": "Contoso",
            "@odata.context": "https://example.com/$metadata",
            "isauditenabled": true,
            "isauditenabled@OData.Community.Display.V1.FormattedValue": "Yes",
            "_modifiedby_value@Microsoft.Dynamics.CRM.lookuplogicalname": "systemuser"
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = OrganizationTableSettingsBackend.ParseOrganizationRow(doc.RootElement);

        var names = result.Select(s => s.Name).ToList();
        Assert.Contains("name", names);
        Assert.Contains("isauditenabled", names);
        Assert.DoesNotContain("@odata.context", names);
        Assert.DoesNotContain("isauditenabled@OData.Community.Display.V1.FormattedValue", names);
        Assert.DoesNotContain("_modifiedby_value@Microsoft.Dynamics.CRM.lookuplogicalname", names);
    }

    [Fact]
    public void ParseOrganizationRow_HandlesNullBooleanAndNumber()
    {
        var json = """
        {
            "nullsetting": null,
            "boolsetting": false,
            "numbersetting": 42,
            "stringsetting": "hello"
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var result = OrganizationTableSettingsBackend.ParseOrganizationRow(doc.RootElement);

        var byName = result.ToDictionary(s => s.Name, s => s.Value);
        Assert.Null(byName["nullsetting"]);
        Assert.Equal(false, (bool)byName["boolsetting"]!);
        Assert.Equal(42, Convert.ToInt32(byName["numbersetting"]));
        Assert.Equal("hello", (string)byName["stringsetting"]!);
    }

    [Fact]
    public void ParseOrganizationRow_EmptyRow_ReturnsEmpty()
    {
        using var doc = JsonDocument.Parse("{}");
        var result = OrganizationTableSettingsBackend.ParseOrganizationRow(doc.RootElement);
        Assert.Empty(result);
    }
}
