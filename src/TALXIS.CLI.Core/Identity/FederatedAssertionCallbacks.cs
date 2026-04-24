using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TALXIS.CLI.Core.Resolution;

namespace TALXIS.CLI.Core.Identity;

/// <summary>
/// Ready-made <see cref="ClientAssertionCallback"/> factories for the three
/// federation providers we target in v1:
/// <list type="number">
///   <item>
///     Azure DevOps (ADO) pipelines — same contract as pac CLI's
///     <c>--azureDevOpsFederated</c> flag. Reads an OIDC issuance URL +
///     bearer token from env vars, POSTs to <c>idp/oidctoken</c> and
///     parses the <c>oidcToken</c> JWT from the response. See discussions 884 / 906 on
///     <c>microsoft/powerplatform-build-tools</c>.
///   </item>
///   <item>
///     GitHub Actions — standard <c>ACTIONS_ID_TOKEN_REQUEST_URL</c> +
///     <c>ACTIONS_ID_TOKEN_REQUEST_TOKEN</c> pair with <c>audience</c>
///     query parameter.
///   </item>
///   <item>
///     Workload-identity-file — az-cli / AKS convention:
///     <c>AZURE_FEDERATED_TOKEN_FILE</c> points at a file whose contents are
///     the JWT.
///   </item>
/// </list>
/// </summary>
/// <remarks>
/// All three flavours return raw JWTs that MSAL then exchanges for Entra
/// access tokens via <c>WithClientAssertion</c> on the confidential client.
/// No secrets are written to disk and the env vars are never logged (the
/// TOKEN env var is a short-lived bearer).
/// </remarks>
public static class FederatedAssertionCallbacks
{
    /// <summary>pac-compatible ADO OIDC-URL env var. Also accepts PAC_* for drop-in parity.</summary>
    public const string AdoRequestUrlVar       = "TXC_ADO_ID_TOKEN_REQUEST_URL";
    public const string AdoRequestUrlVarLegacy = "PAC_ADO_ID_TOKEN_REQUEST_URL";

    /// <summary>pac-compatible ADO OIDC-bearer env var (System.AccessToken).</summary>
    public const string AdoRequestTokenVar       = "TXC_ADO_ID_TOKEN_REQUEST_TOKEN";
    public const string AdoRequestTokenVarLegacy = "PAC_ADO_ID_TOKEN_REQUEST_TOKEN";

    /// <summary>GitHub Actions OIDC standard env vars.</summary>
    public const string GitHubRequestUrlVar   = "ACTIONS_ID_TOKEN_REQUEST_URL";
    public const string GitHubRequestTokenVar = "ACTIONS_ID_TOKEN_REQUEST_TOKEN";

    /// <summary>Workload identity file (az-cli / AKS). File content is the JWT.</summary>
    public const string FederatedTokenFileVar = "AZURE_FEDERATED_TOKEN_FILE";

    /// <summary>
    /// Returns the first callback whose env vars are populated, in priority:
    /// ADO → GitHub Actions → federated-token file. Throws
    /// <see cref="InvalidOperationException"/> when none of them are set —
    /// callers must handle that case explicitly (e.g. prompt or fail fast).
    /// </summary>
    public static ClientAssertionCallback AutoSelect(
        IEnvironmentReader env,
        HttpClient? http = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(env);

        if (HasAny(env, AdoRequestUrlVar, AdoRequestUrlVarLegacy))
            return ForAzureDevOps(env, http, logger);
        if (!string.IsNullOrEmpty(env.Get(GitHubRequestUrlVar)))
            return ForGitHubActions(env, http, logger);
        if (!string.IsNullOrEmpty(env.Get(FederatedTokenFileVar)))
            return ForFederatedTokenFile(env, logger);

        throw new InvalidOperationException(
            "No federated-credential source found. Set one of: " +
            $"{AdoRequestUrlVar} (+ {AdoRequestTokenVar}), " +
            $"{GitHubRequestUrlVar} (+ {GitHubRequestTokenVar}), " +
            $"or {FederatedTokenFileVar}.");
    }

    /// <summary>
    /// Azure DevOps pipelines federation. POSTs to
    /// <c>{TXC_ADO_ID_TOKEN_REQUEST_URL}</c> with
    /// <c>Authorization: Bearer {TXC_ADO_ID_TOKEN_REQUEST_TOKEN}</c>,
    /// parses the <c>oidcToken</c> / <c>value</c> field from the JSON body.
    /// </summary>
    public static ClientAssertionCallback ForAzureDevOps(
        IEnvironmentReader env,
        HttpClient? http = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(env);
        var log = logger ?? NullLogger.Instance;
        var urlEnv   = env.Get(AdoRequestUrlVar)   ?? env.Get(AdoRequestUrlVarLegacy);
        var tokenEnv = env.Get(AdoRequestTokenVar) ?? env.Get(AdoRequestTokenVarLegacy);

        if (string.IsNullOrWhiteSpace(urlEnv))
            throw new InvalidOperationException($"{AdoRequestUrlVar} is not set.");
        if (string.IsNullOrWhiteSpace(tokenEnv))
            throw new InvalidOperationException($"{AdoRequestTokenVar} is not set.");

        return ct => FetchAdoOidcAsync(new Uri(urlEnv), tokenEnv, http, log, ct);
    }

    /// <summary>
    /// GitHub Actions federation. Appends <c>&amp;audience=api://AzureADTokenExchange</c>
    /// to <c>ACTIONS_ID_TOKEN_REQUEST_URL</c> and GETs it with the bearer token
    /// (this one really is a GET — only ADO's endpoint requires POST).
    /// </summary>
    public static ClientAssertionCallback ForGitHubActions(
        IEnvironmentReader env,
        HttpClient? http = null,
        ILogger? logger = null,
        string audience = "api://AzureADTokenExchange")
    {
        ArgumentNullException.ThrowIfNull(env);
        var log = logger ?? NullLogger.Instance;
        var url   = env.Get(GitHubRequestUrlVar);
        var token = env.Get(GitHubRequestTokenVar);

        if (string.IsNullOrWhiteSpace(url))
            throw new InvalidOperationException($"{GitHubRequestUrlVar} is not set.");
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"{GitHubRequestTokenVar} is not set.");

        var separator = url.Contains('?') ? '&' : '?';
        var full = new Uri($"{url}{separator}audience={Uri.EscapeDataString(audience)}");
        return ct => FetchGitHubOidcAsync(full, token, http, log, ct);
    }

    /// <summary>
    /// Reads a JWT from <c>AZURE_FEDERATED_TOKEN_FILE</c>. No HTTP call.
    /// </summary>
    public static ClientAssertionCallback ForFederatedTokenFile(IEnvironmentReader env, ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(env);
        var path = env.Get(FederatedTokenFileVar);
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"{FederatedTokenFileVar} is not set.");

        return async ct =>
        {
            var jwt = (await File.ReadAllTextAsync(path, ct).ConfigureAwait(false)).Trim();
            if (string.IsNullOrEmpty(jwt))
                throw new InvalidOperationException($"Federated token file '{path}' is empty.");
            return jwt;
        };
    }

    private static bool HasAny(IEnvironmentReader env, params string[] vars)
    {
        foreach (var v in vars)
            if (!string.IsNullOrEmpty(env.Get(v))) return true;
        return false;
    }

    private static async Task<string> FetchAdoOidcAsync(
        Uri url, string bearer, HttpClient? http, ILogger log, CancellationToken ct)
    {
        var owned = http is null;
        var client = http ?? new HttpClient();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            // ADO returns {"oidcToken": "...jwt..."}.
            if (doc.RootElement.TryGetProperty("oidcToken", out var jwt) && jwt.ValueKind == JsonValueKind.String)
                return jwt.GetString()!;
            if (doc.RootElement.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String)
                return val.GetString()!;

            throw new InvalidOperationException("ADO OIDC response did not contain an 'oidcToken' or 'value' field.");
        }
        finally
        {
            if (owned) client.Dispose();
            log.LogDebug("Fetched ADO OIDC assertion from {Url}.", url);
        }
    }

    private static async Task<string> FetchGitHubOidcAsync(
        Uri url, string bearer, HttpClient? http, ILogger log, CancellationToken ct)
    {
        var owned = http is null;
        var client = http ?? new HttpClient();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

            // GitHub returns {"value": "...jwt...", "count": 1234}.
            if (doc.RootElement.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String)
                return val.GetString()!;

            throw new InvalidOperationException("GitHub Actions OIDC response did not contain a 'value' field.");
        }
        finally
        {
            if (owned) client.Dispose();
            log.LogDebug("Fetched GitHub OIDC assertion from {Url}.", url);
        }
    }
}
