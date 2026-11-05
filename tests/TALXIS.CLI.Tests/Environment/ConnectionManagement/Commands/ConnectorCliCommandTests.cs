using TALXIS.CLI.Features.Environment.Connector;
using TALXIS.CLI.Features.Environment.Connector.Operation;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.ConnectionManagement.Commands;

[Collection("TxcServicesSerial")]
public sealed class ConnectorCliCommandTests
{
    [Theory]
    [InlineData("custom", "custom")]
    [InlineData(" Built-In ", "built-in")]
    [InlineData(null, null)]
    public async Task List_AKnownKind_IsPassedNormalised(string? kind, string? expected)
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, _) = await ConnectionCommandTestHost.RunAsync(new ConnectorListCliCommand { Kind = kind });

        Assert.Equal(0, exit);
        Assert.Equal(expected, host.Connectors.LastKind);
    }

    [Theory]
    [InlineData("builtin")]
    [InlineData("managed")]
    public async Task List_AnUnknownKind_ExitsTwoWithoutCallingTheService(string kind)
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectorListCliCommand { Kind = kind });

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output);
        Assert.False(host.Connectors.Listed);
    }

    [Theory]
    [InlineData("action", "action")]
    [InlineData(" Trigger ", "trigger")]
    [InlineData("all", "all")]
    public async Task OperationSearch_AKnownKind_IsPassedNormalised(string kind, string expected)
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, _) = await ConnectionCommandTestHost.RunAsync(new ConnectorOperationSearchCliCommand { Search = "send", Kind = kind });

        Assert.Equal(0, exit);
        Assert.Equal(expected, host.Operations.LastKind);
    }

    [Theory]
    [InlineData("actions")]
    [InlineData("everything")]
    public async Task OperationSearch_AnUnknownKind_ExitsTwoWithoutCallingTheService(string kind)
    {
        using var host = new ConnectionCommandTestHost();

        var (exit, output) = await ConnectionCommandTestHost.RunAsync(new ConnectorOperationSearchCliCommand { Search = "send", Kind = kind });

        Assert.Equal(2, exit);
        Assert.Equal(string.Empty, output);
        Assert.False(host.Operations.Searched);
    }
}
