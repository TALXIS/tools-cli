using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using TALXIS.CLI.Platform.Dataverse.Data;
using Xunit;

namespace TALXIS.CLI.Tests.Dataverse;

public class EntityJsonConverterNumberTests
{
    private const string EntityName = "new_sample";

    [Theory]
    [InlineData("0", 0)]
    [InlineData("42", 42)]
    [InlineData("42.5", 42.5)]
    public void DecimalColumn_AnyJsonNumber_BecomesDecimal(string json, double expected)
    {
        var entity = Convert(new DecimalAttributeMetadata { LogicalName = "new_weight" }, "new_weight", json);

        var value = Assert.IsType<decimal>(entity["new_weight"]);
        Assert.Equal((decimal)expected, value);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("42.5", 42.5)]
    public void FloatColumn_AnyJsonNumber_BecomesDouble(string json, double expected)
    {
        var entity = Convert(new DoubleAttributeMetadata { LogicalName = "new_ratio" }, "new_ratio", json);

        var value = Assert.IsType<double>(entity["new_ratio"]);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void ColumnWithoutMetadata_WholeNumber_StaysInt32()
    {
        var entity = Convert(attribute: null, "new_count", "7");

        Assert.IsType<int>(entity["new_count"]);
    }

    private static Entity Convert(AttributeMetadata? attribute, string column, string jsonValue)
    {
        using var doc = JsonDocument.Parse($"{{\"{column}\": {jsonValue}}}");
        var metadata = new EntityMetadata { LogicalName = EntityName };
        if (attribute is not null)
        {
            typeof(EntityMetadata).GetProperty(nameof(EntityMetadata.Attributes))!
                .SetValue(metadata, new[] { attribute });
        }

        return EntityJsonConverter.JsonToEntity(EntityName, doc.RootElement, metadata);
    }
}
