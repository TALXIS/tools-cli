using System;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Metadata;
using TALXIS.CLI.Platform.Dataverse.Data;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

/// <summary>
/// Unit tests for <see cref="EntityJsonConverter"/> value coercion, focused on
/// whole-number (Int32) range handling so out-of-range values produce a clear
/// message instead of a misleading SDK type-mismatch error.
/// </summary>
public class EntityJsonConverterTests
{
    private const string Entity = "txcv3_valv3rt1434";
    private const string Column = "txcv3_wholenumplain";

    private static EntityMetadata MetadataWith(AttributeMetadata attribute)
    {
        var meta = new EntityMetadata { LogicalName = Entity };
        SetProp(meta, nameof(EntityMetadata.Attributes), new[] { attribute });
        return meta;
    }

    private static AttributeMetadata IntegerColumn(string logicalName = Column)
    {
        var attr = new IntegerAttributeMetadata();
        SetProp(attr, nameof(AttributeMetadata.LogicalName), logicalName);
        return attr;
    }

    private static AttributeMetadata BigIntColumn(string logicalName)
    {
        var attr = new BigIntAttributeMetadata();
        SetProp(attr, nameof(AttributeMetadata.LogicalName), logicalName);
        return attr;
    }

    // Xrm SDK metadata properties expose non-public setters — reach them by reflection.
    private static void SetProp(object target, string name, object? value)
        => target.GetType().GetProperty(name)!.SetValue(target, value);

    private static Entity Convert(EntityMetadata metadata, string json)
        => EntityJsonConverter.JsonToEntity(Entity, JsonDocument.Parse(json).RootElement, metadata);

    [Fact]
    public void WholeNumber_WithinInt32Range_StoredAsInt()
    {
        var entity = Convert(MetadataWith(IntegerColumn()), $"{{\"{Column}\":2000000000}}");

        Assert.Equal(2000000000, Assert.IsType<int>(entity[Column]));
    }

    [Fact]
    public void WholeNumber_AboveInt32Max_ThrowsRangeErrorWithValueAndColumn()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Convert(MetadataWith(IntegerColumn()), $"{{\"{Column}\":3000000000}}"));

        Assert.Contains("3000000000", ex.Message);
        Assert.Contains(Column, ex.Message);
        Assert.Contains("range", ex.Message, StringComparison.OrdinalIgnoreCase);
        // The misleading SDK wording must not leak through.
        Assert.DoesNotContain("Incorrect attribute value type", ex.Message);
    }

    [Fact]
    public void WholeNumber_BelowInt32Min_ThrowsRangeError()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Convert(MetadataWith(IntegerColumn()), $"{{\"{Column}\":-3000000000}}"));

        Assert.Contains("-3000000000", ex.Message);
    }

    [Fact]
    public void WholeNumber_NonInteger_ThrowsFormatError()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => Convert(MetadataWith(IntegerColumn()), $"{{\"{Column}\":1.5}}"));

        Assert.Contains("whole number", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BigInt_AboveInt32Max_StoredAsLong()
    {
        const string column = "txcv3_bignum";
        var entity = Convert(MetadataWith(BigIntColumn(column)), $"{{\"{column}\":3000000000}}");

        Assert.Equal(3000000000L, Assert.IsType<long>(entity[column]));
    }
}
