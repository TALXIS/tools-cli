using Microsoft.PowerPlatform.Dataverse.Client;

namespace TALXIS.CLI.Platform.Dataverse;

/// <summary>
/// Disposable wrapper around a <see cref="ServiceClient"/>. Disposing
/// releases the underlying client.
/// </summary>
public sealed class DataverseConnection : IDisposable
{
    public ServiceClient Client { get; }

    private DataverseConnection(ServiceClient client)
    {
        Client = client;
    }

    /// <summary>
    /// Factory used by out-of-assembly callers (e.g. the profile-based
    /// <c>IDataverseConnectionFactory</c> in
    /// <c>TALXIS.CLI.Platform.Dataverse</c>) that already own a
    /// ready <see cref="ServiceClient"/>.
    /// </summary>
    public static DataverseConnection FromServiceClient(ServiceClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new DataverseConnection(client);
    }

    public void Dispose()
    {
        Client.Dispose();
    }
}
