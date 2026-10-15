using TALXIS.CLI.Core.Platforms.PowerPlatform;
using TALXIS.CLI.Platform.Dataverse.Application.Services;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement;

public class ConnectionBindingErrorsTests
{
    [Fact]
    public void ConnectionNotFound_BecomesAConnectionNotFoundException()
    {
        var raw = new InvalidOperationException("Request failed with: 404 {\"error\":{\"code\":\"ConnectionNotFound\"}}");

        Assert.True(ConnectionBindingErrors.TryTranslate(raw, "c9", "shared_x", out var translated));

        var ex = Assert.IsType<ConnectionNotFoundException>(translated);
        Assert.Equal("c9", ex.ConnectionId);
        Assert.Equal("shared_x", ex.ApiId);
    }

    [Fact]
    public void AuthorizationFailed_BecomesAConnectionAccessDeniedWithoutCliSyntax()
    {
        var raw = new InvalidOperationException("... ConnectionAuthorizationFailed: the caller does not have the minimum required permission");

        Assert.True(ConnectionBindingErrors.TryTranslate(raw, "c9", "shared_x", out var translated));

        var ex = Assert.IsType<ConnectionAccessDeniedException>(translated);
        Assert.Contains("'c9'", ex.Message);
        Assert.Contains("share it with the calling identity", ex.Message);
        Assert.DoesNotContain("txc ", ex.Message);
    }

    [Fact]
    public void OtherErrors_AreLeftAlone()
    {
        var raw = new InvalidOperationException("The server is on fire.");

        Assert.False(ConnectionBindingErrors.TryTranslate(raw, "c9", "shared_x", out var translated));
        Assert.Same(raw, translated);
    }

    [Fact]
    public async Task TranslateAsync_ThrowsTheTranslatedException_AndPassesOtherErrorsThrough()
    {
        await Assert.ThrowsAsync<ConnectionNotFoundException>(() => ConnectionBindingErrors.TranslateAsync<int>(
            "shared_x", "c9", () => throw new InvalidOperationException("ConnectionNotFound")));
        await Assert.ThrowsAsync<TimeoutException>(() => ConnectionBindingErrors.TranslateAsync<int>(
            "shared_x", "c9", () => throw new TimeoutException()));
        Assert.Equal(7, await ConnectionBindingErrors.TranslateAsync("shared_x", "c9", () => Task.FromResult(7)));
    }
}
