using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Xrm.Tooling.Connector;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.XrmTools;

/// <summary>
/// MSAL-based auth hook that implements <see cref="IOverrideAuthHookWrapper"/>
/// for the CrmServiceClient shim, and also serves as a
/// <c>Func&lt;string, Task&lt;string&gt;&gt;</c> token provider for ServiceClient.
/// Uses the same public client ID and redirect URI that PAC CLI uses so
/// users don't need to register their own application.
/// </summary>
public sealed class DataverseInteractiveAuthHook : IOverrideAuthHookWrapper, IDisposable
{
    private static readonly Guid PacClientId = new("9cee029c-6210-4654-90bb-17e6e9d36617");
    private static readonly Uri PacRedirectUri = new("http://localhost");
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DataverseInteractiveAuthHook));
    private readonly IPublicClientApplication _publicClientApplication;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly bool _deviceCode;
    private readonly bool _verbose;
    private AuthenticationResult? _lastAuthenticationResult;

    public DataverseInteractiveAuthHook(Uri environmentUrl, bool deviceCode, bool verbose)
    {
        ArgumentNullException.ThrowIfNull(environmentUrl);

        _deviceCode = deviceCode;
        _verbose = verbose;

        string cacheDirectory = Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            ".txc",
            "auth");

        Directory.CreateDirectory(cacheDirectory);

        _publicClientApplication = PublicClientApplicationBuilder
            .Create(PacClientId.ToString())
            .WithRedirectUri(PacRedirectUri.ToString())
            .WithAuthority(ResolveAuthority(environmentUrl).ToString())
            .Build();

        RegisterTokenCache(cacheDirectory);
    }

    public string GetAuthToken(Uri connectedUri)
    {
        ArgumentNullException.ThrowIfNull(connectedUri);
        return GetAuthTokenAsync(connectedUri).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _tokenLock.Dispose();
    }

    public static string BuildDefaultScope(Uri resourceUri)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);
        return resourceUri.GetLeftPart(UriPartial.Authority) + "//.default";
    }

    public static Uri ResolveAuthority(Uri environmentUrl)
    {
        ArgumentNullException.ThrowIfNull(environmentUrl);

        string host = environmentUrl.Host.ToLowerInvariant();
        if (host.EndsWith(".crm.dynamics.us", StringComparison.Ordinal) || host.EndsWith(".crm.appsplatform.us", StringComparison.Ordinal))
        {
            return new Uri("https://login.microsoftonline.us/organizations");
        }

        if (host.EndsWith(".crm.dynamics.cn", StringComparison.Ordinal))
        {
            return new Uri("https://login.partner.microsoftonline.cn/organizations");
        }

        return new Uri("https://login.microsoftonline.com/organizations");
    }

    private async Task<string> GetAuthTokenAsync(Uri connectedUri)
    {
        await _tokenLock.WaitAsync().ConfigureAwait(false);

        try
        {
            if (_lastAuthenticationResult is not null &&
                _lastAuthenticationResult.ExpiresOn > DateTimeOffset.UtcNow.Add(RefreshSkew))
            {
                return _lastAuthenticationResult.AccessToken;
            }

            string[] scopes = [BuildDefaultScope(connectedUri)];
            IEnumerable<IAccount> accounts = await _publicClientApplication.GetAccountsAsync().ConfigureAwait(false);

            foreach (IAccount account in accounts)
            {
                try
                {
                    _lastAuthenticationResult = await _publicClientApplication
                        .AcquireTokenSilent(scopes, account)
                        .ExecuteAsync()
                        .ConfigureAwait(false);

                    return _lastAuthenticationResult.AccessToken;
                }
                catch (MsalUiRequiredException)
                {
                }
            }

            _lastAuthenticationResult = _deviceCode
                ? await _publicClientApplication
                    .AcquireTokenWithDeviceCode(scopes, OnDeviceCodeReceivedAsync)
                    .ExecuteAsync()
                    .ConfigureAwait(false)
                : await _publicClientApplication
                    .AcquireTokenInteractive(scopes)
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync()
                    .ConfigureAwait(false);

            return _lastAuthenticationResult.AccessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private Task OnDeviceCodeReceivedAsync(DeviceCodeResult deviceCodeResult)
    {
        _logger.LogInformation("{DeviceCodeMessage}", deviceCodeResult.Message);
        if (_verbose)
        {
            _logger.LogInformation("Waiting for device code authentication to complete...");
        }

        return Task.CompletedTask;
    }

    private void RegisterTokenCache(string cacheDirectory)
    {
        StorageCreationProperties storageProperties = new StorageCreationPropertiesBuilder("dataverse-auth-cache.bin", cacheDirectory)
            .WithMacKeyChain("com.talxis.txc.dataverse", "dataverse-auth-cache")
            .Build();

        MsalCacheHelper cacheHelper = MsalCacheHelper.CreateAsync(storageProperties).GetAwaiter().GetResult();
        cacheHelper.RegisterCache(_publicClientApplication.UserTokenCache);
    }
}
