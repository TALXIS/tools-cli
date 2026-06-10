using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using Xunit;

namespace TALXIS.CLI.Tests.Environment.Plugin;

public class PluginInventoryManagerTests
{
    [Theory]
    [InlineData(true, 0, 1)]   // enabled  -> statecode=0 (Enabled),  statuscode=1
    [InlineData(false, 1, 2)]  // disabled -> statecode=1 (Disabled), statuscode=2
    public void StepStateCodes_MapsEnabledToStateAndStatus(bool enabled, int expectedState, int expectedStatus)
    {
        var (state, status) = PluginInventoryManager.StepStateCodes(enabled);

        Assert.Equal(expectedState, state);
        Assert.Equal(expectedStatus, status);
    }
}
