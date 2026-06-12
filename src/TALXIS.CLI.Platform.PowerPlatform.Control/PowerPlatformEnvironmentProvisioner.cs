using System.Net;
using System.Text.Json;
using TALXIS.CLI.Core.Abstractions;
using TALXIS.CLI.Core.Model;
using TALXIS.CLI.Platform.PowerPlatform.Control.Bap;

namespace TALXIS.CLI.Platform.PowerPlatform.Control;

/// <summary>
/// Creates Power Platform environments via the BAP admin API. Resolves
/// human-friendly currency/language/template inputs against the per-region
/// catalogs (throwing <see cref="ArgumentException"/> with the valid values on
/// a miss, so callers surface input errors as exit-code 2), issues the create
/// request, and optionally polls the returned operation until it completes.
/// </summary>
public sealed class PowerPlatformEnvironmentProvisioner : IPowerPlatformEnvironmentProvisioner
{
    private const string DatabaseType = "CommonDataService";

    // Poll cadence mirrors the Microsoft PAC CLI: a tight initial interval that
    // backs off once the operation is clearly long-running.
    private static readonly TimeSpan InitialPollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan SteadyPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BackoffAfter = TimeSpan.FromSeconds(10);

    private readonly BapAdminApiClient _bap;

    public PowerPlatformEnvironmentProvisioner(
        IAccessTokenService tokens,
        IHttpClientFactoryWrapper? httpFactory = null)
    {
        _bap = new BapAdminApiClient(tokens, httpFactory);
    }

    public async Task<EnvironmentCreateResult> CreateAsync(
        Connection connection,
        Credential credential,
        EnvironmentCreateRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(request);

        ValidateRequest(request);

        var baseUri = _bap.GetBaseUri(connection);
        var token = await _bap.AcquireTokenAsync(connection, credential, ct).ConfigureAwait(false);
        var region = request.Region.Trim().ToLowerInvariant();
        var sku = request.EnvironmentType.ToString();

        // Resolve + validate the per-region catalog inputs before we POST so a
        // bad currency/language/template fails fast with actionable guidance.
        var currency = await ResolveCurrencyAsync(baseUri, token, region, request.CurrencyCode, ct).ConfigureAwait(false);
        var baseLanguage = await ResolveLanguageAsync(baseUri, token, region, request.Language, ct).ConfigureAwait(false);
        if (request.Templates.Count > 0)
            await ValidateTemplatesAsync(baseUri, token, region, sku, request.Templates, ct).ConfigureAwait(false);

        var body = BuildRequestBody(request, region, sku, currency, baseLanguage, connection.TenantId);

        var createUri = new Uri(
            baseUri,
            $"/providers/Microsoft.BusinessAppPlatform/environments?api-version={BapEndpointProvider.CreateApiVersion}&id=/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments");

        var response = await _bap.SendAsync(HttpMethod.Post, createUri, token, body, ct).ConfigureAwait(false);
        if (!response.IsSuccess && response.StatusCode != HttpStatusCode.Accepted)
        {
            throw new InvalidOperationException(
                $"Environment creation failed ({(int)response.StatusCode} {response.StatusCode}): {BapAdminApiClient.Truncate(response.Body, 500)}");
        }

        var parsed = ParseEnvironmentEnvelope(response.Body);
        var operationLocation = response.Location;

        // Fire-and-forget: return the queued operation so the caller can report
        // the new environment id and where to track progress.
        if (!request.Wait)
        {
            return new EnvironmentCreateResult(
                EnvironmentId: parsed.Id,
                DisplayName: parsed.DisplayName ?? request.DisplayName,
                EnvironmentUrl: parsed.Url,
                EnvironmentType: parsed.Type ?? request.EnvironmentType,
                Status: parsed.State ?? "Provisioning",
                Completed: false,
                OperationLocation: operationLocation);
        }

        // Already complete (synchronous 200/201) — no polling needed.
        if (response.StatusCode != HttpStatusCode.Accepted || operationLocation is null)
        {
            return new EnvironmentCreateResult(
                parsed.Id, parsed.DisplayName ?? request.DisplayName, parsed.Url,
                parsed.Type ?? request.EnvironmentType, parsed.State ?? "Succeeded",
                Completed: true, OperationLocation: null);
        }

        return await PollUntilCompleteAsync(operationLocation, connection, credential, request, parsed, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Validates the cross-field rules the BAP API enforces, surfaced here as
    /// <see cref="ArgumentException"/> so the CLI returns a validation exit code.
    /// </summary>
    private static void ValidateRequest(EnvironmentCreateRequest request)
    {
        if (request.EnvironmentType == EnvironmentType.Default)
            throw new ArgumentException("Environment type 'Default' cannot be created — it is the tenant's auto-provisioned environment.");

        if (request.EnvironmentType == EnvironmentType.Teams)
        {
            if (request.SecurityGroupId is null || request.SecurityGroupId == Guid.Empty)
                throw new ArgumentException("A '--security-group-id' is required when creating a 'Teams' environment.");
        }
        else if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            throw new ArgumentException("A display name ('--name') is required for this environment type.");
        }

        if (request.UserObjectId is { } userId && userId != Guid.Empty
            && request.EnvironmentType != EnvironmentType.Developer)
        {
            throw new ArgumentException("'--user' is only supported when creating a 'Developer' environment.");
        }
    }

    private async Task<EnvironmentCreateResult> PollUntilCompleteAsync(
        Uri operationLocation,
        Connection connection,
        Credential credential,
        EnvironmentCreateRequest request,
        EnvironmentEnvelope initial,
        CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var interval = InitialPollInterval;
        var latest = initial;

        while (true)
        {
            // Re-acquire on every iteration so the token stays fresh across
            // long-running polls (up to MaxWait, default 60 min). MSAL's
            // in-memory cache makes this a no-op when the token is still valid.
            var token = await _bap.AcquireTokenAsync(connection, credential, ct).ConfigureAwait(false);
            var poll = await _bap.SendAsync(HttpMethod.Get, operationLocation, token, jsonBody: null, ct).ConfigureAwait(false);

            // 202 = still provisioning; anything else terminal (success body parsed below).
            if (poll.StatusCode != HttpStatusCode.Accepted)
            {
                if (!poll.IsSuccess)
                {
                    throw new InvalidOperationException(
                        $"Environment provisioning failed ({(int)poll.StatusCode} {poll.StatusCode}): {BapAdminApiClient.Truncate(poll.Body, 500)}");
                }

                var done = ParseEnvironmentEnvelope(poll.Body);
                return new EnvironmentCreateResult(
                    done.Id ?? latest.Id,
                    done.DisplayName ?? latest.DisplayName ?? request.DisplayName,
                    done.Url ?? latest.Url,
                    done.Type ?? latest.Type ?? request.EnvironmentType,
                    done.State ?? "Succeeded",
                    Completed: true,
                    OperationLocation: null);
            }

            if (!string.IsNullOrWhiteSpace(poll.Body))
                latest = ParseEnvironmentEnvelope(poll.Body) is { Id: not null } p ? p : latest;

            if (DateTimeOffset.UtcNow - started >= request.MaxWait)
            {
                // Timed out waiting — report as still provisioning rather than failing.
                return new EnvironmentCreateResult(
                    latest.Id, latest.DisplayName ?? request.DisplayName, latest.Url,
                    latest.Type ?? request.EnvironmentType, "Provisioning",
                    Completed: false, OperationLocation: operationLocation);
            }

            await Task.Delay(interval, ct).ConfigureAwait(false);
            if (DateTimeOffset.UtcNow - started >= BackoffAfter)
                interval = SteadyPollInterval;
        }
    }

    private static Dictionary<string, object?> BuildRequestBody(
        EnvironmentCreateRequest request,
        string region,
        string sku,
        ResolvedCurrency currency,
        int baseLanguage,
        string? tenantId)
    {
        var linkedMetadata = new Dictionary<string, object?>
        {
            ["baseLanguage"] = baseLanguage,
            ["currency"] = new Dictionary<string, object?>
            {
                ["code"] = currency.Code,
                ["name"] = currency.Name,
                ["symbol"] = currency.Symbol,
            },
            ["domainName"] = string.IsNullOrWhiteSpace(request.DomainName) ? null : request.DomainName.Trim(),
        };

        if (request.Templates.Count > 0)
            linkedMetadata["templates"] = request.Templates.ToArray();

        if (request.SecurityGroupId is { } sg && sg != Guid.Empty)
            linkedMetadata["securityGroupId"] = sg;

        var properties = new Dictionary<string, object?>
        {
            ["displayName"] = request.DisplayName,
            ["environmentSku"] = sku,
            ["databaseType"] = DatabaseType,
            ["linkedEnvironmentMetadata"] = linkedMetadata,
        };

        // Teams environments associate the security group as a connected group.
        if (request.EnvironmentType == EnvironmentType.Teams && request.SecurityGroupId is { } teamGroup)
        {
            properties["connectedGroups"] = new[]
            {
                new Dictionary<string, object?> { ["id"] = teamGroup },
            };
        }

        // Developer environments are owned by the specified user.
        if (request.EnvironmentType == EnvironmentType.Developer && request.UserObjectId is { } userId && userId != Guid.Empty)
        {
            properties["usedBy"] = new Dictionary<string, object?>
            {
                ["id"] = userId,
                ["tenantId"] = tenantId,
                ["type"] = "User",
            };
        }

        return new Dictionary<string, object?>
        {
            ["location"] = region,
            ["properties"] = properties,
        };
    }

    private async Task<ResolvedCurrency> ResolveCurrencyAsync(
        Uri baseUri, string token, string region, string currencyCode, CancellationToken ct)
    {
        var uri = new Uri(baseUri, $"/providers/Microsoft.BusinessAppPlatform/locations/{region}/environmentCurrencies?api-version={BapEndpointProvider.CreateApiVersion}");
        using var doc = await GetCatalogAsync(uri, token, region, "currencies", ct).ConfigureAwait(false);

        var valid = new List<string>();
        foreach (var item in EnumerateValue(doc.RootElement))
        {
            if (!item.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
                continue;
            var code = ReadString(props, "code");
            if (code is null)
                continue;
            valid.Add(code);

            if (string.Equals(code, currencyCode.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                var name = ReadString(props, "localizedName") ?? ReadString(props, "name") ?? code;
                var symbol = ReadString(props, "symbol") ?? code;
                return new ResolvedCurrency(code, name, symbol);
            }
        }

        throw new ArgumentException(
            $"Currency '{currencyCode}' is not available in region '{region}'. Valid codes: {string.Join(", ", valid.OrderBy(c => c))}.");
    }

    private async Task<int> ResolveLanguageAsync(
        Uri baseUri, string token, string region, string language, CancellationToken ct)
    {
        // Raw LCID integers are accepted directly (matches PAC behavior).
        if (int.TryParse(language.Trim(), out var lcid))
            return lcid;

        var uri = new Uri(baseUri, $"/providers/Microsoft.BusinessAppPlatform/locations/{region}/environmentLanguages?api-version={BapEndpointProvider.CreateApiVersion}");
        using var doc = await GetCatalogAsync(uri, token, region, "languages", ct).ConfigureAwait(false);

        var matches = new List<int>();
        var valid = new List<string>();
        foreach (var item in EnumerateValue(doc.RootElement))
        {
            if (!item.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
                continue;
            var localizedName = ReadString(props, "localizedName");
            var localeId = ReadString(props, "localeId");
            if (localizedName is null || localeId is null || !int.TryParse(localeId, out var id))
                continue;
            valid.Add($"{localizedName} ({localeId})");

            if (localizedName.StartsWith(language.Trim(), StringComparison.OrdinalIgnoreCase))
                matches.Add(id);
        }

        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ArgumentException(
                $"Language '{language}' was not found in region '{region}'. Valid languages: {string.Join(", ", valid)}."),
            _ => throw new ArgumentException(
                $"Language '{language}' is ambiguous in region '{region}' — refine it or pass the LCID. Valid languages: {string.Join(", ", valid)}."),
        };
    }

    private async Task ValidateTemplatesAsync(
        Uri baseUri, string token, string region, string sku, IReadOnlyList<string> templates, CancellationToken ct)
    {
        var uri = new Uri(baseUri, $"/providers/Microsoft.BusinessAppPlatform/locations/{region}/templates?api-version={BapEndpointProvider.CreateApiVersion}");
        using var doc = await GetCatalogAsync(uri, token, region, "templates", ct).ConfigureAwait(false);

        // The response is an object keyed by SKU; each value is an array of template objects.
        var available = new List<string>();
        foreach (var skuProperty in doc.RootElement.EnumerateObject())
        {
            if (!string.Equals(skuProperty.Name, sku, StringComparison.OrdinalIgnoreCase)
                || skuProperty.Value.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var template in skuProperty.Value.EnumerateArray())
            {
                var name = ReadString(template, "name");
                if (name is not null)
                    available.Add(name);
            }
        }

        var invalid = templates
            .Where(t => !available.Contains(t, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (invalid.Count > 0)
        {
            throw new ArgumentException(
                $"Unknown template(s) for SKU '{sku}' in region '{region}': {string.Join(", ", invalid)}. " +
                $"Valid templates: {(available.Count > 0 ? string.Join(", ", available) : "(none)")}.");
        }
    }

    private async Task<JsonDocument> GetCatalogAsync(Uri uri, string token, string region, string catalog, CancellationToken ct)
    {
        var response = await _bap.SendAsync(HttpMethod.Get, uri, token, jsonBody: null, ct).ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to load {catalog} for region '{region}' ({(int)response.StatusCode} {response.StatusCode}): {BapAdminApiClient.Truncate(response.Body, 300)}");
        }
        return JsonDocument.Parse(response.Body);
    }

    private static EnvironmentEnvelope ParseEnvironmentEnvelope(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return new EnvironmentEnvelope(null, null, null, null, null);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return new EnvironmentEnvelope(null, null, null, null, null);
        }

        using (doc)
        {
            var root = doc.RootElement;
            Guid? id = Guid.TryParse(ReadString(root, "name"), out var parsed) ? parsed : null;

            string? displayName = null;
            Uri? url = null;
            EnvironmentType? type = null;
            string? state = null;

            if (root.TryGetProperty("properties", out var props) && props.ValueKind == JsonValueKind.Object)
            {
                displayName = ReadString(props, "displayName");
                type = EnvironmentSkuParser.TryParse(props);
                state = ReadString(props, "provisioningState") ?? ReadString(props, "state");

                if (props.TryGetProperty("linkedEnvironmentMetadata", out var linked)
                    && linked.ValueKind == JsonValueKind.Object
                    && ReadString(linked, "instanceUrl") is { } instanceUrl
                    && Uri.TryCreate(instanceUrl, UriKind.Absolute, out var parsedUrl))
                {
                    url = parsedUrl;
                }
            }

            return new EnvironmentEnvelope(id, displayName, url, type, state);
        }
    }

    private static IEnumerable<JsonElement> EnumerateValue(JsonElement root)
        => root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    private static string? ReadString(JsonElement element, string property)
        => element.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()?.Trim()
            : null;

    private readonly record struct ResolvedCurrency(string Code, string Name, string Symbol);

    private readonly record struct EnvironmentEnvelope(
        Guid? Id, string? DisplayName, Uri? Url, EnvironmentType? Type, string? State);
}
