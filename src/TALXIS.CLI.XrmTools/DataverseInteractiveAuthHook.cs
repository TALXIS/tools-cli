using Microsoft.Xrm.Tooling.Connector;
using TALXIS.CLI.Dataverse;

namespace TALXIS.CLI.XrmTools;

/// <summary>
/// Legacy <see cref="IOverrideAuthHookWrapper"/> adapter for the CrmServiceClient shim.
/// Delegates all token acquisition to the modern <see cref="DataverseAuthTokenProvider"/>
/// which lives in <c>TALXIS.CLI.Dataverse</c>.
/// </summary>
public sealed class DataverseInteractiveAuthHook : IOverrideAuthHookWrapper, IDisposable
{
    private readonly DataverseAuthTokenProvider _tokenProvider;
    private readonly bool _ownsTokenProvider;

    public DataverseInteractiveAuthHook(Uri environmentUrl, bool deviceCode, bool verbose)
        : this(new DataverseAuthTokenProvider(environmentUrl, deviceCode, verbose), ownsTokenProvider: true)
    {
    }

    public DataverseInteractiveAuthHook(DataverseAuthTokenProvider tokenProvider)
        : this(tokenProvider, ownsTokenProvider: false)
    {
    }

    private DataverseInteractiveAuthHook(DataverseAuthTokenProvider tokenProvider, bool ownsTokenProvider)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _tokenProvider = tokenProvider;
        _ownsTokenProvider = ownsTokenProvider;
    }

    public string GetAuthToken(Uri connectedUri)
    {
        ArgumentNullException.ThrowIfNull(connectedUri);
        return _tokenProvider.GetAccessTokenAsync(connectedUri).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        if (_ownsTokenProvider)
        {
            _tokenProvider.Dispose();
        }
    }
}
