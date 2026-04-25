using System.Net.Http.Headers;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace TALXIS.CLI.Platform.Dataverse.Runtime;

/// <summary>
/// Disposable wrapper around a <see cref="ServiceClient"/>. Disposing
/// releases the underlying client.
/// </summary>
public sealed class DataverseConnection : IDisposable
{
    public ServiceClient Client { get; }

    /// <summary>
    /// Optional token provider for direct Web API calls that bypass the SDK.
    /// Set by the factory when the connection is created with a token callback.
    /// </summary>
    internal Func<string, Task<string>>? TokenProvider { get; init; }

    /// <summary>
    /// The base organization URI (e.g. <c>https://org.crm.dynamics.com</c>).
    /// </summary>
    public Uri OrgUri => Client.ConnectedOrgUriActual;

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
    public static DataverseConnection FromServiceClient(ServiceClient client, Func<string, Task<string>>? tokenProvider = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        return new DataverseConnection(client) { TokenProvider = tokenProvider };
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> configured with OAuth bearer auth
    /// and OData headers for direct Web API calls. Use this for metadata
    /// operations that the SDK's <c>UpdateAttributeRequest</c> doesn't
    /// handle correctly (e.g. RequiredLevel managed property changes).
    /// </summary>
    public async Task<HttpClient> CreateWebApiClientAsync(CancellationToken ct = default)
    {
        if (TokenProvider is null)
            throw new InvalidOperationException("No token provider available. Direct Web API calls require a token-provider connection.");

        var token = await TokenProvider(OrgUri.ToString()).ConfigureAwait(false);
#pragma warning disable RS0030 // HttpClient created intentionally — short-lived, per-request, no IHttpClientFactory available in this context
        var http = new HttpClient { BaseAddress = new Uri(OrgUri, "/api/data/v9.2/") };
#pragma warning restore RS0030
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        http.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
        http.DefaultRequestHeaders.Add("OData-Version", "4.0");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }

    public void Dispose()
    {
        Client.Dispose();
    }
}
