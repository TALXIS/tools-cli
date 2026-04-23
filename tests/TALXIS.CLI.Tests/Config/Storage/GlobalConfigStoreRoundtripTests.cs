using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Storage;

public class GlobalConfigStoreRoundtripTests
{
    [Fact]
    public async Task LoadReturnsDefaultsWhenFileMissing()
    {
        using var dir = new TempConfigDir();
        var store = new GlobalConfigStore(dir.Paths);
        var cfg = await store.LoadAsync(CancellationToken.None);
        Assert.Null(cfg.ActiveProfile);
    }

    [Fact]
    public async Task SaveThenLoadRoundTripsActiveProfile()
    {
        using var dir = new TempConfigDir();
        var store = new GlobalConfigStore(dir.Paths);
        await store.SaveAsync(new GlobalConfig { ActiveProfile = "customer-a-dev" }, CancellationToken.None);
        var loaded = await new GlobalConfigStore(dir.Paths).LoadAsync(CancellationToken.None);
        Assert.Equal("customer-a-dev", loaded.ActiveProfile);
    }
}
