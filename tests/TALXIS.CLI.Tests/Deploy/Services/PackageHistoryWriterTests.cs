using TALXIS.CLI.Dataverse;
using TALXIS.CLI.Deploy;
using Xunit;

namespace TALXIS.CLI.Tests.Deploy.Services;

public class PackageHistoryWriterTests
{
    [Fact]
    public void ResolveStatusCodesFromSamples_MapsKnownLabels()
    {
        (int Status, int State, string? Label)[] samples =
        [
            (Status: 10, State: 0, Label: "In Process"),
            (Status: 30, State: 1, Label: "Completed"),
            (Status: 40, State: 1, Label: "Failed")
        ];

        var codes = PackageHistoryWriter.ResolveStatusCodesFromSamples(samples);

        Assert.Equal(10, codes.InProcessStatus);
        Assert.Equal(0, codes.InProcessState);
        Assert.Equal(30, codes.SuccessStatus);
        Assert.Equal(1, codes.SuccessState);
        Assert.Equal(40, codes.FailedStatus);
        Assert.Equal(1, codes.FailedState);
    }

    [Fact]
    public void ResolveStatusCodesFromSamples_HandlesEmptyOrUnknownLabels()
    {
        (int Status, int State, string? Label)[] samples =
        [
            (Status: 10, State: 0, Label: "Unknown"),
            (Status: 11, State: 0, Label: null)
        ];

        var codes = PackageHistoryWriter.ResolveStatusCodesFromSamples(samples);

        Assert.Null(codes.InProcessStatus);
        Assert.Null(codes.InProcessState);
        Assert.Null(codes.SuccessStatus);
        Assert.Null(codes.SuccessState);
        Assert.Null(codes.FailedStatus);
        Assert.Null(codes.FailedState);
    }

    [Fact]
    public void ResolveStatusCodesFromSamples_PrefersTerminalStateForCompletedAndFailed()
    {
        (int Status, int State, string? Label)[] samples =
        [
            (Status: 526430000, State: 0, Label: "In Process"),
            (Status: 526430003, State: 0, Label: "Completed"),
            (Status: 526430004, State: 1, Label: "Completed"),
            (Status: 526430005, State: 1, Label: "Failed")
        ];

        var codes = PackageHistoryWriter.ResolveStatusCodesFromSamples(samples);

        Assert.Equal(526430000, codes.InProcessStatus);
        Assert.Equal(0, codes.InProcessState);
        Assert.Equal(526430004, codes.SuccessStatus);
        Assert.Equal(1, codes.SuccessState);
        Assert.Equal(526430005, codes.FailedStatus);
        Assert.Equal(1, codes.FailedState);
    }
}
