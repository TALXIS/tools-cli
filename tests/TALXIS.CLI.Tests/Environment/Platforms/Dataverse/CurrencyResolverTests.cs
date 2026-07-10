using System;
using System.Collections.Generic;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Data;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Platforms.Dataverse;

/// <summary>
/// Unit tests for <see cref="CurrencyResolver"/> ISO-code/name matching.
/// </summary>
public class CurrencyResolverTests
{
    private static readonly Guid CzkId = Guid.NewGuid();
    private static readonly Guid UsdId = Guid.NewGuid();
    private static readonly Guid CadId = Guid.NewGuid();

    private static readonly IReadOnlyList<CurrencyInfo> Currencies = new[]
    {
        new CurrencyInfo(CzkId, "CZK", "Czech Koruna", "Kč"),
        new CurrencyInfo(UsdId, "USD", "US Dollar", "$"),
        new CurrencyInfo(CadId, "CAD", "Canadian Dollar", "$"),
    };

    [Fact]
    public void Resolve_ExactIsoCode_ReturnsMatch()
    {
        var match = CurrencyResolver.Resolve(Currencies, "CZK");

        Assert.Equal(CzkId, match.Id);
    }

    [Fact]
    public void Resolve_IsoCodeIsCaseInsensitive()
    {
        var match = CurrencyResolver.Resolve(Currencies, "czk");

        Assert.Equal(CzkId, match.Id);
    }

    [Fact]
    public void Resolve_UniqueNameSubstring_ReturnsMatch()
    {
        var match = CurrencyResolver.Resolve(Currencies, "Koruna");

        Assert.Equal(CzkId, match.Id);
    }

    [Fact]
    public void Resolve_NoMatch_ThrowsWithBrowseHint()
    {
        var ex = Assert.Throws<ArgumentException>(() => CurrencyResolver.Resolve(Currencies, "XYZ"));

        Assert.Contains("XYZ", ex.Message);
        Assert.Contains("currency list", ex.Message);
    }

    [Fact]
    public void Resolve_Ambiguous_ThrowsAndListsCandidates()
    {
        var ex = Assert.Throws<ArgumentException>(() => CurrencyResolver.Resolve(Currencies, "Dollar"));

        Assert.Contains("matches 2 currencies", ex.Message);
        Assert.Contains("USD", ex.Message);
        Assert.Contains("CAD", ex.Message);
    }

    [Fact]
    public void Resolve_EmptyQuery_Throws()
    {
        Assert.Throws<ArgumentException>(() => CurrencyResolver.Resolve(Currencies, "  "));
    }
}
