using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Application.Sdk;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Application.Services;

internal sealed class DataversePublisherService : IPublisherService
{
    public async Task<IReadOnlyList<PublisherRecord>> ListAsync(string? profileName, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await PublisherManager.ListAsync(conn.Client, ct).ConfigureAwait(false);
    }

    public async Task<PublisherRecord?> ShowAsync(string? profileName, string uniqueName, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await PublisherManager.ShowAsync(conn.Client, uniqueName, ct).ConfigureAwait(false);
    }

    public async Task<Guid> CreateAsync(string? profileName, PublisherCreateOptions options, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        return await PublisherManager.CreateAsync(conn.Client, options, ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string? profileName, string uniqueName, CancellationToken ct)
    {
        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);
        await PublisherManager.DeleteAsync(conn.Client, uniqueName, ct).ConfigureAwait(false);
    }
}
