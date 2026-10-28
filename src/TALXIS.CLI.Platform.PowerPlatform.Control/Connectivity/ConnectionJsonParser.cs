using System.Globalization;
using System.Text.Json;
using TALXIS.CLI.Core.Platforms.PowerPlatform;

namespace TALXIS.CLI.Platform.PowerPlatform.Control.Connectivity;

/// <summary>
/// A connection's platform state: its status and, when unhealthy, the first
/// error with its code. <see cref="Error"/> is null when healthy.
/// </summary>
internal sealed record ConnectionState(string? Status, string? ErrorCode, string? Error)
{
    public bool IsUnauthenticated
        => string.Equals(ErrorCode, ConnectionStatusCodes.Unauthenticated, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Where a person (or the silent consent flow) completes a pending sign-in.</summary>
internal sealed record ConnectionConsentInfo(Uri? FirstPartyLoginUri, string? ConsentLink);

/// <summary>Reads connection payloads of the connectivity API.</summary>
internal static class ConnectionJsonParser
{
    /// <summary>The short apiId of a connection, or null when the payload names none.</summary>
    public static string? ApiIdOf(JsonElement connection)
    {
        var apiId = JsonElementReader.TryGetObject(connection, "properties") is { } props
            ? JsonElementReader.TryGetString(props, "apiId")
            : null;

        return apiId is null ? null : ConnectorIds.ToShortOrEmpty(apiId);
    }

    public static ConnectionSummary ParseConnection(JsonElement connection, string apiId)
    {
        var props = connection.TryGetProperty("properties", out var p) ? p : default;
        var state = ReadState(connection);

        string? createdByType = null;
        string? createdBy = null;
        if (JsonElementReader.TryGetObject(props, "createdBy") is { } by)
        {
            createdByType = JsonElementReader.TryGetString(by, "type");
            createdBy = JsonElementReader.TryGetString(by, "userPrincipalName")
                ?? JsonElementReader.TryGetString(by, "displayName")
                ?? JsonElementReader.TryGetString(by, "id");
        }

        DateTimeOffset? created = null;
        if (JsonElementReader.TryGetString(props, "createdTime") is { } time
            && DateTimeOffset.TryParse(time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            created = parsed;
        }

        return new ConnectionSummary(
            JsonElementReader.TryGetString(connection, "name") ?? string.Empty,
            apiId,
            JsonElementReader.TryGetString(props, "displayName"),
            state.Status,
            state.Error,
            createdByType,
            createdBy,
            created);
    }

    public static ConnectionState ReadState(JsonElement connection)
    {
        if (JsonElementReader.TryGetObject(connection, "properties") is not { } props
            || !props.TryGetProperty("statuses", out var statuses)
            || statuses.ValueKind != JsonValueKind.Array)
        {
            return new ConnectionState(null, null, null);
        }

        string? status = null;
        foreach (var entry in statuses.EnumerateArray())
        {
            var entryStatus = JsonElementReader.TryGetString(entry, "status");
            status ??= entryStatus;

            if (JsonElementReader.TryGetObject(entry, "error") is { } error)
            {
                return new ConnectionState(
                    entryStatus ?? "Error",
                    JsonElementReader.TryGetString(error, "code"),
                    JsonElementReader.TryGetString(error, "message")
                        ?? JsonElementReader.TryGetString(error, "code")
                        ?? "The connection is in an error state.");
            }
        }

        return new ConnectionState(status, null, null);
    }

    public static ConnectionConsentInfo ReadConsentInfo(JsonElement connection)
    {
        if (JsonElementReader.TryGetObject(connection, "properties") is not { } props
            || JsonElementReader.TryGetObject(props, "consentInfo") is not { } info)
        {
            return new ConnectionConsentInfo(null, null);
        }

        Uri? login = JsonElementReader.TryGetString(info, "firstPartyLoginUri") is { } text
            && Uri.TryCreate(text, UriKind.Absolute, out var uri)
                ? uri
                : null;
        return new ConnectionConsentInfo(login, JsonElementReader.TryGetString(info, "consentLink"));
    }

    /// <summary>The first test link of the connection, which connectors that support a live test expose.</summary>
    public static Uri? ReadTestLink(JsonElement connection)
    {
        if (JsonElementReader.TryGetObject(connection, "properties") is { } props
            && props.TryGetProperty("testLinks", out var links)
            && links.ValueKind == JsonValueKind.Array
            && links.GetArrayLength() > 0
            && JsonElementReader.TryGetString(links[0], "requestUri") is { } text
            && Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            return uri;
        }

        return null;
    }
}
