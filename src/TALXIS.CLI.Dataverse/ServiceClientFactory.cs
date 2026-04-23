using Microsoft.PowerPlatform.Dataverse.Client;

namespace TALXIS.CLI.Dataverse;

/// <summary>
/// Disposable wrapper around a <see cref="ServiceClient"/> plus its optional
/// <see cref="DataverseAuthTokenProvider"/>. Disposing releases both.
/// </summary>
public sealed class DataverseConnection : IDisposable
{
    public ServiceClient Client { get; }
    public DataverseAuthTokenProvider? TokenProvider { get; }

    internal DataverseConnection(ServiceClient client, DataverseAuthTokenProvider? tokenProvider)
    {
        Client = client;
        TokenProvider = tokenProvider;
    }

    /// <summary>
    /// Factory used by out-of-assembly callers (e.g. the profile-based
    /// <c>IDataverseConnectionFactory</c> in
    /// <c>TALXIS.CLI.Config.Providers.Dataverse</c>) that already own a
    /// ready <see cref="ServiceClient"/>.
    /// </summary>
    public static DataverseConnection FromServiceClient(ServiceClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new DataverseConnection(client, tokenProvider: null);
    }

    public void Dispose()
    {
        Client.Dispose();
        TokenProvider?.Dispose();
    }
}
