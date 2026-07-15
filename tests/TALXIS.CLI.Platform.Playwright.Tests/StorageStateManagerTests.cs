using Microsoft.Playwright;
using Moq;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Core.Storage;
using Xunit;

namespace TALXIS.CLI.Platform.Playwright.Tests;

public class StorageStateManagerTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsEncryptedState()
    {
        using var temp = new TempConfigDir();
        var vault = new FakeVault();
        var manager = new StorageStateManager(temp.Paths, vault);
        var context = new Mock<IBrowserContext>();
        context.Setup(value => value.StorageStateAsync()).ReturnsAsync("{\"cookies\":[],\"origins\":[]}");

        await manager.SaveAsync(context.Object, "dev-profile", CancellationToken.None);

        var loaded = await manager.LoadAsync("dev-profile", CancellationToken.None);

        Assert.Equal("{\"cookies\":[],\"origins\":[]}", loaded);
        Assert.True(await manager.ExistsAsync("dev-profile", CancellationToken.None));
        Assert.NotEmpty(vault.Contents);
    }

    [Fact]
    public async Task LoadAsync_ReturnsNullWhenStateFileDoesNotExist()
    {
        using var temp = new TempConfigDir();
        var manager = new StorageStateManager(temp.Paths, new FakeVault());

        var loaded = await manager.LoadAsync("missing", CancellationToken.None);

        Assert.Null(loaded);
        Assert.False(await manager.ExistsAsync("missing", CancellationToken.None));
    }

    private sealed class TempConfigDir : IDisposable
    {
        public TempConfigDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "txc-playwright-tests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Root);
            Paths = new ConfigPaths(Root);
        }

        public string Root { get; }
        public ConfigPaths Paths { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }

    internal sealed class FakeVault : ICredentialVault
    {
        private readonly Dictionary<string, string> _store = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> Contents => _store;

        public Task<string?> GetSecretAsync(SecretRef reference, CancellationToken ct)
            => Task.FromResult(_store.TryGetValue(reference.Uri, out var value) ? value : null);

        public Task SetSecretAsync(SecretRef reference, string value, CancellationToken ct)
        {
            _store[reference.Uri] = value;
            return Task.CompletedTask;
        }

        public Task<bool> DeleteSecretAsync(SecretRef reference, CancellationToken ct)
            => Task.FromResult(_store.Remove(reference.Uri));

        public Task ClearAsync(CancellationToken ct)
        {
            _store.Clear();
            return Task.CompletedTask;
        }
    }
}
