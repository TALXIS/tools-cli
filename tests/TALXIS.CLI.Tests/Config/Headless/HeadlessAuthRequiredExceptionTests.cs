using TALXIS.CLI.Config.Abstractions;
using TALXIS.CLI.Config.Headless;
using TALXIS.CLI.Config.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Headless;

public sealed class HeadlessAuthRequiredExceptionTests
{
    private sealed class StubDetector : IHeadlessDetector
    {
        public bool IsHeadless { get; init; }
        public string? Reason { get; init; }
    }

    [Fact]
    public void PermittedKinds_ContainsExactlySpecCanonicalSet()
    {
        var expected = new HashSet<CredentialKind>
        {
            CredentialKind.ClientSecret,
            CredentialKind.ClientCertificate,
            CredentialKind.ManagedIdentity,
            CredentialKind.WorkloadIdentityFederation,
            CredentialKind.AzureCli,
            CredentialKind.Pat,
        };
        Assert.True(expected.SetEquals(HeadlessAuthRequiredException.PermittedHeadlessKinds));
    }

    [Fact]
    public void Message_IncludesAttemptedKindInKebab_AndReason()
    {
        var ex = new HeadlessAuthRequiredException(CredentialKind.InteractiveBrowser, "CI=true");
        Assert.Contains("interactive-browser", ex.Message);
        Assert.Contains("CI=true", ex.Message);
    }

    [Fact]
    public void Message_ListsAllPermittedKindsInKebab()
    {
        var ex = new HeadlessAuthRequiredException(CredentialKind.DeviceCode, "stdin and stdout are redirected");
        foreach (var kind in new[]
        {
            "client-secret", "client-certificate", "managed-identity",
            "workload-identity-federation", "azure-cli", "pat",
        })
        {
            Assert.Contains(kind, ex.Message);
        }
    }

    [Fact]
    public void EnsureKindAllowed_NoThrow_WhenInteractive()
    {
        var detector = new StubDetector { IsHeadless = false };
        detector.EnsureKindAllowed(CredentialKind.InteractiveBrowser);
        detector.EnsureKindAllowed(CredentialKind.DeviceCode);
    }

    [Fact]
    public void EnsureKindAllowed_NoThrow_WhenHeadlessAndKindPermitted()
    {
        var detector = new StubDetector { IsHeadless = true, Reason = "CI=true" };
        detector.EnsureKindAllowed(CredentialKind.ClientSecret);
        detector.EnsureKindAllowed(CredentialKind.ManagedIdentity);
    }

    [Fact]
    public void EnsureKindAllowed_Throws_WhenHeadlessAndKindForbidden()
    {
        var detector = new StubDetector { IsHeadless = true, Reason = "CI=true" };
        var ex = Assert.Throws<HeadlessAuthRequiredException>(() =>
            detector.EnsureKindAllowed(CredentialKind.InteractiveBrowser));
        Assert.Equal(CredentialKind.InteractiveBrowser, ex.AttemptedKind);
        Assert.Equal("CI=true", ex.HeadlessReason);
    }

    [Fact]
    public void EnsureKindAllowed_Throws_ForDeviceCode_InHeadless()
    {
        var detector = new StubDetector { IsHeadless = true, Reason = "TXC_NON_INTERACTIVE=1" };
        Assert.Throws<HeadlessAuthRequiredException>(() =>
            detector.EnsureKindAllowed(CredentialKind.DeviceCode));
    }
}
