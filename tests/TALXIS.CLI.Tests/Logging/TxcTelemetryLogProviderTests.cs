using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TALXIS.CLI.Abstractions;
using TALXIS.CLI.Core.Telemetry;
using TALXIS.CLI.Logging;
using Xunit;

namespace TALXIS.CLI.Tests.Logging;

public class TxcTelemetryLogProviderTests
{
    [Fact]
    public void LogError_WithoutException_StampsRedactedErrorMessageOnCurrentActivity()
    {
        using var listener = CreateListener();
        using var activity = TxcActivitySource.Instance.StartActivity("workspace_validate");
        using var provider = new TxcTelemetryLogProvider();
        var logger = provider.CreateLogger("test");

        logger.LogError("Validation failed with ClientSecret={Secret}", "super-secret");

        var errorMessage = Assert.IsType<string>(activity?.GetTagItem(TxcTelemetryTags.ErrorMessage));
        Assert.Contains("***REDACTED***", errorMessage);
        Assert.DoesNotContain("super-secret", errorMessage);
    }

    [Fact]
    public void LogError_WithException_RecordsExceptionEventAndPromotesContext()
    {
        using var listener = CreateListener();
        using var activity = TxcActivitySource.Instance.StartActivity("workspace_validate");
        using var provider = new TxcTelemetryLogProvider();
        var logger = provider.CreateLogger("test");

        activity?.SetTag(TxcTelemetryTags.EndUserId, "user-123");
        activity?.SetTag(TxcTelemetryTags.EndUserIdDimension, "user-123");
        activity?.SetTag(TxcTelemetryTags.EndUserName, "user@example.com");
        activity?.SetTag(TxcTelemetryTags.EndUserScope, "tenant-456");
        activity?.SetTag(TxcTelemetryTags.EnvironmentName, "Sandbox");

        logger.LogError(
            new InvalidOperationException("outer", new InvalidOperationException("boom")),
            "Command failed");

        Assert.Equal("boom", activity?.GetTagItem(TxcTelemetryTags.ErrorMessage));

        var exceptionEvent = Assert.Single(activity!.Events, e => e.Name == "exception");
        Assert.Contains(exceptionEvent.Tags, tag => tag.Key == TxcTelemetryTags.EndUserId && Equals(tag.Value, "user-123"));
        Assert.Contains(exceptionEvent.Tags, tag => tag.Key == TxcTelemetryTags.EndUserIdDimension && Equals(tag.Value, "user-123"));
        Assert.Contains(exceptionEvent.Tags, tag => tag.Key == TxcTelemetryTags.EndUserName && Equals(tag.Value, "user@example.com"));
        Assert.Contains(exceptionEvent.Tags, tag => tag.Key == TxcTelemetryTags.EndUserScope && Equals(tag.Value, "tenant-456"));
        Assert.Contains(exceptionEvent.Tags, tag => tag.Key == TxcTelemetryTags.EnvironmentName && Equals(tag.Value, "Sandbox"));
        Assert.Contains(exceptionEvent.Tags, tag => tag.Key == TxcTelemetryTags.ErrorMessage && Equals(tag.Value, "boom"));
    }

    [Fact]
    public void LogError_WithException_DoesNotOverwriteExistingErrorMessage()
    {
        using var listener = CreateListener();
        using var activity = TxcActivitySource.Instance.StartActivity("workspace_validate");
        using var provider = new TxcTelemetryLogProvider();
        var logger = provider.CreateLogger("test");

        activity?.SetTag(TxcTelemetryTags.ErrorMessage, "existing-message");

        logger.LogError(new InvalidOperationException("boom"), "Command failed");

        Assert.Equal("existing-message", activity?.GetTagItem(TxcTelemetryTags.ErrorMessage));
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
}
