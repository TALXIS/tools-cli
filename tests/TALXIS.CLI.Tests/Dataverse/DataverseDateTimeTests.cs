using System;
using TALXIS.CLI.Platform.Dataverse;
using Xunit;

namespace TALXIS.CLI.Tests.Dataverse;

public class DataverseDateTimeTests
{
    [Fact]
    public void EnsureUtc_LocalValue_IsConverted()
    {
        var local = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Local);
        var utc = DataverseDateTime.EnsureUtc(local);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(local.ToUniversalTime(), utc);
    }

    [Fact]
    public void EnsureUtc_UnspecifiedValue_IsTreatedAsUtc()
    {
        var unspecified = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var utc = DataverseDateTime.EnsureUtc(unspecified);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(unspecified.Ticks, utc.Ticks);
    }

    [Fact]
    public void EnsureUtc_UtcValue_IsReturnedAsIs()
    {
        var utc = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        Assert.Equal(utc, DataverseDateTime.EnsureUtc(utc));
    }

    [Fact]
    public void EnsureUtc_NullStaysNull()
    {
        DateTime? input = null;
        Assert.Null(DataverseDateTime.EnsureUtc(input));
    }
}
