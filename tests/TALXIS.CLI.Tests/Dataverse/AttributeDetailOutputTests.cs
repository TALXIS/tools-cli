using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using TALXIS.CLI.Core;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.Entity;
using TALXIS.CLI.Features.Environment.OptionSet;
using TALXIS.CLI.Platform.Dataverse.Application.Services;
using Xunit;

namespace TALXIS.CLI.Tests.Dataverse;

public class AttributeDetailOutputTests
{
    private static readonly string[] CommonJsonKeys =
    [
        "logicalName",
        "schemaName",
        "displayName",
        "description",
        "attributeTypeName",
        "requiredLevel",
        "requiredLevelCanChange",
        "isCustomAttribute",
        "isAuditable",
        "isSearchable",
        "isSecured",
    ];

    [Fact]
    public void AttributeGet_Json_StringAttribute_UsesCamelCaseKeys()
    {
        var keys = JsonKeys(DataverseEntityMetadataService.BuildAttributeDetail(StringAttribute()));

        Assert.Equal([.. CommonJsonKeys, "maxLength", "formatName"], keys);
    }

    [Fact]
    public void AttributeGet_Json_PicklistAttribute_UsesCamelCaseKeys()
    {
        var json = Serialize(DataverseEntityMetadataService.BuildAttributeDetail(PicklistAttribute()));
        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        Assert.Equal([.. CommonJsonKeys, "isGlobalOptionSet", "optionSetName", "options"], keys);
        var option = doc.RootElement.GetProperty("options")[0];
        Assert.Equal(["label", "value"], option.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("Low", option.GetProperty("label").GetString());
        Assert.Equal(1, option.GetProperty("value").GetInt32());
    }

    [Fact]
    public void AttributeGet_Json_SharedFields_MatchEntityDescribeNames()
    {
        var describeKeys = JsonKeys(new EntityAttributeRecord(
            "skt_rank", "skt_rank", "Rank", "StringType", true, false, false, 100, "desc", "skt_set", "1=A"));
        var getKeys = JsonKeys(DataverseEntityMetadataService.BuildAttributeDetail(PicklistAttribute()));

        string[] shared = ["logicalName", "schemaName", "displayName", "description", "attributeTypeName", "isCustomAttribute", "optionSetName"];
        Assert.All(shared, k => Assert.Contains(k, describeKeys));
        Assert.All(shared, k => Assert.Contains(k, getKeys));
    }

    [Fact]
    public void AttributeGet_Json_MissingLabels_AreNullNotPlaceholder()
    {
        var attr = StringAttribute();
        attr.DisplayName = new Label();
        attr.Description = new Label();

        using var doc = JsonDocument.Parse(Serialize(DataverseEntityMetadataService.BuildAttributeDetail(attr)));

        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("displayName").ValueKind);
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("description").ValueKind);
    }

    [Fact]
    public void AttributeGet_Text_KeepsHumanCaptions()
    {
        var text = CaptureText(() =>
            EntityAttributeGetCliCommand.PrintDetail(DataverseEntityMetadataService.BuildAttributeDetail(StringAttribute())));

        string[] expected =
        [
            "Logical Name               skt_rank",
            "Schema Name                skt_Rank",
            "Display Name               Rank",
            "Description                Rank of the product",
            "Attribute Type             StringType",
            "Required Level             None",
            "Required Level Can Change  True",
            "Is Custom                  False",
            "Is Auditable               False",
            "Is Searchable              True",
            "Is Secured                 False",
            "MaxLength                  100",
            "FormatName                 Text",
        ];
        Assert.Equal(expected, text);
    }

    [Fact]
    public void OptionSetGet_Text_LocalAttribute_ReadsRenamedKeys()
    {
        var text = CaptureText(() =>
            OptionSetGetCliCommand.PrintAttributeDetail(DataverseEntityMetadataService.BuildAttributeDetail(PicklistAttribute())));

        Assert.Equal("Attribute:    skt_priority", text[0]);
        Assert.Equal("Option Set:   skt_product_skt_priority", text[1]);
        Assert.Equal("Global:       False", text[2]);
    }

    private static StringAttributeMetadata StringAttribute() => new()
    {
        LogicalName = "skt_rank",
        SchemaName = "skt_Rank",
        DisplayName = UserLabel("Rank"),
        Description = UserLabel("Rank of the product"),
        RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
        IsAuditEnabled = new BooleanManagedProperty(false),
        IsValidForAdvancedFind = new BooleanManagedProperty(true),
        IsSecured = false,
        MaxLength = 100,
        FormatName = StringFormatName.Text,
    };

    private static PicklistAttributeMetadata PicklistAttribute()
    {
        var optionSet = new OptionSetMetadata
        {
            Name = "skt_product_skt_priority",
            IsGlobal = false,
        };
        optionSet.Options.Add(new OptionMetadata(UserLabel("Low"), 1));

        return new PicklistAttributeMetadata
        {
            LogicalName = "skt_priority",
            SchemaName = "skt_Priority",
            DisplayName = UserLabel("Priority"),
            RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
            OptionSet = optionSet,
        };
    }

    private static Label UserLabel(string text)
    {
        var localized = new LocalizedLabel(text, 1033);
        return new Label(localized, new[] { localized });
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, TxcOutputJsonOptions.Default);

    private static string[] JsonKeys<T>(T value)
    {
        using var doc = JsonDocument.Parse(Serialize(value));
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
    }

    private static string[] CaptureText(Action render)
    {
        var sw = new StringWriter();
        using (OutputWriter.RedirectTo(sw))
        {
            render();
        }
        return sw.ToString().Split(System.Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
    }
}
