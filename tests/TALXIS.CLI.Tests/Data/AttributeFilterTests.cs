using System.Linq;
using TALXIS.CLI.Features.Data.DataModelConverter;
using TALXIS.CLI.Features.Data.DataModelConverter.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Data;

public class AttributeFilterTests
{
    private static Table BuildTable()
    {
        return new Table
        {
            LogicalName = "myprefix_account",
            LocalizedName = "Account",
            SetName = "myprefix_accounts",
            Rows =
            {
                new TableRow("myprefix_accountid", RowType.Primarykey),
                new TableRow("myprefix_name", RowType.Nvarchar),
                new TableRow("myprefix_amount", RowType.Money),
                new TableRow("ownerid", RowType.Owner),
                new TableRow("statecode", RowType.State),
                new TableRow("createdon", RowType.Datetimeoffset),
                new TableRow("modifiedby", RowType.Lookup),
            }
        };
    }

    private static ParsedModel BuildModel(Table table)
    {
        return new ParsedModel
        {
            tables = { table },
            relationships = { },
            optionSets = { }
        };
    }

    [Fact]
    public void Apply_NullPatterns_Throws()
    {
        var model = BuildModel(BuildTable());

        Assert.Throws<System.ArgumentNullException>(() => AttributeFilter.Apply(model, null));
    }

    [Fact]
    public void Apply_EmptyPatterns_KeepsEveryColumn()
    {
        var table = BuildTable();
        var model = BuildModel(table);

        AttributeFilter.Apply(model, AttributeFilter.ParsePatterns(""));

        Assert.Equal(7, table.Rows.Count);
    }

    [Fact]
    public void Apply_PrefixWildcardPlusExactNames_KeepsOnlyMatches()
    {
        var table = BuildTable();
        var model = BuildModel(table);

        AttributeFilter.Apply(model, AttributeFilter.ParsePatterns("myprefix_*,ownerid,statecode"));

        var kept = table.Rows.Select(r => r.Name).ToHashSet();
        Assert.Contains("myprefix_name", kept);
        Assert.Contains("myprefix_amount", kept);
        Assert.Contains("ownerid", kept);
        Assert.Contains("statecode", kept);
        Assert.DoesNotContain("createdon", kept);
    }

    [Fact]
    public void Apply_AlwaysKeepsPrimaryKey_EvenWhenUnmatched()
    {
        var table = BuildTable();
        var model = BuildModel(table);

        // A pattern that matches nothing - the PK must still survive.
        AttributeFilter.Apply(model, AttributeFilter.ParsePatterns("zzz_*"));

        Assert.Single(table.Rows);
        Assert.Equal(RowType.Primarykey, table.Rows[0].RowType);
    }

    [Fact]
    public void Apply_KeepsColumnsBackingRelationships_EvenWhenUnmatched()
    {
        var left = BuildTable();
        var right = new Table
        {
            LogicalName = "systemuser",
            LocalizedName = "User",
            SetName = "systemusers",
            Rows = { new TableRow("systemuserid", RowType.Primarykey) }
        };

        var lookupRow = left.Rows.First(r => r.Name == "modifiedby");
        var pkRow = right.Rows[0];

        var model = new ParsedModel
        {
            tables = { left, right },
            relationships =
            {
                new Relationship("myprefix_account_modifiedby", "ManyToOne", left, lookupRow, right, pkRow)
            },
            optionSets = { }
        };

        // "modifiedby" does not match, but it backs a relationship -> must survive.
        AttributeFilter.Apply(model, AttributeFilter.ParsePatterns("myprefix_*"));

        Assert.Contains(left.Rows, r => r.Name == "modifiedby");
        Assert.Contains(right.Rows, r => r.Name == "systemuserid");
    }

    [Fact]
    public void Apply_IsCaseInsensitive()
    {
        var table = BuildTable();
        var model = BuildModel(table);

        AttributeFilter.Apply(model, AttributeFilter.ParsePatterns("MYPREFIX_NAME"));

        Assert.Contains(table.Rows, r => r.Name == "myprefix_name");
    }

    [Fact]
    public void ParsePatterns_TrimsAndDropsBlankEntries()
    {
        var patterns = AttributeFilter.ParsePatterns(" myprefix_* , , ownerid ");

        Assert.Equal(new[] { "myprefix_*", "ownerid" }, patterns);
    }
}
