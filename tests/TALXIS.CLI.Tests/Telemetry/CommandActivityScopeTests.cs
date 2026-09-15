using System.Diagnostics;
using TALXIS.CLI.Abstractions;
using TALXIS.CLI.Core.Telemetry;
using Xunit;

namespace TALXIS.CLI.Tests.Telemetry;

public class CommandActivityScopeTests
{
    [Fact]
    public void SetError_RecordsErrorKindExitCodeAndMessage()
    {
        using var listener = CreateListener();
        using var scope = new CommandActivityScope("workspace_validate", "cli");

        scope.SetError(2, "internal", "Unexpected failure");

        Assert.Equal(2, scope.Activity?.GetTagItem(TxcTelemetryTags.ExitCode));
        Assert.Equal("internal", scope.Activity?.GetTagItem(TxcTelemetryTags.ErrorKind));
        Assert.Equal("Unexpected failure", scope.Activity?.GetTagItem(TxcTelemetryTags.ErrorMessage));
    }

    [Fact]
    public void SetExitCode_ForNonZeroExitCode_DefaultsErrorKindToValidation()
    {
        using var listener = CreateListener();
        using var scope = new CommandActivityScope("workspace_validate", "cli");

        scope.SetExitCode(1);

        Assert.Equal(1, scope.Activity?.GetTagItem(TxcTelemetryTags.ExitCode));
        Assert.Equal("validation", scope.Activity?.GetTagItem(TxcTelemetryTags.ErrorKind));
        Assert.Equal(ActivityStatusCode.Error, scope.Activity?.Status);
    }

    [Fact]
    public void SetExitCode_DoesNotOverwriteClassifiedErrorKind()
    {
        using var listener = CreateListener();
        using var scope = new CommandActivityScope("workspace_validate", "cli");

        scope.SetError(130, "cancelled", "The operation was cancelled.");
        scope.SetExitCode(130);

        Assert.Equal("cancelled", scope.Activity?.GetTagItem(TxcTelemetryTags.ErrorKind));
        Assert.Equal("The operation was cancelled.", scope.Activity?.GetTagItem(TxcTelemetryTags.ErrorMessage));
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
