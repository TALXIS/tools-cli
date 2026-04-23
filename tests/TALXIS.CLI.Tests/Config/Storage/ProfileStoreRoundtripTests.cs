using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Storage;

public class ProfileStoreRoundtripTests
{
    [Fact]
    public async Task UpsertThenGetReturnsSameProfile()
    {
        using var dir = new TempConfigDir();
        var store = new ProfileStore(dir.Paths);
        var p = new Profile { Id = "customer-a-dev", ConnectionRef = "c1", CredentialRef = "cred1", Description = "test" };

        await store.UpsertAsync(p, CancellationToken.None);
        var got = await store.GetAsync("customer-a-dev", CancellationToken.None);

        Assert.NotNull(got);
        Assert.Equal("c1", got!.ConnectionRef);
        Assert.Equal("cred1", got.CredentialRef);
        Assert.Equal("test", got.Description);
    }

    [Fact]
    public async Task GetIsCaseInsensitive()
    {
        using var dir = new TempConfigDir();
        var store = new ProfileStore(dir.Paths);
        await store.UpsertAsync(new Profile { Id = "Foo", ConnectionRef = "c", CredentialRef = "k" }, CancellationToken.None);

        Assert.NotNull(await store.GetAsync("foo", CancellationToken.None));
        Assert.NotNull(await store.GetAsync("FOO", CancellationToken.None));
    }

    [Fact]
    public async Task UpsertReplacesExistingEntryByIdCaseInsensitive()
    {
        using var dir = new TempConfigDir();
        var store = new ProfileStore(dir.Paths);
        await store.UpsertAsync(new Profile { Id = "x", ConnectionRef = "c1", CredentialRef = "k1" }, CancellationToken.None);
        await store.UpsertAsync(new Profile { Id = "X", ConnectionRef = "c2", CredentialRef = "k2" }, CancellationToken.None);

        var all = await store.ListAsync(CancellationToken.None);
        Assert.Single(all);
        Assert.Equal("c2", all[0].ConnectionRef);
    }

    [Fact]
    public async Task DeleteReturnsTrueOnlyWhenPresent()
    {
        using var dir = new TempConfigDir();
        var store = new ProfileStore(dir.Paths);
        await store.UpsertAsync(new Profile { Id = "a", ConnectionRef = "c", CredentialRef = "k" }, CancellationToken.None);

        Assert.True(await store.DeleteAsync("A", CancellationToken.None));
        Assert.False(await store.DeleteAsync("a", CancellationToken.None));
        Assert.Empty(await store.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ListReturnsEmptyWhenFileMissing()
    {
        using var dir = new TempConfigDir();
        var store = new ProfileStore(dir.Paths);
        Assert.Empty(await store.ListAsync(CancellationToken.None));
    }
}
