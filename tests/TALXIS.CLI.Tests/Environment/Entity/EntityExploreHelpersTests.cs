using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.Entity;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.EntityExplore;

/// <summary>
/// Unit tests for <see cref="EntityExploreHelpers"/> pure formatting logic.
/// </summary>
public class EntityExploreHelpersTests
{
    [Fact]
    public void OptionLines_ParsesValueLabelPairs()
    {
        var lines = EntityExploreHelpers.OptionLines("375970000:Box, 375970001:Bag, 375970002:Envelope");

        Assert.Equal(new[] { "375970000 = Box", "375970001 = Bag", "375970002 = Envelope" }, lines);
    }

    [Fact]
    public void OptionLines_KeepsColonInsideLabel()
    {
        var lines = EntityExploreHelpers.OptionLines("1:Ratio 1:10");

        Assert.Equal(new[] { "1 = Ratio 1:10" }, lines);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void OptionLines_EmptyInput_ReturnsEmpty(string? optionValues)
    {
        Assert.Empty(EntityExploreHelpers.OptionLines(optionValues));
    }

    [Theory]
    [InlineData("ApplicationRequired", "Required")]
    [InlineData("SystemRequired", "Required")]
    [InlineData("Recommended", "Recommended")]
    [InlineData("None", "")]
    [InlineData(null, "")]
    public void RequiredDisplay_MapsLevels(string? requiredLevel, string expected)
    {
        Assert.Equal(expected, EntityExploreHelpers.RequiredDisplay(requiredLevel));
    }

    [Theory]
    [InlineData("OneToMany", "1:N")]
    [InlineData("ManyToOne", "N:1")]
    [InlineData("ManyToMany", "N:N")]
    [InlineData("Weird", "Weird")]
    public void RelationshipTypeShort_MapsTypes(string relationshipType, string expected)
    {
        Assert.Equal(expected, EntityExploreHelpers.RelationshipTypeShort(relationshipType));
    }

    [Fact]
    public void RelatedEntity_ReturnsTheOtherSide()
    {
        var oneToMany = new EntityRelationshipRecord(
            "item_transactions", "OneToMany", "udpp_item", "udpp_transaction", true, null);
        var manyToOne = new EntityRelationshipRecord(
            "item_owner", "ManyToOne", "udpp_item", "systemuser", true, null);

        Assert.Equal("udpp_transaction", EntityExploreHelpers.RelatedEntity(oneToMany, "udpp_item"));
        Assert.Equal("systemuser", EntityExploreHelpers.RelatedEntity(manyToOne, "udpp_item"));
        Assert.Equal("udpp_item", EntityExploreHelpers.RelatedEntity(oneToMany, "udpp_transaction"));
    }

    [Theory]
    [InlineData(2, "Main")]
    [InlineData(7, "Quick Create")]
    [InlineData(11, "Card")]
    [InlineData(99, "Type 99")]
    public void FormTypeName_MapsKnownCodes(int formType, string expected)
    {
        Assert.Equal(expected, EntityExploreHelpers.FormTypeName(formType));
    }
}
