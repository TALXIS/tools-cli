using TALXIS.CLI.Config.Model;
using TALXIS.CLI.Config.Storage;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Storage;

public class ConnectionStoreRoundtripTests
{
    [Fact]
    public async Task UpsertPersistsAllProviderFields()
    {
        using var dir = new TempConfigDir();
        var store = new ConnectionStore(dir.Paths);
        var c = new Connection
        {
            Id = "customer-a-dev",
            Provider = ProviderKind.Dataverse,
            EnvironmentUrl = "https://contoso.crm4.dynamics.com/",
            Cloud = CloudInstance.Public,
            TenantId = "tenant-1",
        };
        await store.UpsertAsync(c, CancellationToken.None);

        // Open a new store instance to force reload from disk.
        var got = await new ConnectionStore(dir.Paths).GetAsync("customer-a-dev", CancellationToken.None);
        Assert.NotNull(got);
        Assert.Equal(ProviderKind.Dataverse, got!.Provider);
        Assert.Equal("https://contoso.crm4.dynamics.com/", got.EnvironmentUrl);
        Assert.Equal(CloudInstance.Public, got.Cloud);
        Assert.Equal("tenant-1", got.TenantId);
    }

    [Fact]
    public async Task UnknownFieldsAreRetainedAcrossRoundTrip()
    {
        using var dir = new TempConfigDir();
        // Write a JSON doc that includes a forward-compat field the model doesn't know.
        var json = """
        {
          "connections": [
            { "id": "future", "provider": "dataverse", "environmentUrl": "https://x/", "someFutureField": "preserve-me" }
          ]
        }
        """;
        await File.WriteAllTextAsync(dir.Paths.ConnectionsFile, json);

        var store = new ConnectionStore(dir.Paths);
        var loaded = await store.GetAsync("future", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.NotNull(loaded!.ExtraFields);
        Assert.True(loaded.ExtraFields!.ContainsKey("someFutureField"));

        // Upsert back out and confirm the unknown field survives.
        await store.UpsertAsync(loaded, CancellationToken.None);
        var rewritten = await File.ReadAllTextAsync(dir.Paths.ConnectionsFile);
        Assert.Contains("someFutureField", rewritten);
        Assert.Contains("preserve-me", rewritten);
    }
}
