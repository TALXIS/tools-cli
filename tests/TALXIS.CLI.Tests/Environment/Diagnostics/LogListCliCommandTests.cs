using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Features.Environment.Diagnostics;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Diagnostics;

public class LogListCliCommandTests
{
    private static PluginTraceRecord Trace(DateTime createdOn, bool hasException) =>
        new(Guid.NewGuid(), createdOn, "Acme.Plugins.Account", "Update", "account",
            "Synchronous", 1, 12, hasException, hasException ? "boom" : null, "trace", null);

    private static AsyncJobRecord Job(DateTime createdOn, bool isError) =>
        new(Guid.NewGuid(), "job", 10, "Workflow", isError ? "Failed" : "Succeeded", isError,
            createdOn, createdOn, createdOn.AddSeconds(2), "account", isError ? "err" : null, null);

    [Fact]
    public void BuildRows_InterleavesBothSourcesByTimestampDesc()
    {
        var older = DateTime.UtcNow.AddHours(-2);
        var newer = DateTime.UtcNow.AddHours(-1);

        var rows = LogListCliCommand.BuildRows(
            new[] { Trace(older, hasException: false) },
            new[] { Job(newer, isError: false) },
            errorsOnly: false);

        Assert.Equal(2, rows.Count);
        Assert.Equal("async", rows[0].Source);
        Assert.Equal("plugin", rows[1].Source);
    }

    [Theory]
    [InlineData(true, "ERROR")]
    [InlineData(false, "OK")]
    public void BuildRows_MapsPluginExceptionToLevel(bool hasException, string expected)
    {
        var rows = LogListCliCommand.BuildRows(
            new[] { Trace(DateTime.UtcNow, hasException) },
            Array.Empty<AsyncJobRecord>(),
            errorsOnly: false);

        Assert.Equal(expected, rows[0].Level);
    }

    [Theory]
    [InlineData(true, "ERROR")]
    [InlineData(false, "OK")]
    public void BuildRows_MapsAsyncErrorToLevel(bool isError, string expected)
    {
        var rows = LogListCliCommand.BuildRows(
            Array.Empty<PluginTraceRecord>(),
            new[] { Job(DateTime.UtcNow, isError) },
            errorsOnly: false);

        Assert.Equal(expected, rows[0].Level);
    }

    [Fact]
    public void BuildRows_ErrorsOnly_KeepsOnlyErrorRows()
    {
        var rows = LogListCliCommand.BuildRows(
            new[] { Trace(DateTime.UtcNow, hasException: false), Trace(DateTime.UtcNow, hasException: true) },
            new[] { Job(DateTime.UtcNow, isError: false), Job(DateTime.UtcNow, isError: true) },
            errorsOnly: true);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal("ERROR", r.Level));
    }
}
