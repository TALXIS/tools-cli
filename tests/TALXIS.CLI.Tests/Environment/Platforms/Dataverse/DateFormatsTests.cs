using System;
using TALXIS.CLI.Core.Contracts.Dataverse;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

/// <summary>
/// Unit tests for <see cref="DateFormats"/> code/format mapping.
/// </summary>
public class DateFormatsTests
{
    [Fact]
    public void ResolveCode_FormatString_MapsToCode()
    {
        Assert.Equal(2, DateFormats.ToCode(DateFormats.Short, "M/d/yyyy", "short date format"));
        Assert.Equal(1, DateFormats.ToCode(DateFormats.Long, "dddd, d MMMM, yyyy", "long date format"));
    }

    [Fact]
    public void ResolveCode_IsCaseInsensitive()
    {
        Assert.Equal(2, DateFormats.ToCode(DateFormats.Short, "m/d/YYYY", "short date format"));
    }

    [Fact]
    public void ResolveCode_NumericInput_PassesThrough()
    {
        Assert.Equal(5, DateFormats.ToCode(DateFormats.Short, "5", "short date format"));
        // Codes outside the known set still pass through; the environment validates them.
        Assert.Equal(42, DateFormats.ToCode(DateFormats.Long, "42", "long date format"));
    }

    [Fact]
    public void ResolveCode_UnknownFormat_ThrowsAndListsOptions()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => DateFormats.ToCode(DateFormats.Short, "dd/MMM/yy", "short date format"));

        Assert.Contains("dd/MMM/yy", ex.Message);
        Assert.Contains("M/d/yyyy", ex.Message);
    }

    [Fact]
    public void ResolveCode_EmptyInput_Throws()
    {
        Assert.Throws<ArgumentException>(() => DateFormats.ToCode(DateFormats.Short, "  ", "short date format"));
    }

    [Fact]
    public void Describe_InRange_ReturnsFormat()
    {
        Assert.Equal("M/d/yyyy", DateFormats.Describe(DateFormats.Short, 2));
        Assert.Equal("dddd, d MMMM, yyyy", DateFormats.Describe(DateFormats.Long, 1));
    }

    [Fact]
    public void Describe_OutOfRangeOrNull_ReturnsNull()
    {
        Assert.Null(DateFormats.Describe(DateFormats.Short, 99));
        Assert.Null(DateFormats.Describe(DateFormats.Short, null));
        Assert.Null(DateFormats.Describe(DateFormats.Long, -1));
    }
}
