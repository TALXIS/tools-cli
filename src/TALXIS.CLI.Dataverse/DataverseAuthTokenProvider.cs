using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using TALXIS.CLI.Logging;

namespace TALXIS.CLI.Dataverse;

/// <summary>
/// MSAL-based access token provider for Dataverse. Uses the same public client ID
/// and redirect URI that PAC CLI uses so users don't need to register their own
/// application. Supports silent acquisition, device-code, and interactive fallback.
/// </summary>
public sealed class DataverseAuthTokenProvider : IDisposable
{
    private static readonly Guid PacClientId = new("9cee029c-6210-4654-90bb-17e6e9d36617");
    private static readonly Uri PacRedirectUri = new("http://localhost");
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromMinutes(5);

    private readonly ILogger _logger = TxcLoggerFactory.CreateLogger(nameof(DataverseAuthTokenProvider));
    private readonly IPublicClientApplication _publicClientApplication;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private readonly bool _deviceCode;
    private readonly bool _verbose;
    private AuthenticationResult? _lastAuthenticationResult;

    public DataverseAuthTokenProvider(Uri environmentUrl, bool deviceCode, bool verbose)
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

    public Task<string> GetAccessTokenAsync(Uri resourceUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resourceUri);
        return AcquireAccessTokenAsync(resourceUri, cancellationToken);
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

    private async Task<string> AcquireAccessTokenAsync(Uri resourceUri, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_lastAuthenticationResult is not null &&
                _lastAuthenticationResult.ExpiresOn > DateTimeOffset.UtcNow.Add(RefreshSkew))
            {
                return _lastAuthenticationResult.AccessToken;
            }

            string[] scopes = [BuildDefaultScope(resourceUri)];
            IEnumerable<IAccount> accounts = await _publicClientApplication.GetAccountsAsync().ConfigureAwait(false);

            foreach (IAccount account in accounts)
            {
                try
                {
                    _lastAuthenticationResult = await _publicClientApplication
                        .AcquireTokenSilent(scopes, account)
                        .ExecuteAsync(cancellationToken)
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
                    .ExecuteAsync(cancellationToken)
                    .ConfigureAwait(false)
                : await _publicClientApplication
                    .AcquireTokenInteractive(scopes)
                    .WithPrompt(Prompt.SelectAccount)
                    .ExecuteAsync(cancellationToken)
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
