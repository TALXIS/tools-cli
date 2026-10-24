using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.TemplateEngine.Abstractions.Installer;
using Microsoft.TemplateEngine.Abstractions.TemplatePackage;
using NuGet.Versioning;
using TALXIS.CLI.Features.Workspace.TemplateEngine;
using Xunit;

namespace TALXIS.CLI.Tests.TemplateEngine;

public class TemplatePackageServiceTests
{
    private const string PackageIdentifier = "TALXIS.DevKit.Templates.Dataverse";

    [Fact]
    public void RankCandidates_PrefersHighestVersion_RegardlessOfListOrder()
    {
        var oldest = new FakeManagedTemplatePackage(PackageIdentifier, "1.0.0", DateTime.UtcNow.AddDays(-10));
        var newest = new FakeManagedTemplatePackage(PackageIdentifier, "2.5.0", DateTime.UtcNow.AddDays(-1));
        var middle = new FakeManagedTemplatePackage(PackageIdentifier, "2.0.0", DateTime.UtcNow.AddDays(-5));

        // Deliberately not in version order - the stale registration happens to be first in the
        // underlying list, which is exactly the scenario that used to break FirstOrDefault().
        var ranked = TemplatePackageService.RankCandidates(new[] { oldest, newest, middle }, PackageIdentifier).ToList();

        Assert.Equal(new[] { newest, middle, oldest }, ranked);
    }

    [Fact]
    public void RankCandidates_IgnoresNonMatchingIdentifiers()
    {
        var match = new FakeManagedTemplatePackage(PackageIdentifier, "1.0.0", DateTime.UtcNow);
        var other = new FakeManagedTemplatePackage("Some.Other.Package", "9.0.0", DateTime.UtcNow);

        var ranked = TemplatePackageService.RankCandidates(new[] { match, other }, PackageIdentifier).ToList();

        Assert.Equal(new[] { match }, ranked);
    }

    [Fact]
    public void RankCandidates_IdentifierMatch_IsCaseInsensitive()
    {
        var match = new FakeManagedTemplatePackage(PackageIdentifier.ToUpperInvariant(), "1.0.0", DateTime.UtcNow);

        var ranked = TemplatePackageService.RankCandidates(new[] { match }, PackageIdentifier).ToList();

        Assert.Equal(new[] { match }, ranked);
    }

    [Fact]
    public void RankCandidates_FallsBackToLastChangeTime_WhenVersionsTie()
    {
        var stale = new FakeManagedTemplatePackage(PackageIdentifier, "1.0.0", DateTime.UtcNow.AddDays(-3));
        var fresh = new FakeManagedTemplatePackage(PackageIdentifier, "1.0.0", DateTime.UtcNow);

        var ranked = TemplatePackageService.RankCandidates(new[] { stale, fresh }, PackageIdentifier).ToList();

        Assert.Equal(new[] { fresh, stale }, ranked);
    }

    [Fact]
    public void RankCandidates_FallsBackToLastChangeTime_WhenVersionsUnparseable()
    {
        var stale = new FakeManagedTemplatePackage(PackageIdentifier, "not-a-version", DateTime.UtcNow.AddDays(-3));
        var fresh = new FakeManagedTemplatePackage(PackageIdentifier, "also-not-a-version", DateTime.UtcNow);

        var ranked = TemplatePackageService.RankCandidates(new[] { stale, fresh }, PackageIdentifier).ToList();

        Assert.Equal(new[] { fresh, stale }, ranked);
    }

    [Fact]
    public void RankCandidates_ParsedVersion_OutranksUnparseableVersion()
    {
        // An unparseable/missing version must never be treated as "higher" than a real one.
        var unparseable = new FakeManagedTemplatePackage(PackageIdentifier, "garbage", DateTime.UtcNow);
        var parsed = new FakeManagedTemplatePackage(PackageIdentifier, "1.0.0", DateTime.UtcNow.AddDays(-100));

        var ranked = TemplatePackageService.RankCandidates(new[] { unparseable, parsed }, PackageIdentifier).ToList();

        Assert.Equal(new[] { parsed, unparseable }, ranked);
    }

    [Fact]
    public void ParsePackageVersion_ReturnsNull_ForNullOrInvalidVersionString()
    {
        Assert.Null(TemplatePackageService.ParsePackageVersion(new FakeManagedTemplatePackage(PackageIdentifier, null, DateTime.UtcNow)));
        Assert.Null(TemplatePackageService.ParsePackageVersion(new FakeManagedTemplatePackage(PackageIdentifier, "not-a-version", DateTime.UtcNow)));
    }

    [Fact]
    public void ParsePackageVersion_ParsesValidVersionString()
    {
        var version = TemplatePackageService.ParsePackageVersion(new FakeManagedTemplatePackage(PackageIdentifier, "3.2.1", DateTime.UtcNow));

        Assert.Equal(NuGetVersion.Parse("3.2.1"), version);
    }

    [Fact]
    public void ParsePackageVersion_ParsesPrereleaseVersionString()
    {
        // System.Version cannot parse a "-beta.1" suffix at all - it would previously return null
        // here, silently demoting a valid, higher-precedence prerelease registration to "unparseable".
        var version = TemplatePackageService.ParsePackageVersion(new FakeManagedTemplatePackage(PackageIdentifier, "2.0.0-beta.1", DateTime.UtcNow));

        Assert.Equal(NuGetVersion.Parse("2.0.0-beta.1"), version);
    }

    [Fact]
    public void RankCandidates_PrereleaseVersion_OutranksLowerReleaseVersion()
    {
        // A working 2.0.0-beta.1 registration must be probed before a working 1.0.0 registration -
        // the whole point of "highest version wins" breaks if prerelease suffixes sort as unparseable.
        var releaseCandidate = new FakeManagedTemplatePackage(PackageIdentifier, "1.0.0", DateTime.UtcNow);
        var prereleaseCandidate = new FakeManagedTemplatePackage(PackageIdentifier, "2.0.0-beta.1", DateTime.UtcNow.AddDays(-10));

        var ranked = TemplatePackageService.RankCandidates(new[] { releaseCandidate, prereleaseCandidate }, PackageIdentifier).ToList();

        Assert.Equal(new[] { prereleaseCandidate, releaseCandidate }, ranked);
    }

    [Fact]
    public void RankCandidates_ReleaseVersion_OutranksPrereleaseOfSameVersion()
    {
        // Per SemVer 2.0 precedence, a version without a prerelease suffix always outranks the same
        // major.minor.patch with one (2.0.0 > 2.0.0-beta.1).
        var prerelease = new FakeManagedTemplatePackage(PackageIdentifier, "2.0.0-beta.1", DateTime.UtcNow);
        var release = new FakeManagedTemplatePackage(PackageIdentifier, "2.0.0", DateTime.UtcNow.AddDays(-10));

        var ranked = TemplatePackageService.RankCandidates(new[] { prerelease, release }, PackageIdentifier).ToList();

        Assert.Equal(new[] { release, prerelease }, ranked);
    }

    [Fact]
    public async Task SelectFirstPackageWithTemplatesAsync_SkipsEmptyCandidate_AndFallsThroughToNext()
    {
        var stale = new FakeManagedTemplatePackage(PackageIdentifier, "2.0.0", DateTime.UtcNow); // ranked first, but broken/empty
        var working = new FakeManagedTemplatePackage(PackageIdentifier, "1.0.0", DateTime.UtcNow.AddDays(-1)); // ranked second, has templates

        var probed = new List<IManagedTemplatePackage>();
        Task<bool> HasTemplates(IManagedTemplatePackage package)
        {
            probed.Add(package);
            return Task.FromResult(!ReferenceEquals(package, stale));
        }

        var selected = await TemplatePackageService.SelectFirstPackageWithTemplatesAsync(new[] { stale, working }, HasTemplates);

        Assert.Same(working, selected);
        Assert.Equal(new IManagedTemplatePackage[] { stale, working }, probed);
    }

    [Fact]
    public async Task SelectFirstPackageWithTemplatesAsync_ReturnsNull_WhenNoCandidateHasTemplates()
    {
        var a = new FakeManagedTemplatePackage(PackageIdentifier, "2.0.0", DateTime.UtcNow);
        var b = new FakeManagedTemplatePackage(PackageIdentifier, "1.0.0", DateTime.UtcNow);

        var selected = await TemplatePackageService.SelectFirstPackageWithTemplatesAsync(new[] { a, b }, _ => Task.FromResult(false));

        Assert.Null(selected);
    }

    [Fact]
    public async Task SelectFirstPackageWithTemplatesAsync_ReturnsNull_WhenNoCandidates()
    {
        var selected = await TemplatePackageService.SelectFirstPackageWithTemplatesAsync(
            Enumerable.Empty<IManagedTemplatePackage>(), _ => Task.FromResult(true));

        Assert.Null(selected);
    }

    /// <summary>
    /// Minimal <see cref="IManagedTemplatePackage"/> test double. Only the members the ranking/selection
    /// logic actually reads (<see cref="Identifier"/>, <see cref="Version"/>, <see cref="ITemplatePackage.LastChangeTime"/>)
    /// carry meaningful values; the rest are unused by the code under test.
    /// </summary>
    private sealed class FakeManagedTemplatePackage : IManagedTemplatePackage
    {
        public FakeManagedTemplatePackage(string identifier, string? version, DateTime lastChangeTime)
        {
            Identifier = identifier;
            Version = version!;
            LastChangeTime = lastChangeTime;
        }

        public string DisplayName => Identifier;
        public string Identifier { get; }
        public IInstaller Installer => null!;
        public IManagedTemplatePackageProvider ManagedProvider => null!;
        public string Version { get; }
        public bool IsLocalPackage => true;
        public DateTime LastChangeTime { get; }
        public string MountPointUri => string.Empty;
        public ITemplatePackageProvider Provider => null!;

        public IReadOnlyDictionary<string, string> GetDetails() => new Dictionary<string, string>();
    }
}
