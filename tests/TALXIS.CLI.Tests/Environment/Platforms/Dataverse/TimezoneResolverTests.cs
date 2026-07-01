using System;
using System.Collections.Generic;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Data;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

/// <summary>
/// Unit tests for <see cref="TimezoneResolver"/> name-to-code matching.
/// </summary>
public class TimezoneResolverTests
{
    private static readonly IReadOnlyList<TimezoneInfo> Timezones = new[]
    {
        new TimezoneInfo(85, "(GMT) Dublin, Edinburgh, Lisbon, London", "GMT Standard Time"),
        new TimezoneInfo(95, "(GMT+01:00) Belgrade, Bratislava, Budapest, Ljubljana, Prague", "Central Europe Standard Time"),
        new TimezoneInfo(105, "(GMT-05:00) Eastern Time (US & Canada)", "Eastern Standard Time"),
        new TimezoneInfo(110, "(GMT-06:00) Central Time (US & Canada)", "Central Standard Time"),
    };

    [Fact]
    public void Resolve_UniqueSubstring_ReturnsMatch()
    {
        var match = TimezoneResolver.Resolve(Timezones, "Prague");

        Assert.Equal(95, match.Code);
    }

    [Fact]
    public void Resolve_MatchesStandardName()
    {
        var match = TimezoneResolver.Resolve(Timezones, "Central Europe");

        Assert.Equal(95, match.Code);
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var match = TimezoneResolver.Resolve(Timezones, "prague");

        Assert.Equal(95, match.Code);
    }

    [Fact]
    public void Resolve_ExactNameWinsOverSubstring()
    {
        var tzs = new[]
        {
            new TimezoneInfo(1, "Prague", null),
            new TimezoneInfo(2, "Greater Prague Area", null),
        };

        var match = TimezoneResolver.Resolve(tzs, "Prague");

        Assert.Equal(1, match.Code);
    }

    [Fact]
    public void Resolve_NoMatch_ThrowsWithBrowseHint()
    {
        var ex = Assert.Throws<ArgumentException>(() => TimezoneResolver.Resolve(Timezones, "Atlantis"));

        Assert.Contains("Atlantis", ex.Message);
        Assert.Contains("timezone list", ex.Message);
    }

    [Fact]
    public void Resolve_Ambiguous_ThrowsAndListsCandidates()
    {
        var ex = Assert.Throws<ArgumentException>(() => TimezoneResolver.Resolve(Timezones, "Canada"));

        Assert.Contains("matches 2 timezones", ex.Message);
        Assert.Contains("--timezone", ex.Message);
        Assert.Contains("105", ex.Message);
        Assert.Contains("110", ex.Message);
    }

    [Fact]
    public void Resolve_EmptyQuery_Throws()
    {
        Assert.Throws<ArgumentException>(() => TimezoneResolver.Resolve(Timezones, "  "));
    }
}
