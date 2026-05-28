using System.Diagnostics;
using TALXIS.CLI.Abstractions;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Telemetry;
using Xunit;

namespace TALXIS.CLI.Tests.Telemetry;

public class ActivityIdentityTaggerTests
{
    [Fact]
    public async Task TagFromProfileAsync_WithExplicitProfile_TagsActivity()
    {
        using var listener = CreateListener();
        using var activity = TxcActivitySource.Instance.StartActivity("workspace_validate");

        var tagger = new ActivityIdentityTagger(new FakeConfigurationResolver(_ => CreateResolvedProfileContext()));

        await tagger.TagFromProfileAsync(activity, "explicit-profile", CancellationToken.None);

        Assert.Equal("11111111-1111-1111-1111-111111111111", activity?.GetTagItem(TxcTelemetryTags.EndUserId));
        Assert.Equal("11111111-1111-1111-1111-111111111111", activity?.GetTagItem(TxcTelemetryTags.EndUserIdDimension));
        Assert.Equal("user@example.com", activity?.GetTagItem(TxcTelemetryTags.EndUserName));
        Assert.Equal("tenant-123", activity?.GetTagItem(TxcTelemetryTags.EndUserScope));
        Assert.Equal("https://contoso.crm.dynamics.com", activity?.GetTagItem(TxcTelemetryTags.EnvironmentUrl));
        Assert.Equal("Contoso Sandbox", activity?.GetTagItem(TxcTelemetryTags.EnvironmentName));
    }

    [Fact]
    public async Task TagFromActiveProfileAsync_UsesResolverFallbackChain()
    {
        using var listener = CreateListener();
        using var activity = TxcActivitySource.Instance.StartActivity("workspace_validate");

        var resolver = new FakeConfigurationResolver(_ => CreateResolvedProfileContext());
        var tagger = new ActivityIdentityTagger(resolver);

        await tagger.TagFromActiveProfileAsync(activity);

        Assert.Single(resolver.RequestedProfiles);
        Assert.Null(resolver.RequestedProfiles[0]);
        Assert.Equal("user@example.com", activity?.GetTagItem(TxcTelemetryTags.EndUserName));
    }

    [Fact]
    public async Task TagFromProfileAsync_OnResolutionFailure_LeavesActivityUntouched()
    {
        using var listener = CreateListener();
        using var activity = TxcActivitySource.Instance.StartActivity("workspace_validate");

        var tagger = new ActivityIdentityTagger(
            new FakeConfigurationResolver(_ => throw new ConfigurationResolutionException("missing profile")));

        await tagger.TagFromProfileAsync(activity, "missing-profile", CancellationToken.None);

        Assert.Null(activity?.GetTagItem(TxcTelemetryTags.EndUserId));
        Assert.Null(activity?.GetTagItem(TxcTelemetryTags.EndUserIdDimension));
        Assert.Null(activity?.GetTagItem(TxcTelemetryTags.EndUserName));
        Assert.Null(activity?.GetTagItem(TxcTelemetryTags.EndUserScope));
    }

    private static ResolvedProfileContext CreateResolvedProfileContext()
    {
        return new ResolvedProfileContext(
            new Profile { Id = "test-profile", ConnectionRef = "conn", CredentialRef = "cred" },
            new Connection
            {
                Id = "conn",
                Provider = ProviderKind.Dataverse,
                EnvironmentUrl = "https://contoso.crm.dynamics.com",
                DisplayName = "Contoso Sandbox",
                TenantId = "tenant-123"
            },
            new Credential
            {
                Id = "cred",
                Kind = CredentialKind.InteractiveBrowser,
                InteractiveAccountId = "11111111-1111-1111-1111-111111111111.tenant-123",
                InteractiveUpn = "user@example.com"
            },
            ResolutionSource.CommandLine);
    }

    private static ActivityListener CreateListener()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TxcActivitySource.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class FakeConfigurationResolver : IConfigurationResolver
    {
        private readonly Func<string?, ResolvedProfileContext> _factory;

        public FakeConfigurationResolver(Func<string?, ResolvedProfileContext> factory)
        {
            _factory = factory;
        }

        public List<string?> RequestedProfiles { get; } = [];

        public Task<ResolvedProfileContext> ResolveAsync(string? profileName, CancellationToken ct)
        {
            RequestedProfiles.Add(profileName);
            return Task.FromResult(_factory(profileName));
        }
    }
}
