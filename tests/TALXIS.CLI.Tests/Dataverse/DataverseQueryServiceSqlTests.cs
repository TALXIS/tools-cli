using TALXIS.CLI.Platform.Dataverse.Data;
using Xunit;

namespace TALXIS.CLI.Tests.Dataverse;

public class DataverseQueryServiceSqlTests
{
    [Theory]
    [InlineData("SELECT TOP 5 name FROM account", 5)]
    [InlineData("SELECT TOP (5) name FROM account", 5)]
    [InlineData("select top 5 name from account", 5)]
    [InlineData("  SELECT DISTINCT TOP 3 name FROM account", 3)]
    public void TryGetSqlTop_OuterTopClause_ReturnsLimit(string sql, int expected)
    {
        Assert.Equal(expected, DataverseQueryService.TryGetSqlTop(sql));
    }

    [Theory]
    [InlineData("SELECT name FROM account")]
    [InlineData("SELECT laptop FROM account")]
    [InlineData("SELECT name FROM account WHERE accountid IN (SELECT TOP 1 accountid FROM account)")]
    public void TryGetSqlTop_NoOuterTopClause_ReturnsNull(string sql)
    {
        Assert.Null(DataverseQueryService.TryGetSqlTop(sql));
    }

    [Theory]
    [InlineData(10, 5, 5)]
    [InlineData(5, 10, 5)]
    [InlineData(null, 5, 5)]
    [InlineData(5, null, 5)]
    [InlineData(null, null, null)]
    public void MinTop_ReturnsSmallerLimit(int? a, int? b, int? expected)
    {
        Assert.Equal(expected, DataverseQueryService.MinTop(a, b));
    }

    [Theory]
    [InlineData("SELECT name FROM account")]
    [InlineData("SELECT name FROM dbo.account")]
    [InlineData("SELECT name FROM [dbo].[account]")]
    [InlineData("SELECT name FROM [account]")]
    [InlineData("SELECT name FROM Account WHERE name = 'x'")]
    public void TryGetSqlTableName_ParsesTable(string sql)
    {
        Assert.Equal("account", DataverseQueryService.TryGetSqlTableName(sql));
    }

    [Fact]
    public void TryGetSqlTableName_NoFromClause_ReturnsNull()
    {
        Assert.Null(DataverseQueryService.TryGetSqlTableName("SELECT 1"));
    }

    [Fact]
    public void BuildHeaders_ReturnsNewDictionaryPerCall()
    {
        var first = DataverseQueryService.BuildHeaders(includeAnnotations: true);
        var second = DataverseQueryService.BuildHeaders(includeAnnotations: true);

        Assert.NotSame(first, second);
        Assert.NotSame(first["Prefer"], second["Prefer"]);
    }
}
