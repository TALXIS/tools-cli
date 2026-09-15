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
    public void LogError_WithException_RecordsExceptionEventWithoutOverwritingErrorMessage()
    {
        using var listener = CreateListener();
        using var activity = TxcActivitySource.Instance.StartActivity("workspace_validate");
        using var provider = new TxcTelemetryLogProvider();
        var logger = provider.CreateLogger("test");

        logger.LogError(new InvalidOperationException("boom"), "Command failed");

        Assert.Null(activity?.GetTagItem(TxcTelemetryTags.ErrorMessage));
        Assert.Contains(activity!.Events, e => e.Name == "exception");
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
