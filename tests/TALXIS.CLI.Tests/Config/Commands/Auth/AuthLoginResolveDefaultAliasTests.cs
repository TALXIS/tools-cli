using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Bootstrapping;
using TALXIS.CLI.Core.Model;
using Xunit;

namespace TALXIS.CLI.Tests.Config.Commands.Auth;

public class AuthLoginResolveDefaultAliasTests
{
    [Fact]
    public async Task PassesCancellationTokenToStore()
    {
        var store = new CancellingStore();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => CredentialAliasResolver.ResolveForUpnAsync(store, "alice@contoso.com", cts.Token));
    }

    private sealed class CancellingStore : ICredentialStore
    {
        public Task<IReadOnlyList<Credential>> ListAsync(CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<Credential?> GetAsync(string id, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<Credential?>(null);
        }
        public Task UpsertAsync(Credential credential, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> DeleteAsync(string id, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
