using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using TALXIS.CLI.Core.Contracts.Dataverse;
using TALXIS.CLI.Platform.Dataverse.Runtime;

namespace TALXIS.CLI.Platform.Dataverse.Data;

/// <summary>
/// Dataverse implementation of <see cref="IDataverseQueryService"/>.
/// Executes read-only SQL, FetchXML, and OData queries via the
/// <c>ServiceClient</c> obtained through <see cref="DataverseCommandBridge"/>.
/// </summary>
internal sealed class DataverseQueryService : IDataverseQueryService
{
    /// <summary>
    /// Regex that detects SQL write operations. Only SELECT queries are allowed.
    /// </summary>
    private static readonly Regex WriteOperationPattern = new(
        @"\b(INSERT|UPDATE|DELETE|DROP|ALTER|TRUNCATE|MERGE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Prefixes used by OData/Dataverse annotations that are stripped from
    /// results unless the caller explicitly asks for them.
    /// </summary>
    private static readonly string[] AnnotationPrefixes = { "@odata.", "@OData.", "@Microsoft." };

    // ───────────────────────────── SQL ──────────────────────────────

    /// <inheritdoc />
    public async Task<DataverseQueryResult> QuerySqlAsync(
        string? profileName,
        string sql,
        int? top,
        bool includeAnnotations,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        ValidateSqlReadOnly(sql);

        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        // Resolve the entity set name from the table's logical name so the
        // Web API request targets the correct OData entity set.
        var entitySetName = ResolveEntitySetName(conn, sql);

        var headers = BuildHeaders(includeAnnotations);
        var queryPath = $"{entitySetName}?sql={Uri.EscapeDataString(sql)}";

        var records = new List<JsonElement>();
        try
        {
            using var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, queryPath, string.Empty, headers);
            await ParseValueArrayAsync(response, records, ct).ConfigureAwait(false);

            // Follow @odata.nextLink pages until exhausted or top limit reached.
            await FollowNextLinksAsync(conn, response, records, top, headers, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"SQL query against '{entitySetName}' failed: {ex.Message}", ex);
        }

        if (!includeAnnotations)
            StripAnnotations(records);

        if (top.HasValue && records.Count > top.Value)
            records.RemoveRange(top.Value, records.Count - top.Value);

        return new DataverseQueryResult(records, records.Count);
    }

    // ──────────────────────────── FetchXML ──────────────────────────

    /// <inheritdoc />
    public async Task<DataverseQueryResult> QueryFetchXmlAsync(
        string? profileName,
        string fetchXml,
        int? top,
        bool includeAnnotations,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fetchXml);

        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var records = new List<JsonElement>();
        bool hasFetchTop = HasTopAttribute(fetchXml);
        int page = 1;
        string? pagingCookie = null;

        while (true)
        {
            var pagedXml = ApplyPaging(fetchXml, page, pagingCookie);
            var fetchExpr = new FetchExpression(pagedXml);
            var response = await conn.Client.RetrieveMultipleAsync(fetchExpr, ct).ConfigureAwait(false);

            foreach (var entity in response.Entities)
            {
                records.Add(EntityToJsonElement(entity, includeAnnotations));

                if (top.HasValue && records.Count >= top.Value)
                    return new DataverseQueryResult(records, records.Count);
            }

            // When the FetchXML has a top attribute, Dataverse handles the
            // row limit server-side and does not support paging alongside it.
            if (hasFetchTop || !response.MoreRecords)
                break;

            pagingCookie = response.PagingCookie;
            page++;
        }

        return new DataverseQueryResult(records, records.Count);
    }

    // ──────────────────────────── OData ─────────────────────────────

    /// <inheritdoc />
    public async Task<DataverseQueryResult> QueryODataAsync(
        string? profileName,
        string entitySetOrPath,
        string? select,
        string? filter,
        string? orderBy,
        int? top,
        bool includeAnnotations,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySetOrPath);

        using var conn = await DataverseCommandBridge.ConnectAsync(profileName, ct).ConfigureAwait(false);

        var queryPath = BuildODataQueryPath(entitySetOrPath, select, filter, orderBy, top);
        var headers = BuildHeaders(includeAnnotations);

        var records = new List<JsonElement>();
        try
        {
            using var response = conn.Client.ExecuteWebRequest(HttpMethod.Get, queryPath, string.Empty, headers);
            await ParseValueArrayAsync(response, records, ct).ConfigureAwait(false);

            // Dataverse can still return @odata.nextLink when $top is specified
            // (for example when the requested top exceeds the server page size).
            // Always follow pagination links and let the helper stop once the
            // requested number of records has been accumulated.
            await FollowNextLinksAsync(conn, response, records, top, headers, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"OData query against '{entitySetOrPath}' failed: {ex.Message}", ex);
        }

        if (!includeAnnotations)
            StripAnnotations(records);

        if (top.HasValue && records.Count > top.Value)
            records.RemoveRange(top.Value, records.Count - top.Value);

        return new DataverseQueryResult(records, records.Count);
    }

    // ──────────────────────── Private helpers ───────────────────────

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the SQL text
    /// contains write-oriented keywords. Only SELECT queries are allowed.
    /// </summary>
    private static void ValidateSqlReadOnly(string sql)
    {
        if (WriteOperationPattern.IsMatch(sql))
            throw new InvalidOperationException(
                "Write operations (INSERT, UPDATE, DELETE, DROP, ALTER, TRUNCATE, MERGE) are not allowed. Only SELECT queries are supported.");
    }

    /// <summary>
    /// Extracts the table logical name from the SQL FROM clause and
    /// retrieves its <c>EntitySetName</c> via entity metadata.
    /// </summary>
    private static string ResolveEntitySetName(DataverseConnection conn, string sql)
    {
        // Simple regex to grab the first table name after FROM.
        var match = Regex.Match(sql, @"\bFROM\s+(\w+)", RegexOptions.IgnoreCase);
        if (!match.Success)
            throw new InvalidOperationException($"Could not parse a table name from the SQL FROM clause in: '{sql}'.");

        var tableName = match.Groups[1].Value.ToLowerInvariant();

        var metaRequest = new RetrieveEntityRequest
        {
            LogicalName = tableName,
            EntityFilters = EntityFilters.Entity,
        };

        var metaResponse = (RetrieveEntityResponse)conn.Client.Execute(metaRequest);
        return metaResponse.EntityMetadata.EntitySetName;
    }

    /// <summary>
    /// Builds custom HTTP headers for ExecuteWebRequest calls.
    /// The Dataverse <c>ServiceClient.ExecuteWebRequest</c> expects headers
    /// as <c>Dictionary&lt;string, List&lt;string&gt;&gt;</c>.
    /// </summary>
    private static Dictionary<string, List<string>> BuildHeaders(bool includeAnnotations)
    {
        var headers = new Dictionary<string, List<string>>();
        if (includeAnnotations)
            headers["Prefer"] = new List<string> { "odata.include-annotations=*" };
        return headers;
    }

    /// <summary>
    /// Constructs the OData query path from individual query parameters.
    /// </summary>
    private static string BuildODataQueryPath(
        string entitySetOrPath, string? select, string? filter, string? orderBy, int? top)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(select))
            parts.Add($"$select={Uri.EscapeDataString(select)}");
        if (!string.IsNullOrWhiteSpace(filter))
            parts.Add($"$filter={Uri.EscapeDataString(filter)}");
        if (!string.IsNullOrWhiteSpace(orderBy))
            parts.Add($"$orderby={Uri.EscapeDataString(orderBy)}");
        if (top.HasValue)
            parts.Add($"$top={top.Value}");

        return parts.Count > 0
            ? $"{entitySetOrPath}?{string.Join("&", parts)}"
            : entitySetOrPath;
    }

    /// <summary>
    /// Parses the <c>value</c> array from a Web API JSON response.
    /// Throws <see cref="InvalidOperationException"/> with the server error
    /// message when the response indicates a non-success HTTP status code.
    /// </summary>
    private static async Task ParseValueArrayAsync(
        HttpResponseMessage response,
        List<JsonElement> target,
        CancellationToken ct)
    {
        var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var serverMessage = TryExtractODataErrorMessage(content);
            throw new InvalidOperationException(
                string.IsNullOrEmpty(serverMessage)
                    ? $"Dataverse returned HTTP {(int)response.StatusCode} ({response.StatusCode})."
                    : $"Dataverse returned HTTP {(int)response.StatusCode} ({response.StatusCode}): {serverMessage}");
        }

        using var doc = JsonDocument.Parse(content);

        if (doc.RootElement.TryGetProperty("value", out var valueArray) &&
            valueArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in valueArray.EnumerateArray())
                target.Add(item.Clone());
        }
    }

    /// <summary>
    /// Attempts to extract the <c>error.message</c> field from an OData
    /// error response body. Returns <c>null</c> when the body is not valid
    /// OData error JSON.
    /// </summary>
    private static string? TryExtractODataErrorMessage(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("error", out var errorElement) &&
                errorElement.TryGetProperty("message", out var messageElement))
            {
                if (messageElement.ValueKind == JsonValueKind.String)
                {
                    return messageElement.GetString();
                }

                // OData error payloads may represent "message" as an object
                // such as { "lang": "en-US", "value": "..." }.
                if (messageElement.ValueKind == JsonValueKind.Object &&
                    messageElement.TryGetProperty("value", out var valueElement) &&
                    valueElement.ValueKind == JsonValueKind.String)
                {
                    return valueElement.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Response is not valid JSON — fall through.
        }
        return null;
    }

    /// <summary>
    /// Follows <c>@odata.nextLink</c> URLs for automatic pagination.
    /// NextLink URLs are absolute; we extract the relative path to pass
    /// to <see cref="Microsoft.PowerPlatform.Dataverse.Client.ServiceClient.ExecuteWebRequest"/>.
    /// </summary>
    private static async Task FollowNextLinksAsync(
        DataverseConnection conn,
        HttpResponseMessage lastResponse,
        List<JsonElement> records,
        int? top,
        Dictionary<string, List<string>> headers,
        CancellationToken ct)
    {
        var currentResponse = lastResponse;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var content = await currentResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(content);

            if (!doc.RootElement.TryGetProperty("@odata.nextLink", out var nextLinkElement))
                break;

            var nextLinkUrl = nextLinkElement.GetString();
            if (string.IsNullOrWhiteSpace(nextLinkUrl))
                break;

            // Extract relative path from the absolute nextLink URL.
            var relativePath = ExtractRelativePath(nextLinkUrl, conn);

            // Dispose paginated responses to avoid socket/handler leaks.
            var nextResponse = conn.Client.ExecuteWebRequest(HttpMethod.Get, relativePath, string.Empty, headers);
            try
            {
                await ParseValueArrayAsync(nextResponse, records, ct).ConfigureAwait(false);
            }
            finally
            {
                // Keep the reference for the next iteration's content read.
                if (currentResponse != lastResponse)
                    currentResponse.Dispose();
                currentResponse = nextResponse;
            }

            if (top.HasValue && records.Count >= top.Value)
                break;
        }

        // Dispose the last paginated response (but not the caller's initial response).
        if (currentResponse != lastResponse)
            currentResponse.Dispose();
    }

    /// <summary>
    /// Extracts the relative path from an absolute nextLink URL by stripping
    /// the base URI and the Web API prefix so the returned path matches the
    /// entity-relative format expected by <c>ExecuteWebRequest</c>.
    /// </summary>
    private static string ExtractRelativePath(string absoluteUrl, DataverseConnection conn)
    {
        if (Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri))
        {
            return NormalizeWebApiRelativePath(uri.PathAndQuery);
        }

        return NormalizeWebApiRelativePath(absoluteUrl);
    }

    /// <summary>
    /// Normalizes a Dataverse Web API path so it matches the format expected by
    /// <c>ExecuteWebRequest</c>, for example <c>accounts?$select=name</c> instead
    /// of <c>api/data/v9.2/accounts?$select=name</c>.
    /// </summary>
    private static string NormalizeWebApiRelativePath(string path)
    {
        var normalizedPath = path.TrimStart('/');

        // Dataverse next links can include the Web API route prefix. Strip it so
        // pagination requests use the same relative path shape as initial requests.
        return Regex.Replace(
            normalizedPath,
            @"^api/data/v\d+\.\d+/",
            string.Empty,
            RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Returns <c>true</c> when the FetchXML root has a <c>top</c> attribute,
    /// meaning the server controls the row limit and paging must not be added
    /// (Dataverse rejects <c>top</c> + <c>page</c> together).
    /// </summary>
    private static bool HasTopAttribute(string fetchXml)
    {
        var doc = XDocument.Parse(fetchXml);
        return doc.Root?.Attribute("top") is not null;
    }

    /// <summary>
    /// Applies paging information (page number and paging cookie) to a
    /// FetchXML query string for multi-page retrieval. Skipped when the
    /// FetchXML already contains a <c>top</c> attribute.
    /// </summary>
    private static string ApplyPaging(string fetchXml, int page, string? pagingCookie)
    {
        var doc = XDocument.Parse(fetchXml);
        var fetchElement = doc.Root
            ?? throw new InvalidOperationException("FetchXML document has no root element.");

        // Do not add paging when the FetchXML already has a top attribute —
        // Dataverse rejects the combination.
        if (fetchElement.Attribute("top") is not null)
            return fetchXml;

        fetchElement.SetAttributeValue("page", page);

        if (!string.IsNullOrWhiteSpace(pagingCookie))
            fetchElement.SetAttributeValue("paging-cookie", pagingCookie);

        return doc.ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>
    /// Converts a Dataverse <see cref="Entity"/> to a <see cref="JsonElement"/>
    /// by serializing its attributes as a flat dictionary.
    /// </summary>
    private static JsonElement EntityToJsonElement(Entity entity, bool includeAnnotations)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var attr in entity.Attributes)
        {
            dict[attr.Key] = ConvertAttributeValue(attr.Value);
        }

        // Include formatted values (annotations) when requested.
        if (includeAnnotations && entity.FormattedValues.Count > 0)
        {
            foreach (var fv in entity.FormattedValues)
                dict[$"{fv.Key}@OData.Community.Display.V1.FormattedValue"] = fv.Value;
        }

        var json = JsonSerializer.Serialize(dict);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Converts SDK attribute values to JSON-safe primitives.
    /// </summary>
    private static object? ConvertAttributeValue(object? value)
    {
        return value switch
        {
            null => null,
            EntityReference er => new { Id = er.Id, LogicalName = er.LogicalName, Name = er.Name },
            OptionSetValue osv => osv.Value,
            Money m => m.Value,
            AliasedValue av => ConvertAttributeValue(av.Value),
            _ => value,
        };
    }

    /// <summary>
    /// Removes OData/Microsoft annotation keys from each record's properties.
    /// Called by default unless <c>includeAnnotations</c> is <c>true</c>.
    /// </summary>
    private static void StripAnnotations(List<JsonElement> records)
    {
        for (int i = 0; i < records.Count; i++)
        {
            if (records[i].ValueKind != JsonValueKind.Object)
                continue;

            var dict = new Dictionary<string, JsonElement>();
            bool hasAnnotations = false;

            foreach (var prop in records[i].EnumerateObject())
            {
                if (IsAnnotationKey(prop.Name))
                {
                    hasAnnotations = true;
                    continue;
                }
                dict[prop.Name] = prop.Value.Clone();
            }

            if (hasAnnotations)
            {
                var json = JsonSerializer.Serialize(dict);
                using var doc = JsonDocument.Parse(json);
                records[i] = doc.RootElement.Clone();
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the property name is an OData/Dataverse annotation.
    /// </summary>
    private static bool IsAnnotationKey(string key)
    {
        foreach (var prefix in AnnotationPrefixes)
        {
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
